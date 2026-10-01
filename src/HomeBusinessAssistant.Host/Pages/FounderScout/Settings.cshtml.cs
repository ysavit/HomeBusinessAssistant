using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FounderScout.Application;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.FounderScout;

/// <summary>Focused OpenAI and local founder-context settings for Founder Scout simple mode.</summary>
[AutoValidateAntiforgeryToken]
public sealed class SettingsModel(HostManagementComposition management) : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Gets or sets the small editable settings surface.</summary>
    [BindProperty]
    public SettingsForm Form { get; set; } = new();

    /// <summary>Gets whether a protected provider key exists; its value is never loaded.</summary>
    public bool ApiKeyConfigured { get; private set; }

    /// <summary>Gets whether all local prerequisites for a provider run are present.</summary>
    public bool AnalysisReady => ApiKeyConfigured
        && !string.IsNullOrWhiteSpace(Form.ModelName)
        && Form.AnalysisEnabled;

    /// <summary>Loads the current immutable configuration and protected-key status.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(loadForm: true, cancellationToken).ConfigureAwait(false)) return NotFound();
        return Page();
    }

    /// <summary>Saves the non-secret OpenAI model and copied local context in a new immutable revision.</summary>
    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (management.Configurations is null) return NotFound();
        if (!ModelState.IsValid)
        {
            _ = await LoadAsync(loadForm: false, cancellationToken).ConfigureAwait(false);
            return Page();
        }

        AgentConfigurationRecord? current = await management.Configurations
            .GetCurrentAsync(FounderScoutDefaults.AgentId, cancellationToken)
            .ConfigureAwait(false);
        if (current is null) return NotFound();

        try
        {
            FounderScoutConfiguration existing = Deserialize(current);
            FounderScoutConfiguration codeOwned = FounderScoutSimpleMode.CreateConfiguration(existing);
            string? additionalContext = string.IsNullOrWhiteSpace(Form.AdditionalContext)
                ? null
                : Form.AdditionalContext.Trim();
            FounderScoutConfiguration updated = codeOwned with
            {
                Analysis = codeOwned.Analysis with
                {
                    Enabled = true,
                    BatchSize = Form.BatchSize,
                    MaximumConcurrency = 1,
                },
                Ai = codeOwned.Ai with
                {
                    Provider = "OpenAI",
                    Endpoint = string.Empty,
                    Deployment = Form.ModelName.Trim(),
                    ApiKeySecretReference = FounderScoutDefaults.ApiKeyReference.Value,
                },
                Persona = codeOwned.Persona with
                {
                    AdditionalContext = additionalContext,
                },
            };
            SaveConfigurationResult saved = await management.Configurations.SaveAsync(new(
                FounderScoutDefaults.AgentId,
                FounderScoutConfiguration.CurrentSchemaVersion,
                JsonSerializer.SerializeToElement(updated, JsonOptions),
                "local-web",
                "Updated Founder Scout OpenAI model and local founder context.",
                Guid.NewGuid(),
                Form.ExpectedRevisionNumber,
                Form.ExpectedHash), cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = saved.CurrentChanged
                ? "Founder Scout AI settings were saved. The API key remains a separate protected value."
                : "Founder Scout AI settings were already current.";
            return RedirectToPage();
        }
        catch (ConfigurationValidationException exception)
        {
            foreach (ConfigurationValidationError error in exception.Errors)
            {
                string field = error.Path switch
                {
                    "$.ai" or "$.ai.deployment" => "Form.ModelName",
                    "$.analysis.batchSize" => "Form.BatchSize",
                    "$.persona.additionalContext" => "Form.AdditionalContext",
                    _ => string.Empty,
                };
                ModelState.AddModelError(field, error.Message);
            }
        }
        catch (ConfigurationConcurrencyException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }

        _ = await LoadAsync(loadForm: false, cancellationToken).ConfigureAwait(false);
        return Page();
    }

    /// <summary>Stores a write-only OpenAI Platform key through the current-user protected secret store.</summary>
    public async Task<IActionResult> OnPostSetApiKeyAsync(string apiKey, CancellationToken cancellationToken)
    {
        if (management.Secrets is null) return NotFound();
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length is < 16 or > 8_192)
        {
            TempData["FlashMessage"] = "The API key was not changed because it was empty or outside the supported size.";
            return RedirectToPage();
        }

        await management.Secrets.SetAsync(
            FounderScoutDefaults.ApiKeyReference,
            apiKey.Trim(),
            cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = "The OpenAI API key was protected for this Windows user. It will never be displayed by the page.";
        return RedirectToPage();
    }

    /// <summary>Deletes the protected provider key without changing candidate history.</summary>
    public async Task<IActionResult> OnPostDeleteApiKeyAsync(CancellationToken cancellationToken)
    {
        if (management.Secrets is null) return NotFound();
        bool deleted = await management.Secrets.DeleteAsync(
            FounderScoutDefaults.ApiKeyReference,
            cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = deleted
            ? "The protected OpenAI API key was removed."
            : "No protected OpenAI API key was stored.";
        return RedirectToPage();
    }

    private async ValueTask<bool> LoadAsync(bool loadForm, CancellationToken cancellationToken)
    {
        if (management.Configurations is null || management.Secrets is null) return false;
        AgentConfigurationRecord? current = await management.Configurations
            .GetCurrentAsync(FounderScoutDefaults.AgentId, cancellationToken)
            .ConfigureAwait(false);
        if (current is null) return false;
        FounderScoutConfiguration configuration = Deserialize(current);
        if (loadForm)
        {
            Form = new()
            {
                ExpectedRevisionNumber = current.CurrentRevision.RevisionNumber,
                ExpectedHash = current.CurrentRevision.ConfigurationHash,
                AnalysisEnabled = configuration.Analysis.Enabled,
                ModelName = configuration.Ai.Deployment,
                BatchSize = configuration.Analysis.BatchSize,
                AdditionalContext = configuration.Persona.AdditionalContext ?? string.Empty,
            };
        }
        ApiKeyConfigured = await management.Secrets
            .ExistsAsync(FounderScoutDefaults.ApiKeyReference, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private static FounderScoutConfiguration Deserialize(AgentConfigurationRecord current)
    {
        using JsonDocument document = JsonDocument.Parse(current.CurrentRevision.CanonicalConfigurationJson);
        return document.RootElement.Deserialize<FounderScoutConfiguration>(JsonOptions)
            ?? throw new InvalidOperationException("The current Founder Scout configuration is invalid.");
    }

    /// <summary>The small non-secret Founder Scout AI form.</summary>
    public sealed class SettingsForm
    {
        /// <summary>Gets or sets the displayed immutable revision.</summary>
        public long ExpectedRevisionNumber { get; set; }

        /// <summary>Gets or sets the displayed immutable revision hash.</summary>
        [Required]
        public string ExpectedHash { get; set; } = string.Empty;

        /// <summary>Gets whether deep evaluation is enabled in the effective document.</summary>
        public bool AnalysisEnabled { get; set; } = true;

        /// <summary>Gets or sets the OpenAI Responses model identifier.</summary>
        [Required, StringLength(128, MinimumLength = 1)]
        [RegularExpression(@"^[A-Za-z0-9._:-]+$", ErrorMessage = "Enter a valid OpenAI model identifier.")]
        public string ModelName { get; set; } = "gpt-5.4-mini";

        /// <summary>Gets or sets the bounded number of candidates evaluated per run.</summary>
        [Range(1, 100)]
        public int BatchSize { get; set; } = 20;

        /// <summary>Gets or sets local founder context copied from ChatGPT or written by the owner.</summary>
        [StringLength(20_000)]
        public string AdditionalContext { get; set; } = string.Empty;
    }
}
