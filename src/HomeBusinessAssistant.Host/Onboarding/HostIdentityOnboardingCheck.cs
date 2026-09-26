using System.Security.Principal;
using System.Text.Json;
using HomeBusinessAssistant.Application.Onboarding;

namespace HomeBusinessAssistant.Host.Onboarding;

/// <summary>Verifies the exact loopback and current-Windows-owner assumptions used by the management Host.</summary>
public sealed class HostIdentityOnboardingCheck(
    HostBootstrapSettings settings,
    TimeProvider timeProvider) : IOnboardingReadinessCheck
{
    /// <inheritdoc />
    public OnboardingReadinessCheckDefinition Definition { get; } = new(
        "platform.owner-loopback",
        OnboardingCheckScope.Platform,
        "Local owner and loopback host",
        "Confirms the UI uses the exact IPv4 loopback origin and that the current Windows owner identity is available for management authorization.",
        Required: true,
        RequiresExplicitAction: false,
        Timeout: TimeSpan.FromSeconds(5),
        RemediationKey: "host-security-help");

    /// <inheritdoc />
    public ValueTask<OnboardingReadinessCheckResult> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset observedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        bool exactLoopback = false;
        bool ownerIdentityAvailable = false;
        try
        {
            string validated = LoopbackUrlPolicy.Validate(settings.Url);
            var origin = new Uri(validated, UriKind.Absolute);
            exactLoopback = origin.Scheme == Uri.UriSchemeHttp
                && origin.Host == "127.0.0.1"
                && origin.AbsolutePath == "/";
            ownerIdentityAvailable = WindowsIdentity.GetCurrent().User is not null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The result below remains a safe blocker and does not expose SID, user name, or raw configuration.
        }

        bool ready = exactLoopback && ownerIdentityAvailable;
        return ValueTask.FromResult(new OnboardingReadinessCheckResult(
            Definition,
            ready ? OnboardingCheckStatus.Passed : OnboardingCheckStatus.Blocked,
            ready ? "host.owner-loopback-ready" : !exactLoopback ? "host.binding-invalid" : "host.owner-unavailable",
            ready
                ? "The management UI is bound to exact IPv4 loopback and protected by the current Windows owner identity."
                : "The Host could not confirm its exact loopback and current-owner security boundary.",
            observedAtUtc,
            observedAtUtc.AddHours(1),
            "1.0",
            JsonSerializer.SerializeToElement(new
            {
                exactLoopback,
                ownerIdentityAvailable,
                ownerAuthorizationRequired = true,
            }),
            Definition.RemediationKey));
    }
}

