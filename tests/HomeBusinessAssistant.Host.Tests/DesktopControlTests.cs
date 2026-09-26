using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Power;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Application.Wake;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Host.Desktop;
using HomeBusinessAssistant.Host.Health;
using HomeBusinessAssistant.Host.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HomeBusinessAssistant.Host.Tests;

internal sealed class DesktopControlTests
{
    [Test]
    public void BootstrapValidationRejectsEscapingRunnerAndUnboundedIntervals()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-host-settings-{Guid.NewGuid():N}");
        string manifests = Path.Combine(root, "manifests");
        Directory.CreateDirectory(manifests);
        try
        {
            HostBootstrapSettings valid = CreateSettings(root, manifests);
            Assert.DoesNotThrow(valid.Validate);
            Assert.Multiple(() =>
            {
                Assert.That(
                    () => (valid with { RunnerExecutablePath = Path.Combine(Path.GetTempPath(), "outside.exe") }).Validate(),
                    Throws.ArgumentException);
                Assert.That(
                    () => (valid with { ScheduleInterval = TimeSpan.FromMilliseconds(500) }).Validate(),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ManualKeepAwakeExtendsWithoutShorteningAndReleasesEveryHandle()
    {
        var power = new FakePowerRequestService();
        await using var service = new ManualKeepAwakeService(power, TimeProvider.System);

        ManualKeepAwakeState first = await service.StartAsync(TimeSpan.FromHours(1));
        ManualKeepAwakeState shorter = await service.StartAsync(TimeSpan.FromMinutes(30));
        ManualKeepAwakeState extended = await service.StartAsync(TimeSpan.FromHours(2));

        Assert.Multiple(() =>
        {
            Assert.That(first.IsActive, Is.True);
            Assert.That(shorter.ExpiresAtUtc, Is.EqualTo(first.ExpiresAtUtc));
            Assert.That(extended.ExpiresAtUtc.GetValueOrDefault(), Is.GreaterThan(first.ExpiresAtUtc.GetValueOrDefault()));
            Assert.That(power.Handles, Has.Count.EqualTo(2));
            Assert.That(power.Handles[0].Disposed, Is.True);
            Assert.That(power.KeepDisplayFlags, Is.All.False);
        });

        await service.ReleaseAsync();
        Assert.Multiple(() =>
        {
            Assert.That(service.GetState().IsActive, Is.False);
            Assert.That(power.Handles[1].Disposed, Is.True);
        });
    }

    [Test]
    public async Task PeriodicLoopSurvivesFailureNeverOverlapsAndStops()
    {
        var iteration = new FlakyIteration();
        var health = new HostHealthState(TimeProvider.System);
        var service = new HostPeriodicBackgroundService(
            HostLoopNames.Schedule,
            TimeSpan.FromMilliseconds(10),
            iteration,
            health,
            TimeProvider.System,
            NullLogger<HostPeriodicBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await iteration.ThirdCall.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await service.StopAsync(CancellationToken.None);
        int callsAtStop = iteration.CallCount;
        await Task.Delay(50);

        HostLoopHealth loop = health.Loops.Single();
        Assert.Multiple(() =>
        {
            Assert.That(callsAtStop, Is.GreaterThanOrEqualTo(3));
            Assert.That(iteration.CallCount, Is.EqualTo(callsAtStop));
            Assert.That(iteration.MaximumConcurrency, Is.EqualTo(1));
            Assert.That(loop.LastSuccessAtUtc, Is.Not.Null);
            Assert.That(loop.IsRunning, Is.False);
            Assert.That(health.SchedulerInitialized, Is.True);
        });
    }

    [Test]
    public async Task ScheduleIterationReconcilesWakeImmediatelyEvenWhenGloballyPaused()
    {
        var sequence = new MockSequence();
        var schedules = new Mock<IScheduleReconciler>(MockBehavior.Strict);
        schedules.InSequence(sequence)
            .Setup(item => item.ReconcileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduleReconciliationSummary(true, 1, 0, 0, 0, 0, [], null, null));
        var global = new Mock<IGlobalScheduleControlService>(MockBehavior.Strict);
        global.InSequence(sequence)
            .Setup(item => item.GetStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalSchedulePauseState(true, []));
        var wake = new Mock<IWakeTaskReconciler>(MockBehavior.Strict);
        wake.InSequence(sequence)
            .Setup(item => item.ReconcileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WakeTaskReconciliationResult(true, true, true, null, null, null, null));
        var occurrences = new Mock<IOccurrenceRepository>(MockBehavior.Strict);
        var dispatcher = new Mock<IOccurrenceRunnerDispatcher>(MockBehavior.Strict);
        var iteration = new ScheduleReconciliationIteration(
            schedules.Object,
            occurrences.Object,
            dispatcher.Object,
            global.Object,
            wake.Object,
            TimeProvider.System);

        await iteration.ExecuteAsync();

        schedules.VerifyAll();
        global.VerifyAll();
        wake.VerifyAll();
        occurrences.VerifyNoOtherCalls();
        dispatcher.VerifyNoOtherCalls();
    }

    [Test]
    public async Task TrayControllerUsesDurableManualLauncherAndReflectsActiveExitState()
    {
        AgentRunId runId = AgentRunId.New();
        HostPlatformStatus platform = new(
            [new(AgentId.Parse("wake-remote"), "Wake & Remote", "diagnostic", true, true, true)],
            [],
            [new(runId, AgentId.Parse("wake-remote"), "Wake & Remote", AgentRunStatus.Running, DateTimeOffset.UtcNow, null, null, null)],
            [],
            null);
        var status = new Mock<IHostPlatformStatusReader>(MockBehavior.Strict);
        status.Setup(item => item.ReadAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(platform);
        var manual = new Mock<IManualAgentRunLauncher>(MockBehavior.Strict);
        manual.Setup(item => item.CreateAndLaunchAsync(
                It.Is<ManualAgentLaunchRequest>(request => request.AgentId.Value == "wake-remote" && request.CommandName == "diagnose"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OccurrenceDispatchResult(OccurrenceId.New(), true, false, true, 1234, "runner.started"));
        var global = new Mock<IGlobalScheduleControlService>(MockBehavior.Strict);
        global.Setup(item => item.GetStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalSchedulePauseState(false, []));
        var dashboard = new Mock<ILocalDashboardLauncher>(MockBehavior.Strict);
        var keepAwake = new FakeManualKeepAwakeService();
        var notifications = new HostNotificationHub();
        var published = new List<HostNotification>();
        notifications.Published += (_, notification) => published.Add(notification);
        var controller = new TrayController(status.Object, manual.Object, global.Object, keepAwake, dashboard.Object, notifications);

        OccurrenceDispatchResult result = await controller.RunWakeRemoteDiagnosticAsync();
        TrayControllerState state = await controller.GetStateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.RunnerStarted, Is.True);
            Assert.That(state.RequiresExitConfirmation, Is.True);
            Assert.That(published.Select(item => item.Code), Does.Contain("runner.started"));
        });
        manual.VerifyAll();
        status.VerifyAll();
        global.VerifyAll();
    }

    private static HostBootstrapSettings CreateSettings(string root, string manifests) => new(
        root,
        Path.Combine(root, "data"),
        Path.Combine(root, "agents"),
        manifests,
        Path.Combine(root, "HomeBusinessAssistant.Runner.exe"),
        root,
        "assistant.db",
        "http://127.0.0.1:5180",
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromSeconds(15));

    private sealed class FakePowerRequestService : IPowerRequestService
    {
        public List<FakeHandle> Handles { get; } = [];
        public List<bool> KeepDisplayFlags { get; } = [];

        public ValueTask<IAsyncDisposable> AcquireSystemRequiredAsync(
            string reason,
            bool keepDisplayOn,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            KeepDisplayFlags.Add(keepDisplayOn);
            var handle = new FakeHandle();
            Handles.Add(handle);
            return ValueTask.FromResult<IAsyncDisposable>(handle);
        }
    }

    private sealed class FakeHandle : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FlakyIteration : IHostLoopIteration
    {
        private int active;
        public int CallCount { get; private set; }
        public int MaximumConcurrency { get; private set; }
        public TaskCompletionSource ThirdCall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ExecuteAsync(CancellationToken cancellationToken = default)
        {
            int concurrency = Interlocked.Increment(ref active);
            MaximumConcurrency = Math.Max(MaximumConcurrency, concurrency);
            int call = ++CallCount;
            try
            {
                await Task.Delay(5, cancellationToken);
                if (call == 1)
                {
                    throw new InvalidOperationException("Synthetic loop failure.");
                }

                if (call >= 3)
                {
                    ThirdCall.TrySetResult();
                }
            }
            finally
            {
                _ = Interlocked.Decrement(ref active);
            }
        }
    }

    private sealed class FakeManualKeepAwakeService : IManualKeepAwakeService
    {
        public ManualKeepAwakeState GetState() => new(false, null);
        public ValueTask<ManualKeepAwakeState> StartAsync(TimeSpan duration, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ManualKeepAwakeState(true, DateTimeOffset.UtcNow.Add(duration)));
        public ValueTask ReleaseAsync() => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
