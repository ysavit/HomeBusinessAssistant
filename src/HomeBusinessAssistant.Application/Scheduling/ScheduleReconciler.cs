using System.Text.Json;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;

namespace HomeBusinessAssistant.Application.Scheduling;

/// <summary>A safe per-schedule reconciliation failure.</summary>
public sealed record ScheduleReconciliationError(Guid ScheduleId, string Code);

/// <summary>The bounded outcome of one reconciliation attempt.</summary>
public sealed record ScheduleReconciliationSummary(
    bool LeaseAcquired,
    int SchedulesExamined,
    int OccurrencesCreated,
    int OccurrencesMadeReady,
    int OccurrencesSkipped,
    int QueuedManualOccurrencesMadeReady,
    IReadOnlyList<ScheduleReconciliationError> Errors,
    OccurrenceId? EarliestWakeOccurrenceId,
    DateTimeOffset? EarliestWakeAtUtc);

/// <summary>Coordinates one host-independent durable scheduler reconciliation.</summary>
public interface IScheduleReconciler
{
    /// <summary>Acquires the scheduler lease and reconciles bounded due state.</summary>
    ValueTask<ScheduleReconciliationSummary> ReconcileAsync(
        string ownerId,
        CancellationToken cancellationToken = default);
}

/// <summary>Lease-fenced schedule validation, planning, policy, audit, and wake reporting.</summary>
public sealed class ScheduleReconciler(
    ILeaseManager leaseManager,
    IScheduleRepository schedules,
    IAgentDefinitionRepository agents,
    IAgentScheduleValidator validator,
    IScheduleOccurrencePlanner planner,
    IOccurrencePolicyService policyService,
    IOccurrenceRepository occurrences,
    IAuditWriter auditWriter,
    TimeProvider timeProvider,
    SchedulingOptions options) : IScheduleReconciler
{
    private const string ReconciliationLeaseName = "scheduler:reconciliation";

    /// <inheritdoc />
    public async ValueTask<ScheduleReconciliationSummary> ReconcileAsync(
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId) || ownerId.Length > 128)
        {
            throw new ArgumentException("A bounded reconciliation owner identifier is required.", nameof(ownerId));
        }

        options.Validate();
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        AgentLeaseRecord? lease = await leaseManager.TryAcquireAsync(
            ReconciliationLeaseName,
            ownerId,
            nowUtc,
            options.ReconciliationLeaseDuration,
            cancellationToken).ConfigureAwait(false);
        if (lease is null)
        {
            return new(false, 0, 0, 0, 0, 0, [], null, null);
        }

        var examined = 0;
        var created = 0;
        var madeReady = 0;
        var skipped = 0;
        var manualReady = 0;
        var errors = new List<ScheduleReconciliationError>();
        try
        {
            IReadOnlyList<AgentScheduleRecord> enabledSchedules = await schedules.GetEnabledAsync(cancellationToken).ConfigureAwait(false);
            foreach (AgentScheduleRecord schedule in enabledSchedules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                examined++;
                AgentScheduleValidationResult validation = await validator.ValidateAsync(schedule, cancellationToken).ConfigureAwait(false);
                if (!validation.IsValid)
                {
                    errors.Add(new(schedule.Id, "schedule.validation-failed"));
                    await WriteValidationAuditAsync(schedule, validation.Errors, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (ScheduleOccurrencePlanner.IsEffectivelyPaused(schedule, nowUtc))
                {
                    continue;
                }

                try
                {
                    OccurrencePlanningResult planned = await planner.PlanAsync(schedule, nowUtc, cancellationToken).ConfigureAwait(false);
                    OccurrencePolicyResult policy = await policyService.ApplyAsync(schedule, nowUtc, cancellationToken).ConfigureAwait(false);
                    created += planned.OccurrencesCreated;
                    madeReady += policy.MadeReady;
                    skipped += policy.Skipped;
                    if (planned.OccurrencesCreated + policy.MadeReady + policy.Skipped > 0)
                    {
                        await WriteChangeAuditAsync(schedule, planned, policy, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or ScheduleDefinitionValidationException)
                {
                    errors.Add(new(schedule.Id, "schedule.reconciliation-failed"));
                    await WriteFailureAuditAsync(schedule, cancellationToken).ConfigureAwait(false);
                }
            }

            manualReady = await PromoteQueuedManualOccurrencesAsync(nowUtc, cancellationToken).ConfigureAwait(false);
            ScheduleOccurrenceRecord? earliestWake = await occurrences.GetEarliestWakeAsync(nowUtc, cancellationToken).ConfigureAwait(false);
            return new(
                true,
                examined,
                created,
                madeReady,
                skipped,
                manualReady,
                errors,
                earliestWake?.Id,
                earliestWake?.DueAtUtc);
        }
        finally
        {
            _ = await leaseManager.ReleaseAsync(lease, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async ValueTask<int> PromoteQueuedManualOccurrencesAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var promoted = 0;
        foreach (AgentDefinitionRecord agent in await agents.GetEnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            if ((await occurrences.GetActiveAsync(agent.Id, scheduleId: null, 1, cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                continue;
            }

            ScheduleOccurrenceRecord? queued = (await occurrences.GetPendingAsync(
                agent.Id,
                scheduleId: null,
                1,
                cancellationToken).ConfigureAwait(false))
                .SingleOrDefault(item => item.Status == OccurrenceStatus.Planned && item.DueAtUtc <= nowUtc);
            if (queued is not null && await occurrences.TryTransitionAsync(new(
                queued.Id,
                [OccurrenceStatus.Planned],
                OccurrenceStatus.Ready,
                nowUtc,
                "concurrency.queue-one-released",
                Message: null), cancellationToken).ConfigureAwait(false) is not null)
            {
                promoted++;
            }
        }

        return promoted;
    }

    private async ValueTask WriteValidationAuditAsync(
        AgentScheduleRecord schedule,
        IReadOnlyList<ScheduleValidationError> validationErrors,
        CancellationToken cancellationToken) =>
        _ = await auditWriter.WriteAsync(new(
            AuditActorType.System,
            "scheduler",
            "schedule.validation-failed",
            "schedule",
            schedule.Id.ToString("D"),
            AuditOutcome.Failed,
            Guid.NewGuid(),
            RunId: null,
            JsonSerializer.SerializeToElement(new
            {
                errors = validationErrors.Take(20).Select(item => new { item.Code, item.Path }).ToArray(),
            })), cancellationToken).ConfigureAwait(false);

    private async ValueTask WriteChangeAuditAsync(
        AgentScheduleRecord schedule,
        OccurrencePlanningResult planning,
        OccurrencePolicyResult policy,
        CancellationToken cancellationToken) =>
        _ = await auditWriter.WriteAsync(new(
            AuditActorType.System,
            "scheduler",
            "schedule.reconciled",
            "schedule",
            schedule.Id.ToString("D"),
            AuditOutcome.Succeeded,
            Guid.NewGuid(),
            RunId: null,
            JsonSerializer.SerializeToElement(new
            {
                planning.OccurrencesCreated,
                policy.MadeReady,
                policy.Skipped,
                policy.Coalesced,
            })), cancellationToken).ConfigureAwait(false);

    private async ValueTask WriteFailureAuditAsync(
        AgentScheduleRecord schedule,
        CancellationToken cancellationToken) =>
        _ = await auditWriter.WriteAsync(new(
            AuditActorType.System,
            "scheduler",
            "schedule.reconciliation-failed",
            "schedule",
            schedule.Id.ToString("D"),
            AuditOutcome.Failed,
            Guid.NewGuid(),
            RunId: null,
            JsonSerializer.SerializeToElement(new { code = "schedule.reconciliation-failed" })), cancellationToken).ConfigureAwait(false);
}
