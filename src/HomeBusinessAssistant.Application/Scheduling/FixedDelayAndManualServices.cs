using System.Text.Json;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;

namespace HomeBusinessAssistant.Application.Scheduling;

/// <summary>The result of applying fixed-delay cadence after a terminal occurrence.</summary>
public sealed record FixedDelayCompletionResult(
    bool Applicable,
    bool Created,
    OccurrenceId? NextOccurrenceId,
    DateTimeOffset? NextDueAtUtc);

/// <summary>Creates the one normal fixed-delay successor after terminal completion.</summary>
public interface IFixedDelayCompletionService
{
    /// <summary>Plans the next due occurrence from the persisted terminal completion timestamp.</summary>
    ValueTask<FixedDelayCompletionResult> OnOccurrenceTerminalAsync(
        OccurrenceId occurrenceId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>Idempotent completion hook for completion-based cadence.</summary>
public sealed class FixedDelayCompletionService(
    IOccurrenceRepository occurrences,
    IScheduleRepository schedules,
    IAgentConfigurationService configurations,
    IAuditWriter auditWriter) : IFixedDelayCompletionService
{
    /// <inheritdoc />
    public async ValueTask<FixedDelayCompletionResult> OnOccurrenceTerminalAsync(
        OccurrenceId occurrenceId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ScheduleOccurrenceRecord occurrence = await occurrences.GetAsync(occurrenceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The completed occurrence does not exist.");
        if (!OccurrenceStateMachine.IsTerminal(occurrence.Status))
        {
            throw new InvalidOperationException("The fixed-delay hook requires a terminal occurrence.");
        }

        if (!occurrence.ScheduleId.HasValue || occurrence.TriggerType == TriggerType.Retry)
        {
            return new(false, false, null, null);
        }

        AgentScheduleRecord schedule = await schedules.GetAsync(occurrence.ScheduleId.Value, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The occurrence schedule does not exist.");
        ScheduleDefinitionParseResult parsed = ScheduleDefinitionJson.Parse(schedule.DefinitionJson, schedule.Kind);
        if (parsed.Definition is not FixedDelayScheduleDefinition definition)
        {
            return new(false, false, null, null);
        }

        if (!schedule.IsEnabled || (schedule.IsPaused && !schedule.PausedUntilUtc.HasValue))
        {
            return new(true, false, null, null);
        }

        Guid revisionId = await ResolveRevisionIdAsync(schedule, cancellationToken).ConfigureAwait(false);
        DateTimeOffset completion = occurrence.CompletedAtUtc?.ToUniversalTime() ?? completedAtUtc.ToUniversalTime();
        DateTimeOffset due = completion.Add(definition.Delay);
        OccurrenceId proposedId = OccurrenceId.New();
        ScheduleOccurrenceRecord persisted = await occurrences.CreateIfAbsentAsync(new(
            proposedId,
            schedule.Id,
            schedule.AgentId,
            schedule.CommandName,
            schedule.ArgumentsJson,
            revisionId,
            due,
            schedule.WakePolicy == WakePolicy.Never ? TriggerType.Schedule : TriggerType.WakeSchedule,
            AttemptNumber: 0,
            ParentOccurrenceId: null,
            InitialStatus: OccurrenceStatus.Planned,
            RequiresWake: schedule.WakePolicy != WakePolicy.Never,
            KeepSystemAwake: schedule.WakePolicy != WakePolicy.Never,
            KeepDisplayOn: false), cancellationToken).ConfigureAwait(false);
        bool created = persisted.Id == proposedId;
        if (created)
        {
            _ = await auditWriter.WriteAsync(new(
                AuditActorType.System,
                "scheduler",
                "schedule.fixed-delay-planned",
                "occurrence",
                persisted.Id.ToString(),
                AuditOutcome.Succeeded,
                Guid.NewGuid(),
                RunId: null,
                JsonSerializer.SerializeToElement(new
                {
                    scheduleId = schedule.Id,
                    predecessorOccurrenceId = occurrence.Id.ToString(),
                    dueAtUtc = due,
                    delaySeconds = definition.Delay.TotalSeconds,
                })), cancellationToken).ConfigureAwait(false);
        }

        return new(true, created, persisted.Id, persisted.DueAtUtc);
    }

    private async ValueTask<Guid> ResolveRevisionIdAsync(AgentScheduleRecord schedule, CancellationToken cancellationToken)
    {
        if (schedule.PinnedConfigurationRevisionId.HasValue)
        {
            ConfigurationRevisionRecord revision = await configurations.GetRevisionAsync(
                schedule.PinnedConfigurationRevisionId.Value,
                cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The pinned configuration revision no longer exists.");
            if (revision.AgentId != schedule.AgentId)
            {
                throw new InvalidOperationException("The pinned configuration revision belongs to another agent.");
            }

            return revision.Id;
        }

        return (await configurations.GetCurrentAsync(schedule.AgentId, cancellationToken).ConfigureAwait(false))?.CurrentRevisionId
            ?? throw new InvalidOperationException("The agent has no current configuration revision.");
    }
}

/// <summary>An explicit request for a durable manual occurrence.</summary>
public sealed record ManualRunRequest(
    AgentId AgentId,
    string CommandName,
    string ArgumentsJson,
    TriggerType TriggerType,
    ConcurrencyPolicy ConcurrencyPolicy,
    Guid? RelatedScheduleId,
    bool BypassSchedulePause,
    string ActorId,
    Guid CorrelationId);

/// <summary>Validates and creates manual occurrences without starting processes.</summary>
public interface IManualRunService
{
    /// <summary>Creates or returns the one allowed durable manual occurrence.</summary>
    ValueTask<OccurrenceId> CreateAsync(
        ManualRunRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Application manual-run use case with explicit trigger, pause, and concurrency semantics.</summary>
public sealed class ManualRunService(
    IAgentDefinitionRepository agents,
    IAgentConfigurationService configurations,
    IScheduleRepository schedules,
    IOccurrenceRepository occurrences,
    IAuditWriter auditWriter,
    TimeProvider timeProvider) : IManualRunService
{
    /// <inheritdoc />
    public async ValueTask<OccurrenceId> CreateAsync(
        ManualRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TriggerType is not (TriggerType.ManualUi or TriggerType.TrayMenu or TriggerType.CommandLine)
            || !Enum.IsDefined(request.ConcurrencyPolicy)
            || string.IsNullOrWhiteSpace(request.ActorId)
            || request.ActorId.Length > 128
            || request.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("The manual-run request is invalid.", nameof(request));
        }

        AgentDefinitionRecord agent = await agents.GetAsync(request.AgentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The selected agent is not installed.");
        if (!agent.Enabled || !agent.SupportsManualRun || !AgentCommandPolicy.SupportsCommand(agent, request.CommandName))
        {
            throw new InvalidOperationException("The selected agent or command is not available for manual execution.");
        }

        ValidateArguments(request.ArgumentsJson);
        if (request.RelatedScheduleId.HasValue)
        {
            AgentScheduleRecord related = await schedules.GetAsync(request.RelatedScheduleId.Value, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The related schedule does not exist.");
            if (related.AgentId != request.AgentId)
            {
                throw new InvalidOperationException("The related schedule belongs to another agent.");
            }

            bool paused = ScheduleOccurrencePlanner.IsEffectivelyPaused(related, timeProvider.GetUtcNow().ToUniversalTime());
            if (paused && !request.BypassSchedulePause)
            {
                throw new InvalidOperationException("The related schedule is paused; an explicit bypass is required.");
            }
        }

        AgentConfigurationRecord configuration = await configurations.GetCurrentAsync(request.AgentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The agent has no current configuration revision.");
        bool active = (await occurrences.GetActiveAsync(request.AgentId, scheduleId: null, 1, cancellationToken).ConfigureAwait(false)).Count > 0;
        if (request.ConcurrencyPolicy == ConcurrencyPolicy.Forbid && active)
        {
            throw new InvalidOperationException("The agent already has active work and its concurrency policy forbids another occurrence.");
        }

        IReadOnlyList<ScheduleOccurrenceRecord> existingPending = request.ConcurrencyPolicy == ConcurrencyPolicy.QueueOne
            ? await occurrences.GetPendingAsync(request.AgentId, scheduleId: null, 1, cancellationToken).ConfigureAwait(false)
            : [];
        if (existingPending.Count > 0)
        {
            return existingPending[0].Id;
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        OccurrenceStatus initialStatus = active && request.ConcurrencyPolicy == ConcurrencyPolicy.QueueOne
            ? OccurrenceStatus.Planned
            : OccurrenceStatus.Ready;
        ScheduleOccurrenceRecord occurrence = await occurrences.CreateIfAbsentAsync(new(
            OccurrenceId.New(),
            ScheduleId: null,
            request.AgentId,
            request.CommandName,
            request.ArgumentsJson,
            configuration.CurrentRevisionId,
            nowUtc,
            request.TriggerType,
            AttemptNumber: 0,
            ParentOccurrenceId: null,
            initialStatus), cancellationToken).ConfigureAwait(false);
        _ = await auditWriter.WriteAsync(new(
            AuditActorType.User,
            request.ActorId,
            "occurrence.manual-created",
            "occurrence",
            occurrence.Id.ToString(),
            AuditOutcome.Succeeded,
            request.CorrelationId,
            RunId: null,
            JsonSerializer.SerializeToElement(new
            {
                agentId = request.AgentId.Value,
                request.TriggerType,
                request.ConcurrencyPolicy,
                queued = initialStatus == OccurrenceStatus.Planned,
                request.RelatedScheduleId,
                request.BypassSchedulePause,
                configurationRevisionId = configuration.CurrentRevisionId,
            })), cancellationToken).ConfigureAwait(false);
        return occurrence.Id;
    }

    private static void ValidateArguments(string argumentsJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(argumentsJson, new JsonDocumentOptions { MaxDepth = 64 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Manual-run arguments must be a JSON object.", nameof(argumentsJson));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Manual-run arguments contain invalid JSON.", nameof(argumentsJson), exception);
        }
    }
}
