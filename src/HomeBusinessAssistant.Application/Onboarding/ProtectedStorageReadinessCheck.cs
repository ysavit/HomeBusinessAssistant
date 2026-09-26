using System.Security.Cryptography;
using System.Text.Json;
using HomeBusinessAssistant.Application.Secrets;

namespace HomeBusinessAssistant.Application.Onboarding;

/// <summary>Explicitly proves a temporary protected-store round trip and cleanup through <see cref="ISecretStore"/>.</summary>
public sealed class ProtectedStorageReadinessCheck(
    ISecretStore secrets,
    TimeProvider timeProvider) : IOnboardingReadinessCheck
{
    /// <inheritdoc />
    public OnboardingReadinessCheckDefinition Definition { get; } = new(
        "persistence.protected-storage",
        OnboardingCheckScope.Persistence,
        "Protected API-key storage",
        "Explicitly writes, reads, and deletes one temporary CurrentUser-protected value. The value and reference are never persisted in onboarding results.",
        Required: true,
        RequiresExplicitAction: true,
        Timeout: TimeSpan.FromSeconds(15),
        RemediationKey: "protected-storage-help");

    /// <inheritdoc />
    public async ValueTask<OnboardingReadinessCheckResult> EvaluateAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset observedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        SecretReference reference = SecretReference.Parse($"secret://platform/onboarding-probe-{Guid.NewGuid():N}");
        string value = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        bool writeCompleted = false;
        bool roundTripCompleted = false;
        bool cleanupCompleted = false;
        string reasonCode = "protected-storage.failed";
        try
        {
            await secrets.SetAsync(reference, value, cancellationToken).ConfigureAwait(false);
            writeCompleted = true;
            string? read = await secrets.GetAsync(reference, cancellationToken).ConfigureAwait(false);
            roundTripCompleted = string.Equals(read, value, StringComparison.Ordinal);
            reasonCode = roundTripCompleted ? "protected-storage.ready" : "protected-storage.roundtrip-mismatch";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            reasonCode = writeCompleted ? "protected-storage.read-failed" : "protected-storage.write-failed";
        }
        finally
        {
            try
            {
                bool deleted = await secrets.DeleteAsync(reference, CancellationToken.None).ConfigureAwait(false);
                cleanupCompleted = deleted || !await secrets.ExistsAsync(reference, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                cleanupCompleted = false;
            }
        }

        bool passed = writeCompleted && roundTripCompleted && cleanupCompleted;
        if (!cleanupCompleted)
        {
            reasonCode = "protected-storage.cleanup-failed";
        }

        return new(
            Definition,
            passed ? OnboardingCheckStatus.Passed : OnboardingCheckStatus.Blocked,
            reasonCode,
            passed
                ? "Protected storage accepted a temporary value for the current Windows user and removed it successfully."
                : cleanupCompleted
                    ? "Protected storage could not complete a safe temporary round trip. Re-entered API keys may not be usable."
                    : "The temporary protected-storage probe could not confirm cleanup. Review protected storage before saving API keys.",
            observedAtUtc,
            observedAtUtc.AddDays(30),
            "1.0",
            JsonSerializer.SerializeToElement(new
            {
                writeCompleted,
                roundTripCompleted,
                cleanupCompleted,
            }),
            Definition.RemediationKey);
    }
}
