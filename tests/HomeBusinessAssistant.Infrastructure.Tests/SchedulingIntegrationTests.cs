using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Execution;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Execution;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;

namespace HomeBusinessAssistant.Infrastructure.Tests;

internal sealed class SchedulingIntegrationTests
{
    private const string CentralTimeZoneId = "Central Standard Time";
    private static readonly AgentId FounderScoutId = AgentId.Parse("founder-scout");
    private static readonly AgentId WakeRemoteId = AgentId.Parse("wake-remote");

    [Test]
    public async Task ValidatorChecksManifestTimeZoneNameDisabledAgentAndConfigurationOwnership()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        ConfigurationRevisionRecord founderRevision = await CreateConfigurationAsync(temporary, FounderScoutId);
        ConfigurationRevisionRecord wakeRevision = await CreateConfigurationAsync(temporary, WakeRemoteId);
        SchedulingComponents components = CreateComponents(temporary);
        AgentScheduleRecord valid = CreateSchedule(
            temporary,
            FounderScoutId,
            "Valid daily",
            new DailyScheduleDefinition(new TimeOnly(9, 0)),
            pinnedRevisionId: founderRevision.Id);

        AgentScheduleValidationResult validResult = await components.Validator.ValidateAsync(valid);
        _ = await components.Schedules.SaveAsync(valid, expectedConcurrencyToken: null);
        AgentScheduleValidationResult duplicate = await components.Validator.ValidateAsync(valid with
        {
            Id = Guid.NewGuid(),
            PinnedConfigurationRevisionId = wakeRevision.Id,
            CommandName = "unsupported",
            TimeZoneId = "Missing/Test Zone",
        });
        var definitions = new AgentDefinitionRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        Assert.That(await definitions.SetEnabledAsync(FounderScoutId, enabled: false), Is.True);
        AgentScheduleValidationResult disabled = await components.Validator.ValidateAsync(valid with
        {
            Id = Guid.NewGuid(),
            Name = "Disabled agent",
            PinnedConfigurationRevisionId = founderRevision.Id,
        });
        AgentScheduleValidationResult explicitlyAllowed = await components.Validator.ValidateAsync(valid with
        {
            Id = Guid.NewGuid(),
            Name = "Explicit disabled policy",
            AllowDisabledAgent = true,
        });

        Assert.Multiple(() =>
        {
            Assert.That(validResult.IsValid, Is.True);
            Assert.That(duplicate.Errors.Select(item => item.Code), Does.Contain("schedule.nameCollision"));
            Assert.That(duplicate.Errors.Select(item => item.Code), Does.Contain("schedule.commandUnsupported"));
            Assert.That(duplicate.Errors.Select(item => item.Code), Does.Contain("schedule.timeZoneUnavailable"));
            Assert.That(duplicate.Errors.Select(item => item.Code), Does.Contain("schedule.configurationAgentMismatch"));
            Assert.That(disabled.Errors.Select(item => item.Code), Does.Contain("schedule.agentDisabled"));
            Assert.That(explicitlyAllowed.Errors.Select(item => item.Code), Does.Not.Contain("schedule.agentDisabled"));
        });
    }

    [Test]
    public async Task ReconciliationIsIdempotentReportsWakeAndFixedDelayUsesCompletionPlusCooldown()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        ConfigurationRevisionRecord founderRevision = await CreateConfigurationAsync(temporary, FounderScoutId);
        _ = await CreateConfigurationAsync(temporary, WakeRemoteId);
        SchedulingComponents components = CreateComponents(temporary);
        AgentScheduleRecord founderSchedule = await SaveValidScheduleAsync(
            components,
            CreateSchedule(
                temporary,
                FounderScoutId,
                "Discovery cooldown",
                new FixedDelayScheduleDefinition(TimeSpan.FromMinutes(10), InitialDueAtUtc: null, StartImmediately: true),
                wakePolicy: WakePolicy.Never));
        AgentScheduleRecord wakeSchedule = await SaveValidScheduleAsync(
            components,
            CreateSchedule(
                temporary,
                WakeRemoteId,
                "Sunday availability",
                new WeekdayScheduleDefinition([DayOfWeek.Sunday], new TimeOnly(13, 0)),
                wakePolicy: WakePolicy.Required));

        ScheduleReconciliationSummary first = await components.Reconciler.ReconcileAsync("integration-reconciler");
        ScheduleReconciliationSummary second = await components.Reconciler.ReconcileAsync("integration-reconciler");
        IReadOnlyList<ScheduleOccurrenceRecord> founderOccurrences = await components.Occurrences.GetForScheduleAsync(
            founderSchedule.Id, null, null, 10);
        IReadOnlyList<ScheduleOccurrenceRecord> wakeOccurrences = await components.Occurrences.GetForScheduleAsync(
            wakeSchedule.Id, null, null, 10);
        ScheduleOccurrenceRecord founderInitial = founderOccurrences.Single();
        DateTimeOffset completedAt = temporary.TimeProvider.GetUtcNow().AddMinutes(2);
        await MoveToRunningAsync(components.Occurrences, founderInitial.Id, temporary.TimeProvider.GetUtcNow());
        ScheduleOccurrenceRecord completed = await TransitionAsync(
            components.Occurrences,
            founderInitial.Id,
            OccurrenceStatus.Running,
            OccurrenceStatus.Completed,
            completedAt,
            "run.completed");
        ConfigurationRevisionRecord promotedRevision = await CreateConfigurationAsync(
            temporary,
            FounderScoutId,
            new { schemaVersion = "1.0", batchSize = 25 });
        FixedDelayCompletionResult completion = await components.FixedDelay.OnOccurrenceTerminalAsync(completed.Id, completedAt);
        FixedDelayCompletionResult repeated = await components.FixedDelay.OnOccurrenceTerminalAsync(completed.Id, completedAt);
        ScheduleOccurrenceRecord successor = await components.Occurrences.GetAsync(completion.NextOccurrenceId!.Value)
            ?? throw new InvalidOperationException("Fixed-delay successor was not persisted.");

        Assert.Multiple(() =>
        {
            Assert.That(first.LeaseAcquired, Is.True);
            Assert.That(first.SchedulesExamined, Is.EqualTo(2));
            Assert.That(first.OccurrencesCreated, Is.EqualTo(2));
            Assert.That(first.OccurrencesMadeReady, Is.EqualTo(1));
            Assert.That(first.EarliestWakeOccurrenceId, Is.EqualTo(wakeOccurrences.Single().Id));
            Assert.That(first.EarliestWakeAtUtc, Is.EqualTo(new DateTimeOffset(2026, 8, 30, 18, 0, 0, TimeSpan.Zero)));
            Assert.That(second.OccurrencesCreated, Is.Zero);
            Assert.That(completion.Created, Is.True);
            Assert.That(repeated.Created, Is.False);
            Assert.That(successor.DueAtUtc, Is.EqualTo(completedAt.AddMinutes(10)));
            Assert.That(successor.ConfigurationRevisionId, Is.EqualTo(promotedRevision.Id));
            Assert.That(successor.ConfigurationRevisionId, Is.Not.EqualTo(founderRevision.Id));
            Assert.That(successor.Status, Is.EqualTo(OccurrenceStatus.Planned));
        });
    }

    [Test]
    public async Task PlannerLimitUniqueIdentityAndLeaseContentionAreDurable()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        _ = await CreateConfigurationAsync(temporary, FounderScoutId);
        SchedulingOptions options = SchedulingOptions.Default with { MaximumOccurrencesPerSchedule = 3 };
        SchedulingComponents components = CreateComponents(temporary, options);
        AgentScheduleRecord schedule = await SaveValidScheduleAsync(
            components,
            CreateSchedule(
                temporary,
                FounderScoutId,
                "Frequent interval",
                new FixedIntervalScheduleDefinition(TimeSpan.FromMinutes(1), temporary.TimeProvider.GetUtcNow().AddMinutes(-5)),
                concurrencyPolicy: ConcurrencyPolicy.AllowParallel));
        AgentScheduleRecord queueOne = await SaveValidScheduleAsync(
            components,
            CreateSchedule(
                temporary,
                FounderScoutId,
                "Queue one interval",
                new FixedIntervalScheduleDefinition(TimeSpan.FromMinutes(1), temporary.TimeProvider.GetUtcNow()),
                concurrencyPolicy: ConcurrencyPolicy.QueueOne));

        OccurrencePlanningResult planning = await components.Planner.PlanAsync(schedule, temporary.TimeProvider.GetUtcNow());
        DateTimeOffset racedDue = temporary.TimeProvider.GetUtcNow().AddDays(30);
        Guid revisionId = (await components.Occurrences.GetForScheduleAsync(schedule.Id, null, null, 10))
            .Select(item => item.ConfigurationRevisionId)
            .Distinct()
            .Single();
        Task<ScheduleOccurrenceRecord>[] racers = Enumerable.Range(0, 6)
            .Select(_ => components.Occurrences.CreateIfAbsentAsync(new(
                OccurrenceId.New(),
                schedule.Id,
                schedule.AgentId,
                schedule.CommandName,
                schedule.ArgumentsJson,
                revisionId,
                racedDue,
                TriggerType.Schedule,
                AttemptNumber: 0,
                ParentOccurrenceId: null,
                InitialStatus: OccurrenceStatus.Planned)).AsTask())
            .ToArray();
        ScheduleOccurrenceRecord[] raced = await Task.WhenAll(racers);
        OccurrencePlanningResult queueOnePlanning = await components.Planner.PlanAsync(queueOne, temporary.TimeProvider.GetUtcNow());
        IReadOnlyList<ScheduleOccurrenceRecord> persisted = await components.Occurrences.GetForScheduleAsync(schedule.Id, null, null, 100);
        AgentLeaseRecord lease = await components.Leases.TryAcquireAsync(
            "scheduler:reconciliation",
            "holder",
            temporary.TimeProvider.GetUtcNow(),
            TimeSpan.FromSeconds(30)) ?? throw new InvalidOperationException("Test lease was not acquired.");
        ScheduleReconciliationSummary contended = await components.Reconciler.ReconcileAsync("contender");
        _ = await components.Leases.ReleaseAsync(lease);

        Assert.Multiple(() =>
        {
            Assert.That(planning.OccurrencesCreated, Is.EqualTo(3));
            Assert.That(raced.Select(item => item.Id).Distinct().ToArray(), Has.Length.EqualTo(1));
            Assert.That(persisted, Has.Count.EqualTo(4));
            Assert.That(persisted.Select(item => (item.ScheduleId, item.DueAtUtc)).Distinct().ToArray(), Has.Length.EqualTo(4));
            Assert.That(persisted.Select(item => item.ConfigurationRevisionId).Distinct().ToArray(), Has.Length.EqualTo(1));
            Assert.That(queueOnePlanning.OccurrencesCreated, Is.EqualTo(1));
            Assert.That(contended.LeaseAcquired, Is.False);
        });
    }

    [Test]
    public async Task MisfireGraceAndConcurrencyPoliciesPersistExplicitDecisions()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        ConfigurationRevisionRecord revision = await CreateConfigurationAsync(temporary, FounderScoutId);
        SchedulingComponents components = CreateComponents(temporary);
        DateTimeOffset now = temporary.TimeProvider.GetUtcNow();

        AgentScheduleRecord immediate = await SaveValidScheduleAsync(components, CreateSchedule(
            temporary,
            FounderScoutId,
            "Immediate misfire",
            new FixedIntervalScheduleDefinition(TimeSpan.FromMinutes(30), now.AddHours(-2)),
            misfirePolicy: MisfirePolicy.RunImmediately));
        ScheduleOccurrenceRecord older = await CreateOccurrenceAsync(components.Occurrences, immediate, revision.Id, now.AddHours(-2));
        ScheduleOccurrenceRecord newest = await CreateOccurrenceAsync(components.Occurrences, immediate, revision.Id, now.AddHours(-1));
        OccurrencePolicyResult immediateResult = await components.Policy.ApplyAsync(immediate, now);

        AgentScheduleRecord skip = await SaveValidScheduleAsync(components, CreateSchedule(
            temporary,
            FounderScoutId,
            "Skip misfire",
            new OneTimeScheduleDefinition(ScheduleTimeSemantics.Utc, default, now.AddHours(-1)),
            misfirePolicy: MisfirePolicy.Skip));
        ScheduleOccurrenceRecord skippedOccurrence = await CreateOccurrenceAsync(components.Occurrences, skip, revision.Id, now.AddHours(-1));
        _ = await components.Policy.ApplyAsync(skip, now);

        AgentScheduleRecord runNext = await SaveValidScheduleAsync(components, CreateSchedule(
            temporary,
            FounderScoutId,
            "Run next scheduled",
            OneTimeScheduleDefinition.AtUtc(now.AddHours(-1)),
            misfirePolicy: MisfirePolicy.RunNextScheduled));
        ScheduleOccurrenceRecord runNextOccurrence = await CreateOccurrenceAsync(components.Occurrences, runNext, revision.Id, now.AddHours(-1));
        _ = await components.Policy.ApplyAsync(runNext, now);

        AgentScheduleRecord grace = await SaveValidScheduleAsync(components, CreateSchedule(
            temporary,
            FounderScoutId,
            "Grace period",
            new OneTimeScheduleDefinition(ScheduleTimeSemantics.Utc, default, now.AddSeconds(-10)),
            misfirePolicy: MisfirePolicy.Skip,
            misfireGrace: TimeSpan.FromSeconds(30)));
        ScheduleOccurrenceRecord graceOccurrence = await CreateOccurrenceAsync(components.Occurrences, grace, revision.Id, now.AddSeconds(-10));
        _ = await components.Policy.ApplyAsync(grace, now);

        await MoveToRunningAsync(components.Occurrences, newest.Id, now);
        ScheduleOccurrenceRecord blocked = await CreateOccurrenceAsync(components.Occurrences, immediate, revision.Id, now.AddMinutes(-2));
        _ = await components.Policy.ApplyAsync(immediate with { ConcurrencyPolicy = ConcurrencyPolicy.Forbid }, now);

        AgentScheduleRecord parallel = await SaveValidScheduleAsync(components, CreateSchedule(
            temporary,
            FounderScoutId,
            "Parallel policy",
            new FixedIntervalScheduleDefinition(TimeSpan.FromMinutes(30), now.AddHours(-2)),
            misfirePolicy: MisfirePolicy.RunImmediately,
            concurrencyPolicy: ConcurrencyPolicy.AllowParallel));
        ScheduleOccurrenceRecord parallelActive = await CreateOccurrenceAsync(components.Occurrences, parallel, revision.Id, now.AddHours(-1));
        await MoveToRunningAsync(components.Occurrences, parallelActive.Id, now);
        ScheduleOccurrenceRecord parallelDue = await CreateOccurrenceAsync(components.Occurrences, parallel, revision.Id, now.AddMinutes(-2));
        _ = await components.Policy.ApplyAsync(parallel, now);

        ScheduleOccurrenceRecord storedOlder = (await components.Occurrences.GetAsync(older.Id))!;
        ScheduleOccurrenceRecord storedNewest = (await components.Occurrences.GetAsync(newest.Id))!;
        ScheduleOccurrenceRecord storedSkipped = (await components.Occurrences.GetAsync(skippedOccurrence.Id))!;
        ScheduleOccurrenceRecord storedGrace = (await components.Occurrences.GetAsync(graceOccurrence.Id))!;
        ScheduleOccurrenceRecord storedRunNext = (await components.Occurrences.GetAsync(runNextOccurrence.Id))!;
        ScheduleOccurrenceRecord storedBlocked = (await components.Occurrences.GetAsync(blocked.Id))!;
        ScheduleOccurrenceRecord storedParallel = (await components.Occurrences.GetAsync(parallelDue.Id))!;
        Assert.Multiple(() =>
        {
            Assert.That(immediateResult.Skipped, Is.EqualTo(1));
            Assert.That(storedOlder.Status, Is.EqualTo(OccurrenceStatus.Skipped));
            Assert.That(storedNewest.Status, Is.EqualTo(OccurrenceStatus.Running));
            Assert.That(storedSkipped.TerminalReasonCode, Is.EqualTo("misfire.skip"));
            Assert.That(storedGrace.Status, Is.EqualTo(OccurrenceStatus.Ready));
            Assert.That(storedRunNext.TerminalReasonCode, Is.EqualTo("misfire.run-next-scheduled"));
            Assert.That(storedBlocked.Status, Is.EqualTo(OccurrenceStatus.Planned));
            Assert.That(storedParallel.Status, Is.EqualTo(OccurrenceStatus.Ready));
        });
    }

    [Test]
    public async Task FixedDelaySchedulesEveryTerminalResultButNeverRetriesOrIndefinitePause()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        ConfigurationRevisionRecord revision = await CreateConfigurationAsync(temporary, FounderScoutId);
        SchedulingComponents components = CreateComponents(temporary);
        DateTimeOffset baseTime = temporary.TimeProvider.GetUtcNow();
        OccurrenceStatus[] terminalStatuses =
        [
            OccurrenceStatus.Completed,
            OccurrenceStatus.Failed,
            OccurrenceStatus.TimedOut,
            OccurrenceStatus.Cancelled,
            OccurrenceStatus.Abandoned,
            OccurrenceStatus.Skipped,
        ];

        for (var index = 0; index < terminalStatuses.Length; index++)
        {
            OccurrenceStatus terminal = terminalStatuses[index];
            AgentScheduleRecord schedule = await SaveValidScheduleAsync(components, CreateSchedule(
                temporary,
                FounderScoutId,
                $"Terminal cadence {terminal}",
                new FixedDelayScheduleDefinition(TimeSpan.FromMinutes(10), InitialDueAtUtc: null, StartImmediately: true),
                concurrencyPolicy: ConcurrencyPolicy.AllowParallel));
            ScheduleOccurrenceRecord occurrence = await components.Occurrences.CreateIfAbsentAsync(new(
                OccurrenceId.New(),
                schedule.Id,
                schedule.AgentId,
                schedule.CommandName,
                schedule.ArgumentsJson,
                revision.Id,
                baseTime.AddSeconds(index),
                TriggerType.Schedule,
                AttemptNumber: 0,
                ParentOccurrenceId: null,
                InitialStatus: OccurrenceStatus.Ready));
            DateTimeOffset completedAt = baseTime.AddMinutes(index + 1);
            if (terminal == OccurrenceStatus.Skipped)
            {
                _ = await TransitionAsync(
                    components.Occurrences,
                    occurrence.Id,
                    OccurrenceStatus.Ready,
                    terminal,
                    completedAt,
                    "test.skipped");
            }
            else
            {
                await MoveToRunningAsync(components.Occurrences, occurrence.Id, baseTime.AddSeconds(index));
                _ = await TransitionAsync(
                    components.Occurrences,
                    occurrence.Id,
                    OccurrenceStatus.Running,
                    terminal,
                    completedAt,
                    $"test.{terminal.ToString().ToLowerInvariant()}");
            }

            FixedDelayCompletionResult result = await components.FixedDelay.OnOccurrenceTerminalAsync(occurrence.Id, completedAt);
            Assert.That(result.NextDueAtUtc, Is.EqualTo(completedAt.AddMinutes(10)), terminal.ToString());
        }

        AgentScheduleRecord paused = await SaveValidScheduleAsync(components, CreateSchedule(
            temporary,
            FounderScoutId,
            "Indefinite pause cadence",
            new FixedDelayScheduleDefinition(TimeSpan.FromMinutes(10), InitialDueAtUtc: null, StartImmediately: true)));
        ScheduleOccurrenceRecord pausedOccurrence = await components.Occurrences.CreateIfAbsentAsync(new(
            OccurrenceId.New(), paused.Id, paused.AgentId, paused.CommandName, paused.ArgumentsJson, revision.Id, baseTime,
            TriggerType.Schedule, 0, ParentOccurrenceId: null, OccurrenceStatus.Ready));
        _ = await TransitionAsync(components.Occurrences, pausedOccurrence.Id, OccurrenceStatus.Ready, OccurrenceStatus.Skipped, baseTime, "test.skipped");
        _ = await components.Schedules.SaveAsync(paused with
        {
            IsPaused = true,
            PausedUntilUtc = null,
            UpdatedAtUtc = baseTime,
        }, paused.ConcurrencyToken);
        FixedDelayCompletionResult pausedResult = await components.FixedDelay.OnOccurrenceTerminalAsync(pausedOccurrence.Id, baseTime);

        ScheduleOccurrenceRecord retry = await components.Occurrences.CreateIfAbsentAsync(new(
            OccurrenceId.New(), ScheduleId: null, FounderScoutId, "run", "{}", revision.Id, baseTime,
            TriggerType.Retry, AttemptNumber: 1, pausedOccurrence.Id, OccurrenceStatus.Ready));
        await MoveToRunningAsync(components.Occurrences, retry.Id, baseTime);
        _ = await TransitionAsync(components.Occurrences, retry.Id, OccurrenceStatus.Running, OccurrenceStatus.Completed, baseTime, "run.completed");
        FixedDelayCompletionResult retryResult = await components.FixedDelay.OnOccurrenceTerminalAsync(retry.Id, baseTime);

        Assert.Multiple(() =>
        {
            Assert.That(pausedResult.Created, Is.False);
            Assert.That(retryResult.Applicable, Is.False);
            Assert.That(retryResult.Created, Is.False);
        });
    }

    [Test]
    public async Task ScheduleControlsDistinguishTimedAndIndefinitePauseAndAuditChanges()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        _ = await CreateConfigurationAsync(temporary, FounderScoutId);
        SchedulingComponents components = CreateComponents(temporary);
        AgentScheduleRecord schedule = await SaveValidScheduleAsync(components, CreateSchedule(
            temporary,
            FounderScoutId,
            "Controlled schedule",
            new DailyScheduleDefinition(new TimeOnly(9, 0))));
        var control = new ScheduleControlService(
            components.Schedules,
            components.Validator,
            new AuditWriter(temporary.Database.ContextFactory, temporary.TimeProvider),
            temporary.TimeProvider);

        AgentScheduleRecord timed = await control.PauseAsync(
            schedule.Id,
            temporary.TimeProvider.GetUtcNow().AddMinutes(5),
            "integration-user",
            Guid.NewGuid());
        Assert.That(await components.Schedules.GetActiveAsync(), Is.Empty);
        temporary.TimeProvider.Advance(TimeSpan.FromMinutes(6));
        Assert.That(await components.Schedules.GetActiveAsync(), Has.Count.EqualTo(1));
        AgentScheduleRecord indefinite = await control.PauseAsync(
            schedule.Id,
            untilUtc: null,
            "integration-user",
            Guid.NewGuid());
        Assert.That(await components.Schedules.GetActiveAsync(), Is.Empty);
        AgentScheduleRecord resumed = await control.ResumeAsync(schedule.Id, "integration-user", Guid.NewGuid());
        AgentScheduleRecord disabled = await control.SetEnabledAsync(schedule.Id, enabled: false, "integration-user", Guid.NewGuid());
        Assert.Multiple(() =>
        {
            Assert.That(timed.IsPaused, Is.True);
            Assert.That(timed.PausedUntilUtc, Is.Not.Null);
            Assert.That(indefinite.IsPaused, Is.True);
            Assert.That(indefinite.PausedUntilUtc, Is.Null);
            Assert.That(resumed.IsPaused, Is.False);
            Assert.That(disabled.IsEnabled, Is.False);
        });
        Assert.That(await components.Schedules.GetEnabledAsync(), Is.Empty);
    }

    [Test]
    public async Task ManualRunsCaptureTriggerRevisionPauseBypassAndQueueOne()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        ConfigurationRevisionRecord revision = await CreateConfigurationAsync(temporary, FounderScoutId);
        SchedulingComponents components = CreateComponents(temporary);
        AgentScheduleRecord pausedSchedule = await SaveValidScheduleAsync(components, CreateSchedule(
            temporary,
            FounderScoutId,
            "Paused manual context",
            new ManualScheduleDefinition(),
            wakePolicy: WakePolicy.Never) with
        {
            IsPaused = true,
            PausedUntilUtc = null,
        });
        var blockedRequest = new ManualRunRequest(
            FounderScoutId,
            "run",
            "{}",
            TriggerType.TrayMenu,
            ConcurrencyPolicy.AllowParallel,
            pausedSchedule.Id,
            BypassSchedulePause: false,
            "integration-user",
            Guid.NewGuid());
        Assert.ThrowsAsync<InvalidOperationException>(async () => await components.ManualRuns.CreateAsync(blockedRequest));

        OccurrenceId runningId = await components.ManualRuns.CreateAsync(blockedRequest with
        {
            TriggerType = TriggerType.ManualUi,
            BypassSchedulePause = true,
            CorrelationId = Guid.NewGuid(),
        });
        await MoveToRunningAsync(components.Occurrences, runningId, temporary.TimeProvider.GetUtcNow());
        Assert.ThrowsAsync<InvalidOperationException>(async () => await components.ManualRuns.CreateAsync(blockedRequest with
        {
            ConcurrencyPolicy = ConcurrencyPolicy.Forbid,
            BypassSchedulePause = true,
            CorrelationId = Guid.NewGuid(),
        }));

        ManualRunRequest queueRequest = blockedRequest with
        {
            ConcurrencyPolicy = ConcurrencyPolicy.QueueOne,
            BypassSchedulePause = true,
            CorrelationId = Guid.NewGuid(),
        };
        OccurrenceId queuedId = await components.ManualRuns.CreateAsync(queueRequest);
        OccurrenceId coalescedId = await components.ManualRuns.CreateAsync(queueRequest with { CorrelationId = Guid.NewGuid() });
        ScheduleOccurrenceRecord queued = await components.Occurrences.GetAsync(queuedId)
            ?? throw new InvalidOperationException("Queued manual occurrence was not found.");
        _ = await TransitionAsync(
            components.Occurrences,
            runningId,
            OccurrenceStatus.Running,
            OccurrenceStatus.Completed,
            temporary.TimeProvider.GetUtcNow().AddMinutes(1),
            "run.completed");
        temporary.TimeProvider.Advance(TimeSpan.FromMinutes(1));
        ScheduleReconciliationSummary reconciliation = await components.Reconciler.ReconcileAsync("manual-queue-reconciler");
        ScheduleOccurrenceRecord promoted = await components.Occurrences.GetAsync(queuedId)
            ?? throw new InvalidOperationException("Promoted manual occurrence was not found.");

        Assert.Multiple(() =>
        {
            Assert.That(queued.Status, Is.EqualTo(OccurrenceStatus.Planned));
            Assert.That(queued.TriggerType, Is.EqualTo(TriggerType.TrayMenu));
            Assert.That(queued.ConfigurationRevisionId, Is.EqualTo(revision.Id));
            Assert.That(coalescedId, Is.EqualTo(queuedId));
            Assert.That(reconciliation.QueuedManualOccurrencesMadeReady, Is.EqualTo(1));
            Assert.That(promoted.Status, Is.EqualTo(OccurrenceStatus.Ready));
        });
    }

    [Test]
    public async Task AgentRequestedSafeStopPausesFixedDelayScheduleBeforeSuccessorPlanning()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        ConfigurationRevisionRecord revision = await CreateConfigurationAsync(temporary, FounderScoutId);
        SchedulingComponents components = CreateComponents(temporary);
        AgentScheduleRecord schedule = await SaveValidScheduleAsync(
            components,
            CreateSchedule(
                temporary,
                FounderScoutId,
                "Discovery safe stop",
                new FixedDelayScheduleDefinition(TimeSpan.FromMinutes(10), InitialDueAtUtc: null, StartImmediately: true),
                pinnedRevisionId: revision.Id));
        ScheduleOccurrenceRecord occurrence = await CreateOccurrenceAsync(
            components.Occurrences,
            schedule,
            revision.Id,
            temporary.TimeProvider.GetUtcNow());
        _ = await TransitionAsync(
            components.Occurrences,
            occurrence.Id,
            OccurrenceStatus.Planned,
            OccurrenceStatus.Ready,
            temporary.TimeProvider.GetUtcNow(),
            "test.ready");
        _ = await components.Occurrences.TryClaimAsync(
            occurrence.Id,
            "test-runner",
            temporary.TimeProvider.GetUtcNow(),
            TimeSpan.FromMinutes(5)) ?? throw new InvalidOperationException("The test occurrence could not be claimed.");
        var runs = new AgentRunRepository(temporary.Database.ContextFactory);
        AgentRunId runId = AgentRunId.New();
        AgentRunRecord starting = await runs.CreateAsync(new(
            runId,
            occurrence.Id,
            FounderScoutId,
            revision.Id,
            revision.ConfigurationHash,
            TriggerType.Schedule,
            AgentRunStatus.Starting,
            "test-runner",
            "1.2.0",
            "1.2.0",
            new string('a', 64),
            Environment.MachineName,
            ProcessId: null,
            ProcessStartedAtUtc: null,
            StartedAtUtc: temporary.TimeProvider.GetUtcNow(),
            LastHeartbeatAtUtc: temporary.TimeProvider.GetUtcNow(),
            CompletedAtUtc: null,
            DurationMilliseconds: null,
            ExitCode: null,
            SummaryText: null,
            SummaryJson: null,
            ErrorType: null,
            ErrorMessage: null,
            CreatedAtUtc: temporary.TimeProvider.GetUtcNow(),
            UpdatedAtUtc: temporary.TimeProvider.GetUtcNow(),
            ConcurrencyToken: 0));
        AgentRunRecord running = await runs.MarkRunningAsync(
            runId,
            Environment.ProcessId,
            temporary.TimeProvider.GetUtcNow(),
            temporary.TimeProvider.GetUtcNow(),
            starting.ConcurrencyToken) ?? throw new InvalidOperationException("The test run could not start.");
        var audit = new AuditWriter(temporary.Database.ContextFactory, temporary.TimeProvider);
        var configurations = new AgentConfigurationService(
            temporary.Database.ContextFactory,
            new BasicAgentConfigurationValidator(),
            temporary.TimeProvider);
        var finalizer = new RunFinalizer(
            runs,
            components.Occurrences,
            components.Schedules,
            new FixedDelayCompletionService(components.Occurrences, components.Schedules, configurations, audit),
            audit,
            temporary.TimeProvider);

        RunFinalizationResult result = await finalizer.FinalizeAsync(new(
            running.Id,
            new(AgentExitCode.PermanentFailure, false, false, false, false, null),
            new(2, 0, false, null, "Source enforcement stop.", "{\"scheduleAction\":\"Pause\"}", AgentRunStatus.Failed, AgentExitCode.PermanentFailure, true),
            RunnerFailureType: null,
            RunnerFailureMessage: null));
        AgentScheduleRecord persisted = await components.Schedules.GetAsync(schedule.Id)
            ?? throw new InvalidOperationException("The test schedule disappeared.");

        Assert.Multiple(() =>
        {
            Assert.That(result.Run.Status, Is.EqualTo(AgentRunStatus.Failed));
            Assert.That(persisted.IsPaused, Is.True);
            Assert.That(persisted.PausedUntilUtc, Is.Null);
            Assert.That(result.FixedDelayApplicable, Is.True);
            Assert.That(result.NextFixedDelayOccurrenceId, Is.Null);
        });
    }

    private static SchedulingComponents CreateComponents(
        TemporaryAssistantDatabase temporary,
        SchedulingOptions? options = null)
    {
        options ??= SchedulingOptions.Default;
        var agentDefinitions = new AgentDefinitionRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        var configurations = new AgentConfigurationService(
            temporary.Database.ContextFactory,
            new BasicAgentConfigurationValidator(),
            temporary.TimeProvider);
        var schedules = new ScheduleRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        var occurrences = new OccurrenceRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        var leases = new LeaseManager(temporary.Database.ContextFactory);
        var audit = new AuditWriter(temporary.Database.ContextFactory, temporary.TimeProvider);
        var timeZones = new ScheduleTimeZoneService();
        var validator = new AgentScheduleValidator(agentDefinitions, configurations, schedules, timeZones);
        var planner = new ScheduleOccurrencePlanner(new ScheduleCalculator(timeZones), occurrences, configurations, options);
        var policy = new OccurrencePolicyService(occurrences, options);
        var fixedDelay = new FixedDelayCompletionService(occurrences, schedules, configurations, audit);
        var manual = new ManualRunService(agentDefinitions, configurations, schedules, occurrences, audit, temporary.TimeProvider);
        var reconciler = new ScheduleReconciler(
            leases,
            schedules,
            agentDefinitions,
            validator,
            planner,
            policy,
            occurrences,
            audit,
            temporary.TimeProvider,
            options);
        return new(schedules, occurrences, leases, validator, planner, policy, fixedDelay, manual, reconciler);
    }

    private static AgentScheduleRecord CreateSchedule(
        TemporaryAssistantDatabase temporary,
        AgentId agentId,
        string name,
        ScheduleDefinition definition,
        MisfirePolicy misfirePolicy = MisfirePolicy.RunImmediately,
        ConcurrencyPolicy concurrencyPolicy = ConcurrencyPolicy.Forbid,
        WakePolicy wakePolicy = WakePolicy.Never,
        TimeSpan? misfireGrace = null,
        Guid? pinnedRevisionId = null)
    {
        DateTimeOffset now = temporary.TimeProvider.GetUtcNow();
        return new(
            Guid.NewGuid(),
            agentId,
            name,
            "run",
            "{}",
            definition.Kind,
            ScheduleDefinitionJson.Serialize(definition),
            CentralTimeZoneId,
            misfirePolicy,
            concurrencyPolicy,
            TimeSpan.FromHours(1),
            misfireGrace ?? TimeSpan.FromSeconds(30),
            RetryPolicyJson.Serialize(RetryPolicyDefinition.None),
            wakePolicy,
            pinnedRevisionId,
            AllowDisabledAgent: false,
            IsEnabled: true,
            IsPaused: false,
            PausedUntilUtc: null,
            now,
            now,
            ConcurrencyToken: 0);
    }

    private static async ValueTask<AgentScheduleRecord> SaveValidScheduleAsync(
        SchedulingComponents components,
        AgentScheduleRecord schedule)
    {
        AgentScheduleValidationResult validation = await components.Validator.ValidateAsync(schedule);
        Assert.That(validation.Errors, Is.Empty, string.Join(", ", validation.Errors.Select(item => item.Code)));
        return await components.Schedules.SaveAsync(schedule, expectedConcurrencyToken: null);
    }

    private static async ValueTask<ConfigurationRevisionRecord> CreateConfigurationAsync(
        TemporaryAssistantDatabase temporary,
        AgentId agentId,
        object? value = null)
    {
        var service = new AgentConfigurationService(
            temporary.Database.ContextFactory,
            new BasicAgentConfigurationValidator(),
            temporary.TimeProvider);
        JsonElement configuration = JsonSerializer.SerializeToElement(value ?? new { schemaVersion = "1.0", enabled = true });
        SaveConfigurationResult result = await service.SaveAsync(new(
            agentId,
            "1.0",
            configuration,
            "scheduling-integration-test",
            "Saved non-sensitive scheduling test configuration.",
            Guid.NewGuid()));
        return result.Revision;
    }

    private static async ValueTask<ScheduleOccurrenceRecord> CreateOccurrenceAsync(
        IOccurrenceRepository occurrences,
        AgentScheduleRecord schedule,
        Guid revisionId,
        DateTimeOffset dueAtUtc) => await occurrences.CreateIfAbsentAsync(new(
            OccurrenceId.New(),
            schedule.Id,
            schedule.AgentId,
            schedule.CommandName,
            schedule.ArgumentsJson,
            revisionId,
            dueAtUtc,
            TriggerType.Schedule,
            AttemptNumber: 0,
            ParentOccurrenceId: null,
            InitialStatus: OccurrenceStatus.Planned));

    private static async ValueTask MoveToRunningAsync(
        IOccurrenceRepository occurrences,
        OccurrenceId occurrenceId,
        DateTimeOffset nowUtc)
    {
        ScheduleOccurrenceRecord current = await occurrences.GetAsync(occurrenceId)
            ?? throw new InvalidOperationException("Occurrence was not found.");
        if (current.Status == OccurrenceStatus.Planned)
        {
            current = await TransitionAsync(occurrences, occurrenceId, OccurrenceStatus.Planned, OccurrenceStatus.Ready, nowUtc, "test.ready");
        }

        if (current.Status == OccurrenceStatus.Ready)
        {
            current = await occurrences.TryClaimAsync(occurrenceId, "test-runner", nowUtc, TimeSpan.FromMinutes(5))
                ?? throw new InvalidOperationException("Occurrence could not be claimed.");
        }

        current = await TransitionAsync(occurrences, occurrenceId, current.Status, OccurrenceStatus.Starting, nowUtc, "test.starting");
        _ = await TransitionAsync(occurrences, occurrenceId, current.Status, OccurrenceStatus.Running, nowUtc, "test.running");
    }

    private static async ValueTask<ScheduleOccurrenceRecord> TransitionAsync(
        IOccurrenceRepository occurrences,
        OccurrenceId occurrenceId,
        OccurrenceStatus expected,
        OccurrenceStatus target,
        DateTimeOffset atUtc,
        string reason) => await occurrences.TryTransitionAsync(new(
            occurrenceId,
            [expected],
            target,
            atUtc,
            reason,
            Message: null)) ?? throw new InvalidOperationException($"Transition {expected} -> {target} failed.");

    private sealed record SchedulingComponents(
        IScheduleRepository Schedules,
        IOccurrenceRepository Occurrences,
        ILeaseManager Leases,
        IAgentScheduleValidator Validator,
        IScheduleOccurrencePlanner Planner,
        IOccurrencePolicyService Policy,
        IFixedDelayCompletionService FixedDelay,
        IManualRunService ManualRuns,
        IScheduleReconciler Reconciler);
}
