using System.Text.Json;
using System.Text.Json.Serialization;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Secrets;
using HomeBusinessAssistant.Domain.Agents;

namespace FounderScout.Application;

/// <summary>Bounded discovery settings available before the Founder Scout domain stages.</summary>
public sealed record FounderScoutDiscoverySettings(
    bool Enabled,
    int BrowserConcurrency,
    int MaxNewProfilesPerRun,
    int MaxViewedProfilesPerRun,
    int MaxNewProfilesPerDay,
    int MaxRuntimeSeconds,
    int CooldownAfterCompletionSeconds,
    int StopAfterConsecutiveKnownProfiles,
    bool StopOnAuthenticationFailure,
    bool StopOnAccessDenied,
    bool StopOnThrottle,
    bool StopOnChallenge,
    bool AutomaticFailoverAfterEnforcementSignal);

/// <summary>One ordered semantic or stable-attribute locator candidate.</summary>
public sealed record BrowserLocatorCandidate(
    string Kind,
    string Value,
    string? Name = null,
    bool Exact = false);

/// <summary>Versioned Startup School navigation and parser configuration.</summary>
public sealed record StartupSchoolSourceOptions(
    string AdapterVersion,
    string EntryUrl,
    IReadOnlyList<string> AllowedHosts,
    IReadOnlyList<BrowserLocatorCandidate> AuthenticatedLocators,
    IReadOnlyList<BrowserLocatorCandidate> LoginLocators,
    IReadOnlyList<BrowserLocatorCandidate> ProfileLinkLocators,
    IReadOnlyList<BrowserLocatorCandidate> ProfileRootLocators,
    IReadOnlyList<BrowserLocatorCandidate> DisplayNameLocators,
    IReadOnlyList<BrowserLocatorCandidate> NextPageLocators,
    IReadOnlyList<BrowserLocatorCandidate> LoadMoreLocators,
    IReadOnlyList<BrowserLocatorCandidate> ChallengeLocators,
    IReadOnlyList<BrowserLocatorCandidate> ThrottleLocators,
    IReadOnlyList<BrowserLocatorCandidate> AccessDeniedLocators,
    string DiscoveryMode,
    int NavigationTimeoutSeconds,
    int AuthenticationTimeoutSeconds,
    int SettleDelayMilliseconds,
    int MinimumRequestSpacingMilliseconds,
    int MaximumTransientNavigationRetries,
    string? BrowserChannel,
    bool HeadlessDiscovery,
    bool StoreRawHtml)
{
    /// <summary>The supported source adapter contract.</summary>
    public const string CurrentAdapterVersion = "startup-school-1.6";

    /// <summary>Safe initial values; live locators remain explicitly versioned and editable.</summary>
    public static StartupSchoolSourceOptions Default { get; } = new(
        CurrentAdapterVersion,
        "https://www.startupschool.org/cofounder-matching/candidate/next",
        ["www.startupschool.org", "startupschool.org"],
        [new("css", "[data-testid='cofounder-matching']"), new("role", "heading", "Co-Founder Matching")],
        [new("css", "form[action*='login']"), new("text", "Log in", Exact: true)],
        [new("css", "a[data-profile-id]"), new("css", "[data-testid='profile-card'] a[href]"), new("css", "a[href^='/cofounder-matching/']")],
        [new("css", "main[data-profile-id]"), new("css", "[data-testid='founder-profile']"), new("css", "body")],
        [new("css", "[data-testid='profile-name']"), new("role", "heading")],
        [new("css", "a[href='/cofounder-matching/candidate/next']"), new("role", "link", "Next"), new("css", "a[rel='next']")],
        [new("role", "button", "Load more"), new("css", "button[data-load-more]")],
        [new("css", "[data-testid='challenge']"), new("text", "Verify you are human")],
        [new("css", "[data-testid='throttled']"), new("text", "Too many requests")],
        [new("css", "[data-testid='access-denied']"), new("text", "Access denied")],
        "pagination",
        30,
        900,
        500,
        1_000,
        1,
        null,
        HeadlessDiscovery: true,
        StoreRawHtml: false);
}

/// <summary>Bounded analysis settings available before the evaluation pipeline.</summary>
public sealed record FounderScoutAnalysisSettings(
    bool Enabled,
    int BatchSize,
    int MaximumConcurrency,
    bool FastScreenEnabled,
    int DeepAnalysisThreshold,
    bool SkipUnchangedProfiles,
    int MaximumRetries);

/// <summary>Versioned deterministic parsing, redaction, identity, and fast-screen settings.</summary>
public sealed record FounderScoutProcessingSettings(
    string ParserVersion,
    string RedactorVersion,
    string RulesetVersion,
    decimal MinimumParserCompleteness,
    int MaximumConsecutiveParserFailures,
    int ProcessingLeaseSeconds,
    int DeepAnalysisThreshold,
    int MonitorThreshold,
    bool PreferNonTechnical,
    bool RequireLocationCompatibility,
    bool HardFilterUnpaidImplementationLabor,
    bool FilterIdenticalTechnicalPreference)
{
    /// <summary>Safe initial Stage 11 processing policy.</summary>
    public static FounderScoutProcessingSettings Default { get; } = new(
        DeterministicFounderProfileParser.CurrentVersion,
        DeterministicProfileRedactor.CurrentVersion,
        "founder-screen-rules-1.0",
        0.35m,
        3,
        300,
        65,
        45,
        PreferNonTechnical: true,
        RequireLocationCompatibility: false,
        HardFilterUnpaidImplementationLabor: true,
        FilterIdenticalTechnicalPreference: true);
}

/// <summary>Founder Scout ranking and bounded queue thresholds.</summary>
public sealed record FounderScoutRankingSettings(
    int StrongConnectThreshold,
    int ExploratoryThreshold,
    int MonitorThreshold,
    decimal MinimumConfidence,
    int TopCandidateCount,
    int ManualInvitationQueueSize,
    int ReserveQueueSize);

/// <summary>Configurable deterministic invitation-priority formula weights.</summary>
public sealed record FounderScoutPrioritySettings(
    decimal OurFitWeight,
    decimal FounderQualityWeight,
    decimal ConfidenceWeight,
    decimal ActivityWeight,
    decimal MaximumRiskPenalty);

/// <summary>Human-review-only introduction draft constraints.</summary>
public sealed record FounderScoutInvitationSettings(
    bool GenerateShortVersion,
    bool GenerateDetailedVersion,
    int MaximumCharacters,
    bool RequireCandidateSpecificFact,
    bool RequireComplementarityStatement,
    bool RequireConversationTopic,
    decimal SimilarityThreshold);

/// <summary>AI endpoint metadata with an opaque secret reference, never secret material.</summary>
public sealed record FounderScoutAiSettings(
    string Provider,
    string Endpoint,
    string Deployment,
    string ApiKeySecretReference,
    int RequestTimeoutSeconds,
    string? ApiVersion = null,
    int MaxOutputTokens = 6_000,
    decimal? Temperature = null,
    string? ReasoningEffort = null,
    string ProviderPolicyVersion = "responses-provider-1.0");

/// <summary>Independent Founder Scout retention periods.</summary>
public sealed record FounderScoutRetentionSettings(int RawProfileDays, int ErrorArtifactDays, int ReportDays);

/// <summary>Typed local-founder persona content used only after protected-attribute filtering.</summary>
public sealed record FounderScoutPersonaSettings(
    string SchemaVersion,
    string Reference,
    string DisplayName,
    string TargetRole,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Seeking,
    string MessageTone,
    IReadOnlyList<string> AvoidClaims,
    string? AdditionalContext = null);

/// <summary>Versioned baseline configuration surfaced by the Stage 08 typed editor.</summary>
public sealed record FounderScoutConfiguration(
    string SchemaVersion,
    string DataDirectory,
    FounderScoutDiscoverySettings Discovery,
    FounderScoutAnalysisSettings Analysis,
    FounderScoutRankingSettings Ranking,
    FounderScoutPrioritySettings Priority,
    FounderScoutInvitationSettings Invitation,
    FounderScoutAiSettings Ai,
    FounderScoutRetentionSettings Retention,
    FounderScoutPersonaSettings Persona,
    StartupSchoolSourceOptions? StartupSchool = null,
    FounderScoutProcessingSettings? Processing = null,
    FounderScoutActivitySettings? Activity = null)
{
    /// <summary>The supported configuration schema.</summary>
    public const string CurrentSchemaVersion = "1.0";
}

/// <summary>Strictly validates the Founder Scout Stage 08 baseline without performing domain work.</summary>
public sealed class FounderScoutConfigurationValidator : IAgentConfigurationValidator
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ConfigurationValidationError>> ValidateAsync(
        AgentId agentId,
        string schemaVersion,
        JsonElement configuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (agentId != FounderScoutDefaults.AgentId)
        {
            return ValueTask.FromResult<IReadOnlyList<ConfigurationValidationError>>([]);
        }

        FounderScoutConfiguration? value;
        try
        {
            value = configuration.Deserialize<FounderScoutConfiguration>(Options);
        }
        catch (JsonException)
        {
            return ValueTask.FromResult<IReadOnlyList<ConfigurationValidationError>>(
                [new("founderScout.configuration.invalidShape", "$", "The Founder Scout configuration shape is invalid.")]);
        }

        var errors = new List<ConfigurationValidationError>();
        if (value is null)
        {
            errors.Add(new("founderScout.configuration.required", "$", "Founder Scout configuration is required."));
            return ValueTask.FromResult<IReadOnlyList<ConfigurationValidationError>>(errors);
        }

        if (schemaVersion != FounderScoutConfiguration.CurrentSchemaVersion
            || value.SchemaVersion != FounderScoutConfiguration.CurrentSchemaVersion)
        {
            errors.Add(new("founderScout.configuration.unsupportedSchema", "$.schemaVersion", "The Founder Scout configuration schema is unsupported."));
        }

        Validate(value, errors);
        return ValueTask.FromResult<IReadOnlyList<ConfigurationValidationError>>(errors);
    }

    private static void Validate(FounderScoutConfiguration value, List<ConfigurationValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value.DataDirectory) || value.DataDirectory.Length > 512)
        {
            errors.Add(new("founderScout.configuration.invalidDataDirectory", "$.dataDirectory", "A bounded Founder Scout data directory is required."));
        }
        else
        {
            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(value.DataDirectory);
                if (!Path.IsPathFullyQualified(expanded))
                {
                    errors.Add(new("founderScout.configuration.invalidDataDirectory", "$.dataDirectory", "The Founder Scout data directory must resolve to an absolute path."));
                }

                _ = Path.GetFullPath(expanded);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errors.Add(new("founderScout.configuration.invalidDataDirectory", "$.dataDirectory", "The Founder Scout data directory is invalid."));
            }
        }

        if (value.Discovery is null)
        {
            errors.Add(new("founderScout.configuration.discoveryRequired", "$.discovery", "Discovery settings are required."));
        }
        else
        {
            AddRange(errors, value.Discovery.BrowserConcurrency, 1, 1, "$.discovery.browserConcurrency", "Browser concurrency must remain one in V1.");
            AddRange(errors, value.Discovery.MaxNewProfilesPerRun, 1, 500, "$.discovery.maxNewProfilesPerRun", "New profiles per run must be between 1 and 500.");
            AddRange(errors, value.Discovery.MaxViewedProfilesPerRun, 1, 1_000, "$.discovery.maxViewedProfilesPerRun", "Viewed profiles per run must be between 1 and 1000.");
            AddRange(errors, value.Discovery.MaxNewProfilesPerDay, 1, 2_000, "$.discovery.maxNewProfilesPerDay", "New profiles per day must be between 1 and 2000.");
            AddRange(errors, value.Discovery.MaxRuntimeSeconds, 60, 7_200, "$.discovery.maxRuntimeSeconds", "Discovery runtime must be between 60 and 7200 seconds.");
            AddRange(errors, value.Discovery.CooldownAfterCompletionSeconds, 60, 604_800, "$.discovery.cooldownAfterCompletionSeconds", "Cooldown must be between one minute and seven days.");
            AddRange(errors, value.Discovery.StopAfterConsecutiveKnownProfiles, 1, 100, "$.discovery.stopAfterConsecutiveKnownProfiles", "Known-profile stop count must be between 1 and 100.");
            if (value.Discovery.AutomaticFailoverAfterEnforcementSignal)
            {
                errors.Add(new("founderScout.configuration.failoverForbidden", "$.discovery.automaticFailoverAfterEnforcementSignal", "Automatic failover after an enforcement signal is forbidden."));
            }
        }

        if (value.Analysis is null)
        {
            errors.Add(new("founderScout.configuration.analysisRequired", "$.analysis", "Analysis settings are required."));
        }
        else
        {
            AddRange(errors, value.Analysis.BatchSize, 1, 500, "$.analysis.batchSize", "Analysis batch size must be between 1 and 500.");
            AddRange(errors, value.Analysis.MaximumConcurrency, 1, 16, "$.analysis.maximumConcurrency", "Analysis concurrency must be between 1 and 16.");
            AddRange(errors, value.Analysis.DeepAnalysisThreshold, 0, 100, "$.analysis.deepAnalysisThreshold", "Deep-analysis threshold must be between 0 and 100.");
            AddRange(errors, value.Analysis.MaximumRetries, 0, 10, "$.analysis.maximumRetries", "Analysis retries must be between 0 and 10.");
        }

        if (value.Ranking is null)
        {
            errors.Add(new("founderScout.configuration.rankingRequired", "$.ranking", "Ranking settings are required."));
        }
        else
        {
            AddRange(errors, value.Ranking.StrongConnectThreshold, 0, 100, "$.ranking.strongConnectThreshold", "Strong-connect threshold must be between 0 and 100.");
            AddRange(errors, value.Ranking.ExploratoryThreshold, 0, 100, "$.ranking.exploratoryThreshold", "Exploratory threshold must be between 0 and 100.");
            AddRange(errors, value.Ranking.MonitorThreshold, 0, 100, "$.ranking.monitorThreshold", "Monitor threshold must be between 0 and 100.");
            if (value.Ranking.StrongConnectThreshold < value.Ranking.ExploratoryThreshold
                || value.Ranking.ExploratoryThreshold < value.Ranking.MonitorThreshold)
            {
                errors.Add(new("founderScout.configuration.thresholdOrder", "$.ranking", "Ranking thresholds must descend from strong to exploratory to monitor."));
            }

            AddDecimalRange(errors, value.Ranking.MinimumConfidence, "$.ranking.minimumConfidence", "Minimum confidence must be between zero and one.");
            AddRange(errors, value.Ranking.TopCandidateCount, 1, 1_000, "$.ranking.topCandidateCount", "Top candidate count must be between 1 and 1000.");
            AddRange(errors, value.Ranking.ManualInvitationQueueSize, 1, 500, "$.ranking.manualInvitationQueueSize", "Manual queue size must be between 1 and 500.");
            AddRange(errors, value.Ranking.ReserveQueueSize, 0, 500, "$.ranking.reserveQueueSize", "Reserve queue size must be between 0 and 500.");
        }

        if (value.Priority is null)
        {
            errors.Add(new("founderScout.configuration.priorityRequired", "$.priority", "Invitation-priority formula weights are required."));
        }
        else
        {
            AddDecimalRange(errors, value.Priority.OurFitWeight, "$.priority.ourFitWeight", "Our-fit weight must be between zero and one.");
            AddDecimalRange(errors, value.Priority.FounderQualityWeight, "$.priority.founderQualityWeight", "Founder-quality weight must be between zero and one.");
            AddDecimalRange(errors, value.Priority.ConfidenceWeight, "$.priority.confidenceWeight", "Confidence weight must be between zero and one.");
            AddDecimalRange(errors, value.Priority.ActivityWeight, "$.priority.activityWeight", "Activity weight must be between zero and one.");
            decimal total = value.Priority.OurFitWeight
                + value.Priority.FounderQualityWeight
                + value.Priority.ConfidenceWeight
                + value.Priority.ActivityWeight;
            if (total != 1m)
            {
                errors.Add(new("founderScout.configuration.priorityWeightTotal", "$.priority", "Invitation-priority weights must total exactly one."));
            }

            if (value.Priority.MaximumRiskPenalty is < 0 or > 100)
            {
                errors.Add(new("founderScout.configuration.outOfRange", "$.priority.maximumRiskPenalty", "Maximum risk penalty must be between zero and one hundred."));
            }
        }

        if (value.Invitation is null)
        {
            errors.Add(new("founderScout.configuration.invitationRequired", "$.invitation", "Invitation draft settings are required."));
        }
        else
        {
            AddRange(errors, value.Invitation.MaximumCharacters, 100, 5_000, "$.invitation.maximumCharacters", "Invitation maximum characters must be between 100 and 5000.");
            AddDecimalRange(errors, value.Invitation.SimilarityThreshold, "$.invitation.similarityThreshold", "Similarity threshold must be between zero and one.");
            if (!value.Invitation.GenerateShortVersion && !value.Invitation.GenerateDetailedVersion)
            {
                errors.Add(new("founderScout.configuration.draftRequired", "$.invitation", "At least one invitation draft length must be enabled."));
            }
        }

        if (value.Ai is null)
        {
            errors.Add(new("founderScout.configuration.aiRequired", "$.ai", "AI settings are required."));
        }
        else
        {
            if (value.Ai.Provider is not ("OpenAI" or "AzureOpenAI" or "DiagnosticFake"))
            {
                errors.Add(new("founderScout.configuration.invalidProvider", "$.ai.provider", "AI provider must be OpenAI, AzureOpenAI, or the explicitly labeled DiagnosticFake test provider."));
            }

            if (value.Ai.Endpoint is null
                || value.Ai.Endpoint.Length > 512
                || string.IsNullOrWhiteSpace(value.Ai.Deployment)
                || value.Ai.Deployment.Length > 128)
            {
                errors.Add(new("founderScout.configuration.invalidEndpoint", "$.ai", "AI endpoint and deployment metadata are invalid."));
            }

            if (value.Ai.Provider == "DiagnosticFake"
                && !string.Equals(value.Ai.Endpoint, "diagnostic://fake", StringComparison.Ordinal))
            {
                errors.Add(new("founderScout.configuration.fakeEndpoint", "$.ai.endpoint", "DiagnosticFake requires the explicit diagnostic://fake endpoint."));
            }
            else if (value.Ai.Provider != "DiagnosticFake"
                && value.Ai.Endpoint is { Length: > 0 }
                && (!Uri.TryCreate(value.Ai.Endpoint, UriKind.Absolute, out Uri? endpoint)
                    || endpoint.Scheme != Uri.UriSchemeHttps
                    || !string.IsNullOrEmpty(endpoint.UserInfo)))
            {
                errors.Add(new("founderScout.configuration.invalidEndpoint", "$.ai.endpoint", "A configured AI endpoint must be an HTTPS URI without credentials."));
            }

            if (!SecretReference.TryParse(value.Ai.ApiKeySecretReference, out _))
            {
                errors.Add(new("founderScout.configuration.invalidSecretReference", "$.ai.apiKeySecretReference", "A valid opaque AI secret reference is required."));
            }

            AddRange(errors, value.Ai.RequestTimeoutSeconds, 1, 900, "$.ai.requestTimeoutSeconds", "AI timeout must be between 1 and 900 seconds.");
            AddRange(errors, value.Ai.MaxOutputTokens, 256, 100_000, "$.ai.maxOutputTokens", "AI output tokens must be between 256 and 100000.");
            if (value.Ai.ApiVersion is { Length: > 64 } || value.Ai.ApiVersion?.Any(char.IsControl) == true)
            {
                errors.Add(new("founderScout.configuration.apiVersion", "$.ai.apiVersion", "The optional Azure API version is invalid."));
            }
            if (value.Ai.Temperature is < 0 or > 2)
            {
                errors.Add(new("founderScout.configuration.temperature", "$.ai.temperature", "Temperature must be between zero and two when configured."));
            }
            if (value.Ai.ReasoningEffort is not null and not ("none" or "minimal" or "low" or "medium" or "high"))
            {
                errors.Add(new("founderScout.configuration.reasoningEffort", "$.ai.reasoningEffort", "Reasoning effort is unsupported."));
            }
            AddBoundedText(errors, value.Ai.ProviderPolicyVersion, 128, "$.ai.providerPolicyVersion", "A bounded provider policy version is required.");
        }

        if (value.Retention is null)
        {
            errors.Add(new("founderScout.configuration.retentionRequired", "$.retention", "Retention settings are required."));
        }
        else
        {
            AddRange(errors, value.Retention.RawProfileDays, 1, 365, "$.retention.rawProfileDays", "Raw profile retention must be between 1 and 365 days.");
            AddRange(errors, value.Retention.ErrorArtifactDays, 1, 365, "$.retention.errorArtifactDays", "Error artifact retention must be between 1 and 365 days.");
            AddRange(errors, value.Retention.ReportDays, 1, 3_650, "$.retention.reportDays", "Report retention must be between 1 and 3650 days.");
        }

        ValidatePersona(value.Persona, errors);
        ValidateSource(value.StartupSchool ?? StartupSchoolSourceOptions.Default, errors);
        ValidateProcessing(value.Processing ?? FounderScoutProcessingSettings.Default, errors);
        ValidateActivity(value.Activity ?? FounderScoutActivitySettings.Default, errors);
    }

    private static void ValidateActivity(FounderScoutActivitySettings activity, List<ConfigurationValidationError> errors)
    {
        AddRange(errors, activity.RecentDays, 0, 365, "$.activity.recentDays", "Recent activity days must be between 0 and 365.");
        AddRange(errors, activity.ActiveDays, 1, 730, "$.activity.activeDays", "Active activity days must be between 1 and 730.");
        AddRange(errors, activity.StaleDays, 1, 3_650, "$.activity.staleDays", "Stale activity days must be between 1 and 3650.");
        if (activity.RecentDays >= activity.ActiveDays || activity.ActiveDays >= activity.StaleDays)
        {
            errors.Add(new("founderScout.configuration.activityOrder", "$.activity", "Activity day buckets must increase from recent through stale."));
        }
        foreach (decimal score in new[] { activity.RecentScore, activity.ActiveScore, activity.StaleScore, activity.OlderScore, activity.UnknownScore })
        {
            if (score is < 0 or > 100)
            {
                errors.Add(new("founderScout.configuration.activityScore", "$.activity", "Activity scores must be between zero and one hundred."));
                break;
            }
        }
    }

    private static void ValidateProcessing(FounderScoutProcessingSettings processing, List<ConfigurationValidationError> errors)
    {
        if (processing.ParserVersion != DeterministicFounderProfileParser.CurrentVersion)
        {
            errors.Add(new("founderScout.configuration.parserVersion", "$.processing.parserVersion", "The Founder Scout parser version is unsupported."));
        }
        if (processing.RedactorVersion != DeterministicProfileRedactor.CurrentVersion)
        {
            errors.Add(new("founderScout.configuration.redactorVersion", "$.processing.redactorVersion", "The Founder Scout redactor version is unsupported."));
        }
        AddBoundedText(errors, processing.RulesetVersion, 128, "$.processing.rulesetVersion", "A bounded screening ruleset version is required.");
        AddDecimalRange(errors, processing.MinimumParserCompleteness, "$.processing.minimumParserCompleteness", "Minimum parser completeness must be between zero and one.");
        AddRange(errors, processing.MaximumConsecutiveParserFailures, 1, 20, "$.processing.maximumConsecutiveParserFailures", "Consecutive parser failures must be between 1 and 20.");
        AddRange(errors, processing.ProcessingLeaseSeconds, 10, 86_400, "$.processing.processingLeaseSeconds", "Processing lease duration must be between 10 and 86400 seconds.");
        AddRange(errors, processing.DeepAnalysisThreshold, 0, 100, "$.processing.deepAnalysisThreshold", "Deep-analysis threshold must be between 0 and 100.");
        AddRange(errors, processing.MonitorThreshold, 0, 100, "$.processing.monitorThreshold", "Monitor threshold must be between 0 and 100.");
        if (processing.DeepAnalysisThreshold < processing.MonitorThreshold)
        {
            errors.Add(new("founderScout.configuration.processingThresholdOrder", "$.processing", "The deep-analysis threshold must be at least the monitor threshold."));
        }
    }

    private static void ValidateSource(
        StartupSchoolSourceOptions source,
        List<ConfigurationValidationError> errors)
    {
        if (source.AdapterVersion != StartupSchoolSourceOptions.CurrentAdapterVersion)
        {
            errors.Add(new("founderScout.configuration.sourceAdapterVersion", "$.startupSchool.adapterVersion", "The Startup School adapter version is unsupported."));
        }

        if (!TryGetTrustedTransportUri(source.EntryUrl, out Uri? entryUri))
        {
            errors.Add(new("founderScout.configuration.sourceEntryUrl", "$.startupSchool.entryUrl", "A valid HTTPS source entry URL without credentials is required; HTTP is limited to loopback fixtures."));
        }

        if (source.AllowedHosts is null || source.AllowedHosts.Count is < 1 or > 20
            || source.AllowedHosts.Any(host => !IsValidHost(host))
            || (entryUri is not null && !source.AllowedHosts.Contains(entryUri.IdnHost, StringComparer.OrdinalIgnoreCase)))
        {
            errors.Add(new("founderScout.configuration.allowedHosts", "$.startupSchool.allowedHosts", "Allowed hosts must be bounded DNS names and include the entry host."));
        }

        if (source.DiscoveryMode is not ("pagination" or "load-more" or "infinite-scroll"))
        {
            errors.Add(new("founderScout.configuration.discoveryMode", "$.startupSchool.discoveryMode", "Discovery mode must be pagination, load-more, or infinite-scroll."));
        }

        AddRange(errors, source.NavigationTimeoutSeconds, 5, 120, "$.startupSchool.navigationTimeoutSeconds", "Navigation timeout must be between 5 and 120 seconds.");
        AddRange(errors, source.AuthenticationTimeoutSeconds, 60, 3_600, "$.startupSchool.authenticationTimeoutSeconds", "Authentication timeout must be between 60 and 3600 seconds.");
        AddRange(errors, source.SettleDelayMilliseconds, 0, 30_000, "$.startupSchool.settleDelayMilliseconds", "Settle delay must be between 0 and 30000 milliseconds.");
        AddRange(errors, source.MinimumRequestSpacingMilliseconds, 0, 60_000, "$.startupSchool.minimumRequestSpacingMilliseconds", "Request spacing must be between 0 and 60000 milliseconds.");
        AddRange(errors, source.MaximumTransientNavigationRetries, 0, 2, "$.startupSchool.maximumTransientNavigationRetries", "Transient navigation retries must be between 0 and 2.");
        if (source.BrowserChannel is not null && source.BrowserChannel is not ("chrome" or "msedge" or "chrome-beta" or "msedge-beta" or "chrome-dev" or "msedge-dev"))
        {
            errors.Add(new("founderScout.configuration.browserChannel", "$.startupSchool.browserChannel", "The configured browser channel is not allow-listed."));
        }

        ValidateLocators(source.AuthenticatedLocators, "authenticatedLocators", errors);
        ValidateLocators(source.LoginLocators, "loginLocators", errors);
        ValidateLocators(source.ProfileLinkLocators, "profileLinkLocators", errors);
        ValidateLocators(source.ProfileRootLocators, "profileRootLocators", errors);
        ValidateLocators(source.DisplayNameLocators, "displayNameLocators", errors);
        ValidateLocators(source.ChallengeLocators, "challengeLocators", errors);
        ValidateLocators(source.ThrottleLocators, "throttleLocators", errors);
        ValidateLocators(source.AccessDeniedLocators, "accessDeniedLocators", errors);
    }

    private static bool TryGetTrustedTransportUri(string value, out Uri? uri)
    {
        bool valid = Uri.TryCreate(value, UriKind.Absolute, out uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
            && string.IsNullOrEmpty(uri.UserInfo)
            && value.Length <= 2_048;
        return valid;
    }

    private static bool IsValidHost(string host) =>
        !string.IsNullOrWhiteSpace(host)
        && host.Length <= 253
        && Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;

    private static void ValidateLocators(
        IReadOnlyList<BrowserLocatorCandidate>? locators,
        string property,
        List<ConfigurationValidationError> errors)
    {
        if (locators is null || locators.Count is < 1 or > 20
            || locators.Any(locator => locator.Kind is not ("role" or "text" or "label" or "css")
                || string.IsNullOrWhiteSpace(locator.Value)
                || locator.Value.Length > 500
                || locator.Value.Contains("xpath", StringComparison.OrdinalIgnoreCase)
                || locator.Value.Contains("nth-child", StringComparison.OrdinalIgnoreCase)
                || (locator.Name?.Length ?? 0) > 200))
        {
            errors.Add(new("founderScout.configuration.locators", $"$.startupSchool.{property}", "Locator candidates must use bounded semantic or stable-attribute selectors."));
        }
    }

    private static void ValidatePersona(
        FounderScoutPersonaSettings? persona,
        List<ConfigurationValidationError> errors)
    {
        if (persona is null)
        {
            errors.Add(new("founderScout.configuration.personaRequired", "$.persona", "A typed founder persona is required."));
            return;
        }

        if (persona.SchemaVersion != "1.0")
        {
            errors.Add(new("founderScout.configuration.personaSchema", "$.persona.schemaVersion", "The founder persona schema is unsupported."));
        }

        AddBoundedText(errors, persona.Reference, 128, "$.persona.reference", "A bounded persona reference is required.");
        AddBoundedText(errors, persona.DisplayName, 100, "$.persona.displayName", "A bounded persona display name is required.");
        AddBoundedText(errors, persona.TargetRole, 200, "$.persona.targetRole", "A bounded target role is required.");
        AddBoundedText(errors, persona.MessageTone, 200, "$.persona.messageTone", "A bounded message tone is required.");
        ValidateStringList(errors, persona.Strengths, 20, "$.persona.strengths");
        ValidateStringList(errors, persona.Seeking, 20, "$.persona.seeking");
        ValidateStringList(errors, persona.AvoidClaims, 20, "$.persona.avoidClaims");
        if (persona.AdditionalContext is { Length: > 0 } context
            && (context.Length > 20_000
                || context.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t')))
        {
            errors.Add(new(
                "founderScout.configuration.invalidPersonaContext",
                "$.persona.additionalContext",
                "Founder context must be at most 20000 characters and contain only normal text, line breaks, and tabs."));
        }
    }

    private static void ValidateStringList(
        List<ConfigurationValidationError> errors,
        IReadOnlyList<string>? values,
        int maximumCount,
        string path)
    {
        if (values is null || values.Count is < 1 || values.Count > maximumCount
            || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 500 || value.Any(char.IsControl)))
        {
            errors.Add(new("founderScout.configuration.invalidPersonaList", path, "Persona lists must contain bounded non-empty text."));
        }
    }

    private static void AddBoundedText(
        List<ConfigurationValidationError> errors,
        string? value,
        int maximumLength,
        string path,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl))
        {
            errors.Add(new("founderScout.configuration.invalidPersonaText", path, message));
        }
    }

    private static void AddRange(List<ConfigurationValidationError> errors, int value, int minimum, int maximum, string path, string message)
    {
        if (value < minimum || value > maximum)
        {
            errors.Add(new("founderScout.configuration.outOfRange", path, message));
        }
    }

    private static void AddDecimalRange(List<ConfigurationValidationError> errors, decimal value, string path, string message)
    {
        if (value is < 0 or > 1)
        {
            errors.Add(new("founderScout.configuration.outOfRange", path, message));
        }
    }
}

/// <summary>Safe Founder Scout defaults installed only when no user revision exists.</summary>
public sealed class FounderScoutDefaults : IAgentDefaultConfigurationProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The stable Founder Scout identifier.</summary>
    public static AgentId AgentId { get; } = AgentId.Parse("founder-scout");

    /// <summary>The stable opaque AI key reference used by the separate secret action.</summary>
    public static SecretReference ApiKeyReference { get; } = SecretReference.Parse("secret://founder-scout/azure-openai-key");

    /// <summary>
    /// Creates the code-owned Founder Scout configuration used by simple mode.
    /// Deep provider evaluation is available through the focused settings page. Discovery remains capture-first
    /// and never depends on the provider being configured or available.
    /// </summary>
    public static FounderScoutConfiguration CreateConfiguration() => new(
        FounderScoutConfiguration.CurrentSchemaVersion,
        "%LOCALAPPDATA%/HomeBusinessAssistant/data/founder-scout",
        new(true, 1, 20, 40, 60, 600, 600, 8, true, true, true, true, false),
        new(true, 20, 1, true, 65, true, 0),
        new(82, 72, 62, 0.60m, 30, 15, 15),
        new(0.55m, 0.25m, 0.15m, 0.05m, 100m),
        new(true, true, 1_000, true, true, true, 0.85m),
        new("OpenAI", string.Empty, "gpt-5.4-mini", ApiKeyReference.Value, 120),
        new(30, 14, 90),
        new(
            "1.0",
            "founder-persona/default",
            "Local founder",
            "Technical Co-Founder / CTO",
            ["Software architecture", "Product engineering", "Responsible AI workflows"],
            ["Complementary business leadership", "Customer access or distribution", "Founder-level commitment"],
            "Direct, thoughtful, founder-to-founder",
            ["Do not imply a commitment to join.", "Do not promise investment or delivery.", "Do not mention automated scoring."],
            AdditionalContext: null),
        StartupSchoolSourceOptions.Default with
        {
            EntryUrl = "https://www.startupschool.org/cofounder-matching/candidate/next",
            AllowedHosts =
            [
                "www.startupschool.org",
                "startupschool.org",
                "account.ycombinator.com",
                "www.ycombinator.com",
                "ycombinator.com",
            ],
            AuthenticatedLocators =
            [
                new("role", "heading", "Co-Founder Matching"),
                new("css", "a[href*='/cofounder-matching/profile']"),
            ],
            LoginLocators =
            [
                new("css", ".sign-in-card"),
                new("css", "input.ycid-input"),
                new("text", "Sign in"),
            ],
            BrowserChannel = "chrome",
        },
        FounderScoutProcessingSettings.Default,
        FounderScoutActivitySettings.Default);

    /// <inheritdoc />
    public AgentDefaultConfiguration GetDefault() => new(
        AgentId,
        FounderScoutConfiguration.CurrentSchemaVersion,
        JsonSerializer.SerializeToElement(CreateConfiguration(), SerializerOptions),
        "Installed code-owned Founder Scout simple-mode settings.");
}
