using System.Diagnostics;
using System.Text.Json;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;

namespace HomeBusinessAssistant.Infrastructure.Execution;

/// <summary>Conservative stale claim/run recovery that never kills an ambiguous PID.</summary>
public sealed class StaleRunRecoveryService(
    IAgentRunRepository runs,
    IOccurrenceRepository occurrences,
    IAgentDefinitionRepository agents,
    IExecutableIntegrityService integrity,
    IRunFinalizer finalizer,
    IFixedDelayCompletionService fixedDelay,
    IAuditWriter auditWriter,
    IRunTemporaryFileManager temporaryFiles,
    TimeProvider timeProvider,
    RunnerSupervisionOptions options) : IStaleRunRecoveryService
{
    private static readonly TimeSpan ProcessStartTolerance = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public async ValueTask<StaleRunRecoveryResult> RecoverAsync(CancellationToken cancellationToken = default)
    {
        options.Validate();
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        int temporaryDeleted = temporaryFiles.CleanupStale(nowUtc.Subtract(options.StaleTemporaryFileAge));
        var staleClaimsAbandoned = 0;
        var staleRunsAbandoned = 0;
        var liveLeftRunning = 0;
        var errors = 0;

        IReadOnlyList<ScheduleOccurrenceRecord> staleClaims = await occurrences.GetStaleClaimsAsync(
            nowUtc,
            options.MaximumRecoveryResults,
            cancellationToken).ConfigureAwait(false);
        foreach (ScheduleOccurrenceRecord occurrence in staleClaims)
        {
            try
            {
                ScheduleOccurrenceRecord? abandoned = await occurrences.TryTransitionAsync(new(
                    occurrence.Id,
                    [OccurrenceStatus.Claimed],
                    OccurrenceStatus.Abandoned,
                    nowUtc,
                    "recovery.claim-expired",
                    "The occurrence claim expired before a run was initialized."), cancellationToken).ConfigureAwait(false);
                if (abandoned is null)
                {
                    continue;
                }

                staleClaimsAbandoned++;
                _ = await fixedDelay.OnOccurrenceTerminalAsync(
                    occurrence.Id,
                    abandoned.CompletedAtUtc ?? nowUtc,
                    cancellationToken).ConfigureAwait(false);
                await WriteAuditAsync(
                    "recovery.claim-abandoned",
                    occurrence.Id.ToString(),
                    runId: null,
                    new { occurrence.ClaimedBy, occurrence.ClaimExpiresAtUtc },
                    AuditOutcome.Failed,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors++;
                await WriteErrorAuditAsync("occurrence", occurrence.Id.ToString(), null, exception, cancellationToken).ConfigureAwait(false);
            }
        }

        IReadOnlyList<AgentRunRecord> staleRuns = await runs.GetStaleAsync(new(
            nowUtc.Subtract(options.StaleRunThreshold),
            options.MaximumRecoveryResults), cancellationToken).ConfigureAwait(false);
        foreach (AgentRunRecord run in staleRuns)
        {
            try
            {
                ProcessIdentityResult identity = await InspectProcessIdentityAsync(run, cancellationToken).ConfigureAwait(false);
                if (identity is ProcessIdentityResult.Matching or ProcessIdentityResult.AmbiguousLive)
                {
                    liveLeftRunning++;
                    await WriteAuditAsync(
                        identity == ProcessIdentityResult.Matching
                            ? "recovery.live-process-left-running"
                            : "recovery.ambiguous-process-left-running",
                        run.Id.ToString(),
                        run.Id,
                        new { run.ProcessId, identity = identity.ToString() },
                        AuditOutcome.Informational,
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                RunFinalizationResult terminal = await finalizer.FinalizeAsync(new(
                    run.Id,
                    new(
                        ExitCode: null,
                        TimedOut: false,
                        CancellationRequested: false,
                        LeaseLost: false,
                        ProcessTreeKilled: false,
                        FailureReason: "recovery.orphaned"),
                    EmptyProtocol(),
                    "recovery.orphaned",
                    "The runner heartbeat became stale and the original child process was absent or no longer matched.",
                    ForcedStatus: AgentRunStatus.Abandoned), cancellationToken).ConfigureAwait(false);
                temporaryFiles.CleanupRun(run.Id);
                staleRunsAbandoned++;
                await WriteAuditAsync(
                    "recovery.run-abandoned",
                    run.Id.ToString(),
                    terminal.Run.Id,
                    new { run.ProcessId, identity = identity.ToString() },
                    AuditOutcome.Failed,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors++;
                await WriteErrorAuditAsync("agent-run", run.Id.ToString(), run.Id, exception, cancellationToken).ConfigureAwait(false);
            }
        }

        return new(staleClaimsAbandoned, staleRunsAbandoned, liveLeftRunning, temporaryDeleted, errors);
    }

    private async ValueTask<ProcessIdentityResult> InspectProcessIdentityAsync(
        AgentRunRecord run,
        CancellationToken cancellationToken)
    {
        if (!run.ProcessId.HasValue || !run.ProcessStartedAtUtc.HasValue)
        {
            return ProcessIdentityResult.Missing;
        }

        Process process;
        try
        {
            process = Process.GetProcessById(run.ProcessId.Value);
        }
        catch (ArgumentException)
        {
            return ProcessIdentityResult.Missing;
        }

        using (process)
        {
            try
            {
                if (process.HasExited)
                {
                    return ProcessIdentityResult.Missing;
                }

                DateTimeOffset startedAtUtc = process.StartTime.ToUniversalTime();
                if ((startedAtUtc - run.ProcessStartedAtUtc.Value).Duration() > ProcessStartTolerance)
                {
                    return ProcessIdentityResult.ReusedPid;
                }

                ScheduleOccurrenceRecord? occurrence = await occurrences.GetAsync(run.OccurrenceId, cancellationToken).ConfigureAwait(false);
                AgentDefinitionRecord? definition = await agents.GetAsync(run.AgentId, cancellationToken).ConfigureAwait(false);
                if (occurrence is null || definition is null)
                {
                    return ProcessIdentityResult.AmbiguousLive;
                }
                ValidatedAgentExecutable executable = await integrity.ValidateAsync(
                    definition,
                    occurrence.CommandName,
                    cancellationToken).ConfigureAwait(false);
                string? actualPath = process.MainModule?.FileName;
                if (actualPath is null)
                {
                    return ProcessIdentityResult.AmbiguousLive;
                }

                return string.Equals(
                    Path.GetFullPath(actualPath),
                    Path.GetFullPath(executable.ExecutablePath),
                    StringComparison.OrdinalIgnoreCase)
                    ? ProcessIdentityResult.Matching
                    : ProcessIdentityResult.ReusedPid;
            }
            catch (Exception exception) when (exception is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or UnauthorizedAccessException
                or IOException)
            {
                return ProcessIdentityResult.AmbiguousLive;
            }
        }
    }

    private async ValueTask WriteAuditAsync(
        string action,
        string targetId,
        AgentRunId? runId,
        object data,
        AuditOutcome outcome,
        CancellationToken cancellationToken) =>
        _ = await auditWriter.WriteAsync(new(
                AuditActorType.Recovery,
                "runner-recovery",
                action,
                runId.HasValue ? "agent-run" : "occurrence",
                targetId,
                outcome,
                Guid.NewGuid(),
                runId,
                JsonSerializer.SerializeToElement(data)), cancellationToken)
            .ConfigureAwait(false);

    private async ValueTask WriteErrorAuditAsync(
        string targetType,
        string targetId,
        AgentRunId? runId,
        Exception exception,
        CancellationToken cancellationToken) =>
        _ = await auditWriter.WriteAsync(new(
                AuditActorType.Recovery,
                "runner-recovery",
                "recovery.decision-failed",
                targetType,
                targetId,
                AuditOutcome.Failed,
                Guid.NewGuid(),
                runId,
                JsonSerializer.SerializeToElement(new { failureType = exception.GetType().Name })), cancellationToken)
            .ConfigureAwait(false);

    private static AgentProtocolIngestionResult EmptyProtocol() => new(
        AcceptedEvents: 0,
        ProtocolViolations: 0,
        FatalProtocolIssue: false,
        FatalReasonCode: null,
        SummaryText: null,
        SummaryJson: null,
        ReportedSummaryStatus: null,
        ReportedCompletedExitCode: null,
        CompletedEventSeen: false);

    private enum ProcessIdentityResult
    {
        Missing,
        Matching,
        ReusedPid,
        AmbiguousLive,
    }
}
