using System.ComponentModel.DataAnnotations;
using FounderScout.Application;
using FounderScout.Domain;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Onboarding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.Onboarding;

/// <summary>Resumable specialized Founder Scout onboarding wizard.</summary>
[AutoValidateAntiforgeryToken]
public sealed class FounderScoutModel(HostManagementComposition management, IWebHostEnvironment environment) : PageModel
{
    private static readonly HashSet<string> Steps =
    [
        FounderScoutOnboardingSteps.Purpose,
        FounderScoutOnboardingSteps.BrowserAccount,
        FounderScoutOnboardingSteps.Source,
        FounderScoutOnboardingSteps.Policy,
        FounderScoutOnboardingSteps.Ai,
        FounderScoutOnboardingSteps.Checks,
        FounderScoutOnboardingSteps.Review,
    ];

    /// <summary>Gets or sets the durable onboarding session ID.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid SessionId { get; set; }

    /// <summary>Gets or sets the stable active wizard step.</summary>
    [BindProperty(SupportsGet = true)]
    public string Step { get; set; } = FounderScoutOnboardingSteps.Purpose;

    /// <summary>Gets or sets the current step fields.</summary>
    [BindProperty]
    public FounderScoutWizardForm Form { get; set; } = new();

    /// <summary>Gets the safe current wizard projection.</summary>
    public FounderScoutOnboardingEditor? Editor { get; private set; }

    /// <summary>Gets whether the diagnostic fake provider may be displayed.</summary>
    public bool ShowDiagnosticFake => environment.IsDevelopment() || environment.IsEnvironment("Testing");

    /// <summary>Loads current authoritative state without reading secret content or creating a browser directory.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (FounderScoutSimpleMode.Enabled)
        {
            TempData["FlashMessage"] = "Founder Scout uses code-owned settings in simple mode; there is no setup wizard.";
            return RedirectToPage("/Agents/Configuration", new { id = FounderScoutDefaults.AgentId.Value });
        }
        if (!TryNormalizeStep() || management.FounderScoutOnboarding is null) return NotFound();
        try
        {
            Editor = await management.FounderScoutOnboarding.GetAsync(SessionId, "local-web", cancellationToken).ConfigureAwait(false);
            Form = FounderScoutWizardForm.From(Editor);
            return Page();
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    /// <summary>Saves live/local mode, explicit acknowledgement, and capture/deep-analysis selection.</summary>
    public async Task<IActionResult> OnPostPurposeAsync(CancellationToken cancellationToken)
    {
        if (!TryService(out IFounderScoutOnboardingService service)) return NotFound();
        if (Form.LiveSource && !Form.LiveConsentAcknowledged)
        {
            TempData["FlashMessage"] = "Live browser setup requires the explicit authorization and terms acknowledgement.";
            return RedirectToStep(FounderScoutOnboardingSteps.Purpose);
        }
        try
        {
            FounderScoutOnboardingEditor editor = await service.GetAsync(SessionId, "local-web", cancellationToken).ConfigureAwait(false);
            await service.SaveLiveConsentAsync(SessionId, Form.LiveSource, Form.LiveConsentAcknowledged, "local-web", cancellationToken).ConfigureAwait(false);
            FounderScoutConfiguration updated = editor.Configuration with
            {
                Discovery = editor.Configuration.Discovery with { Enabled = Form.LiveSource },
                Analysis = editor.Configuration.Analysis with { Enabled = Form.DeepAnalysis },
            };
            _ = await SaveConfigAsync(service, editor, FounderScoutOnboardingSteps.BrowserAccount, updated, cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = "Purpose, privacy acknowledgement, and processing mode were saved. Nothing was enabled or run.";
            return RedirectToStep(FounderScoutOnboardingSteps.BrowserAccount);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return RedirectFailure(FounderScoutOnboardingSteps.Purpose, exception);
        }
    }

    /// <summary>Saves one canonical disabled-first browser account.</summary>
    public async Task<IActionResult> OnPostAccountAsync(CancellationToken cancellationToken)
    {
        if (!TryService(out IFounderScoutOnboardingService service)) return NotFound();
        try
        {
            _ = await service.SaveBrowserAccountAsync(new(SessionId, Form.ExpectedSelectionRevision, Form.AccountId, Form.AccountDisplayName, string.IsNullOrWhiteSpace(Form.SegmentId) ? null : Form.SegmentId, "local-web"), cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = "The dedicated account was saved disabled-first. Its profile directory is created only when authentication starts.";
            return RedirectToStep(FounderScoutOnboardingSteps.Source);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return RedirectFailure(FounderScoutOnboardingSteps.BrowserAccount, exception);
        }
    }

    /// <summary>Saves conservative discovery limits and one code-owned Startup School segment.</summary>
    public async Task<IActionResult> OnPostSourceAsync(CancellationToken cancellationToken)
    {
        if (!TryService(out IFounderScoutOnboardingService service)) return NotFound();
        try
        {
            FounderScoutOnboardingEditor editor = await service.GetAsync(SessionId, "local-web", cancellationToken).ConfigureAwait(false);
            OnboardingAgentSelection selection = await service.SaveSegmentAsync(new(SessionId, Form.ExpectedSelectionRevision, Form.SegmentId, Form.SegmentName, Form.AccountId, Form.MaximumPages, Form.SafeFilter, "local-web"), cancellationToken).ConfigureAwait(false);
            StartupSchoolSourceOptions source = (editor.Configuration.StartupSchool ?? StartupSchoolSourceOptions.Default) with { StoreRawHtml = Form.StoreRawHtml };
            FounderScoutProcessingSettings processing = (editor.Configuration.Processing ?? FounderScoutProcessingSettings.Default) with { MinimumParserCompleteness = Form.MinimumParserCompleteness };
            FounderScoutConfiguration updated = editor.Configuration with
            {
                Discovery = editor.Configuration.Discovery with
                {
                    MaxNewProfilesPerRun = Form.MaxNewProfilesPerRun,
                    MaxViewedProfilesPerRun = Form.MaxViewedProfilesPerRun,
                    MaxNewProfilesPerDay = Form.MaxNewProfilesPerDay,
                    MaxRuntimeSeconds = Form.MaxRuntimeSeconds,
                    CooldownAfterCompletionSeconds = Form.CooldownSeconds,
                },
                StartupSchool = source,
                Processing = processing,
            };
            _ = await service.SaveConfigurationAsync(SessionId, selection.Revision, FounderScoutOnboardingSteps.Policy, updated, editor.ConfigurationRecord.CurrentRevision.RevisionNumber, editor.ConfigurationRecord.CurrentRevision.ConfigurationHash, "local-web", cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = Form.StoreRawHtml ? "Source limits saved. Raw HTML retention is enabled and must be reviewed carefully." : "Conservative source limits were saved.";
            return RedirectToStep(FounderScoutOnboardingSteps.Policy);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return RedirectFailure(FounderScoutOnboardingSteps.Source, exception);
        }
    }

    /// <summary>Saves deterministic screening, fit, queue, draft, persona, and retention policy.</summary>
    public async Task<IActionResult> OnPostPolicyAsync(CancellationToken cancellationToken)
    {
        if (!TryService(out IFounderScoutOnboardingService service)) return NotFound();
        try
        {
            FounderScoutOnboardingEditor editor = await service.GetAsync(SessionId, "local-web", cancellationToken).ConfigureAwait(false);
            FounderScoutConfiguration updated = editor.Configuration with
            {
                Ranking = editor.Configuration.Ranking with
                {
                    StrongConnectThreshold = Form.StrongThreshold,
                    ExploratoryThreshold = Form.ExploratoryThreshold,
                    MonitorThreshold = Form.MonitorThreshold,
                    MinimumConfidence = Form.MinimumConfidence,
                    ManualInvitationQueueSize = Form.PrimaryQueueCapacity,
                    ReserveQueueSize = Form.ReserveQueueCapacity,
                },
                Invitation = editor.Configuration.Invitation with { MaximumCharacters = Form.DraftMaximumCharacters },
                Retention = new(Form.RawProfileDays, Form.ErrorArtifactDays, Form.ReportDays),
                Persona = editor.Configuration.Persona with
                {
                    DisplayName = Form.PersonaDisplayName.Trim(),
                    TargetRole = Form.PersonaTargetRole.Trim(),
                    Strengths = SplitLines(Form.PersonaStrengths),
                    Seeking = SplitLines(Form.PersonaSeeking),
                    MessageTone = Form.PersonaMessageTone.Trim(),
                },
            };
            _ = await SaveConfigAsync(service, editor, FounderScoutOnboardingSteps.Ai, updated, cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = "Screening, fit, human-review queue, persona, draft, and retention policy were saved locally.";
            return RedirectToStep(FounderScoutOnboardingSteps.Ai);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return RedirectFailure(FounderScoutOnboardingSteps.Policy, exception);
        }
    }

    /// <summary>Saves provider metadata and the opaque fixed secret reference, never key material.</summary>
    public async Task<IActionResult> OnPostAiAsync(CancellationToken cancellationToken)
    {
        if (!TryService(out IFounderScoutOnboardingService service)) return NotFound();
        if (Form.AiProvider == "DiagnosticFake" && !ShowDiagnosticFake) return NotFound();
        try
        {
            FounderScoutOnboardingEditor editor = await service.GetAsync(SessionId, "local-web", cancellationToken).ConfigureAwait(false);
            FounderScoutAiSettings ai = editor.Configuration.Ai with
            {
                Provider = Form.AiProvider,
                Endpoint = Form.AiEndpoint.Trim(),
                Deployment = Form.AiDeployment.Trim(),
                RequestTimeoutSeconds = Form.AiTimeoutSeconds,
                MaxOutputTokens = Form.AiMaxOutputTokens,
                ApiKeySecretReference = FounderScoutDefaults.ApiKeyReference.Value,
            };
            _ = await SaveConfigAsync(service, editor, FounderScoutOnboardingSteps.Checks, editor.Configuration with { Ai = ai }, cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = "Provider metadata was saved. The protected key is a separate action and is never stored in configuration.";
            return RedirectToStep(FounderScoutOnboardingSteps.Checks);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return RedirectFailure(FounderScoutOnboardingSteps.Ai, exception);
        }
    }

    /// <summary>Stores a provider key separately and always redirects without echoing it.</summary>
    public async Task<IActionResult> OnPostSetSecretAsync(string secretValue, CancellationToken cancellationToken)
    {
        if (!TryService(out IFounderScoutOnboardingService service)) return NotFound();
        try
        {
            await service.SetSecretAsync(SessionId, Form.ExpectedSelectionRevision, secretValue, "local-web", cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = "The provider key was protected for the current Windows user and was not retained by this page.";
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            TempData["FlashMessage"] = exception.Message;
        }
        return RedirectToStep(FounderScoutOnboardingSteps.Ai);
    }

    /// <summary>Deletes protected provider key material separately.</summary>
    public async Task<IActionResult> OnPostDeleteSecretAsync(CancellationToken cancellationToken)
    {
        if (!TryService(out IFounderScoutOnboardingService service)) return NotFound();
        try
        {
            await service.DeleteSecretAsync(SessionId, Form.ExpectedSelectionRevision, "local-web", cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = "The protected provider key was removed.";
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            TempData["FlashMessage"] = exception.Message;
        }
        return RedirectToStep(FounderScoutOnboardingSteps.Ai);
    }

    /// <summary>Runs the explicit no-download Playwright runtime check.</summary>
    public async Task<IActionResult> OnPostTestBrowserAsync(CancellationToken cancellationToken)
    {
        if (!TryService(out IFounderScoutOnboardingService service)) return NotFound();
        try
        {
            BrowserRuntimeStatus result = await service.TestBrowserRuntimeAsync(SessionId, "local-web", cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = result.Message;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            TempData["FlashMessage"] = exception.Message;
        }
        return RedirectToStep(FounderScoutOnboardingSteps.Checks);
    }

    /// <summary>Runs one explicitly confirmed, potentially chargeable provider request.</summary>
    public async Task<IActionResult> OnPostTestProviderAsync(CancellationToken cancellationToken)
    {
        if (!TryService(out IFounderScoutOnboardingService service)) return NotFound();
        try
        {
            FounderProviderReadinessResult result = await service.TestProviderAsync(SessionId, Form.ConfirmProviderCharge, "local-web", cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = result.Message;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            TempData["FlashMessage"] = exception.Message;
        }
        return RedirectToStep(FounderScoutOnboardingSteps.Checks);
    }

    /// <summary>Creates one confirmed durable authentication occurrence and dispatches only through Runner.</summary>
    public async Task<IActionResult> OnPostAuthenticateAsync(CancellationToken cancellationToken)
    {
        if (!TryService(out IFounderScoutOnboardingService service)) return NotFound();
        try
        {
            _ = await service.AuthenticateAsync(SessionId, Form.AccountId, Form.ConfirmHeadedAuthentication, "local-web", cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = "Runner started the headed authentication occurrence. Complete login only in the opened browser.";
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            TempData["FlashMessage"] = exception.Message;
        }
        return RedirectToStep(FounderScoutOnboardingSteps.Checks);
    }

    /// <summary>Marks the selected setup ready for Stage 22 only when chosen prerequisites pass.</summary>
    public async Task<IActionResult> OnPostReviewAsync(CancellationToken cancellationToken)
    {
        if (!TryService(out IFounderScoutOnboardingService service)) return NotFound();
        try
        {
            OnboardingAgentSelection selection = await service.ReviewAsync(SessionId, Form.ExpectedSelectionRevision, "local-web", cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = selection.ProgressStatus == OnboardingAgentProgressStatus.ReadyForValidation
                ? "Founder Scout is ready for the Stage 22 diagnostic. It remains disabled and no schedule was created."
                : "Founder Scout still has blocking prerequisites. Review the items on this page.";
            return RedirectToStep(FounderScoutOnboardingSteps.Review);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return RedirectFailure(FounderScoutOnboardingSteps.Review, exception);
        }
    }

    private async ValueTask<OnboardingAgentSelection> SaveConfigAsync(IFounderScoutOnboardingService service, FounderScoutOnboardingEditor editor, string nextStep, FounderScoutConfiguration configuration, CancellationToken cancellationToken) =>
        await service.SaveConfigurationAsync(SessionId, Form.ExpectedSelectionRevision, nextStep, configuration, editor.ConfigurationRecord.CurrentRevision.RevisionNumber, editor.ConfigurationRecord.CurrentRevision.ConfigurationHash, "local-web", cancellationToken).ConfigureAwait(false);

    private bool TryService(out IFounderScoutOnboardingService service)
    {
        service = management.FounderScoutOnboarding!;
        return !FounderScoutSimpleMode.Enabled
            && SessionId != Guid.Empty
            && management.FounderScoutOnboarding is not null;
    }

    private bool TryNormalizeStep()
    {
        if (SessionId == Guid.Empty) return false;
        if (string.IsNullOrWhiteSpace(Step)) Step = FounderScoutOnboardingSteps.Purpose;
        return Steps.Contains(Step);
    }

    private RedirectToPageResult RedirectToStep(string step) => RedirectToPage(new { sessionId = SessionId, step });

    private RedirectToPageResult RedirectFailure(string step, Exception exception)
    {
        TempData["FlashMessage"] = exception.Message;
        return RedirectToStep(step);
    }

    private static bool IsExpected(Exception exception) => exception is
        ConfigurationValidationException or
        ConfigurationConcurrencyException or
        OnboardingConcurrencyException or
        InvalidOperationException or
        ArgumentException;

    private static string[] SplitLines(string value) => value
        .Split(["\r\n", "\n"], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.Ordinal)
        .Take(50)
        .ToArray();

    /// <summary>Bounded specialized form fields; no secret value is retained here.</summary>
    public sealed class FounderScoutWizardForm
    {
        /// <summary>Displayed optimistic selection revision.</summary>
        public long ExpectedSelectionRevision { get; set; }
        /// <summary>Whether live browser discovery is selected.</summary>
        public bool LiveSource { get; set; }
        /// <summary>Explicit live-source authorization acknowledgement.</summary>
        public bool LiveConsentAcknowledged { get; set; }
        /// <summary>Whether deep AI analysis is selected.</summary>
        public bool DeepAnalysis { get; set; }
        /// <summary>Stable browser account ID.</summary>
        [StringLength(64)] public string AccountId { get; set; } = "primary-founder-account";
        /// <summary>Safe browser account display name.</summary>
        [StringLength(100)] public string AccountDisplayName { get; set; } = "Primary Founder Scout account";
        /// <summary>Stable segment ID.</summary>
        [StringLength(64)] public string SegmentId { get; set; } = "initial-founder-search";
        /// <summary>Safe segment display name.</summary>
        [StringLength(100)] public string SegmentName { get; set; } = "Initial founder search";
        /// <summary>Bounded page limit metadata.</summary>
        [Range(1, 25)] public int MaximumPages { get; set; } = 2;
        /// <summary>Optional safe source adapter filter text.</summary>
        [StringLength(200)] public string? SafeFilter { get; set; }
        /// <summary>Maximum new profiles per run.</summary>
        [Range(1, 500)] public int MaxNewProfilesPerRun { get; set; }
        /// <summary>Maximum viewed profiles per run.</summary>
        [Range(1, 1000)] public int MaxViewedProfilesPerRun { get; set; }
        /// <summary>Maximum new profiles per day.</summary>
        [Range(1, 2000)] public int MaxNewProfilesPerDay { get; set; }
        /// <summary>Maximum runtime seconds.</summary>
        [Range(60, 7200)] public int MaxRuntimeSeconds { get; set; }
        /// <summary>Fixed delay preference in seconds.</summary>
        [Range(60, 604800)] public int CooldownSeconds { get; set; }
        /// <summary>Minimum deterministic parser completeness.</summary>
        [Range(typeof(decimal), "0", "1")] public decimal MinimumParserCompleteness { get; set; }
        /// <summary>Whether bounded raw HTML retention is enabled.</summary>
        public bool StoreRawHtml { get; set; }
        /// <summary>Strong-connect ranking threshold.</summary>
        [Range(0, 100)] public int StrongThreshold { get; set; }
        /// <summary>Exploratory ranking threshold.</summary>
        [Range(0, 100)] public int ExploratoryThreshold { get; set; }
        /// <summary>Monitor ranking threshold.</summary>
        [Range(0, 100)] public int MonitorThreshold { get; set; }
        /// <summary>Minimum evaluation confidence.</summary>
        [Range(typeof(decimal), "0", "1")] public decimal MinimumConfidence { get; set; }
        /// <summary>Primary manual review queue capacity.</summary>
        [Range(1, 500)] public int PrimaryQueueCapacity { get; set; }
        /// <summary>Reserve queue capacity.</summary>
        [Range(0, 500)] public int ReserveQueueCapacity { get; set; }
        /// <summary>Maximum generated draft characters.</summary>
        [Range(100, 5000)] public int DraftMaximumCharacters { get; set; }
        /// <summary>Raw profile retention days.</summary>
        [Range(0, 3650)] public int RawProfileDays { get; set; }
        /// <summary>Error artifact retention days.</summary>
        [Range(0, 3650)] public int ErrorArtifactDays { get; set; }
        /// <summary>Derived report retention days.</summary>
        [Range(0, 3650)] public int ReportDays { get; set; }
        /// <summary>Sensitive local persona display name.</summary>
        [StringLength(200)] public string PersonaDisplayName { get; set; } = string.Empty;
        /// <summary>Sensitive local persona target role.</summary>
        [StringLength(500)] public string PersonaTargetRole { get; set; } = string.Empty;
        /// <summary>Sensitive newline-delimited persona strengths.</summary>
        [StringLength(5000)] public string PersonaStrengths { get; set; } = string.Empty;
        /// <summary>Sensitive newline-delimited desired complements.</summary>
        [StringLength(5000)] public string PersonaSeeking { get; set; } = string.Empty;
        /// <summary>Sensitive local draft tone.</summary>
        [StringLength(500)] public string PersonaMessageTone { get; set; } = string.Empty;
        /// <summary>Production provider discriminator.</summary>
        [StringLength(32)] public string AiProvider { get; set; } = "OpenAI";
        /// <summary>HTTPS provider endpoint.</summary>
        [StringLength(512)] public string AiEndpoint { get; set; } = string.Empty;
        /// <summary>Model or Azure deployment.</summary>
        [StringLength(128)] public string AiDeployment { get; set; } = string.Empty;
        /// <summary>Provider request timeout seconds.</summary>
        [Range(5, 600)] public int AiTimeoutSeconds { get; set; }
        /// <summary>Maximum provider output tokens.</summary>
        [Range(256, 32000)] public int AiMaxOutputTokens { get; set; }
        /// <summary>Explicit acknowledgement of a potentially chargeable provider request.</summary>
        public bool ConfirmProviderCharge { get; set; }
        /// <summary>Explicit confirmation that a headed browser will open.</summary>
        public bool ConfirmHeadedAuthentication { get; set; }

        /// <summary>Maps authoritative typed values into bounded fields without secret content.</summary>
        public static FounderScoutWizardForm From(FounderScoutOnboardingEditor editor)
        {
            FounderScoutConfiguration value = editor.Configuration;
            BrowserAccount? account = editor.BrowserAccounts.Count == 0 ? null : editor.BrowserAccounts[0];
            DiscoverySegment? segment = editor.Segments.Count == 0 ? null : editor.Segments[0];
            StartupSchoolSourceOptions source = value.StartupSchool ?? StartupSchoolSourceOptions.Default;
            FounderScoutProcessingSettings processing = value.Processing ?? FounderScoutProcessingSettings.Default;
            bool consent = editor.Checks.GetValueOrDefault(FounderScoutOnboardingChecks.LiveConsent)?.Status == OnboardingCheckStatus.Passed;
            return new()
            {
                ExpectedSelectionRevision = editor.Selection.Revision,
                LiveSource = value.Discovery.Enabled,
                LiveConsentAcknowledged = consent,
                DeepAnalysis = value.Analysis.Enabled,
                AccountId = account?.Id ?? "primary-founder-account",
                AccountDisplayName = account?.DisplayName ?? "Primary Founder Scout account",
                SegmentId = segment?.Id ?? (account?.AssignedSegmentIds.Count > 0 ? account.AssignedSegmentIds[0] : null) ?? "initial-founder-search",
                SegmentName = segment?.Name ?? "Initial founder search",
                MaximumPages = ReadMaximumPages(segment?.ConfigurationJson) ?? 2,
                MaxNewProfilesPerRun = value.Discovery.MaxNewProfilesPerRun,
                MaxViewedProfilesPerRun = value.Discovery.MaxViewedProfilesPerRun,
                MaxNewProfilesPerDay = value.Discovery.MaxNewProfilesPerDay,
                MaxRuntimeSeconds = value.Discovery.MaxRuntimeSeconds,
                CooldownSeconds = value.Discovery.CooldownAfterCompletionSeconds,
                MinimumParserCompleteness = processing.MinimumParserCompleteness,
                StoreRawHtml = source.StoreRawHtml,
                StrongThreshold = value.Ranking.StrongConnectThreshold,
                ExploratoryThreshold = value.Ranking.ExploratoryThreshold,
                MonitorThreshold = value.Ranking.MonitorThreshold,
                MinimumConfidence = value.Ranking.MinimumConfidence,
                PrimaryQueueCapacity = value.Ranking.ManualInvitationQueueSize,
                ReserveQueueCapacity = value.Ranking.ReserveQueueSize,
                DraftMaximumCharacters = value.Invitation.MaximumCharacters,
                RawProfileDays = value.Retention.RawProfileDays,
                ErrorArtifactDays = value.Retention.ErrorArtifactDays,
                ReportDays = value.Retention.ReportDays,
                PersonaDisplayName = value.Persona.DisplayName,
                PersonaTargetRole = value.Persona.TargetRole,
                PersonaStrengths = string.Join(Environment.NewLine, value.Persona.Strengths),
                PersonaSeeking = string.Join(Environment.NewLine, value.Persona.Seeking),
                PersonaMessageTone = value.Persona.MessageTone,
                AiProvider = value.Ai.Provider,
                AiEndpoint = value.Ai.Endpoint,
                AiDeployment = value.Ai.Deployment,
                AiTimeoutSeconds = value.Ai.RequestTimeoutSeconds,
                AiMaxOutputTokens = value.Ai.MaxOutputTokens,
            };
        }

        private static int? ReadMaximumPages(string? configurationJson)
        {
            if (string.IsNullOrWhiteSpace(configurationJson)) return null;
            try
            {
                using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(configurationJson);
                return document.RootElement.TryGetProperty("maximumPages", out System.Text.Json.JsonElement value) && value.TryGetInt32(out int result) ? result : null;
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }
    }
}
