using System.Text.Json;
using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Application.Secrets;

namespace HomeBusinessAssistant.Application.Tests;

internal sealed class OnboardingReadinessTests
{
    private static readonly string[] ExpectedOrdinaryOrder = ["a.pass", "c.throw", "b.timeout"];
    private static readonly string[] ExpectedExplicitOrder = ["z.explicit"];

    [Test]
    public async Task ReadinessRunnerIsolatesFailuresTimeoutsAndExplicitChecksInStableOrder()
    {
        var runner = new OnboardingReadinessRunner(
        [
            new TestCheck("z.explicit", OnboardingCheckScope.Storage, explicitAction: true, Behavior.Pass),
            new TestCheck("b.timeout", OnboardingCheckScope.Windows, explicitAction: false, Behavior.Wait),
            new TestCheck("a.pass", OnboardingCheckScope.Platform, explicitAction: false, Behavior.Pass),
            new TestCheck("c.throw", OnboardingCheckScope.Persistence, explicitAction: false, Behavior.Throw),
        ], TimeProvider.System, maximumConcurrency: 2, totalTimeout: TimeSpan.FromSeconds(2));

        IReadOnlyList<OnboardingReadinessCheckResult> ordinary = await runner.RunAsync(explicitChecksOnly: false);
        IReadOnlyList<OnboardingReadinessCheckResult> explicitResults = await runner.RunAsync(explicitChecksOnly: true);

        Assert.Multiple(() =>
        {
            Assert.That(ordinary.Select(item => item.Definition.Key), Is.EqualTo(ExpectedOrdinaryOrder));
            Assert.That(ordinary.Single(item => item.Definition.Key == "a.pass").Status, Is.EqualTo(OnboardingCheckStatus.Passed));
            Assert.That(ordinary.Single(item => item.Definition.Key == "c.throw").ReasonCode, Is.EqualTo("readiness.check-failed"));
            Assert.That(ordinary.Single(item => item.Definition.Key == "b.timeout").ReasonCode, Is.EqualTo("readiness.check-timeout"));
            Assert.That(explicitResults.Select(item => item.Definition.Key), Is.EqualTo(ExpectedExplicitOrder));
        });
    }

    [Test]
    public async Task ProtectedStorageProbeAlwaysAttemptsCleanupAndNeverReturnsSecretMaterial()
    {
        var store = new TrackingSecretStore();
        var check = new ProtectedStorageReadinessCheck(store, TimeProvider.System);

        OnboardingReadinessCheckResult passed = await check.EvaluateAsync();
        store.ThrowOnRead = true;
        OnboardingReadinessCheckResult readFailure = await check.EvaluateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(passed.Status, Is.EqualTo(OnboardingCheckStatus.Passed));
            Assert.That(readFailure.Status, Is.EqualTo(OnboardingCheckStatus.Blocked));
            Assert.That(store.DeleteAttempts, Is.EqualTo(2));
            Assert.That(store.Values, Is.Empty);
            Assert.That(passed.Details.GetRawText(), Does.Not.Contain("secret://"));
            Assert.That(passed.Details.GetRawText(), Does.Not.Contain(store.LastValue ?? "unexpected"));
        });
    }

    [Test]
    public async Task ProtectedStorageCleanupFailureIsBlockingAndBounded()
    {
        var store = new TrackingSecretStore { ThrowOnDelete = true };
        var check = new ProtectedStorageReadinessCheck(store, TimeProvider.System);

        OnboardingReadinessCheckResult result = await check.EvaluateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(OnboardingCheckStatus.Blocked));
            Assert.That(result.ReasonCode, Is.EqualTo("protected-storage.cleanup-failed"));
            Assert.That(store.DeleteAttempts, Is.EqualTo(1));
            Assert.That(result.Message, Does.Not.Contain(store.LastValue ?? "unexpected"));
        });
    }

    [Test]
    public void ProtectedStorageProbeCleansUpWhenCancellationInterruptsTheRead()
    {
        var store = new TrackingSecretStore { CancelOnRead = true };
        var check = new ProtectedStorageReadinessCheck(store, TimeProvider.System);

        Assert.That(async () => await check.EvaluateAsync(), Throws.TypeOf<OperationCanceledException>());
        Assert.Multiple(() =>
        {
            Assert.That(store.DeleteAttempts, Is.EqualTo(1));
            Assert.That(store.Values, Is.Empty);
        });
    }

    private enum Behavior { Pass, Throw, Wait }

    private sealed class TestCheck(
        string key,
        OnboardingCheckScope scope,
        bool explicitAction,
        Behavior behavior) : IOnboardingReadinessCheck
    {
        public OnboardingReadinessCheckDefinition Definition { get; } = new(
            key,
            scope,
            key,
            $"Description for {key}.",
            Required: true,
            explicitAction,
            Timeout: behavior == Behavior.Wait ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(1));

        public async ValueTask<OnboardingReadinessCheckResult> EvaluateAsync(CancellationToken cancellationToken = default)
        {
            if (behavior == Behavior.Throw)
            {
                throw new InvalidOperationException("sensitive failure detail");
            }

            if (behavior == Behavior.Wait)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new(
                Definition,
                OnboardingCheckStatus.Passed,
                "test.passed",
                "The test check passed.",
                DateTimeOffset.UtcNow,
                ExpiresAtUtc: null,
                "1.0",
                JsonSerializer.SerializeToElement(new { safe = true }));
        }
    }

    private sealed class TrackingSecretStore : ISecretStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public bool ThrowOnRead { get; set; }
        public bool ThrowOnDelete { get; set; }
        public bool CancelOnRead { get; set; }
        public int DeleteAttempts { get; private set; }
        public string? LastValue { get; private set; }

        public ValueTask SetAsync(SecretReference reference, string secret, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastValue = secret;
            Values[reference.Value] = secret;
            return ValueTask.CompletedTask;
        }

        public ValueTask<string?> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CancelOnRead)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (ThrowOnRead)
            {
                throw new InvalidOperationException("read failed");
            }

            Values.TryGetValue(reference.Value, out string? value);
            return ValueTask.FromResult(value);
        }

        public ValueTask<bool> ExistsAsync(SecretReference reference, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Values.ContainsKey(reference.Value));
        }

        public ValueTask<bool> DeleteAsync(SecretReference reference, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteAttempts++;
            if (ThrowOnDelete)
            {
                throw new InvalidOperationException("delete failed");
            }

            return ValueTask.FromResult(Values.Remove(reference.Value));
        }
    }
}
