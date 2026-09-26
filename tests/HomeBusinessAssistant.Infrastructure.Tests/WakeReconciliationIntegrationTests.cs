using System.Text.Json;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Application.Wake;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Tests;

internal sealed class WakeReconciliationIntegrationTests
{
    private static readonly AgentId WakeRemoteId = AgentId.Parse("wake-remote");

    [Test]
    public async Task ReconciliationIsIdempotentReplacesEarliestRemovesEmptyAndHonorsLease()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        Components components = await CreateComponentsAsync(temporary);
        DateTimeOffset now = temporary.TimeProvider.GetUtcNow();
        ScheduleOccurrenceRecord later = await CreateWakeOccurrenceAsync(
            components.Occurrences,
            components.Configuration.Id,
            now.AddMinutes(20));
        ScheduleOccurrenceRecord earlier = await CreateWakeOccurrenceAsync(
            components.Occurrences,
            components.Configuration.Id,
            now.AddMinutes(10));

        WakeTaskReconciliationResult first = await components.Reconciler.ReconcileAsync("test-owner");
        WakeTaskReconciliationResult repeated = await components.Reconciler.ReconcileAsync("test-owner");
        _ = await components.Occurrences.TryTransitionAsync(new(
            earlier.Id,
            [OccurrenceStatus.Planned],
            OccurrenceStatus.Skipped,
            now,
            "test.completed",
            Message: null));
        WakeTaskReconciliationResult replacement = await components.Reconciler.ReconcileAsync("test-owner");
        _ = await components.Occurrences.TryTransitionAsync(new(
            later.Id,
            [OccurrenceStatus.Planned],
            OccurrenceStatus.Skipped,
            now,
            "test.completed",
            Message: null));
        WakeTaskReconciliationResult removed = await components.Reconciler.ReconcileAsync("test-owner");
        AgentLeaseRecord held = await components.Leases.TryAcquireAsync(
            "wake:reconciliation",
            "competing-owner",
            now,
            TimeSpan.FromMinutes(1)) ?? throw new InvalidOperationException("Test lease was not acquired.");
        WakeTaskReconciliationResult contended = await components.Reconciler.ReconcileAsync("test-owner");
        _ = await components.Leases.ReleaseAsync(held);

        Assert.Multiple(() =>
        {
            Assert.That(first.Changed, Is.True);
            Assert.That(first.RegisteredOccurrenceId, Is.EqualTo(earlier.Id));
            Assert.That(repeated.Changed, Is.False);
            Assert.That(replacement.Changed, Is.True);
            Assert.That(replacement.RegisteredOccurrenceId, Is.EqualTo(later.Id));
            Assert.That(removed.Removed, Is.True);
            Assert.That(components.Bridge.RemoveCalls, Is.EqualTo(1));
            Assert.That(components.Bridge.ReconcileCalls, Is.EqualTo(2));
            Assert.That(contended.LeaseAcquired, Is.False);
            Assert.That(components.Bridge.State.Exists, Is.False);
        });
    }

    [Test]
    public async Task PermissionFailureReturnsStructuredErrorAndWritesFailedAudit()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        Components components = await CreateComponentsAsync(temporary);
        _ = await CreateWakeOccurrenceAsync(
            components.Occurrences,
            components.Configuration.Id,
            temporary.TimeProvider.GetUtcNow().AddMinutes(3));
        components.Bridge.Failure = new(new(
            "wake.task-registration-failed",
            WakeTaskErrorCategory.PermissionDenied,
            "Permission is required.",
            5));

        WakeTaskReconciliationResult result = await components.Reconciler.ReconcileAsync("test-owner");
        long failedAudits = await CountFailedAuditsAsync(temporary);

        Assert.Multiple(() =>
        {
            Assert.That(result.Error?.Category, Is.EqualTo(WakeTaskErrorCategory.PermissionDenied));
            Assert.That(result.Error?.Code, Is.EqualTo("wake.task-registration-failed"));
            Assert.That(failedAudits, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task WakeTestCreatesThreeMinuteRunnableOccurrenceWithExplicitPowerPolicy()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        var agents = new AgentDefinitionRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        var configurations = new AgentConfigurationService(
            temporary.Database.ContextFactory,
            new BasicAgentConfigurationValidator(),
            temporary.TimeProvider);
        var occurrences = new OccurrenceRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        var runs = new AgentRunRepository(temporary.Database.ContextFactory);
        var audit = new AuditWriter(temporary.Database.ContextFactory, temporary.TimeProvider);
        var bridge = new FakeWakeBridge();
        var reconciliation = new FakeWakeReconciler();
        var service = new WakeTestService(
            agents,
            configurations,
            occurrences,
            runs,
            reconciliation,
            bridge,
            audit,
            temporary.TimeProvider);

        WakeTestPreparationResult result = await service.PrepareAsync(new(
            3,
            "integration-test",
            Guid.NewGuid()));
        ScheduleOccurrenceRecord occurrence = await occurrences.GetAsync(result.OccurrenceId)
            ?? throw new InvalidOperationException("Wake-test occurrence was not persisted.");
        AgentConfigurationRecord? configuration = await configurations.GetCurrentAsync(WakeRemoteId);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExpectedWakeAtUtc, Is.EqualTo(temporary.TimeProvider.GetUtcNow().AddMinutes(3)));
            Assert.That(occurrence.CommandName, Is.EqualTo("wake-test"));
            Assert.That(occurrence.Status, Is.EqualTo(OccurrenceStatus.Ready));
            Assert.That(occurrence.RequiresWake, Is.True);
            Assert.That(occurrence.KeepSystemAwake, Is.True);
            Assert.That(occurrence.KeepDisplayOn, Is.False);
            Assert.That(configuration, Is.Not.Null);
            Assert.That(reconciliation.Calls, Is.EqualTo(1));
        });
    }

    private static async ValueTask<Components> CreateComponentsAsync(TemporaryAssistantDatabase temporary)
    {
        var configurations = new AgentConfigurationService(
            temporary.Database.ContextFactory,
            new BasicAgentConfigurationValidator(),
            temporary.TimeProvider);
        SaveConfigurationResult saved = await configurations.SaveAsync(new(
            WakeRemoteId,
            "1.0",
            JsonSerializer.SerializeToElement(new { schemaVersion = "1.0" }),
            "wake-tests",
            "Create wake test configuration.",
            Guid.NewGuid()));
        var occurrences = new OccurrenceRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        var schedules = new ScheduleRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        var leases = new LeaseManager(temporary.Database.ContextFactory);
        var settings = new SystemSettingRepository(temporary.Database.ContextFactory);
        var audit = new AuditWriter(temporary.Database.ContextFactory, temporary.TimeProvider);
        var bridge = new FakeWakeBridge();
        string applicationRoot = Path.Combine(temporary.Root, "app");
        string data = Path.Combine(temporary.Root, "data");
        string agents = Path.Combine(temporary.Root, "agents");
        string manifests = Path.Combine(temporary.Root, "manifests");
        Directory.CreateDirectory(applicationRoot);
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(agents);
        Directory.CreateDirectory(manifests);
        var options = new WakeTaskReconciliationOptions(
            applicationRoot,
            Path.Combine(applicationRoot, "HomeBusinessAssistant.Runner.exe"),
            applicationRoot,
            new(data, agents, manifests, "assistant.db"),
            @"TEST\User",
            "Central Standard Time",
            TimeSpan.FromSeconds(30),
            TimeSpan.FromDays(7),
            TimeSpan.FromMinutes(5),
            StartWhenAvailable: true,
            AllowStartOnBatteries: true,
            StopIfGoingOnBatteries: false);
        var reconciler = new WakeTaskReconciler(
            leases,
            occurrences,
            schedules,
            settings,
            audit,
            bridge,
            new ScheduleTimeZoneService(),
            temporary.TimeProvider,
            options);
        return new(occurrences, leases, bridge, reconciler, saved.Revision);
    }

    private static async ValueTask<long> CountFailedAuditsAsync(TemporaryAssistantDatabase temporary)
    {
        await using var context = await temporary.Database.ContextFactory.CreateDbContextAsync();
        await context.Database.OpenConnectionAsync();
        await using System.Data.Common.DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM AuditEvents WHERE Action = 'wake-task.reconciliation-failed'";
        object? value = await command.ExecuteScalarAsync();
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ValueTask<ScheduleOccurrenceRecord> CreateWakeOccurrenceAsync(
        OccurrenceRepository occurrences,
        Guid configurationRevisionId,
        DateTimeOffset dueAtUtc) => occurrences.CreateIfAbsentAsync(new(
            OccurrenceId.New(),
            ScheduleId: null,
            WakeRemoteId,
            "wake-test",
            "{}",
            configurationRevisionId,
            dueAtUtc,
            TriggerType.WakeSchedule,
            AttemptNumber: 0,
            ParentOccurrenceId: null,
            InitialStatus: OccurrenceStatus.Planned,
            RequiresWake: true,
            KeepSystemAwake: true,
            KeepDisplayOn: false));

    private sealed record Components(
        OccurrenceRepository Occurrences,
        LeaseManager Leases,
        FakeWakeBridge Bridge,
        WakeTaskReconciler Reconciler,
        ConfigurationRevisionRecord Configuration);

    private sealed class FakeWakeBridge : IWakeTaskSchedulerBridge
    {
        public WakeTaskState State { get; private set; } = WakeTaskState.Absent;

        public int ReconcileCalls { get; private set; }

        public int RemoveCalls { get; private set; }

        public WakeTaskBridgeException? Failure { get; set; }

        public Task<WakeTaskState> GetStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(State);

        public Task ReconcileAsync(WakeTaskRequest? nextWake, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            if (nextWake is null)
            {
                return RemoveAsync(cancellationToken);
            }

            ReconcileCalls++;
            State = new(
                true,
                true,
                nextWake.OccurrenceId,
                nextWake.DueAtUtc,
                nextWake.GetFingerprint(),
                null);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(CancellationToken cancellationToken = default)
        {
            RemoveCalls++;
            State = WakeTaskState.Absent;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWakeReconciler : IWakeTaskReconciler
    {
        public int Calls { get; private set; }

        public ValueTask<WakeTaskReconciliationResult> ReconcileAsync(
            string ownerId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(new WakeTaskReconciliationResult(
                true,
                true,
                false,
                null,
                null,
                "fake",
                null));
        }
    }
}
