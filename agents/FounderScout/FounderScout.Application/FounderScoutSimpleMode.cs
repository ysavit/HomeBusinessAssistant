using System.Text.Json;
using FounderScout.Domain;
using HomeBusinessAssistant.Application.Persistence;

namespace FounderScout.Application;

/// <summary>Code-owned Founder Scout settings for the minimal local workflow.</summary>
public static class FounderScoutSimpleMode
{
    /// <summary>Simple mode is intentionally compiled on for the current product increment.</summary>
    public static bool Enabled => true;

    /// <summary>The one dedicated Startup School browser account.</summary>
    public const string BrowserAccountId = "startup-school-primary";

    /// <summary>The one code-owned discovery segment.</summary>
    public const string DiscoverySegmentId = "startup-school-default";

    /// <summary>The bounded number of listing pages inspected by the default segment.</summary>
    public const int MaximumPages = 3;

    /// <summary>
    /// Creates the effective immutable configuration document. Operational browser/discovery policy remains
    /// code-owned while the focused local settings page may preserve OpenAI model and founder-context choices.
    /// </summary>
    public static FounderScoutConfiguration CreateConfiguration(FounderScoutConfiguration? current = null)
    {
        FounderScoutConfiguration defaults = FounderScoutDefaults.CreateConfiguration();
        if (current is null || !string.Equals(current.Ai.Provider, "OpenAI", StringComparison.Ordinal))
        {
            return defaults;
        }

        return defaults with
        {
            Analysis = defaults.Analysis with
            {
                Enabled = true,
                BatchSize = current.Analysis.BatchSize,
                MaximumConcurrency = current.Analysis.MaximumConcurrency,
                MaximumRetries = current.Analysis.MaximumRetries,
            },
            Ai = defaults.Ai with
            {
                Deployment = current.Ai.Deployment,
                RequestTimeoutSeconds = current.Ai.RequestTimeoutSeconds,
                MaxOutputTokens = current.Ai.MaxOutputTokens,
                ReasoningEffort = current.Ai.ReasoningEffort,
            },
            Persona = defaults.Persona with
            {
                AdditionalContext = current.Persona.AdditionalContext,
            },
        };
    }

    /// <summary>Creates the safe source-segment document stored in Founder Scout persistence.</summary>
    public static string CreateSegmentConfigurationJson() => JsonSerializer.Serialize(new
    {
        schemaVersion = "1.0",
        source = "startup-school",
        route = "cofounder-matching",
        maximumPages = MaximumPages,
        safeFilter = (string?)null,
    });
}

/// <summary>Reports which code-owned values changed during startup reconciliation.</summary>
public sealed record FounderScoutSimpleModeInitializationResult(
    bool ConfigurationChanged,
    bool AgentEnabled,
    bool BrowserAccountChanged,
    bool DiscoverySegmentChanged);

/// <summary>
/// Reconciles the minimal code-owned configuration while preserving browser health,
/// authentication timestamps, discovery counters, and enforcement pauses.
/// </summary>
public sealed class FounderScoutSimpleModeInitializer(
    IAgentConfigurationService configurations,
    IAgentDefinitionRepository agents,
    IBrowserAccountRepository browserAccounts,
    IDiscoverySegmentRepository discoverySegments,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Promotes the hardcoded values and creates missing account/source metadata.</summary>
    public async ValueTask<FounderScoutSimpleModeInitializationResult> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        AgentConfigurationRecord? current = await configurations.GetCurrentAsync(
            FounderScoutDefaults.AgentId,
            cancellationToken).ConfigureAwait(false);
        FounderScoutConfiguration? currentValue = null;
        if (current is not null)
        {
            try
            {
                currentValue = JsonSerializer.Deserialize<FounderScoutConfiguration>(
                    current.CurrentRevision.CanonicalConfigurationJson,
                    JsonOptions);
            }
            catch (JsonException)
            {
                // The authoritative configuration service validates the replacement below. A malformed
                // historical document must not prevent startup from restoring safe code-owned defaults.
            }
        }
        FounderScoutConfiguration value = FounderScoutSimpleMode.CreateConfiguration(currentValue);
        SaveConfigurationResult saved = await configurations.SaveAsync(new(
            FounderScoutDefaults.AgentId,
            FounderScoutConfiguration.CurrentSchemaVersion,
            JsonSerializer.SerializeToElement(value, JsonOptions),
            "simple-mode",
            "Applied code-owned Founder Scout simple-mode settings.",
            Guid.NewGuid(),
            current?.CurrentRevision.RevisionNumber,
            current?.CurrentRevision.ConfigurationHash), cancellationToken).ConfigureAwait(false);

        bool agentEnabled = await agents.SetEnabledAsync(
            FounderScoutDefaults.AgentId,
            enabled: true,
            cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = timeProvider.GetUtcNow().ToUniversalTime();
        BrowserAccount? existingAccount = await browserAccounts.GetAsync(
            FounderScoutSimpleMode.BrowserAccountId,
            cancellationToken).ConfigureAwait(false);
        var desiredAccount = new BrowserAccount(
            FounderScoutSimpleMode.BrowserAccountId,
            "Startup School",
            $"browser/{FounderScoutSimpleMode.BrowserAccountId}",
            existingAccount?.Enabled ?? false,
            existingAccount?.SessionStatus ?? BrowserSessionStatus.Disabled,
            [FounderScoutSimpleMode.DiscoverySegmentId],
            existingAccount?.LastAuthenticatedAtUtc,
            existingAccount?.LastSuccessfulRunAtUtc,
            existingAccount?.LastFailedRunAtUtc,
            existingAccount?.LastErrorReasonCode,
            existingAccount?.LastErrorMessage,
            existingAccount?.SuspendedUntilUtc,
            existingAccount?.CreatedAtUtc ?? now,
            now,
            existingAccount?.Version ?? 0);
        bool accountChanged = existingAccount is null
            || !string.Equals(existingAccount.DisplayName, desiredAccount.DisplayName, StringComparison.Ordinal)
            || !string.Equals(
                existingAccount.BrowserProfileRelativePath.Replace('\\', '/'),
                desiredAccount.BrowserProfileRelativePath,
                StringComparison.Ordinal)
            || !existingAccount.AssignedSegmentIds.SequenceEqual(
                desiredAccount.AssignedSegmentIds,
                StringComparer.Ordinal);
        if (accountChanged)
        {
            _ = await browserAccounts.UpsertAsync(desiredAccount, cancellationToken).ConfigureAwait(false);
        }

        DiscoverySegment? existingSegment = await discoverySegments.GetAsync(
            FounderScoutSimpleMode.DiscoverySegmentId,
            cancellationToken).ConfigureAwait(false);
        var desiredSegment = new DiscoverySegment(
            FounderScoutSimpleMode.DiscoverySegmentId,
            "Startup School co-founder matching",
            existingSegment?.Enabled ?? true,
            10,
            FounderScoutSimpleMode.CreateSegmentConfigurationJson(),
            FounderScoutSimpleMode.BrowserAccountId,
            existingSegment?.LastRunAtUtc,
            existingSegment?.ViewedCount ?? 0,
            existingSegment?.NewCount ?? 0,
            existingSegment?.DuplicateCount ?? 0,
            existingSegment?.ErrorCount ?? 0,
            existingSegment?.ConsecutiveLowYieldRuns ?? 0,
            existingSegment?.PausedUntilUtc,
            existingSegment?.CreatedAtUtc ?? now,
            now,
            existingSegment?.Version ?? 0);
        bool segmentChanged = existingSegment is null
            || !string.Equals(existingSegment.Name, desiredSegment.Name, StringComparison.Ordinal)
            || existingSegment.Priority != desiredSegment.Priority
            || !string.Equals(existingSegment.ConfigurationJson, desiredSegment.ConfigurationJson, StringComparison.Ordinal)
            || !string.Equals(existingSegment.AssignedAccountId, desiredSegment.AssignedAccountId, StringComparison.Ordinal);
        if (segmentChanged)
        {
            _ = await discoverySegments.UpsertAsync(desiredSegment, cancellationToken).ConfigureAwait(false);
        }

        return new(saved.CurrentChanged, agentEnabled, accountChanged, segmentChanged);
    }
}
