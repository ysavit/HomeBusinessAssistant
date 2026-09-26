using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Application.Power;
using HomeBusinessAssistant.Application.Wake;
using HomeBusinessAssistant.Windows.Onboarding;

namespace HomeBusinessAssistant.Windows.Tests;

internal sealed class OnboardingWindowsReadinessTests
{
    [Test]
    public async Task TaskSchedulerCheckQueriesOnlyAndClassifiesAbsentStartupAsWarning()
    {
        var startup = new TrackingStartupScheduler { State = HostStartupTaskState.Absent };
        var wake = new TrackingWakeScheduler { State = WakeTaskState.Absent };
        var check = new TaskSchedulerOnboardingCheck(startup, wake, TimeProvider.System);

        OnboardingReadinessCheckResult result = await check.EvaluateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(OnboardingCheckStatus.Warning));
            Assert.That(result.ReasonCode, Is.EqualTo("task-scheduler.host-at-logon-absent"));
            Assert.That(startup.QueryCount, Is.EqualTo(1));
            Assert.That(startup.MutationCount, Is.Zero);
            Assert.That(wake.QueryCount, Is.EqualTo(1));
            Assert.That(wake.MutationCount, Is.Zero);
            Assert.That(result.Details.GetProperty("mutationAttempted").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task TaskSchedulerOwnershipConflictBlocksWithoutMutation()
    {
        var startup = new TrackingStartupScheduler
        {
            State = new HostStartupTaskState(true, false, null, null, "startup.task.settings-mismatch"),
        };
        var wake = new TrackingWakeScheduler { State = WakeTaskState.Absent };
        var check = new TaskSchedulerOnboardingCheck(startup, wake, TimeProvider.System);

        OnboardingReadinessCheckResult result = await check.EvaluateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(OnboardingCheckStatus.Blocked));
            Assert.That(result.ReasonCode, Is.EqualTo("task-scheduler.ownership-conflict"));
            Assert.That(startup.MutationCount + wake.MutationCount, Is.Zero);
        });
    }

    [TestCase("startup.task.service-unavailable", "task-scheduler.service-unavailable")]
    [TestCase("startup.task.folder-unavailable", "task-scheduler.folder-unavailable")]
    [TestCase("startup.task.permission-denied", "task-scheduler.permission-denied")]
    [TestCase("startup.task.policy-denied", "task-scheduler.policy-denied")]
    public async Task TaskSchedulerCheckPreservesSafeFailureCategory(string errorCode, string expectedReasonCode)
    {
        var startup = new TrackingStartupScheduler
        {
            State = new HostStartupTaskState(false, false, null, null, errorCode),
        };
        var wake = new TrackingWakeScheduler { State = WakeTaskState.Absent };
        var check = new TaskSchedulerOnboardingCheck(startup, wake, TimeProvider.System);

        OnboardingReadinessCheckResult result = await check.EvaluateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(OnboardingCheckStatus.Warning));
            Assert.That(result.ReasonCode, Is.EqualTo(expectedReasonCode));
            Assert.That(startup.MutationCount + wake.MutationCount, Is.Zero);
        });
    }

    [Test]
    public async Task PowerCheckPersistsOnlyTypedFlagsAndNeverRawCommandOutput()
    {
        var diagnostics = new FakePowerDiagnostics();
        var check = new PowerAndWakeOnboardingCheck(diagnostics, TimeProvider.System);

        OnboardingReadinessCheckResult result = await check.EvaluateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(OnboardingCheckStatus.Warning));
            Assert.That(diagnostics.CaptureCount, Is.EqualTo(1));
            Assert.That(result.Details.GetRawText(), Does.Not.Contain("raw-sensitive-output"));
            Assert.That(result.Details.GetProperty("mutationAttempted").GetBoolean(), Is.False);
        });
    }

    private sealed class TrackingStartupScheduler : IHostStartupTaskScheduler
    {
        public HostStartupTaskState State { get; set; } = HostStartupTaskState.Absent;
        public int QueryCount { get; private set; }
        public int MutationCount { get; private set; }

        public ValueTask<HostStartupTaskState> GetStateAsync(CancellationToken cancellationToken = default)
        {
            QueryCount++;
            return ValueTask.FromResult(State);
        }

        public ValueTask<HostStartupTaskState> ReconcileAsync(HostStartupTaskRequest request, CancellationToken cancellationToken = default)
        {
            MutationCount++;
            return ValueTask.FromResult(State);
        }

        public ValueTask RemoveAsync(CancellationToken cancellationToken = default)
        {
            MutationCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingWakeScheduler : IWakeTaskSchedulerBridge
    {
        public WakeTaskState State { get; set; } = WakeTaskState.Absent;
        public int QueryCount { get; private set; }
        public int MutationCount { get; private set; }

        public Task<WakeTaskState> GetStateAsync(CancellationToken cancellationToken = default)
        {
            QueryCount++;
            return Task.FromResult(State);
        }

        public Task ReconcileAsync(WakeTaskRequest? nextWake, CancellationToken cancellationToken = default)
        {
            MutationCount++;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(CancellationToken cancellationToken = default)
        {
            MutationCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePowerDiagnostics : IPowerDiagnosticsService
    {
        public int CaptureCount { get; private set; }

        public Task<PowerDiagnosticsSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
        {
            CaptureCount++;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return Task.FromResult(new PowerDiagnosticsSnapshot(
                now,
                [
                    new(PowerDiagnosticKind.AvailableSleepStates, 0, false, "raw-sensitive-output", string.Empty, false),
                    new(PowerDiagnosticKind.WakeTimers, 1, false, string.Empty, "raw-sensitive-output", false),
                    new(PowerDiagnosticKind.LastWake, 0, false, string.Empty, string.Empty, false),
                    new(PowerDiagnosticKind.WakeArmedDevices, 0, false, string.Empty, string.Empty, true),
                ],
                SleepStatesQuerySucceeded: true,
                WakeTimersQuerySucceeded: false,
                HasReportedWakeTimer: false,
                HasArmedWakeDevice: false));
        }
    }
}
