using System.Text.Json;
using System.Text.Json.Nodes;
using HomeBusinessAssistant.AgentSdk.Execution;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;

namespace HomeBusinessAssistant.Infrastructure.Execution;

/// <summary>Authoritative, idempotent run finalization and durable follow-up planning.</summary>
public sealed class RunFinalizer(
    IAgentRunRepository runs,
    IOccurrenceRepository occurrences,
    IScheduleRepository schedules,
    IFixedDelayCompletionService fixedDelay,
    IAuditWriter auditWriter,
    TimeProvider timeProvider) : IRunFinalizer
{
    /// <inheritdoc />
    public async ValueTask<RunFinalizationResult> FinalizeAsync(
        RunFinalizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AgentRunRecord current = await runs.GetAsync(request.RunId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The run to finalize does not exist.");
        FinalState final = DetermineFinalState(request);
        DateTimeOffset completedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        final = final with { SummaryJson = CreateStandardSummaryJson(current, final, request, completedAtUtc) };
        AgentRunRecord terminal = current;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            AgentRunRecord? saved = await runs.MarkTerminalAsync(
                current.Id,
                final.Status,
                completedAtUtc,
                request.Supervision.ExitCode,
                final.SummaryText,
                final.SummaryJson,
                final.ErrorType,
                final.ErrorMessage,
                current.ConcurrencyToken,
                cancellationToken).ConfigureAwait(false);
            if (saved is not null)
            {
                terminal = saved;
                break;
            }

            current = await runs.GetAsync(request.RunId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The run disappeared during finalization.");
            if (IsTerminal(current.Status))
            {
                terminal = current;
                break;
            }

            if (attempt == 4)
            {
                throw new InvalidOperationException("The run could not be finalized after concurrent heartbeat updates.");
            }
        }

        bool schedulePaused = await PauseScheduleIfRequestedAsync(
            terminal,
            request.Protocol.SummaryJson,
            completedAtUtc,
            cancellationToken).ConfigureAwait(false);
        FixedDelayCompletionResult fixedDelayResult = await fixedDelay.OnOccurrenceTerminalAsync(
            terminal.OccurrenceId,
            terminal.CompletedAtUtc ?? completedAtUtc,
            cancellationToken).ConfigureAwait(false);
        OccurrenceId? retryOccurrenceId = await CreateRetryIfRequiredAsync(
            terminal,
            request.Supervision.ExitCode,
            cancellationToken).ConfigureAwait(false);
        _ = await auditWriter.WriteAsync(new(
            AuditActorType.Runner,
            "runner",
            "run.finalized",
            "agent-run",
            terminal.Id.ToString(),
            terminal.Status == AgentRunStatus.Completed ? AuditOutcome.Succeeded : AuditOutcome.Failed,
            terminal.OccurrenceId.Value,
            terminal.Id,
            JsonSerializer.SerializeToElement(new
            {
                status = terminal.Status.ToString(),
                terminal.ExitCode,
                protocolViolations = request.Protocol.ProtocolViolations,
                request.Protocol.FatalProtocolIssue,
                request.Supervision.ProcessTreeKilled,
                request.Supervision.LeaseLost,
                schedulePaused,
                fixedDelayOccurrenceId = fixedDelayResult.NextOccurrenceId?.ToString(),
                retryOccurrenceId = retryOccurrenceId?.ToString(),
            })), cancellationToken).ConfigureAwait(false);
        return new(
            terminal,
            fixedDelayResult.Applicable,
            fixedDelayResult.NextOccurrenceId,
            retryOccurrenceId);
    }

    private async ValueTask<bool> PauseScheduleIfRequestedAsync(
        AgentRunRecord terminal,
        string? summaryJson,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        if (!RequestsSchedulePause(summaryJson))
        {
            return false;
        }

        ScheduleOccurrenceRecord occurrence = await occurrences.GetAsync(terminal.OccurrenceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The finalized occurrence does not exist.");
        if (!occurrence.ScheduleId.HasValue)
        {
            return false;
        }

        AgentScheduleRecord? schedule = await schedules.GetAsync(occurrence.ScheduleId.Value, cancellationToken).ConfigureAwait(false);
        if (schedule is null || schedule.IsPaused && !schedule.PausedUntilUtc.HasValue)
        {
            return schedule is not null;
        }

        AgentScheduleRecord saved = await schedules.SaveAsync(schedule with
        {
            IsPaused = true,
            PausedUntilUtc = null,
            UpdatedAtUtc = completedAtUtc,
        }, schedule.ConcurrencyToken, cancellationToken).ConfigureAwait(false);
        _ = await auditWriter.WriteAsync(new(
            AuditActorType.Agent,
            terminal.AgentId.Value,
            "schedule.paused-by-agent-stop",
            "schedule",
            saved.Id.ToString("D"),
            AuditOutcome.Succeeded,
            terminal.OccurrenceId.Value,
            terminal.Id,
            JsonSerializer.SerializeToElement(new
            {
                reason = "agent-requested-safe-stop",
                indefinitely = true,
            })), cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static bool RequestsSchedulePause(string? summaryJson)
    {
        if (string.IsNullOrWhiteSpace(summaryJson) || summaryJson.Length > 262_144)
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(summaryJson, new JsonDocumentOptions { MaxDepth = 32 });
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("scheduleAction", out JsonElement action)
                && action.ValueKind == JsonValueKind.String
                && action.GetString() == "Pause";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async ValueTask<OccurrenceId?> CreateRetryIfRequiredAsync(
        AgentRunRecord terminal,
        int? actualExitCode,
        CancellationToken cancellationToken)
    {
        if (terminal.Status != AgentRunStatus.Failed || actualExitCode != AgentExitCode.TransientFailure)
        {
            return null;
        }

        ScheduleOccurrenceRecord occurrence = await occurrences.GetAsync(terminal.OccurrenceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The finalized occurrence does not exist.");
        AgentScheduleRecord? schedule = await ResolveScheduleAsync(occurrence, cancellationToken).ConfigureAwait(false);
        if (schedule is null || !schedule.IsEnabled)
        {
            return null;
        }

        (RetryPolicyDefinition? policy, _) = RetryPolicyJson.Parse(schedule.RetryPolicyJson);
        if (policy is null || occurrence.AttemptNumber >= policy.MaximumRetries)
        {
            return null;
        }

        int nextAttempt = occurrence.AttemptNumber + 1;
        double multiplier = Math.Pow(2, Math.Max(0, occurrence.AttemptNumber));
        TimeSpan delay = TimeSpan.FromTicks(Math.Min(
            policy.MaximumDelay.Ticks,
            checked((long)(policy.InitialDelay.Ticks * multiplier))));
        DateTimeOffset dueAtUtc = (terminal.CompletedAtUtc ?? timeProvider.GetUtcNow()).ToUniversalTime().Add(delay);
        OccurrenceId proposedId = OccurrenceId.New();
        ScheduleOccurrenceRecord retry = await occurrences.CreateIfAbsentAsync(new(
            proposedId,
            ScheduleId: null,
            occurrence.AgentId,
            occurrence.CommandName,
            occurrence.ArgumentsJson,
            occurrence.ConfigurationRevisionId,
            dueAtUtc,
            TriggerType.Retry,
            nextAttempt,
            occurrence.Id,
            OccurrenceStatus.Ready,
            occurrence.RequiresWake,
            occurrence.KeepSystemAwake,
            occurrence.KeepDisplayOn), cancellationToken).ConfigureAwait(false);
        if (retry.Id == proposedId)
        {
            _ = await auditWriter.WriteAsync(new(
                AuditActorType.Runner,
                "runner",
                "occurrence.retry-planned",
                "occurrence",
                retry.Id.ToString(),
                AuditOutcome.Succeeded,
                terminal.OccurrenceId.Value,
                terminal.Id,
                JsonSerializer.SerializeToElement(new
                {
                    parentOccurrenceId = occurrence.Id.ToString(),
                    attemptNumber = nextAttempt,
                    dueAtUtc,
                    configurationRevisionId = occurrence.ConfigurationRevisionId,
                })), cancellationToken).ConfigureAwait(false);
        }

        return retry.Id;
    }

    private async ValueTask<AgentScheduleRecord?> ResolveScheduleAsync(
        ScheduleOccurrenceRecord occurrence,
        CancellationToken cancellationToken)
    {
        ScheduleOccurrenceRecord cursor = occurrence;
        for (var depth = 0; depth <= 10; depth++)
        {
            if (cursor.ScheduleId.HasValue)
            {
                return await schedules.GetAsync(cursor.ScheduleId.Value, cancellationToken).ConfigureAwait(false);
            }

            if (!cursor.ParentOccurrenceId.HasValue)
            {
                return null;
            }

            cursor = await occurrences.GetAsync(cursor.ParentOccurrenceId.Value, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("A retry parent occurrence does not exist.");
        }

        throw new InvalidOperationException("The retry occurrence chain exceeds the supported bound.");
    }

    private static FinalState DetermineFinalState(RunFinalizationRequest request)
    {
        if (request.ForcedStatus.HasValue)
        {
            AgentRunStatus forced = request.ForcedStatus.Value;
            if (!IsTerminal(forced))
            {
                throw new ArgumentException("A forced finalization status must be terminal.", nameof(request));
            }

            return new(
                forced,
                request.Protocol.SummaryText ?? CreateFallbackSummary(forced, request.Supervision.ExitCode),
                request.Protocol.SummaryJson,
                request.RunnerFailureType,
                request.RunnerFailureMessage);
        }

        bool completedCodeMismatch = request.Protocol.ReportedCompletedExitCode.HasValue
            && request.Supervision.ExitCode.HasValue
            && request.Protocol.ReportedCompletedExitCode != request.Supervision.ExitCode;
        AgentRunStatus preliminary = request switch
        {
            { Supervision.TimedOut: true } => AgentRunStatus.TimedOut,
            { Supervision.CancellationRequested: true } => AgentRunStatus.Cancelled,
            { Supervision.LeaseLost: true } => AgentRunStatus.Failed,
            { RunnerFailureType: not null } => AgentRunStatus.Failed,
            { Protocol.FatalProtocolIssue: true } => AgentRunStatus.Failed,
            { Supervision.ExitCode: AgentExitCode.Success } when !completedCodeMismatch => AgentRunStatus.Completed,
            { Supervision.ExitCode: AgentExitCode.Cancelled } => AgentRunStatus.Cancelled,
            _ => AgentRunStatus.Failed,
        };
        bool summaryStatusMismatch = request.Protocol.ReportedSummaryStatus.HasValue
            && request.Protocol.ReportedSummaryStatus != preliminary;
        AgentRunStatus finalStatus = preliminary is AgentRunStatus.TimedOut or AgentRunStatus.Cancelled
            ? preliminary
            : completedCodeMismatch || summaryStatusMismatch
                ? AgentRunStatus.Failed
                : preliminary;
        string summaryText = request.Protocol.SummaryText ?? CreateFallbackSummary(finalStatus, request.Supervision.ExitCode);
        string? errorType = finalStatus == AgentRunStatus.Completed ? null : GetErrorType(request, completedCodeMismatch, summaryStatusMismatch);
        string? errorMessage = finalStatus == AgentRunStatus.Completed
            ? null
            : request.RunnerFailureMessage
                ?? request.Supervision.FailureReason
                ?? errorType;
        return new(finalStatus, summaryText, request.Protocol.SummaryJson, errorType, errorMessage);
    }

    private static string GetErrorType(
        RunFinalizationRequest request,
        bool completedCodeMismatch,
        bool summaryStatusMismatch)
    {
        if (request.Supervision.TimedOut)
        {
            return "runner.timeout";
        }

        if (request.Supervision.CancellationRequested)
        {
            return "runner.cancelled";
        }

        if (request.Supervision.LeaseLost)
        {
            return "runner.execution-lease-lost";
        }

        if (request.RunnerFailureType is not null)
        {
            return request.RunnerFailureType;
        }

        if (request.Protocol.FatalProtocolIssue)
        {
            return request.Protocol.FatalReasonCode ?? "runner.protocol-fatal";
        }

        if (completedCodeMismatch)
        {
            return "runner.completed-exit-mismatch";
        }

        if (summaryStatusMismatch)
        {
            return "runner.summary-status-mismatch";
        }

        return request.Supervision.ExitCode.HasValue
            ? $"agent.exit-{request.Supervision.ExitCode.Value}"
            : "runner.process-exit-unavailable";
    }

    private static string CreateFallbackSummary(AgentRunStatus status, int? exitCode) => status switch
    {
        AgentRunStatus.Completed => "The agent completed successfully without supplying a summary.",
        AgentRunStatus.TimedOut => "The agent exceeded its configured timeout.",
        AgentRunStatus.Cancelled => "The agent run was cancelled.",
        AgentRunStatus.Abandoned => "The agent run was abandoned during recovery.",
        _ => exitCode.HasValue
            ? $"The agent failed with process exit code {exitCode.Value}."
            : "The agent failed before an exit code was available.",
    };

    private static string CreateStandardSummaryJson(
        AgentRunRecord run,
        FinalState final,
        RunFinalizationRequest request,
        DateTimeOffset completedAtUtc)
    {
        JsonElement? agentData = null;
        if (!string.IsNullOrWhiteSpace(final.SummaryJson))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(final.SummaryJson, new JsonDocumentOptions { MaxDepth = 32 });
                agentData = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                agentData = JsonSerializer.SerializeToElement(new { unavailable = "invalid-agent-summary-json" });
            }
        }

        JsonObject result = JsonSerializer.SerializeToNode(new
        {
            schemaVersion = "1.0",
            generatedBy = "platform-run-finalizer",
            runId = run.Id.ToString(),
            occurrenceId = run.OccurrenceId.ToString(),
            agentId = run.AgentId.Value,
            status = final.Status.ToString(),
            run.StartedAtUtc,
            completedAtUtc,
            durationMilliseconds = Math.Max(0, (long)(completedAtUtc - run.StartedAtUtc).TotalMilliseconds),
            request.Supervision.ExitCode,
            request.Protocol.ProtocolViolations,
            request.Supervision.TimedOut,
            request.Supervision.CancellationRequested,
            request.Supervision.ProcessTreeKilled,
            errorType = final.ErrorType,
            agentData,
        })!.AsObject();
        if (agentData is JsonElement { ValueKind: JsonValueKind.Object } details)
        {
            foreach (JsonProperty property in details.EnumerateObject())
            {
                if (!result.ContainsKey(property.Name))
                {
                    result[property.Name] = JsonNode.Parse(property.Value.GetRawText());
                }
            }
        }

        return result.ToJsonString();
    }

    private static bool IsTerminal(AgentRunStatus status) => status is AgentRunStatus.Completed
        or AgentRunStatus.Failed
        or AgentRunStatus.TimedOut
        or AgentRunStatus.Cancelled
        or AgentRunStatus.Abandoned;

    private sealed record FinalState(
        AgentRunStatus Status,
        string SummaryText,
        string? SummaryJson,
        string? ErrorType,
        string? ErrorMessage);
}
