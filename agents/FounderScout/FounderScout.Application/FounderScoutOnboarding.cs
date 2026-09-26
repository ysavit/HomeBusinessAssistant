using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FounderScout.Domain;
using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Secrets;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;

namespace FounderScout.Application;

/// <summary>Stable specialized Founder Scout onboarding step keys.</summary>
public static class FounderScoutOnboardingSteps
{
    /// <summary>Purpose, mode, consent, and privacy.</summary>
    public const string Purpose = "purpose";
    /// <summary>Dedicated browser account.</summary>
    public const string BrowserAccount = "browser-account";
    /// <summary>Discovery source and limits.</summary>
    public const string Source = "source-limits";
    /// <summary>Screening, persona, draft, and retention policy.</summary>
    public const string Policy = "screening-fit";
    /// <summary>Optional production AI provider.</summary>
    public const string Ai = "ai-provider";
    /// <summary>Explicit browser, provider, and authentication checks.</summary>
    public const string Checks = "readiness-checks";
    /// <summary>Final readiness review.</summary>
    public const string Review = "review";
}

/// <summary>Stable persisted safe readiness check keys.</summary>
public static class FounderScoutOnboardingChecks
{
    /// <summary>Explicit live-source acknowledgement.</summary>
    public const string LiveConsent = "founder-scout.live-consent";
    /// <summary>Local Playwright runtime readiness.</summary>
    public const string BrowserRuntime = "founder-scout.browser-runtime";
    /// <summary>Explicit production provider readiness.</summary>
    public const string Provider = "founder-scout.provider";
    /// <summary>Latest Runner-backed headed authentication.</summary>
    public const string Authentication = "founder-scout.authentication";
}

/// <summary>The user-selected processing boundary.</summary>
public enum FounderScoutOnboardingMode
{
    /// <summary>Capture and deterministic screening without deep AI evaluation.</summary>
    CaptureAndScreen = 0,
    /// <summary>Capture, screen, and deep Responses-based evaluation.</summary>
    DeepAnalysis = 1,
}

/// <summary>Safe stable classifications for an explicit, potentially chargeable provider check.</summary>
public enum FounderProviderReadinessCode
{
    /// <summary>The minimal structured request succeeded.</summary>
    Success = 0,
    /// <summary>The provider rejected the credential.</summary>
    InvalidCredential = 1,
    /// <summary>The provider denied access.</summary>
    Forbidden = 2,
    /// <summary>The endpoint or deployment was not found.</summary>
    DeploymentNotFound = 3,
    /// <summary>The provider reported quota exhaustion or throttling.</summary>
    QuotaOrRateLimit = 4,
    /// <summary>The request encountered a network, TLS, or timeout failure.</summary>
    NetworkOrTls = 5,
    /// <summary>The provider response or rejection was not a valid readiness response.</summary>
    MalformedResponse = 6,
    /// <summary>Local provider settings were invalid.</summary>
    InvalidConfiguration = 7,
}

/// <summary>One provider probe request. The API key is transient and must never be logged or persisted.</summary>
public sealed record FounderProviderReadinessRequest(
    FounderScoutAiSettings Settings,
    string ApiKey,
    TimeSpan Timeout);

/// <summary>A bounded provider result safe for onboarding persistence.</summary>
public sealed record FounderProviderReadinessResult(
    FounderProviderReadinessCode Code,
    string ReasonCode,
    string Message,
    TimeSpan Elapsed)
{
    /// <summary>Gets whether the provider passed.</summary>
    public bool IsSuccess => Code == FounderProviderReadinessCode.Success;
}

/// <summary>Infrastructure boundary for the explicit one-shot Responses provider check.</summary>
public interface IFounderProviderReadinessProbe
{
    /// <summary>Sends exactly one minimal structured request without candidate or persona data.</summary>
    ValueTask<FounderProviderReadinessResult> TestAsync(
        FounderProviderReadinessRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>One safe browser account mutation from the specialized wizard.</summary>
public sealed record SaveFounderScoutBrowserAccountRequest(
    Guid SessionId,
    long ExpectedSelectionRevision,
    string AccountId,
    string DisplayName,
    string? SegmentId,
    string ActorId);

/// <summary>One safe discovery segment mutation from the specialized wizard.</summary>
public sealed record SaveFounderScoutSegmentRequest(
    Guid SessionId,
    long ExpectedSelectionRevision,
    string SegmentId,
    string Name,
    string AccountId,
    int MaximumPages,
    string? SafeFilter,
    string ActorId);

/// <summary>Current typed wizard projection. It contains no secret or full browser path.</summary>
public sealed record FounderScoutOnboardingEditor(
    OnboardingSession Session,
    OnboardingAgentSelection Selection,
    AgentConfigurationRecord ConfigurationRecord,
    FounderScoutConfiguration Configuration,
    IReadOnlyList<BrowserAccount> BrowserAccounts,
    IReadOnlyList<DiscoverySegment> Segments,
    IReadOnlyDictionary<string, OnboardingCheckRecord> Checks,
    bool SecretExists,
    ScheduleOccurrenceRecord? AuthenticationOccurrence,
    AgentRunRecord? AuthenticationRun,
    IReadOnlyList<string> Blockers)
{
    /// <summary>Gets the selected capture/screen or deep-analysis mode.</summary>
    public FounderScoutOnboardingMode Mode => Configuration.Analysis.Enabled
        ? FounderScoutOnboardingMode.DeepAnalysis
        : FounderScoutOnboardingMode.CaptureAndScreen;
}

/// <summary>Specialized onboarding operations used by the Host Razor workflow.</summary>
public interface IFounderScoutOnboardingService
{
    /// <summary>Loads current durable typed setup and safe readiness evidence.</summary>
    ValueTask<FounderScoutOnboardingEditor> GetAsync(Guid sessionId, string actorId, CancellationToken cancellationToken = default);
    /// <summary>Persists an explicit live-source acknowledgement or local-only non-applicability.</summary>
    ValueTask SaveLiveConsentAsync(Guid sessionId, bool liveSource, bool acknowledged, string actorId, CancellationToken cancellationToken = default);
    /// <summary>Saves one typed configuration step through immutable revisioning.</summary>
    ValueTask<OnboardingAgentSelection> SaveConfigurationAsync(Guid sessionId, long expectedSelectionRevision, string stepKey, FounderScoutConfiguration configuration, long expectedConfigurationRevision, string expectedConfigurationHash, string actorId, CancellationToken cancellationToken = default);
    /// <summary>Saves one disabled-first canonical browser account.</summary>
    ValueTask<OnboardingAgentSelection> SaveBrowserAccountAsync(SaveFounderScoutBrowserAccountRequest request, CancellationToken cancellationToken = default);
    /// <summary>Saves one bounded code-owned Startup School segment.</summary>
    ValueTask<OnboardingAgentSelection> SaveSegmentAsync(SaveFounderScoutSegmentRequest request, CancellationToken cancellationToken = default);
    /// <summary>Replaces the protected provider key without returning it.</summary>
    ValueTask SetSecretAsync(Guid sessionId, long expectedSelectionRevision, string secret, string actorId, CancellationToken cancellationToken = default);
    /// <summary>Deletes the protected provider key.</summary>
    ValueTask DeleteSecretAsync(Guid sessionId, long expectedSelectionRevision, string actorId, CancellationToken cancellationToken = default);
    /// <summary>Runs the no-download local browser runtime check.</summary>
    ValueTask<BrowserRuntimeStatus> TestBrowserRuntimeAsync(Guid sessionId, string actorId, CancellationToken cancellationToken = default);
    /// <summary>Runs the explicitly confirmed one-shot provider check.</summary>
    ValueTask<FounderProviderReadinessResult> TestProviderAsync(Guid sessionId, bool confirmedPotentialCharge, string actorId, CancellationToken cancellationToken = default);
    /// <summary>Creates and dispatches one confirmed Runner-backed headed authentication occurrence.</summary>
    ValueTask<OccurrenceDispatchResult> AuthenticateAsync(Guid sessionId, string accountId, bool confirmed, string actorId, CancellationToken cancellationToken = default);
    /// <summary>Recomputes blockers and marks the selection ready or needing attention.</summary>
    ValueTask<OnboardingAgentSelection> ReviewAsync(Guid sessionId, long expectedSelectionRevision, string actorId, CancellationToken cancellationToken = default);
}

/// <summary>Explicit typed Stage 20 adapter registered ahead of the generic fallback.</summary>
public sealed class FounderScoutOnboardingAdapter(
    IAgentConfigurationService configurations,
    ISecretStore secrets) : IAgentOnboardingAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public AgentOnboardingAdapterDescriptor Descriptor { get; } = new(
        "founder-scout.specialized",
        "20.1",
        FounderScoutDefaults.AgentId,
        AgentOnboardingSupport.Specialized,
        true,
        [
            new(FounderScoutOnboardingSteps.Purpose, "Purpose and privacy", "Choose live or local-only setup and review safety boundaries."),
            new(FounderScoutOnboardingSteps.BrowserAccount, "Browser account", "Create a dedicated disabled-first browser account."),
            new(FounderScoutOnboardingSteps.Source, "Source and limits", "Configure one conservative Startup School segment."),
            new(FounderScoutOnboardingSteps.Policy, "Screening and fit", "Review deterministic screening, persona, drafts, and retention."),
            new(FounderScoutOnboardingSteps.Ai, "AI provider", "Configure an optional production Responses provider and protected key."),
            new(FounderScoutOnboardingSteps.Checks, "Readiness checks", "Test the local browser and optional provider, then authenticate explicitly."),
            new(FounderScoutOnboardingSteps.Review, "Review", "Confirm blockers before the Stage 22 diagnostic."),
        ]);

    /// <inheritdoc />
    public async ValueTask<AgentOnboardingAssessment> AssessAsync(AgentOnboardingContext context, CancellationToken cancellationToken = default)
    {
        FounderScoutConfiguration? configuration = Parse(context.Configuration);
        bool deep = configuration?.Analysis.Enabled == true;
        bool secretExists = !deep || await secrets.ExistsAsync(FounderScoutDefaults.ApiKeyReference, cancellationToken).ConfigureAwait(false);
        bool reviewed = context.Selection?.AdapterId == Descriptor.AdapterId
            && context.Selection.ProgressStatus == OnboardingAgentProgressStatus.ReadyForValidation;
        var prerequisites = new List<AgentOnboardingPrerequisite>
        {
            new("typed-configuration", "Typed configuration", configuration is null ? OnboardingCheckStatus.Blocked : OnboardingCheckStatus.Passed, configuration is null ? "founder-scout.configuration.invalid" : "founder-scout.configuration.valid", configuration is null ? "The current typed configuration is invalid." : "The typed configuration is valid.", true),
            new("specialized-review", "Specialized review", reviewed ? OnboardingCheckStatus.Passed : OnboardingCheckStatus.Unknown, reviewed ? "founder-scout.onboarding.ready" : "founder-scout.onboarding.review-required", reviewed ? "Specialized setup is ready for validation." : "Complete the Founder Scout guided setup.", true),
        };
        if (deep)
        {
            prerequisites.Add(new("provider-secret", "AI key", secretExists ? OnboardingCheckStatus.Passed : OnboardingCheckStatus.Blocked, secretExists ? "secret.configured" : "secret.missing", secretExists ? "A protected provider key exists." : "Deep analysis requires a protected provider key.", true));
        }

        return new(
            configuration is not null,
            !reviewed,
            reviewed && configuration is not null && secretExists,
            reviewed ? "founder-scout.ready-for-validation" : "founder-scout.specialized-review-required",
            reviewed ? "Founder Scout is ready for the Stage 22 diagnostic." : "Complete the specialized Founder Scout wizard.",
            prerequisites,
            [new("api-key", "Provider API key", FounderScoutDefaults.ApiKeyReference, deep, secretExists, secretExists ? "secret.configured" : "secret.missing")],
            new("diagnose", "{}", false),
            new(context.Definition.SupportsScheduling, context.Definition.SupportsManualRun, context.Definition.RequiresInteractiveUserSession, ["authenticate", "diagnose", "discover", "analyze", "run"]),
            reviewed ? "Ready for a guided diagnostic; the agent remains unchanged." : "Specialized setup is incomplete.");
    }

    /// <inheritdoc />
    public ValueTask<AgentConfigurationRecord?> LoadConfigurationAsync(AgentId agentId, CancellationToken cancellationToken = default) =>
        configurations.GetCurrentAsync(agentId, cancellationToken);

    /// <inheritdoc />
    public ValueTask<SaveConfigurationResult> SaveConfigurationAsync(SaveConfigurationRequest request, CancellationToken cancellationToken = default) =>
        configurations.SaveAsync(request, cancellationToken);

    private static FounderScoutConfiguration? Parse(AgentConfigurationRecord? record)
    {
        if (record is null) return null;
        try
        {
            return JsonSerializer.Deserialize<FounderScoutConfiguration>(record.CurrentRevision.CanonicalConfigurationJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Durable specialized Founder Scout onboarding orchestration.</summary>
public sealed partial class FounderScoutOnboardingService(
    IAgentConfigurationService configurations,
    ISecretStore secrets,
    IBrowserAccountRepository accounts,
    IDiscoverySegmentRepository segments,
    IOnboardingRepository onboarding,
    IOnboardingAgentSelectionRepository selections,
    IOccurrenceRepository occurrences,
    IAgentRunRepository runs,
    IOccurrenceRunnerDispatcher dispatcher,
    IBrowserRuntimeFactory browserRuntime,
    IFounderProviderReadinessProbe providerProbe,
    IAuditWriter audit,
    TimeProvider timeProvider) : IFounderScoutOnboardingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] StableSteps =
    [
        FounderScoutOnboardingSteps.Purpose,
        FounderScoutOnboardingSteps.BrowserAccount,
        FounderScoutOnboardingSteps.Source,
        FounderScoutOnboardingSteps.Policy,
        FounderScoutOnboardingSteps.Ai,
        FounderScoutOnboardingSteps.Checks,
        FounderScoutOnboardingSteps.Review,
    ];

    /// <inheritdoc />
    public async ValueTask<FounderScoutOnboardingEditor> GetAsync(Guid sessionId, string actorId, CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId);
        (OnboardingSession session, OnboardingAgentSelection selection) = await LoadSelectionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        AgentConfigurationRecord configurationRecord = await configurations.GetCurrentAsync(FounderScoutDefaults.AgentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Founder Scout has no current configuration.");
        FounderScoutConfiguration configuration = Parse(configurationRecord);
        IReadOnlyList<OnboardingCheckRecord> allChecks = await onboarding.GetChecksAsync(sessionId, cancellationToken).ConfigureAwait(false);
        Dictionary<string, OnboardingCheckRecord> checks = allChecks
            .Where(item => item.AgentId == FounderScoutDefaults.AgentId)
            .ToDictionary(item => item.CheckKey, StringComparer.Ordinal);
        ScheduleOccurrenceRecord? authenticationOccurrence = TryGetOccurrenceId(checks.GetValueOrDefault(FounderScoutOnboardingChecks.Authentication), out OccurrenceId occurrenceId)
            ? await occurrences.GetAsync(occurrenceId, cancellationToken).ConfigureAwait(false)
            : null;
        AgentRunRecord? authenticationRun = authenticationOccurrence is null
            ? null
            : await runs.GetForOccurrenceAsync(authenticationOccurrence.Id, cancellationToken).ConfigureAwait(false);
        bool secretExists = await secrets.ExistsAsync(FounderScoutDefaults.ApiKeyReference, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<BrowserAccount> accountList = await accounts.ListAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DiscoverySegment> segmentList = await GetSegmentsAsync(accountList, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> blockers = BuildBlockers(configuration, accountList, segmentList, checks, secretExists);
        return new(session, selection, configurationRecord, configuration, accountList, segmentList, checks, secretExists, authenticationOccurrence, authenticationRun, blockers);
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingAgentSelection> SaveConfigurationAsync(
        Guid sessionId,
        long expectedSelectionRevision,
        string stepKey,
        FounderScoutConfiguration configuration,
        long expectedConfigurationRevision,
        string expectedConfigurationHash,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ValidateStep(stepKey);
        ValidateActor(actorId);
        (OnboardingSession _, OnboardingAgentSelection selection) = await LoadSelectionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        EnsureRevision(selection, expectedSelectionRevision);
        FounderScoutConfiguration normalized = configuration with
        {
            SchemaVersion = FounderScoutConfiguration.CurrentSchemaVersion,
            Discovery = configuration.Discovery with { AutomaticFailoverAfterEnforcementSignal = false },
            Ai = configuration.Ai with { ApiKeySecretReference = FounderScoutDefaults.ApiKeyReference.Value },
            StartupSchool = NormalizeSource(configuration.StartupSchool),
        };
        SaveConfigurationResult saved = await configurations.SaveAsync(new(
            FounderScoutDefaults.AgentId,
            FounderScoutConfiguration.CurrentSchemaVersion,
            JsonSerializer.SerializeToElement(normalized, JsonOptions),
            actorId,
            $"Founder Scout onboarding: {stepKey}.",
            Guid.NewGuid(),
            expectedConfigurationRevision,
            expectedConfigurationHash), cancellationToken).ConfigureAwait(false);
        return await UpdateSelectionAsync(selection, expectedSelectionRevision, stepKey, saved.Revision, OnboardingAgentProgressStatus.Configuring, "founder-scout.configuration.saved", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingAgentSelection> SaveBrowserAccountAsync(SaveFounderScoutBrowserAccountRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateActor(request.ActorId);
        ValidateStableId(request.AccountId, nameof(request.AccountId));
        ValidateName(request.DisplayName, nameof(request.DisplayName));
        if (request.SegmentId is not null) ValidateStableId(request.SegmentId, nameof(request.SegmentId));
        (OnboardingSession _, OnboardingAgentSelection selection) = await LoadSelectionAsync(request.SessionId, cancellationToken).ConfigureAwait(false);
        EnsureRevision(selection, request.ExpectedSelectionRevision);
        BrowserAccount? existing = await accounts.GetAsync(request.AccountId, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = timeProvider.GetUtcNow().ToUniversalTime();
        string[] assigned = (existing?.AssignedSegmentIds ?? [])
            .Concat(request.SegmentId is null ? [] : [request.SegmentId])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var account = new BrowserAccount(
            request.AccountId,
            request.DisplayName.Trim(),
            $"browser/{request.AccountId}",
            existing?.Enabled ?? false,
            existing?.SessionStatus ?? BrowserSessionStatus.Disabled,
            assigned,
            existing?.LastAuthenticatedAtUtc,
            existing?.LastSuccessfulRunAtUtc,
            existing?.LastFailedRunAtUtc,
            existing?.LastErrorReasonCode,
            existing?.LastErrorMessage,
            existing?.SuspendedUntilUtc,
            existing?.CreatedAtUtc ?? now,
            now,
            existing?.Version ?? 0);
        if (existing is null
            || existing.DisplayName != account.DisplayName
            || !existing.AssignedSegmentIds.SequenceEqual(account.AssignedSegmentIds, StringComparer.Ordinal)
            || existing.BrowserProfileRelativePath.Replace('\\', '/') != account.BrowserProfileRelativePath)
        {
            _ = await accounts.UpsertAsync(account, cancellationToken).ConfigureAwait(false);
        }
        await WriteAuditAsync(request.ActorId, "founder-scout.onboarding.account-saved", "browser-account", request.AccountId, new { accountId = request.AccountId, profile = account.BrowserProfileRelativePath }, cancellationToken).ConfigureAwait(false);
        return await UpdateSelectionAsync(selection, request.ExpectedSelectionRevision, FounderScoutOnboardingSteps.Source, null, OnboardingAgentProgressStatus.Configuring, "founder-scout.account.saved", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingAgentSelection> SaveSegmentAsync(SaveFounderScoutSegmentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateActor(request.ActorId);
        ValidateStableId(request.SegmentId, nameof(request.SegmentId));
        ValidateStableId(request.AccountId, nameof(request.AccountId));
        ValidateName(request.Name, nameof(request.Name));
        if (request.MaximumPages is < 1 or > 25) throw new ArgumentOutOfRangeException(nameof(request), "Maximum pages must be between 1 and 25.");
        if (request.SafeFilter is { Length: > 200 }) throw new ArgumentException("The optional safe filter is too long.", nameof(request));
        (OnboardingSession _, OnboardingAgentSelection selection) = await LoadSelectionAsync(request.SessionId, cancellationToken).ConfigureAwait(false);
        EnsureRevision(selection, request.ExpectedSelectionRevision);
        BrowserAccount account = await accounts.GetAsync(request.AccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The selected browser account does not exist.");
        DiscoverySegment? existing = await segments.GetAsync(request.SegmentId, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = timeProvider.GetUtcNow().ToUniversalTime();
        string json = JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            source = "startup-school",
            route = "cofounder-matching",
            maximumPages = request.MaximumPages,
            safeFilter = string.IsNullOrWhiteSpace(request.SafeFilter) ? null : request.SafeFilter.Trim(),
        }, JsonOptions);
        var segment = new DiscoverySegment(request.SegmentId, request.Name.Trim(), true, existing?.Priority ?? 10, json, request.AccountId, existing?.LastRunAtUtc, existing?.ViewedCount ?? 0, existing?.NewCount ?? 0, existing?.DuplicateCount ?? 0, existing?.ErrorCount ?? 0, existing?.ConsecutiveLowYieldRuns ?? 0, existing?.PausedUntilUtc, existing?.CreatedAtUtc ?? now, now, existing?.Version ?? 0);
        if (existing is null
            || existing.Name != segment.Name
            || existing.ConfigurationJson != segment.ConfigurationJson
            || existing.AssignedAccountId != segment.AssignedAccountId)
        {
            _ = await segments.UpsertAsync(segment, cancellationToken).ConfigureAwait(false);
        }
        if (!account.AssignedSegmentIds.Contains(request.SegmentId, StringComparer.Ordinal))
        {
            _ = await accounts.UpsertAsync(account with { AssignedSegmentIds = [.. account.AssignedSegmentIds, request.SegmentId], UpdatedAtUtc = now }, cancellationToken).ConfigureAwait(false);
        }
        await WriteAuditAsync(request.ActorId, "founder-scout.onboarding.segment-saved", "discovery-segment", request.SegmentId, new { segmentId = request.SegmentId, accountId = request.AccountId, request.MaximumPages }, cancellationToken).ConfigureAwait(false);
        return await UpdateSelectionAsync(selection, request.ExpectedSelectionRevision, FounderScoutOnboardingSteps.Policy, null, OnboardingAgentProgressStatus.Configuring, "founder-scout.segment.saved", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask SetSecretAsync(Guid sessionId, long expectedSelectionRevision, string secret, string actorId, CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId);
        if (string.IsNullOrWhiteSpace(secret) || secret.Length > 65_536) throw new ArgumentException("The provider key is empty or too large.", nameof(secret));
        (OnboardingSession _, OnboardingAgentSelection selection) = await LoadSelectionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        EnsureRevision(selection, expectedSelectionRevision);
        await secrets.SetAsync(FounderScoutDefaults.ApiKeyReference, secret, cancellationToken).ConfigureAwait(false);
        await SaveCheckAsync(sessionId, ProviderCheck(OnboardingCheckStatus.Unknown, "founder-scout.provider.not-tested", "The provider key changed; run the explicit provider test again.", new { provider = "unvalidated" }), actorId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DeleteSecretAsync(Guid sessionId, long expectedSelectionRevision, string actorId, CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId);
        (OnboardingSession _, OnboardingAgentSelection selection) = await LoadSelectionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        EnsureRevision(selection, expectedSelectionRevision);
        _ = await secrets.DeleteAsync(FounderScoutDefaults.ApiKeyReference, cancellationToken).ConfigureAwait(false);
        await SaveCheckAsync(sessionId, ProviderCheck(OnboardingCheckStatus.Blocked, "founder-scout.provider.secret-missing", "No protected provider key is configured.", new { provider = "unconfigured" }), actorId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<BrowserRuntimeStatus> TestBrowserRuntimeAsync(Guid sessionId, string actorId, CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId);
        FounderScoutConfiguration configuration = Parse(await configurations.GetCurrentAsync(FounderScoutDefaults.AgentId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Founder Scout has no current configuration."));
        BrowserRuntimeStatus result = await browserRuntime.DiagnoseAsync(configuration.StartupSchool?.BrowserChannel, cancellationToken).ConfigureAwait(false);
        await SaveCheckAsync(sessionId, Check(FounderScoutOnboardingChecks.BrowserRuntime, "Browser runtime", result.IsAvailable ? OnboardingCheckStatus.Passed : OnboardingCheckStatus.Blocked, result.ReasonCode, result.Message, new { result.RuntimeName, result.PlaywrightVersion, result.BrowserVersion }), actorId, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<FounderProviderReadinessResult> TestProviderAsync(Guid sessionId, bool confirmedPotentialCharge, string actorId, CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId);
        if (!confirmedPotentialCharge) throw new InvalidOperationException("Confirm that this one-shot network test may incur a small provider charge.");
        FounderScoutConfiguration configuration = Parse(await configurations.GetCurrentAsync(FounderScoutDefaults.AgentId, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Founder Scout has no current configuration."));
        string? secret = await secrets.GetAsync(FounderScoutDefaults.ApiKeyReference, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(secret))
        {
            var missing = new FounderProviderReadinessResult(FounderProviderReadinessCode.InvalidConfiguration, "founder-scout.provider.secret-missing", "Configure a protected provider key before testing.", TimeSpan.Zero);
            await PersistProviderResultAsync(sessionId, configuration.Ai, missing, actorId, cancellationToken).ConfigureAwait(false);
            return missing;
        }
        FounderProviderReadinessResult result = await providerProbe.TestAsync(new(configuration.Ai, secret, TimeSpan.FromSeconds(Math.Min(configuration.Ai.RequestTimeoutSeconds, 30))), cancellationToken).ConfigureAwait(false);
        await PersistProviderResultAsync(sessionId, configuration.Ai, result, actorId, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<OccurrenceDispatchResult> AuthenticateAsync(Guid sessionId, string accountId, bool confirmed, string actorId, CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId);
        if (!confirmed) throw new InvalidOperationException("Confirm that a headed browser will open for manual authentication.");
        ValidateStableId(accountId, nameof(accountId));
        (OnboardingSession _, OnboardingAgentSelection selection) = await LoadSelectionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        BrowserAccount account = await accounts.GetAsync(accountId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The selected browser account does not exist.");
        IReadOnlyList<OnboardingCheckRecord> checks = await onboarding.GetChecksAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (checks.SingleOrDefault(item => item.AgentId == FounderScoutDefaults.AgentId && item.CheckKey == FounderScoutOnboardingChecks.BrowserRuntime)?.Status != OnboardingCheckStatus.Passed)
        {
            throw new InvalidOperationException("Run the browser readiness check successfully before authentication.");
        }
        AgentConfigurationRecord configuration = await configurations.GetCurrentAsync(FounderScoutDefaults.AgentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Founder Scout has no current configuration.");
        DateTimeOffset now = timeProvider.GetUtcNow().ToUniversalTime();
        var occurrenceId = OccurrenceId.New();
        ScheduleOccurrenceRecord occurrence = await occurrences.CreateIfAbsentAsync(new(
            occurrenceId,
            null,
            FounderScoutDefaults.AgentId,
            "authenticate",
            JsonSerializer.Serialize(new { accountId }, JsonOptions),
            configuration.CurrentRevisionId,
            now,
            TriggerType.ManualUi,
            0,
            null,
            OccurrenceStatus.Ready,
            RequiresWake: false,
            KeepSystemAwake: true,
            KeepDisplayOn: true,
            AllowDisabledAgent: true), cancellationToken).ConfigureAwait(false);
        await SaveCheckAsync(sessionId, Check(FounderScoutOnboardingChecks.Authentication, "Headed authentication", OnboardingCheckStatus.Unknown, "founder-scout.authentication.started", "The confirmed Runner-backed authentication occurrence was started.", new { occurrenceId = occurrence.Id.Value, accountId }), actorId, cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync(actorId, "founder-scout.onboarding.authentication-requested", "occurrence", occurrence.Id.ToString(), new { occurrenceId = occurrence.Id.Value, accountId, allowDisabledAgent = true }, cancellationToken).ConfigureAwait(false);
        _ = selection;
        return await dispatcher.DispatchAsync(occurrence.Id, actorId, Guid.NewGuid(), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingAgentSelection> ReviewAsync(Guid sessionId, long expectedSelectionRevision, string actorId, CancellationToken cancellationToken = default)
    {
        FounderScoutOnboardingEditor editor = await GetAsync(sessionId, actorId, cancellationToken).ConfigureAwait(false);
        EnsureRevision(editor.Selection, expectedSelectionRevision);
        OnboardingAgentProgressStatus progress = editor.Blockers.Count == 0 ? OnboardingAgentProgressStatus.ReadyForValidation : OnboardingAgentProgressStatus.NeedsAttention;
        return await UpdateSelectionAsync(editor.Selection, expectedSelectionRevision, FounderScoutOnboardingSteps.Review, editor.ConfigurationRecord.CurrentRevision, progress, editor.Blockers.Count == 0 ? "founder-scout.ready-for-validation" : "founder-scout.prerequisites-blocked", cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<(OnboardingSession Session, OnboardingAgentSelection Selection)> LoadSelectionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        OnboardingSession session = await onboarding.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The onboarding session does not exist.");
        OnboardingAgentSelection selection = (await selections.GetSelectionsAsync(sessionId, cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(item => item.AgentId == FounderScoutDefaults.AgentId)
            ?? throw new InvalidOperationException("Founder Scout is not part of this onboarding session.");
        if (selection.SelectionStatus != OnboardingAgentSelectionStatus.Selected) throw new InvalidOperationException("Founder Scout is not selected for setup.");
        return (session, selection);
    }

    private async ValueTask<OnboardingAgentSelection> UpdateSelectionAsync(OnboardingAgentSelection selection, long expectedRevision, string stepKey, ConfigurationRevisionRecord? revision, OnboardingAgentProgressStatus progress, string reasonCode, CancellationToken cancellationToken) =>
        await selections.UpdateProgressAsync(new(
            selection.SessionId,
            selection.AgentId,
            expectedRevision,
            OnboardingAgentSelectionStatus.Selected,
            progress,
            "founder-scout.specialized",
            stepKey,
            selection.ReviewedManifestVersion,
            FounderScoutConfiguration.CurrentSchemaVersion,
            revision?.Id ?? selection.SavedConfigurationRevisionId,
            revision?.ConfigurationHash ?? selection.SavedConfigurationHash,
            reasonCode,
            null), cancellationToken).ConfigureAwait(false);

    private async ValueTask<IReadOnlyList<DiscoverySegment>> GetSegmentsAsync(IReadOnlyList<BrowserAccount> accountList, CancellationToken cancellationToken)
    {
        var result = new List<DiscoverySegment>();
        foreach (string id in accountList.SelectMany(item => item.AssignedSegmentIds).Distinct(StringComparer.Ordinal))
        {
            DiscoverySegment? segment = await segments.GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (segment is not null) result.Add(segment);
        }
        return result.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }

    private static List<string> BuildBlockers(FounderScoutConfiguration configuration, IReadOnlyList<BrowserAccount> accounts, IReadOnlyList<DiscoverySegment> segments, IReadOnlyDictionary<string, OnboardingCheckRecord> checks, bool secretExists)
    {
        var blockers = new List<string>();
        if (configuration.Discovery.Enabled)
        {
            if (checks.GetValueOrDefault(FounderScoutOnboardingChecks.LiveConsent)?.Status != OnboardingCheckStatus.Passed) blockers.Add("Acknowledge live-source authorization and terms responsibility.");
            if (checks.GetValueOrDefault(FounderScoutOnboardingChecks.BrowserRuntime)?.Status != OnboardingCheckStatus.Passed) blockers.Add("Pass the local browser-runtime check.");
            if (!accounts.Any(item => item.Enabled && item.SessionStatus == BrowserSessionStatus.Healthy)) blockers.Add("Complete headed authentication for a browser account.");
            if (!segments.Any(item => item.Enabled)) blockers.Add("Configure one enabled discovery segment.");
        }
        if (configuration.Analysis.Enabled)
        {
            if (!secretExists) blockers.Add("Store a protected AI provider key.");
            if (checks.GetValueOrDefault(FounderScoutOnboardingChecks.Provider)?.Status != OnboardingCheckStatus.Passed) blockers.Add("Pass the explicit AI provider test.");
        }
        return blockers;
    }

    /// <inheritdoc />
    public async ValueTask SaveLiveConsentAsync(Guid sessionId, bool liveSource, bool acknowledged, string actorId, CancellationToken cancellationToken = default)
    {
        OnboardingCheckStatus status = liveSource ? acknowledged ? OnboardingCheckStatus.Passed : OnboardingCheckStatus.Blocked : OnboardingCheckStatus.NotApplicable;
        string code = liveSource ? acknowledged ? "founder-scout.consent.acknowledged" : "founder-scout.consent.required" : "founder-scout.consent.not-applicable";
        string message = liveSource ? acknowledged ? "The owner acknowledged live-source authorization and terms responsibility." : "Live setup requires an explicit authorization acknowledgment." : "Fixture-only/local setup does not claim live-source consent.";
        await SaveCheckAsync(sessionId, Check(FounderScoutOnboardingChecks.LiveConsent, "Live-source acknowledgment", status, code, message, new { liveSource, acknowledged = liveSource && acknowledged }), actorId, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask PersistProviderResultAsync(Guid sessionId, FounderScoutAiSettings settings, FounderProviderReadinessResult result, string actorId, CancellationToken cancellationToken)
    {
        string endpointIdentity = EndpointIdentity(settings.Endpoint);
        string latency = result.Elapsed < TimeSpan.FromSeconds(1) ? "under-1s" : result.Elapsed < TimeSpan.FromSeconds(5) ? "1-5s" : result.Elapsed < TimeSpan.FromSeconds(15) ? "5-15s" : "15s-plus";
        await SaveCheckAsync(sessionId, ProviderCheck(result.IsSuccess ? OnboardingCheckStatus.Passed : OnboardingCheckStatus.Blocked, result.ReasonCode, result.Message, new { provider = settings.Provider, endpoint = endpointIdentity, model = settings.Deployment, latency, result = result.Code.ToString() }), actorId, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask SaveCheckAsync(Guid sessionId, OnboardingReadinessCheckResult result, string actorId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            OnboardingSession session = await onboarding.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The onboarding session does not exist.");
            try
            {
                _ = await onboarding.SaveCheckBatchAsync(sessionId, session.Revision, [result], actorId, Guid.NewGuid(), cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OnboardingConcurrencyException) when (attempt < 2)
            {
            }
        }
        throw new OnboardingConcurrencyException();
    }

    private OnboardingReadinessCheckResult ProviderCheck(OnboardingCheckStatus status, string reasonCode, string message, object details) =>
        Check(FounderScoutOnboardingChecks.Provider, "AI provider", status, reasonCode, message, details);

    private OnboardingReadinessCheckResult Check(string key, string title, OnboardingCheckStatus status, string reasonCode, string message, object details) => new(
        new(key, OnboardingCheckScope.Agent, title, title, true, true, TimeSpan.FromSeconds(30), AgentId: FounderScoutDefaults.AgentId),
        status,
        reasonCode,
        message,
        timeProvider.GetUtcNow().ToUniversalTime(),
        null,
        "1.0",
        JsonSerializer.SerializeToElement(details, JsonOptions));

    private async ValueTask WriteAuditAsync(string actorId, string action, string targetType, string targetId, object data, CancellationToken cancellationToken) =>
        _ = await audit.WriteAsync(new(AuditActorType.User, actorId, action, targetType, targetId, AuditOutcome.Succeeded, Guid.NewGuid(), null, JsonSerializer.SerializeToElement(data, JsonOptions)), cancellationToken).ConfigureAwait(false);

    private static StartupSchoolSourceOptions NormalizeSource(StartupSchoolSourceOptions? source)
    {
        StartupSchoolSourceOptions value = source ?? StartupSchoolSourceOptions.Default;
        return value with
        {
            AdapterVersion = StartupSchoolSourceOptions.CurrentAdapterVersion,
            EntryUrl = StartupSchoolSourceOptions.Default.EntryUrl,
            AllowedHosts = StartupSchoolSourceOptions.Default.AllowedHosts,
            AuthenticatedLocators = StartupSchoolSourceOptions.Default.AuthenticatedLocators,
            LoginLocators = StartupSchoolSourceOptions.Default.LoginLocators,
            ProfileLinkLocators = StartupSchoolSourceOptions.Default.ProfileLinkLocators,
            ProfileRootLocators = StartupSchoolSourceOptions.Default.ProfileRootLocators,
            DisplayNameLocators = StartupSchoolSourceOptions.Default.DisplayNameLocators,
            NextPageLocators = StartupSchoolSourceOptions.Default.NextPageLocators,
            LoadMoreLocators = StartupSchoolSourceOptions.Default.LoadMoreLocators,
            ChallengeLocators = StartupSchoolSourceOptions.Default.ChallengeLocators,
            ThrottleLocators = StartupSchoolSourceOptions.Default.ThrottleLocators,
            AccessDeniedLocators = StartupSchoolSourceOptions.Default.AccessDeniedLocators,
            MaximumTransientNavigationRetries = Math.Min(value.MaximumTransientNavigationRetries, 1),
        };
    }

    private static FounderScoutConfiguration Parse(AgentConfigurationRecord record) =>
        JsonSerializer.Deserialize<FounderScoutConfiguration>(record.CurrentRevision.CanonicalConfigurationJson, JsonOptions)
        ?? throw new InvalidOperationException("The current Founder Scout configuration is invalid.");

    private static bool TryGetOccurrenceId(OnboardingCheckRecord? check, out OccurrenceId occurrenceId)
    {
        occurrenceId = default;
        if (check is null) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(check.DetailsJson);
            return document.RootElement.TryGetProperty("occurrenceId", out JsonElement value)
                && value.TryGetGuid(out Guid id)
                && (occurrenceId = OccurrenceId.FromGuid(id)) != default;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string EndpointIdentity(string endpoint)
    {
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            return uri.IdnHost;
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint ?? string.Empty)))[..16];
    }

    private static void EnsureRevision(OnboardingAgentSelection selection, long expected)
    {
        if (selection.Revision != expected) throw new OnboardingConcurrencyException();
    }

    private static void ValidateStep(string stepKey)
    {
        if (!StableSteps.Contains(stepKey, StringComparer.Ordinal)) throw new ArgumentException("The Founder Scout onboarding step is invalid.", nameof(stepKey));
    }

    private static void ValidateActor(string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId) || actorId.Length > 128) throw new ArgumentException("A bounded actor is required.", nameof(actorId));
    }

    private static void ValidateStableId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !StableIdRegex().IsMatch(value)) throw new ArgumentException("Use 1–64 lower-case letters, digits, and internal hyphens.", parameterName);
    }

    private static void ValidateName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 100) throw new ArgumentException("A name between 1 and 100 characters is required.", parameterName);
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdRegex();
}
