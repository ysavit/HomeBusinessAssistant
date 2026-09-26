using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FounderScout.Application;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WakeRemote.Application;

namespace HomeBusinessAssistant.Host.Pages.Agents;

/// <summary>Typed immutable configuration editor for both built-in agents.</summary>
[AutoValidateAntiforgeryToken]
public sealed class ConfigurationModel(HostManagementComposition management) : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions IndentedJsonOptions = new(JsonOptions) { WriteIndented = true };

    /// <summary>Gets or sets the typed editor fields.</summary>
    [BindProperty]
    public ConfigurationForm Form { get; set; } = new();

    /// <summary>Gets or sets display-safe primitive values for the generic editor.</summary>
    [BindProperty]
    public Dictionary<string, string> GenericValues { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets the selected agent definition.</summary>
    public AgentDefinitionRecord? Agent { get; private set; }

    /// <summary>Gets recent immutable revisions.</summary>
    public IReadOnlyList<ConfigurationRevisionRecord> History { get; private set; } = [];

    /// <summary>Gets whether the Founder Scout AI secret exists; its value is never read.</summary>
    public bool SecretExists { get; private set; }

    /// <summary>Gets the latest append-only audit event for the configured secret reference.</summary>
    public AuditEventRecord? LastSecretChange { get; private set; }

    /// <summary>Gets whether the editor is for Wake Remote.</summary>
    public bool IsWakeRemote => Form.AgentId == WakeRemoteDefaults.AgentId.Value;

    /// <summary>Gets whether the current agent uses the Founder Scout typed adapter.</summary>
    public bool IsFounderScout => Form.AgentId == FounderScoutDefaults.AgentId.Value;

    /// <summary>Gets safe parsed schema metadata for an agent without a typed UI adapter.</summary>
    public GenericConfigurationSchema? GenericSchema { get; private set; }

    /// <summary>Gets protected secret existence by generic field name.</summary>
    public IReadOnlyDictionary<string, bool> GenericSecretExists { get; private set; } =
        new Dictionary<string, bool>(StringComparer.Ordinal);

    /// <summary>Loads the current revision into typed fields.</summary>
    public async Task<IActionResult> OnGetAsync(string id, CancellationToken cancellationToken)
    {
        if (!AgentId.TryParse(id, out AgentId agentId)
            || management.Queries is null
            || management.Configurations is null)
        {
            return NotFound();
        }

        var detail = await management.Queries.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (detail is null || detail.Configuration is null)
        {
            return NotFound();
        }

        Agent = detail.Agent.Definition;
        History = detail.ConfigurationHistory;
        if (agentId == WakeRemoteDefaults.AgentId || agentId == FounderScoutDefaults.AgentId)
        {
            Form = ParseForm(detail.Configuration);
        }
        else
        {
            GenericSchema = await LoadGenericSchemaAsync(agentId, cancellationToken).ConfigureAwait(false);
            Form = ConfigurationForm.FromGeneric(detail.Configuration);
            GenericValues = ParseGenericValues(detail.Configuration, GenericSchema);
        }
        await LoadSecretStateAsync(cancellationToken).ConfigureAwait(false);
        return Page();
    }

    /// <summary>Validates and creates/promotes one immutable configuration revision.</summary>
    public async Task<IActionResult> OnPostSaveAsync(string id, CancellationToken cancellationToken)
    {
        if (!AgentId.TryParse(id, out AgentId agentId)
            || Form.AgentId != agentId.Value
            || management.Queries is null
            || management.Configurations is null)
        {
            return NotFound();
        }

        var detail = await management.Queries.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            return NotFound();
        }

        Agent = detail.Agent.Definition;
        History = detail.ConfigurationHistory;
        if (agentId != WakeRemoteDefaults.AgentId && agentId != FounderScoutDefaults.AgentId)
        {
            GenericSchema = await LoadGenericSchemaAsync(agentId, cancellationToken).ConfigureAwait(false);
        }
        await LoadSecretStateAsync(cancellationToken).ConfigureAwait(false);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            JsonElement document = Form.AgentId switch
            {
                "wake-remote" => JsonSerializer.SerializeToElement(Form.ToWakeRemote(), JsonOptions),
                "founder-scout" => JsonSerializer.SerializeToElement(Form.ToFounderScout(), JsonOptions),
                _ when TryBuildGenericDocument(out JsonElement generic) => generic,
                _ => throw new ConfigurationValidationException(
                    [new("configuration.genericInput", "$", "One or more generic configuration values are invalid.")]),
            };
            SaveConfigurationResult result = await management.Configurations.SaveAsync(new(
                agentId,
                "1.0",
                document,
                "local-web",
                Form.ChangeSummary,
                Guid.NewGuid(),
                Form.ExpectedRevisionNumber,
                Form.ExpectedHash), cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = result.CurrentChanged
                ? $"Configuration revision {result.Revision.RevisionNumber} is now current."
                : $"No changes detected; revision {result.Revision.RevisionNumber} remains current.";
            return RedirectToPage(new { id });
        }
        catch (ConfigurationValidationException exception)
        {
            foreach (ConfigurationValidationError error in exception.Errors)
            {
                ModelState.AddModelError(MapPath(error.Path), error.Message);
            }
        }
        catch (ConfigurationConcurrencyException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }
        catch (JsonException)
        {
            ModelState.AddModelError("Form.StartupSchoolOptionsJson", "The versioned browser source options must be valid JSON matching the typed schema.");
            ModelState.AddModelError("Form.ProcessingOptionsJson", "The versioned processing options must be valid JSON matching the typed schema.");
        }

        return Page();
    }

    /// <summary>Stores a Founder Scout API secret separately through DPAPI and redirects without echoing it.</summary>
    public async Task<IActionResult> OnPostSetSecretAsync(
        string id,
        string secretValue,
        string? fieldName,
        CancellationToken cancellationToken)
    {
        if (!AgentId.TryParse(id, out AgentId agentId) || management.Secrets is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(secretValue) || secretValue.Length > 65_536)
        {
            TempData["FlashMessage"] = "The secret was not changed because the submitted value was empty or too large.";
            return RedirectToPage(new { id });
        }

        HomeBusinessAssistant.Application.Secrets.SecretReference? reference = id == FounderScoutDefaults.AgentId.Value
            ? FounderScoutDefaults.ApiKeyReference
            : await ResolveGenericSecretReferenceAsync(agentId, fieldName, cancellationToken).ConfigureAwait(false);
        if (reference is null)
        {
            return NotFound();
        }

        await management.Secrets.SetAsync(reference.Value, secretValue, cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = "The API secret was protected for the current Windows user. Its value was not retained by the page.";
        return RedirectToPage(new { id });
    }

    /// <summary>Deletes the separate Founder Scout secret value.</summary>
    public async Task<IActionResult> OnPostDeleteSecretAsync(
        string id,
        string? fieldName,
        CancellationToken cancellationToken)
    {
        if (!AgentId.TryParse(id, out AgentId agentId) || management.Secrets is null)
        {
            return NotFound();
        }

        HomeBusinessAssistant.Application.Secrets.SecretReference? reference = id == FounderScoutDefaults.AgentId.Value
            ? FounderScoutDefaults.ApiKeyReference
            : await ResolveGenericSecretReferenceAsync(agentId, fieldName, cancellationToken).ConfigureAwait(false);
        if (reference is null)
        {
            return NotFound();
        }

        bool deleted = await management.Secrets.DeleteAsync(reference.Value, cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = deleted ? "The protected API secret was removed." : "No API secret was stored.";
        return RedirectToPage(new { id });
    }

    private static ConfigurationForm ParseForm(AgentConfigurationRecord configuration)
    {
        using JsonDocument document = JsonDocument.Parse(configuration.CurrentRevision.CanonicalConfigurationJson);
        if (configuration.AgentId == WakeRemoteDefaults.AgentId)
        {
            WakeRemoteConfiguration value = WakeRemoteInput.ParseConfiguration(document.RootElement).Value
                ?? throw new InvalidOperationException("The current Wake Remote configuration is invalid.");
            return ConfigurationForm.From(configuration, value);
        }

        if (configuration.AgentId == FounderScoutDefaults.AgentId)
        {
            FounderScoutConfiguration value = document.RootElement.Deserialize<FounderScoutConfiguration>(JsonOptions)
                ?? throw new InvalidOperationException("The current Founder Scout configuration is invalid.");
            return ConfigurationForm.From(configuration, value);
        }

        throw new InvalidOperationException("This installed agent has no typed Stage 08 configuration adapter.");
    }

    private async ValueTask LoadSecretStateAsync(CancellationToken cancellationToken)
    {
        SecretExists = Form.AgentId == FounderScoutDefaults.AgentId.Value
            && management.Secrets is not null
            && await management.Secrets.ExistsAsync(FounderScoutDefaults.ApiKeyReference, cancellationToken).ConfigureAwait(false);
        if (Form.AgentId != FounderScoutDefaults.AgentId.Value || management.Queries is null)
        {
            if (GenericSchema is not null && management.Secrets is not null)
            {
                var statuses = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (GenericConfigurationField field in GenericSchema.Fields.Where(item => item.IsSecretReference))
                {
                    if (GenericValues.TryGetValue(field.Name, out string? text)
                        && HomeBusinessAssistant.Application.Secrets.SecretReference.TryParse(text, out var reference))
                    {
                        statuses[field.Name] = await management.Secrets.ExistsAsync(reference, cancellationToken).ConfigureAwait(false);
                    }
                }

                GenericSecretExists = statuses;
            }

            return;
        }

        string referenceHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(FounderScoutDefaults.ApiKeyReference.Value)));
        ManagementPage<AuditEventRecord> events = await management.Queries.GetAuditAsync(
            new(null, null, null, "secret.", "secret-reference", null, referenceHash, null, 1, 1),
            cancellationToken).ConfigureAwait(false);
        LastSecretChange = events.Items.Count == 0 ? null : events.Items[0];
    }

    private string MapPath(string path)
    {
        if (GenericSchema is not null && path.StartsWith("$.", StringComparison.Ordinal))
        {
            return $"GenericValues[{path[2..]}]";
        }

        return path switch
        {
            "$.timeZoneId" => "Form.TimeZoneId",
            "$.networkReadyTimeoutSeconds" => "Form.NetworkReadyTimeoutSeconds",
            "$.networkProbeIntervalSeconds" => "Form.NetworkProbeIntervalSeconds",
            "$.windowHeartbeatIntervalSeconds" => "Form.WindowHeartbeatIntervalSeconds",
            "$.maximumWakeStalenessSeconds" => "Form.MaximumWakeStalenessSeconds",
            "$.ai.apiKeySecretReference" => "Form.AiSecretReference",
            "$.ai.requestTimeoutSeconds" => "Form.AiRequestTimeoutSeconds",
            "$.discovery.browserConcurrency" => "Form.BrowserConcurrency",
            "$.discovery.maxNewProfilesPerRun" => "Form.MaxNewProfilesPerRun",
            var sourcePath when sourcePath.StartsWith("$.startupSchool", StringComparison.Ordinal) => "Form.StartupSchoolOptionsJson",
            var processingPath when processingPath.StartsWith("$.processing", StringComparison.Ordinal) => "Form.ProcessingOptionsJson",
            "$.analysis.batchSize" => "Form.AnalysisBatchSize",
            "$.analysis.maximumConcurrency" => "Form.AnalysisMaximumConcurrency",
            "$.ranking" => "Form.StrongConnectThreshold",
            "$.invitation.maximumCharacters" => "Form.InvitationMaximumCharacters",
            _ => string.Empty,
        };
    }

    private async ValueTask<GenericConfigurationSchema> LoadGenericSchemaAsync(
        AgentId agentId,
        CancellationToken cancellationToken)
    {
        return management.ConfigurationSchemas is null
            ? throw new InvalidOperationException("The generic configuration schema catalog is unavailable.")
            : await management.ConfigurationSchemas.GetAsync(agentId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The installed agent configuration schema is unavailable.");
    }

    private static Dictionary<string, string> ParseGenericValues(
        AgentConfigurationRecord configuration,
        GenericConfigurationSchema schema)
    {
        using JsonDocument document = JsonDocument.Parse(configuration.CurrentRevision.CanonicalConfigurationJson);
        return new Dictionary<string, string>(
            GenericConfigurationFormCodec.ReadValues(schema, document.RootElement),
            StringComparer.Ordinal);
    }

    private bool TryBuildGenericDocument(out JsonElement document)
    {
        document = default;
        if (GenericSchema is null || !GenericSchema.SupportsGenericEditor)
        {
            ModelState.AddModelError(string.Empty, "This schema requires a typed configuration adapter.");
            return false;
        }

        GenericConfigurationFormResult result = GenericConfigurationFormCodec.Build(GenericSchema, GenericValues);
        foreach (GenericConfigurationFormError error in result.Errors)
        {
            ModelState.AddModelError(
                string.IsNullOrEmpty(error.FieldName) ? string.Empty : $"GenericValues[{error.FieldName}]",
                error.Message);
        }

        if (!ModelState.IsValid || result.Configuration is not JsonElement value) return false;
        document = value;
        return true;
    }

    private async ValueTask<HomeBusinessAssistant.Application.Secrets.SecretReference?> ResolveGenericSecretReferenceAsync(
        AgentId agentId,
        string? fieldName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fieldName)
            || management.Configurations is null
            || management.ConfigurationSchemas is null) return null;
        GenericConfigurationSchema? schema = await management.ConfigurationSchemas.GetAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (schema?.Fields.Any(field => field.Name == fieldName && field.IsSecretReference) != true) return null;
        AgentConfigurationRecord? configuration = await management.Configurations.GetCurrentAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (configuration is null) return null;
        using JsonDocument document = JsonDocument.Parse(configuration.CurrentRevision.CanonicalConfigurationJson);
        return document.RootElement.TryGetProperty(fieldName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && HomeBusinessAssistant.Application.Secrets.SecretReference.TryParse(value.GetString(), out var reference)
                ? reference : null;
    }

    /// <summary>All typed fields shared by the two built-in configuration forms.</summary>
    public sealed class ConfigurationForm
    {
        /// <summary>Gets or sets the stable agent ID.</summary>
        [Required]
        public string AgentId { get; set; } = string.Empty;

        /// <summary>Gets or sets the displayed revision for optimistic concurrency.</summary>
        public long ExpectedRevisionNumber { get; set; }

        /// <summary>Gets or sets the displayed hash for optimistic concurrency.</summary>
        [Required]
        public string ExpectedHash { get; set; } = string.Empty;

        /// <summary>Gets or sets the required human change summary.</summary>
        [Required, StringLength(1_000, MinimumLength = 3)]
        public string ChangeSummary { get; set; } = "Updated typed settings from the local management UI.";

        /// <summary>Gets or sets the Wake Remote Windows time zone.</summary>
        public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
        /// <summary>Gets or sets the network readiness timeout.</summary>
        public int NetworkReadyTimeoutSeconds { get; set; }
        /// <summary>Gets or sets the network probe interval.</summary>
        public int NetworkProbeIntervalSeconds { get; set; }
        /// <summary>Gets or sets the availability heartbeat interval.</summary>
        public int WindowHeartbeatIntervalSeconds { get; set; }
        /// <summary>Gets or sets the maximum acceptable wake staleness.</summary>
        public int MaximumWakeStalenessSeconds { get; set; }
        /// <summary>Gets or sets whether the display remains on.</summary>
        public bool KeepDisplayOn { get; set; }
        /// <summary>Gets or sets the read-only remote provider kind.</summary>
        public RemoteProviderKind RemoteProviderKind { get; set; }
        /// <summary>Gets or sets comma-separated service names.</summary>
        public string ServiceNames { get; set; } = string.Empty;
        /// <summary>Gets or sets comma-separated process names.</summary>
        public string ProcessNames { get; set; } = string.Empty;
        /// <summary>Gets or sets whether a loopback listener is checked.</summary>
        public bool CheckLocalListener { get; set; }
        /// <summary>Gets or sets the loopback listener host.</summary>
        public string ListenerHost { get; set; } = "127.0.0.1";
        /// <summary>Gets or sets the loopback listener port.</summary>
        public int ListenerPort { get; set; }
        /// <summary>Gets or sets whether the optional DNS probe is enabled.</summary>
        public bool DnsProbeEnabled { get; set; }
        /// <summary>Gets or sets the optional DNS hostname.</summary>
        public string? DnsHostName { get; set; }
        /// <summary>Gets or sets whether the optional TCP probe is enabled.</summary>
        public bool TcpProbeEnabled { get; set; }
        /// <summary>Gets or sets the optional TCP host.</summary>
        public string? TcpHost { get; set; }
        /// <summary>Gets or sets the optional TCP port.</summary>
        public int TcpPort { get; set; }

        /// <summary>Gets or sets the displayed Founder Scout data root.</summary>
        public string DataDirectory { get; set; } = string.Empty;
        /// <summary>Gets or sets whether discovery is enabled.</summary>
        public bool DiscoveryEnabled { get; set; }
        /// <summary>Gets or sets the browser concurrency, fixed at one in V1.</summary>
        public int BrowserConcurrency { get; set; }
        /// <summary>Gets or sets the new-profile run limit.</summary>
        public int MaxNewProfilesPerRun { get; set; }
        /// <summary>Gets or sets the viewed-profile run limit.</summary>
        public int MaxViewedProfilesPerRun { get; set; }
        /// <summary>Gets or sets the daily new-profile limit.</summary>
        public int MaxNewProfilesPerDay { get; set; }
        /// <summary>Gets or sets the discovery runtime limit.</summary>
        public int DiscoveryMaxRuntimeSeconds { get; set; }
        /// <summary>Gets or sets the completion-based cooldown.</summary>
        public int CooldownAfterCompletionSeconds { get; set; }
        /// <summary>Gets or sets the consecutive-known stop threshold.</summary>
        public int StopAfterConsecutiveKnownProfiles { get; set; }
        /// <summary>Gets or sets the complete versioned browser source options as typed JSON.</summary>
        [Required, StringLength(65_536, MinimumLength = 2)]
        public string StartupSchoolOptionsJson { get; set; } = string.Empty;
        /// <summary>Gets or sets the versioned deterministic parsing and screening policy as typed JSON.</summary>
        [Required, StringLength(32_768, MinimumLength = 2)]
        public string ProcessingOptionsJson { get; set; } = string.Empty;
        /// <summary>Gets or sets whether analysis is enabled.</summary>
        public bool AnalysisEnabled { get; set; }
        /// <summary>Gets or sets the analysis batch size.</summary>
        public int AnalysisBatchSize { get; set; }
        /// <summary>Gets or sets the analysis concurrency.</summary>
        public int AnalysisMaximumConcurrency { get; set; }
        /// <summary>Gets or sets whether the fast screen is enabled.</summary>
        public bool FastScreenEnabled { get; set; }
        /// <summary>Gets or sets the deep-analysis threshold.</summary>
        public int DeepAnalysisThreshold { get; set; }
        /// <summary>Gets or sets the analysis retry bound.</summary>
        public int AnalysisMaximumRetries { get; set; }
        /// <summary>Gets or sets the strong-connect threshold.</summary>
        public int StrongConnectThreshold { get; set; }
        /// <summary>Gets or sets the exploratory threshold.</summary>
        public int ExploratoryThreshold { get; set; }
        /// <summary>Gets or sets the monitor threshold.</summary>
        public int MonitorThreshold { get; set; }
        /// <summary>Gets or sets the minimum evidence confidence.</summary>
        public decimal MinimumConfidence { get; set; }
        /// <summary>Gets or sets the top-candidate list bound.</summary>
        public int TopCandidateCount { get; set; }
        /// <summary>Gets or sets the manual invitation queue bound.</summary>
        public int ManualInvitationQueueSize { get; set; }
        /// <summary>Gets or sets the reserve queue bound.</summary>
        public int ReserveQueueSize { get; set; }
        /// <summary>Gets or sets the invitation-priority fit weight.</summary>
        public decimal OurFitWeight { get; set; }
        /// <summary>Gets or sets the invitation-priority founder-quality weight.</summary>
        public decimal FounderQualityWeight { get; set; }
        /// <summary>Gets or sets the invitation-priority confidence weight.</summary>
        public decimal ConfidenceWeight { get; set; }
        /// <summary>Gets or sets the invitation-priority activity weight.</summary>
        public decimal ActivityWeight { get; set; }
        /// <summary>Gets or sets the maximum risk penalty.</summary>
        public decimal MaximumRiskPenalty { get; set; }
        /// <summary>Gets or sets whether short drafts are generated.</summary>
        public bool GenerateShortVersion { get; set; }
        /// <summary>Gets or sets whether detailed drafts are generated.</summary>
        public bool GenerateDetailedVersion { get; set; }
        /// <summary>Gets or sets the draft character limit.</summary>
        public int InvitationMaximumCharacters { get; set; }
        /// <summary>Gets or sets the draft similarity threshold.</summary>
        public decimal SimilarityThreshold { get; set; }
        /// <summary>Gets or sets the AI provider name.</summary>
        public string AiProvider { get; set; } = string.Empty;
        /// <summary>Gets or sets the non-secret AI endpoint.</summary>
        public string AiEndpoint { get; set; } = string.Empty;
        /// <summary>Gets or sets the AI deployment name.</summary>
        public string AiDeployment { get; set; } = string.Empty;
        /// <summary>Gets or sets the opaque secret reference, never its value.</summary>
        public string AiSecretReference { get; set; } = FounderScoutDefaults.ApiKeyReference.Value;
        /// <summary>Gets or sets the AI request timeout.</summary>
        public int AiRequestTimeoutSeconds { get; set; }
        /// <summary>Gets or sets raw profile retention days.</summary>
        public int RawProfileDays { get; set; }
        /// <summary>Gets or sets error artifact retention days.</summary>
        public int ErrorArtifactDays { get; set; }
        /// <summary>Gets or sets report retention days.</summary>
        public int ReportDays { get; set; }
        /// <summary>Gets or sets the stable founder-persona reference.</summary>
        public string PersonaReference { get; set; } = string.Empty;
        /// <summary>Gets or sets the local founder display name.</summary>
        public string PersonaDisplayName { get; set; } = string.Empty;
        /// <summary>Gets or sets the target founder role.</summary>
        public string PersonaTargetRole { get; set; } = string.Empty;
        /// <summary>Gets or sets newline-delimited founder strengths.</summary>
        public string PersonaStrengths { get; set; } = string.Empty;
        /// <summary>Gets or sets newline-delimited desired complements.</summary>
        public string PersonaSeeking { get; set; } = string.Empty;
        /// <summary>Gets or sets the message tone.</summary>
        public string PersonaMessageTone { get; set; } = string.Empty;
        /// <summary>Gets or sets newline-delimited claims drafts must avoid.</summary>
        public string PersonaAvoidClaims { get; set; } = string.Empty;

        /// <summary>Creates a typed Wake Remote document; forced sleep stays false in V1.</summary>
        public WakeRemoteConfiguration ToWakeRemote() => new(
            WakeRemoteConfiguration.CurrentSchemaVersion,
            TimeZoneId,
            NetworkReadyTimeoutSeconds,
            NetworkProbeIntervalSeconds,
            WindowHeartbeatIntervalSeconds,
            MaximumWakeStalenessSeconds,
            KeepDisplayOn,
            ReleaseToNormalPowerPolicyAfterWindow: true,
            ForceSleepAfterWindow: false,
            new(
                RemoteProviderKind,
                SplitNames(ServiceNames),
                SplitNames(ProcessNames),
                CheckLocalListener,
                ListenerHost,
                ListenerPort,
                DiagnosticReady: RemoteProviderKind == RemoteProviderKind.DiagnosticFake),
            new(DnsProbeEnabled, NullIfBlank(DnsHostName)),
            new(TcpProbeEnabled, NullIfBlank(TcpHost), TcpPort));

        /// <summary>Creates a typed Founder Scout baseline document with fixed safety switches.</summary>
        public FounderScoutConfiguration ToFounderScout()
        {
            StartupSchoolSourceOptions source = string.IsNullOrWhiteSpace(StartupSchoolOptionsJson)
                ? StartupSchoolSourceOptions.Default
                : JsonSerializer.Deserialize<StartupSchoolSourceOptions>(StartupSchoolOptionsJson, JsonOptions)
                    ?? throw new JsonException("Startup School source options are required.");
            FounderScoutProcessingSettings processing = string.IsNullOrWhiteSpace(ProcessingOptionsJson)
                ? FounderScoutProcessingSettings.Default
                : JsonSerializer.Deserialize<FounderScoutProcessingSettings>(ProcessingOptionsJson, JsonOptions)
                    ?? throw new JsonException("Founder Scout processing options are required.");
            return new(
                FounderScoutConfiguration.CurrentSchemaVersion,
                DataDirectory,
                new(DiscoveryEnabled, BrowserConcurrency, MaxNewProfilesPerRun, MaxViewedProfilesPerRun, MaxNewProfilesPerDay, DiscoveryMaxRuntimeSeconds, CooldownAfterCompletionSeconds, StopAfterConsecutiveKnownProfiles, true, true, true, true, false),
                new(AnalysisEnabled, AnalysisBatchSize, AnalysisMaximumConcurrency, FastScreenEnabled, DeepAnalysisThreshold, true, AnalysisMaximumRetries),
                new(StrongConnectThreshold, ExploratoryThreshold, MonitorThreshold, MinimumConfidence, TopCandidateCount, ManualInvitationQueueSize, ReserveQueueSize),
                new(OurFitWeight, FounderQualityWeight, ConfidenceWeight, ActivityWeight, MaximumRiskPenalty),
                new(GenerateShortVersion, GenerateDetailedVersion, InvitationMaximumCharacters, true, true, true, SimilarityThreshold),
                new(AiProvider, AiEndpoint, AiDeployment, AiSecretReference, AiRequestTimeoutSeconds),
                new(RawProfileDays, ErrorArtifactDays, ReportDays),
                new(
                    "1.0",
                    PersonaReference,
                    PersonaDisplayName,
                    PersonaTargetRole,
                    SplitLines(PersonaStrengths),
                    SplitLines(PersonaSeeking),
                    PersonaMessageTone,
                    SplitLines(PersonaAvoidClaims)),
                source,
                processing);
        }

        /// <summary>Maps current Wake Remote values into the form.</summary>
        public static ConfigurationForm From(AgentConfigurationRecord current, WakeRemoteConfiguration value) => new()
        {
            AgentId = current.AgentId.Value,
            ExpectedRevisionNumber = current.CurrentRevision.RevisionNumber,
            ExpectedHash = current.CurrentRevision.ConfigurationHash,
            TimeZoneId = value.TimeZoneId,
            NetworkReadyTimeoutSeconds = value.NetworkReadyTimeoutSeconds,
            NetworkProbeIntervalSeconds = value.NetworkProbeIntervalSeconds,
            WindowHeartbeatIntervalSeconds = value.WindowHeartbeatIntervalSeconds,
            MaximumWakeStalenessSeconds = value.MaximumWakeStalenessSeconds,
            KeepDisplayOn = value.KeepDisplayOn,
            RemoteProviderKind = value.RemoteProvider.Kind,
            ServiceNames = string.Join(", ", value.RemoteProvider.ServiceNames),
            ProcessNames = string.Join(", ", value.RemoteProvider.ProcessNames),
            CheckLocalListener = value.RemoteProvider.CheckLocalListener,
            ListenerHost = value.RemoteProvider.ListenerHost,
            ListenerPort = value.RemoteProvider.ListenerPort,
            DnsProbeEnabled = value.DnsProbe.Enabled,
            DnsHostName = value.DnsProbe.HostName,
            TcpProbeEnabled = value.TcpProbe.Enabled,
            TcpHost = value.TcpProbe.Host,
            TcpPort = value.TcpProbe.Port,
        };

        /// <summary>Maps current Founder Scout values into the form.</summary>
        public static ConfigurationForm From(AgentConfigurationRecord current, FounderScoutConfiguration value) => new()
        {
            AgentId = current.AgentId.Value,
            ExpectedRevisionNumber = current.CurrentRevision.RevisionNumber,
            ExpectedHash = current.CurrentRevision.ConfigurationHash,
            DataDirectory = value.DataDirectory,
            DiscoveryEnabled = value.Discovery.Enabled,
            BrowserConcurrency = value.Discovery.BrowserConcurrency,
            MaxNewProfilesPerRun = value.Discovery.MaxNewProfilesPerRun,
            MaxViewedProfilesPerRun = value.Discovery.MaxViewedProfilesPerRun,
            MaxNewProfilesPerDay = value.Discovery.MaxNewProfilesPerDay,
            DiscoveryMaxRuntimeSeconds = value.Discovery.MaxRuntimeSeconds,
            CooldownAfterCompletionSeconds = value.Discovery.CooldownAfterCompletionSeconds,
            StopAfterConsecutiveKnownProfiles = value.Discovery.StopAfterConsecutiveKnownProfiles,
            StartupSchoolOptionsJson = JsonSerializer.Serialize(value.StartupSchool ?? StartupSchoolSourceOptions.Default, IndentedJsonOptions),
            ProcessingOptionsJson = JsonSerializer.Serialize(value.Processing ?? FounderScoutProcessingSettings.Default, IndentedJsonOptions),
            AnalysisEnabled = value.Analysis.Enabled,
            AnalysisBatchSize = value.Analysis.BatchSize,
            AnalysisMaximumConcurrency = value.Analysis.MaximumConcurrency,
            FastScreenEnabled = value.Analysis.FastScreenEnabled,
            DeepAnalysisThreshold = value.Analysis.DeepAnalysisThreshold,
            AnalysisMaximumRetries = value.Analysis.MaximumRetries,
            StrongConnectThreshold = value.Ranking.StrongConnectThreshold,
            ExploratoryThreshold = value.Ranking.ExploratoryThreshold,
            MonitorThreshold = value.Ranking.MonitorThreshold,
            MinimumConfidence = value.Ranking.MinimumConfidence,
            TopCandidateCount = value.Ranking.TopCandidateCount,
            ManualInvitationQueueSize = value.Ranking.ManualInvitationQueueSize,
            ReserveQueueSize = value.Ranking.ReserveQueueSize,
            OurFitWeight = value.Priority.OurFitWeight,
            FounderQualityWeight = value.Priority.FounderQualityWeight,
            ConfidenceWeight = value.Priority.ConfidenceWeight,
            ActivityWeight = value.Priority.ActivityWeight,
            MaximumRiskPenalty = value.Priority.MaximumRiskPenalty,
            GenerateShortVersion = value.Invitation.GenerateShortVersion,
            GenerateDetailedVersion = value.Invitation.GenerateDetailedVersion,
            InvitationMaximumCharacters = value.Invitation.MaximumCharacters,
            SimilarityThreshold = value.Invitation.SimilarityThreshold,
            AiProvider = value.Ai.Provider,
            AiEndpoint = value.Ai.Endpoint,
            AiDeployment = value.Ai.Deployment,
            AiSecretReference = value.Ai.ApiKeySecretReference,
            AiRequestTimeoutSeconds = value.Ai.RequestTimeoutSeconds,
            RawProfileDays = value.Retention.RawProfileDays,
            ErrorArtifactDays = value.Retention.ErrorArtifactDays,
            ReportDays = value.Retention.ReportDays,
            PersonaReference = value.Persona.Reference,
            PersonaDisplayName = value.Persona.DisplayName,
            PersonaTargetRole = value.Persona.TargetRole,
            PersonaStrengths = string.Join(Environment.NewLine, value.Persona.Strengths),
            PersonaSeeking = string.Join(Environment.NewLine, value.Persona.Seeking),
            PersonaMessageTone = value.Persona.MessageTone,
            PersonaAvoidClaims = string.Join(Environment.NewLine, value.Persona.AvoidClaims),
        };

        /// <summary>Maps common immutable-revision fields for a generic agent.</summary>
        public static ConfigurationForm FromGeneric(AgentConfigurationRecord current) => new()
        {
            AgentId = current.AgentId.Value,
            ExpectedRevisionNumber = current.CurrentRevision.RevisionNumber,
            ExpectedHash = current.CurrentRevision.ConfigurationHash,
            ChangeSummary = "Updated settings from the safe generic configuration editor.",
        };

        private static string[] SplitNames(string value) => value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        private static string[] SplitLines(string value) => value
            .Split(["\r\n", "\n"], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
