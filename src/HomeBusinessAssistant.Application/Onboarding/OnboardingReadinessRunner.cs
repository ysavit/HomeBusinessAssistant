using System.Collections.Concurrent;
using System.Text.Json;

namespace HomeBusinessAssistant.Application.Onboarding;

/// <summary>Bounds concurrency, total duration, per-check duration, and failure isolation.</summary>
public sealed class OnboardingReadinessRunner : IOnboardingReadinessRunner
{
    private readonly IReadOnlyList<IOnboardingReadinessCheck> checks;
    private readonly TimeProvider timeProvider;
    private readonly int maximumConcurrency;
    private readonly TimeSpan totalTimeout;

    /// <summary>Creates a deterministic readiness runner.</summary>
    public OnboardingReadinessRunner(
        IEnumerable<IOnboardingReadinessCheck> checks,
        TimeProvider timeProvider,
        int maximumConcurrency = 3,
        TimeSpan? totalTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(checks);
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        if (maximumConcurrency is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        }

        this.totalTimeout = totalTimeout ?? TimeSpan.FromSeconds(60);
        if (this.totalTimeout < TimeSpan.FromSeconds(1) || this.totalTimeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(totalTimeout));
        }

        this.maximumConcurrency = maximumConcurrency;
        this.checks = checks
            .OrderBy(item => item.Definition.Scope)
            .ThenBy(item => item.Definition.Key, StringComparer.Ordinal)
            .ToArray();
        ValidateDefinitions(this.checks);
        Definitions = this.checks.Select(item => item.Definition).ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<OnboardingReadinessCheckDefinition> Definitions { get; }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<OnboardingReadinessCheckResult>> RunAsync(
        bool explicitChecksOnly,
        CancellationToken cancellationToken = default)
    {
        IOnboardingReadinessCheck[] selected = checks
            .Where(item => item.Definition.RequiresExplicitAction == explicitChecksOnly)
            .ToArray();
        if (selected.Length == 0)
        {
            return [];
        }

        using var timeout = new CancellationTokenSource(totalTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var gate = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
        var results = new ConcurrentBag<OnboardingReadinessCheckResult>();
        Task[] tasks = selected.Select(check => EvaluateIsolatedAsync(check, gate, results, linked.Token)).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return results
            .OrderBy(item => item.Definition.Scope)
            .ThenBy(item => item.Definition.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task EvaluateIsolatedAsync(
        IOnboardingReadinessCheck check,
        SemaphoreSlim gate,
        ConcurrentBag<OnboardingReadinessCheckResult> results,
        CancellationToken cancellationToken)
    {
        bool entered = false;
        CancellationTokenSource? checkTimeout = null;
        CancellationTokenSource? checkCancellation = null;
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            checkTimeout = new CancellationTokenSource(check.Definition.Timeout, timeProvider);
            checkCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, checkTimeout.Token);
            OnboardingReadinessCheckResult result = await check.EvaluateAsync(checkCancellation.Token)
                .AsTask()
                .WaitAsync(check.Definition.Timeout, timeProvider, cancellationToken)
                .ConfigureAwait(false);
            ValidateResult(check.Definition, result);
            results.Add(result);
        }
        catch (TimeoutException)
        {
            checkTimeout?.Cancel();
            results.Add(Failure(check.Definition, "readiness.check-timeout", "The check did not finish within its bounded timeout."));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && checkTimeout?.IsCancellationRequested == true)
        {
            results.Add(Failure(check.Definition, "readiness.check-timeout", "The check did not finish within its bounded timeout."));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            results.Add(Failure(check.Definition, "readiness.check-cancelled", "The check was cancelled before it produced a result."));
        }
        catch (OperationCanceledException)
        {
            results.Add(Failure(check.Definition, "readiness.batch-timeout", "The readiness batch reached its bounded total timeout."));
        }
        catch (Exception)
        {
            results.Add(Failure(check.Definition, "readiness.check-failed", "The check failed without exposing local diagnostic details."));
        }
        finally
        {
            checkCancellation?.Dispose();
            checkTimeout?.Dispose();
            if (entered)
            {
                gate.Release();
            }
        }
    }

    private OnboardingReadinessCheckResult Failure(
        OnboardingReadinessCheckDefinition definition,
        string reasonCode,
        string message) => new(
            definition,
            definition.Required ? OnboardingCheckStatus.Unknown : OnboardingCheckStatus.Warning,
            reasonCode,
            message,
            timeProvider.GetUtcNow().ToUniversalTime(),
            ExpiresAtUtc: null,
            "1.0",
            JsonSerializer.SerializeToElement(new { isolated = true }),
            definition.RemediationKey);

    private static void ValidateDefinitions(IReadOnlyList<IOnboardingReadinessCheck> checks)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (IOnboardingReadinessCheck check in checks)
        {
            OnboardingReadinessCheckDefinition definition = check.Definition;
            string identity = $"{definition.Scope}:{definition.Key}:{definition.AgentId?.Value ?? string.Empty}";
            if (string.IsNullOrWhiteSpace(definition.Key)
                || definition.Key.Length > 128
                || string.IsNullOrWhiteSpace(definition.Title)
                || definition.Title.Length > 160
                || string.IsNullOrWhiteSpace(definition.Description)
                || definition.Description.Length > 1_000
                || definition.Timeout < TimeSpan.FromMilliseconds(100)
                || definition.Timeout > TimeSpan.FromMinutes(2)
                || !identities.Add(identity))
            {
                throw new ArgumentException("Onboarding readiness definitions are invalid or duplicated.", nameof(checks));
            }
        }
    }

    private static void ValidateResult(
        OnboardingReadinessCheckDefinition expected,
        OnboardingReadinessCheckResult result)
    {
        if (result.Definition != expected
            || string.IsNullOrWhiteSpace(result.ReasonCode)
            || result.ReasonCode.Length > 128
            || string.IsNullOrWhiteSpace(result.Message)
            || result.Message.Length > 1_000
            || string.IsNullOrWhiteSpace(result.DetailsSchemaVersion)
            || result.DetailsSchemaVersion.Length > 32
            || result.Details.ValueKind is not JsonValueKind.Object)
        {
            throw new InvalidOperationException("A readiness adapter returned an invalid result.");
        }
    }
}
