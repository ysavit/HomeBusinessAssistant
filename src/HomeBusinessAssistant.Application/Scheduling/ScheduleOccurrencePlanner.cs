using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Application.Scheduling;

/// <summary>The bounded outcome of planning one schedule.</summary>
public sealed record OccurrencePlanningResult(
    int OccurrencesCreated,
    IReadOnlyList<OccurrenceId> CreatedOccurrenceIds);

/// <summary>Calculates and durably creates missing scheduled occurrences.</summary>
public interface IScheduleOccurrencePlanner
{
    /// <summary>Plans one valid schedule at a controlled UTC instant.</summary>
    ValueTask<OccurrencePlanningResult> PlanAsync(
        AgentScheduleRecord schedule,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>Builds immutable per-occurrence arguments from a schedule template and due time.</summary>
public interface IScheduleOccurrenceArgumentsProvider
{
    /// <summary>Returns the arguments captured on one durable occurrence.</summary>
    string CreateArguments(AgentScheduleRecord schedule, DateTimeOffset dueAtUtc);
}

/// <summary>Copies schedule arguments unchanged for agents without occurrence-specific payloads.</summary>
public sealed class PassthroughScheduleOccurrenceArgumentsProvider : IScheduleOccurrenceArgumentsProvider
{
    /// <inheritdoc />
    public string CreateArguments(AgentScheduleRecord schedule, DateTimeOffset dueAtUtc)
    {
        _ = dueAtUtc;
        return schedule.ArgumentsJson;
    }
}

/// <summary>Bounded idempotent occurrence planner backed by focused persistence ports.</summary>
public sealed class ScheduleOccurrencePlanner(
    IScheduleCalculator calculator,
    IOccurrenceRepository occurrences,
    IAgentConfigurationService configurations,
    SchedulingOptions options,
    IScheduleOccurrenceArgumentsProvider? occurrenceArguments = null) : IScheduleOccurrencePlanner
{
    private readonly IScheduleOccurrenceArgumentsProvider argumentsProvider =
        occurrenceArguments ?? new PassthroughScheduleOccurrenceArgumentsProvider();

    /// <inheritdoc />
    public async ValueTask<OccurrencePlanningResult> PlanAsync(
        AgentScheduleRecord schedule,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        options.Validate();
        DateTimeOffset now = nowUtc.ToUniversalTime();
        if (!schedule.IsEnabled || IsEffectivelyPaused(schedule, now) || schedule.Kind == ScheduleKind.Manual)
        {
            return new(0, []);
        }

        ScheduleDefinitionParseResult parsed = ScheduleDefinitionJson.Parse(schedule.DefinitionJson, schedule.Kind);
        if (!parsed.IsValid)
        {
            throw new ScheduleDefinitionValidationException(parsed.Errors);
        }

        Guid revisionId = await ResolveRevisionIdAsync(schedule, cancellationToken).ConfigureAwait(false);
        ScheduleOccurrenceRecord? latest = await occurrences.GetLatestForScheduleAsync(schedule.Id, cancellationToken).ConfigureAwait(false);
        if (parsed.Definition is FixedDelayScheduleDefinition fixedDelay)
        {
            return await PlanFixedDelayInitialAsync(schedule, fixedDelay, latest, revisionId, now, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<ScheduleOccurrenceRecord> pending = schedule.ConcurrencyPolicy == ConcurrencyPolicy.QueueOne
            ? await occurrences.GetPendingAsync(schedule.AgentId, schedule.Id, 2, cancellationToken).ConfigureAwait(false)
            : [];
        if (pending.Count > 0)
        {
            return new(0, []);
        }

        DateTimeOffset rangeStart = latest is null
            ? InitialRangeStart(schedule, parsed.Definition!, now)
            : latest.DueAtUtc.AddTicks(1);
        DateTimeOffset rangeEnd = now.Add(options.CalendarPlanningHorizon);
        IReadOnlyList<DateTimeOffset> candidates = calculator.Calculate(
            parsed.Definition!,
            schedule.TimeZoneId,
            rangeStart,
            rangeEnd,
            options.MaximumOccurrencesPerSchedule);
        if (schedule.ConcurrencyPolicy == ConcurrencyPolicy.QueueOne)
        {
            candidates = candidates.Take(1).ToArray();
        }

        return await CreateAsync(schedule, revisionId, candidates, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<OccurrencePlanningResult> PlanFixedDelayInitialAsync(
        AgentScheduleRecord schedule,
        FixedDelayScheduleDefinition definition,
        ScheduleOccurrenceRecord? latest,
        Guid revisionId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (latest is not null)
        {
            return new(0, []);
        }

        DateTimeOffset due = definition.StartImmediately
            ? nowUtc
            : definition.InitialDueAtUtc!.Value.ToUniversalTime();
        if (due > nowUtc.Add(options.CalendarPlanningHorizon))
        {
            return new(0, []);
        }

        return await CreateAsync(schedule, revisionId, [due], cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<OccurrencePlanningResult> CreateAsync(
        AgentScheduleRecord schedule,
        Guid revisionId,
        IReadOnlyList<DateTimeOffset> dueTimes,
        CancellationToken cancellationToken)
    {
        var created = new List<OccurrenceId>();
        foreach (DateTimeOffset due in dueTimes.Take(options.MaximumOccurrencesPerSchedule))
        {
            OccurrenceId proposedId = OccurrenceId.New();
            ScheduleOccurrenceRecord persisted = await occurrences.CreateIfAbsentAsync(new(
                proposedId,
                schedule.Id,
                schedule.AgentId,
                schedule.CommandName,
                argumentsProvider.CreateArguments(schedule, due),
                revisionId,
                due,
                schedule.WakePolicy == WakePolicy.Never ? TriggerType.Schedule : TriggerType.WakeSchedule,
                AttemptNumber: 0,
                ParentOccurrenceId: null,
                InitialStatus: OccurrenceStatus.Planned,
                RequiresWake: schedule.WakePolicy != WakePolicy.Never,
                KeepSystemAwake: schedule.WakePolicy != WakePolicy.Never,
                KeepDisplayOn: false), cancellationToken).ConfigureAwait(false);
            if (persisted.Id == proposedId)
            {
                created.Add(proposedId);
            }
        }

        return new(created.Count, created);
    }

    private async ValueTask<Guid> ResolveRevisionIdAsync(
        AgentScheduleRecord schedule,
        CancellationToken cancellationToken)
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

        AgentConfigurationRecord current = await configurations.GetCurrentAsync(schedule.AgentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The agent has no current configuration revision.");
        return current.CurrentRevisionId;
    }

    private DateTimeOffset InitialRangeStart(
        AgentScheduleRecord schedule,
        ScheduleDefinition definition,
        DateTimeOffset nowUtc) =>
        definition is OneTimeScheduleDefinition
            ? DateTimeOffset.MinValue
            : new[] { schedule.CreatedAtUtc.ToUniversalTime(), nowUtc.Subtract(options.MisfireLookback) }.Max();

    /// <summary>Returns whether an indefinite or not-yet-expired timed pause is active.</summary>
    public static bool IsEffectivelyPaused(AgentScheduleRecord schedule, DateTimeOffset nowUtc) =>
        schedule.IsPaused && (!schedule.PausedUntilUtc.HasValue || schedule.PausedUntilUtc > nowUtc);
}

/// <summary>The persisted result of applying misfire and concurrency decisions.</summary>
public sealed record OccurrencePolicyResult(int MadeReady, int Skipped, int Coalesced);

/// <summary>Applies schedule-level misfire and concurrency rules without launching work.</summary>
public interface IOccurrencePolicyService
{
    /// <summary>Applies policy to bounded pending occurrences for one schedule.</summary>
    ValueTask<OccurrencePolicyResult> ApplyAsync(
        AgentScheduleRecord schedule,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>Deterministic occurrence-policy engine using atomic legal transitions.</summary>
public sealed class OccurrencePolicyService(
    IOccurrenceRepository occurrences,
    SchedulingOptions options) : IOccurrencePolicyService
{
    /// <inheritdoc />
    public async ValueTask<OccurrencePolicyResult> ApplyAsync(
        AgentScheduleRecord schedule,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        DateTimeOffset now = nowUtc.ToUniversalTime();
        if (!schedule.IsEnabled || ScheduleOccurrencePlanner.IsEffectivelyPaused(schedule, now))
        {
            return new(0, 0, 0);
        }

        IReadOnlyList<ScheduleOccurrenceRecord> all = await occurrences.GetForScheduleAsync(
            schedule.Id,
            dueFromUtc: now.Subtract(options.MisfireLookback),
            dueThroughUtc: now,
            maximumResults: Math.Min(1_000, options.MaximumOccurrencesPerSchedule * 2),
            cancellationToken).ConfigureAwait(false);
        List<ScheduleOccurrenceRecord> due = all
            .Where(item => item.Status is OccurrenceStatus.Planned or OccurrenceStatus.Ready)
            .OrderBy(item => item.DueAtUtc)
            .ThenBy(item => item.Id.Value)
            .ToList();
        if (due.Count == 0)
        {
            return new(0, 0, 0);
        }

        bool active = (await occurrences.GetActiveAsync(schedule.AgentId, schedule.Id, 1, cancellationToken).ConfigureAwait(false)).Count > 0;
        DateTimeOffset misfireBefore = now.Subtract(schedule.MisfireGracePeriod);
        List<ScheduleOccurrenceRecord> missed = due.Where(item => item.DueAtUtc < misfireBefore).ToList();
        var madeReady = 0;
        var skipped = 0;
        var coalesced = 0;

        if (missed.Count > 0)
        {
            if (schedule.MisfirePolicy == MisfirePolicy.RunImmediately)
            {
                ScheduleOccurrenceRecord retained = missed[^1];
                foreach (ScheduleOccurrenceRecord occurrence in missed.Take(missed.Count - 1))
                {
                    if (await SkipAsync(occurrence, now, "misfire.coalesced", "Older missed occurrence coalesced into the latest missed slot.", cancellationToken).ConfigureAwait(false))
                    {
                        skipped++;
                        coalesced++;
                    }
                }

                if (!active || schedule.ConcurrencyPolicy == ConcurrencyPolicy.AllowParallel)
                {
                    if (await MakeReadyAsync(retained, now, "misfire.run-immediately", cancellationToken).ConfigureAwait(false))
                    {
                        madeReady++;
                    }
                }
            }
            else
            {
                string reason = schedule.MisfirePolicy == MisfirePolicy.Skip
                    ? "misfire.skip"
                    : "misfire.run-next-scheduled";
                foreach (ScheduleOccurrenceRecord occurrence in missed)
                {
                    if (await SkipAsync(occurrence, now, reason, "The occurrence exceeded its misfire grace period.", cancellationToken).ConfigureAwait(false))
                    {
                        skipped++;
                    }
                }
            }
        }

        List<ScheduleOccurrenceRecord> withinGrace = due.Except(missed).ToList();
        bool canRun = !active || schedule.ConcurrencyPolicy == ConcurrencyPolicy.AllowParallel;
        if (canRun)
        {
            IEnumerable<ScheduleOccurrenceRecord> runnable = schedule.ConcurrencyPolicy == ConcurrencyPolicy.QueueOne
                ? withinGrace.Take(1)
                : withinGrace;
            foreach (ScheduleOccurrenceRecord occurrence in runnable)
            {
                if (await MakeReadyAsync(occurrence, now, "schedule.due", cancellationToken).ConfigureAwait(false))
                {
                    madeReady++;
                }
            }
        }

        return new(madeReady, skipped, coalesced);
    }

    private async ValueTask<bool> MakeReadyAsync(
        ScheduleOccurrenceRecord occurrence,
        DateTimeOffset nowUtc,
        string reason,
        CancellationToken cancellationToken)
    {
        if (occurrence.Status == OccurrenceStatus.Ready)
        {
            return false;
        }

        return await occurrences.TryTransitionAsync(new(
            occurrence.Id,
            [OccurrenceStatus.Planned],
            OccurrenceStatus.Ready,
            nowUtc,
            reason,
            Message: null), cancellationToken).ConfigureAwait(false) is not null;
    }

    private async ValueTask<bool> SkipAsync(
        ScheduleOccurrenceRecord occurrence,
        DateTimeOffset nowUtc,
        string reason,
        string message,
        CancellationToken cancellationToken) =>
        await occurrences.TryTransitionAsync(new(
            occurrence.Id,
            [occurrence.Status],
            OccurrenceStatus.Skipped,
            nowUtc,
            reason,
            message), cancellationToken).ConfigureAwait(false) is not null;
}
