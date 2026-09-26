using System.Text.Json;
using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Application.Wake;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;

namespace HomeBusinessAssistant.Application.Management;

/// <summary>Coordinates scheduler and managed wake reconciliation after local mutations.</summary>
public interface IManagementReconciliationService
{
    /// <summary>Reconciles durable occurrences and the one Windows wake task.</summary>
    ValueTask<ManagementReconciliationResult> ReconcileAsync(CancellationToken cancellationToken = default);
}

/// <summary>Safe combined reconciliation status for local feedback.</summary>
public sealed record ManagementReconciliationResult(
    bool ScheduleLeaseAcquired,
    int OccurrencesCreated,
    int ScheduleErrors,
    bool WakeLeaseAcquired,
    bool WakeChanged,
    string? WakeErrorCode);

/// <summary>Production composition of existing durable schedule and wake reconcilers.</summary>
public sealed class ManagementReconciliationService(
    IScheduleReconciler schedules,
    IWakeTaskReconciler wake) : IManagementReconciliationService
{
    /// <inheritdoc />
    public async ValueTask<ManagementReconciliationResult> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        ScheduleReconciliationSummary schedule = await schedules.ReconcileAsync(
            $"management-{Environment.ProcessId}",
            cancellationToken).ConfigureAwait(false);
        WakeTaskReconciliationResult wakeResult = await wake.ReconcileAsync(
            $"management-wake-{Environment.ProcessId}",
            cancellationToken).ConfigureAwait(false);
        return new(
            schedule.LeaseAcquired,
            schedule.OccurrencesCreated,
            schedule.Errors.Count,
            wakeResult.LeaseAcquired,
            wakeResult.Changed,
            wakeResult.Error?.Code);
    }
}

/// <summary>A validated manual run request from the local management UI.</summary>
public sealed record ManagementManualRunRequest(
    AgentId AgentId,
    string CommandName,
    string ArgumentsJson,
    ConcurrencyPolicy ConcurrencyPolicy,
    Guid? RelatedScheduleId,
    bool BypassSchedulePause,
    string ActorId,
    Guid CorrelationId);

/// <summary>Application use cases used by mutation page handlers.</summary>
public interface IManagementCommandService
{
    /// <summary>Calculates the next five due slots with the real scheduler rules.</summary>
    ValueTask<IReadOnlyList<DateTimeOffset>> PreviewScheduleAsync(AgentScheduleRecord schedule, CancellationToken cancellationToken = default);

    /// <summary>Validates, saves, audits, and reconciles one schedule.</summary>
    ValueTask<AgentScheduleRecord> SaveScheduleAsync(AgentScheduleRecord schedule, long? expectedConcurrencyToken, string actorId, Guid correlationId, CancellationToken cancellationToken = default);

    /// <summary>Enables or disables an installed agent without cancelling active work.</summary>
    ValueTask<bool> SetAgentEnabledAsync(AgentId agentId, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Enables or disables and reconciles one schedule.</summary>
    ValueTask<AgentScheduleRecord> SetScheduleEnabledAsync(Guid scheduleId, bool enabled, string actorId, Guid correlationId, CancellationToken cancellationToken = default);

    /// <summary>Pauses and reconciles one schedule.</summary>
    ValueTask<AgentScheduleRecord> PauseScheduleAsync(Guid scheduleId, DateTimeOffset? untilUtc, string actorId, Guid correlationId, CancellationToken cancellationToken = default);

    /// <summary>Resumes and reconciles one schedule.</summary>
    ValueTask<AgentScheduleRecord> ResumeScheduleAsync(Guid scheduleId, string actorId, Guid correlationId, CancellationToken cancellationToken = default);

    /// <summary>Creates durable manual work and invokes the central Runner abstraction.</summary>
    ValueTask<OccurrenceDispatchResult> RunNowAsync(ManagementManualRunRequest request, CancellationToken cancellationToken = default);

    /// <summary>Requests cancellation of the occurrence linked to an eligible run.</summary>
    ValueTask<bool> CancelRunAsync(AgentRunId runId, string actorId, Guid correlationId, CancellationToken cancellationToken = default);

    /// <summary>Creates an explicit retry occurrence using the original immutable inputs.</summary>
    ValueTask<OccurrenceDispatchResult> RetryRunAsync(AgentRunId runId, string actorId, Guid correlationId, CancellationToken cancellationToken = default);

    /// <summary>Runs schedule and wake reconciliation without another mutation.</summary>
    ValueTask<ManagementReconciliationResult> ReconcileAsync(CancellationToken cancellationToken = default);
}

/// <summary>Validated and audited local management command implementation.</summary>
public sealed class ManagementCommandService(
    IAgentDefinitionRepository agents,
    IScheduleRepository schedules,
    IAgentScheduleValidator scheduleValidator,
    IScheduleControlService scheduleControls,
    IScheduleCalculator scheduleCalculator,
    IManualRunService manualRuns,
    IOccurrenceRunnerDispatcher dispatcher,
    IOccurrenceRepository occurrences,
    IAgentRunRepository runs,
    IAuditWriter audit,
    IManagementReconciliationService reconciliation,
    TimeProvider timeProvider) : IManagementCommandService
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DateTimeOffset>> PreviewScheduleAsync(
        AgentScheduleRecord schedule,
        CancellationToken cancellationToken = default)
    {
        AgentScheduleValidationResult validation = await scheduleValidator.ValidateAsync(schedule, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            throw new AgentScheduleValidationException(validation.Errors);
        }

        ScheduleDefinitionParseResult parsed = ScheduleDefinitionJson.Parse(schedule.DefinitionJson, schedule.Kind);
        if (!parsed.IsValid)
        {
            throw new ScheduleDefinitionValidationException(parsed.Errors);
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        return scheduleCalculator.Calculate(
            parsed.Definition!,
            schedule.TimeZoneId,
            nowUtc,
            nowUtc.AddDays(366),
            5);
    }

    /// <inheritdoc />
    public async ValueTask<AgentScheduleRecord> SaveScheduleAsync(
        AgentScheduleRecord schedule,
        long? expectedConcurrencyToken,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId, correlationId);
        AgentScheduleValidationResult validation = await scheduleValidator.ValidateAsync(schedule, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            throw new AgentScheduleValidationException(validation.Errors);
        }

        bool created = await schedules.GetAsync(schedule.Id, cancellationToken).ConfigureAwait(false) is null;
        AgentScheduleRecord saved = await schedules.SaveAsync(schedule, expectedConcurrencyToken, cancellationToken).ConfigureAwait(false);
        _ = await audit.WriteAsync(new(
            AuditActorType.User,
            actorId,
            created ? "schedule.created" : "schedule.updated",
            "schedule",
            saved.Id.ToString("D"),
            AuditOutcome.Succeeded,
            correlationId,
            RunId: null,
            JsonSerializer.SerializeToElement(new
            {
                saved.AgentId,
                saved.Name,
                saved.CommandName,
                saved.Kind,
                saved.TimeZoneId,
                saved.IsEnabled,
                saved.WakePolicy,
            })), cancellationToken).ConfigureAwait(false);
        _ = await reconciliation.ReconcileAsync(cancellationToken).ConfigureAwait(false);
        return saved;
    }

    /// <inheritdoc />
    public async ValueTask<bool> SetAgentEnabledAsync(
        AgentId agentId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        bool changed = await agents.SetEnabledAsync(agentId, enabled, cancellationToken).ConfigureAwait(false);
        if (changed)
        {
            _ = await reconciliation.ReconcileAsync(cancellationToken).ConfigureAwait(false);
        }

        return changed;
    }

    /// <inheritdoc />
    public async ValueTask<AgentScheduleRecord> SetScheduleEnabledAsync(
        Guid scheduleId,
        bool enabled,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        AgentScheduleRecord result = await scheduleControls.SetEnabledAsync(
            scheduleId,
            enabled,
            actorId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        _ = await reconciliation.ReconcileAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<AgentScheduleRecord> PauseScheduleAsync(
        Guid scheduleId,
        DateTimeOffset? untilUtc,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        AgentScheduleRecord result = await scheduleControls.PauseAsync(
            scheduleId,
            untilUtc,
            actorId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        _ = await reconciliation.ReconcileAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<AgentScheduleRecord> ResumeScheduleAsync(
        Guid scheduleId,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        AgentScheduleRecord result = await scheduleControls.ResumeAsync(
            scheduleId,
            actorId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        _ = await reconciliation.ReconcileAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<OccurrenceDispatchResult> RunNowAsync(
        ManagementManualRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        OccurrenceId occurrence = await manualRuns.CreateAsync(new(
            request.AgentId,
            request.CommandName,
            request.ArgumentsJson,
            TriggerType.ManualUi,
            request.ConcurrencyPolicy,
            request.RelatedScheduleId,
            request.BypassSchedulePause,
            request.ActorId,
            request.CorrelationId), cancellationToken).ConfigureAwait(false);
        OccurrenceDispatchResult result = await dispatcher.DispatchAsync(
            occurrence,
            request.ActorId,
            request.CorrelationId,
            cancellationToken).ConfigureAwait(false);
        return result with { Created = true };
    }

    /// <inheritdoc />
    public async ValueTask<bool> CancelRunAsync(
        AgentRunId runId,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId, correlationId);
        AgentRunRecord run = await runs.GetAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The run does not exist.");
        if (run.Status is not (AgentRunStatus.Starting or AgentRunStatus.Running))
        {
            throw new InvalidOperationException("Only an active run can be cancelled.");
        }

        bool changed = await occurrences.RequestCancellationAsync(
            run.OccurrenceId,
            timeProvider.GetUtcNow().ToUniversalTime(),
            "Cancellation requested from the local management UI.",
            cancellationToken).ConfigureAwait(false);
        _ = await audit.WriteAsync(new(
            AuditActorType.User,
            actorId,
            "run.cancellation-requested",
            "run",
            run.Id.ToString(),
            changed ? AuditOutcome.Succeeded : AuditOutcome.Failed,
            correlationId,
            run.Id,
            JsonSerializer.SerializeToElement(new { occurrenceId = run.OccurrenceId.ToString(), changed })), cancellationToken).ConfigureAwait(false);
        return changed;
    }

    /// <inheritdoc />
    public async ValueTask<OccurrenceDispatchResult> RetryRunAsync(
        AgentRunId runId,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId, correlationId);
        AgentRunRecord run = await runs.GetAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The run does not exist.");
        if (run.Status is not (AgentRunStatus.Completed or AgentRunStatus.Failed or AgentRunStatus.TimedOut or AgentRunStatus.Cancelled or AgentRunStatus.Abandoned))
        {
            throw new InvalidOperationException("Only a terminal run can be retried.");
        }

        ScheduleOccurrenceRecord source = await occurrences.GetAsync(run.OccurrenceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The source occurrence does not exist.");
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        ScheduleOccurrenceRecord retry = await occurrences.CreateIfAbsentAsync(new(
            OccurrenceId.New(),
            source.ScheduleId,
            source.AgentId,
            source.CommandName,
            source.ArgumentsJson,
            source.ConfigurationRevisionId,
            nowUtc,
            TriggerType.Retry,
            source.AttemptNumber + 1,
            source.Id,
            OccurrenceStatus.Ready,
            source.RequiresWake,
            source.KeepSystemAwake,
            source.KeepDisplayOn), cancellationToken).ConfigureAwait(false);
        _ = await audit.WriteAsync(new(
            AuditActorType.User,
            actorId,
            "run.retry-created",
            "occurrence",
            retry.Id.ToString(),
            AuditOutcome.Succeeded,
            correlationId,
            run.Id,
            JsonSerializer.SerializeToElement(new
            {
                sourceRunId = run.Id.ToString(),
                sourceOccurrenceId = source.Id.ToString(),
                retryOccurrenceId = retry.Id.ToString(),
                retry.AttemptNumber,
            })), cancellationToken).ConfigureAwait(false);
        OccurrenceDispatchResult result = await dispatcher.DispatchAsync(
            retry.Id,
            actorId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        return result with { Created = true };
    }

    /// <inheritdoc />
    public ValueTask<ManagementReconciliationResult> ReconcileAsync(CancellationToken cancellationToken = default) =>
        reconciliation.ReconcileAsync(cancellationToken);

    private static void ValidateActor(string actorId, Guid correlationId)
    {
        if (string.IsNullOrWhiteSpace(actorId) || actorId.Length > 128 || correlationId == Guid.Empty)
        {
            throw new ArgumentException("A bounded actor and correlation identifier are required.");
        }
    }
}
