using System.ComponentModel.DataAnnotations;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Domain.Agents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.Onboarding;

/// <summary>Safe generic per-agent configuration and protected-secret wizard.</summary>
[AutoValidateAntiforgeryToken]
public sealed class AgentModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets or sets the active durable onboarding session.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid SessionId { get; set; }

    /// <summary>Gets or sets the installed agent identifier.</summary>
    [BindProperty(SupportsGet = true)]
    public string AgentId { get; set; } = string.Empty;

    /// <summary>Gets or sets the displayed selection revision.</summary>
    [BindProperty]
    public long ExpectedSelectionRevision { get; set; }

    /// <summary>Gets or sets the displayed immutable configuration revision number.</summary>
    [BindProperty]
    public long? ExpectedConfigurationRevision { get; set; }

    /// <summary>Gets or sets the displayed immutable configuration hash.</summary>
    [BindProperty]
    public string? ExpectedConfigurationHash { get; set; }

    /// <summary>Gets or sets safe primitive generic form values.</summary>
    [BindProperty]
    public Dictionary<string, string> GenericValues { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the bounded immutable-revision note.</summary>
    [BindProperty]
    [StringLength(512)]
    public string ChangeSummary { get; set; } = "Reviewed during onboarding.";

    /// <summary>Gets the current editor projection.</summary>
    public GenericAgentOnboardingEditor? Editor { get; private set; }

    /// <summary>Loads current values and only protected-secret status.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!TryGetAgentId(out AgentId agentId) || management.AgentOnboarding is null)
        {
            return NotFound();
        }

        try
        {
            Editor = await management.AgentOnboarding.GetGenericEditorAsync(
                SessionId,
                agentId,
                "local-web",
                cancellationToken).ConfigureAwait(false);
            ExpectedSelectionRevision = Editor.Selection.Revision;
            ExpectedConfigurationRevision = Editor.Configuration?.CurrentRevision.RevisionNumber;
            ExpectedConfigurationHash = Editor.Configuration?.CurrentRevision.ConfigurationHash;
            GenericValues = new(Editor.Values, StringComparer.Ordinal);
            return Page();
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    /// <summary>Validates, saves/reviews, and continues to the agent list or exits to Dashboard.</summary>
    public async Task<IActionResult> OnPostSaveAsync(string destination, CancellationToken cancellationToken)
    {
        if (!TryGetAgentId(out AgentId agentId) || management.AgentOnboarding is null)
        {
            return NotFound();
        }

        try
        {
            Editor = await management.AgentOnboarding.GetGenericEditorAsync(
                SessionId,
                agentId,
                "local-web",
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        GenericConfigurationFormResult form = GenericConfigurationFormCodec.Build(Editor.Schema, GenericValues);
        foreach (GenericConfigurationFormError error in form.Errors)
        {
            ModelState.AddModelError(
                string.IsNullOrEmpty(error.FieldName) ? string.Empty : $"GenericValues[{error.FieldName}]",
                error.Message);
        }

        if (!ModelState.IsValid || form.Configuration is not System.Text.Json.JsonElement configuration)
        {
            return Page();
        }

        try
        {
            OnboardingAgentSelection selection = await management.AgentOnboarding.SaveGenericConfigurationAsync(
                SessionId,
                agentId,
                ExpectedSelectionRevision,
                configuration,
                ExpectedConfigurationRevision,
                ExpectedConfigurationHash,
                ChangeSummary,
                "local-web",
                cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = selection.ProgressStatus == OnboardingAgentProgressStatus.ReadyForValidation
                ? "Configuration reviewed and ready for a later diagnostic. The agent remains disabled."
                : "Configuration saved, but a required protected value still needs attention.";
            return string.Equals(destination, "exit", StringComparison.Ordinal)
                ? RedirectToPage("/Index")
                : RedirectToPage("/Onboarding/Agents", new { sessionId = SessionId });
        }
        catch (Exception exception) when (exception is
            ConfigurationValidationException or
            ConfigurationConcurrencyException or
            OnboardingConcurrencyException or
            InvalidOperationException or
            ArgumentException)
        {
            if (exception is ConfigurationValidationException validation)
            {
                foreach (ConfigurationValidationError error in validation.Errors)
                {
                    string key = error.Path.StartsWith("$.", StringComparison.Ordinal)
                        ? $"GenericValues[{error.Path[2..]}]"
                        : string.Empty;
                    ModelState.AddModelError(key, error.Message);
                }
            }
            else
            {
                ModelState.AddModelError(string.Empty, exception.Message);
            }

            return Page();
        }
    }

    /// <summary>Sets a protected value in a separate non-echoing POST.</summary>
    public async Task<IActionResult> OnPostSetSecretAsync(
        string fieldKey,
        string secretValue,
        CancellationToken cancellationToken)
    {
        if (!TryGetAgentId(out AgentId agentId) || management.AgentOnboarding is null)
        {
            return NotFound();
        }

        try
        {
            _ = await management.AgentOnboarding.SetGenericSecretAsync(
                SessionId,
                agentId,
                ExpectedSelectionRevision,
                fieldKey,
                secretValue,
                "local-web",
                cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = "The protected value was stored for the current Windows user and was not retained by the page.";
        }
        catch (Exception exception) when (exception is OnboardingConcurrencyException or InvalidOperationException or ArgumentException)
        {
            TempData["FlashMessage"] = exception.Message;
        }

        return RedirectToPage(new { sessionId = SessionId, agentId = AgentId });
    }

    /// <summary>Deletes a protected value and moves required configurations back to attention.</summary>
    public async Task<IActionResult> OnPostDeleteSecretAsync(
        string fieldKey,
        CancellationToken cancellationToken)
    {
        if (!TryGetAgentId(out AgentId agentId) || management.AgentOnboarding is null)
        {
            return NotFound();
        }

        try
        {
            _ = await management.AgentOnboarding.DeleteGenericSecretAsync(
                SessionId,
                agentId,
                ExpectedSelectionRevision,
                fieldKey,
                "local-web",
                cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = "The protected value was removed. Required-secret readiness was recalculated.";
        }
        catch (Exception exception) when (exception is OnboardingConcurrencyException or InvalidOperationException or ArgumentException)
        {
            TempData["FlashMessage"] = exception.Message;
        }

        return RedirectToPage(new { sessionId = SessionId, agentId = AgentId });
    }

    /// <summary>Defers this agent without changing its existing configuration or schedules.</summary>
    public async Task<IActionResult> OnPostConfigureLaterAsync(CancellationToken cancellationToken)
    {
        if (!TryGetAgentId(out AgentId agentId) || management.AgentOnboarding is null)
        {
            return NotFound();
        }

        try
        {
            _ = await management.AgentOnboarding.ConfigureLaterAsync(
                SessionId,
                agentId,
                ExpectedSelectionRevision,
                "local-web",
                cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = "Agent configuration was deferred. Existing settings and schedules were preserved.";
        }
        catch (Exception exception) when (exception is OnboardingConcurrencyException or InvalidOperationException or ArgumentException)
        {
            TempData["FlashMessage"] = exception.Message;
        }

        return RedirectToPage("/Onboarding/Agents", new { sessionId = SessionId });
    }

    private bool TryGetAgentId(out AgentId agentId)
    {
        agentId = default;
        return SessionId != Guid.Empty
            && HomeBusinessAssistant.Domain.Agents.AgentId.TryParse(AgentId, out agentId);
    }
}
