using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Domain.Agents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.Onboarding;

/// <summary>Explicit installed-agent selection and durable progress overview.</summary>
[AutoValidateAntiforgeryToken]
public sealed class AgentsModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets or sets the durable onboarding session.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid SessionId { get; set; }

    /// <summary>Gets or sets the displayed session revision.</summary>
    [BindProperty]
    public long ExpectedRevision { get; set; }

    /// <summary>Gets or sets only explicitly checked agent IDs.</summary>
    [BindProperty]
    public List<string> SelectedAgentIds { get; set; } = [];

    /// <summary>Gets the bounded current selection projection.</summary>
    public AgentSelectionOverview? Overview { get; private set; }

    /// <summary>Loads existing durable choices without preselecting newly installed agents.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (management.AgentOnboarding is null || SessionId == Guid.Empty)
        {
            return NotFound();
        }

        try
        {
            Overview = await management.AgentOnboarding.GetSelectionOverviewAsync(
                SessionId,
                "local-web",
                cancellationToken).ConfigureAwait(false);
            ExpectedRevision = Overview.Session.Revision;
            SelectedAgentIds = Overview.Agents
                .Where(item => item.Selection?.SelectionStatus == OnboardingAgentSelectionStatus.Selected)
                .Select(item => item.Definition.Id.Value)
                .ToList();
            return Page();
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    /// <summary>Starts or joins an AddAgents session from Dashboard or Settings.</summary>
    public async Task<IActionResult> OnPostStartAsync(CancellationToken cancellationToken)
    {
        if (management.AgentOnboarding is null)
        {
            return NotFound();
        }

        try
        {
            OnboardingSession session = await management.AgentOnboarding.StartAddAgentsAsync(
                "local-web",
                cancellationToken).ConfigureAwait(false);
            return RedirectToPage(new { sessionId = session.Id });
        }
        catch (InvalidOperationException exception)
        {
            TempData["FlashMessage"] = exception.Message;
            return RedirectToPage("/Index");
        }
    }

    /// <summary>Transactionally saves select/defer choices for every currently available safe agent.</summary>
    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (management.AgentOnboarding is null || SessionId == Guid.Empty)
        {
            return NotFound();
        }

        var selected = new HashSet<AgentId>();
        foreach (string value in SelectedAgentIds.Distinct(StringComparer.Ordinal))
        {
            if (!AgentId.TryParse(value, out AgentId agentId))
            {
                TempData["FlashMessage"] = "One or more submitted agent choices were invalid.";
                return RedirectToPage(new { sessionId = SessionId });
            }

            selected.Add(agentId);
        }

        try
        {
            AgentSelectionOverview result = await management.AgentOnboarding.SaveChoicesAsync(
                SessionId,
                ExpectedRevision,
                selected,
                "local-web",
                cancellationToken).ConfigureAwait(false);
            if (result.Session.Status == OnboardingSessionStatus.Deferred)
            {
                TempData["FlashMessage"] = "All agents were deferred. Nothing was enabled, scheduled, or run.";
                return RedirectToPage("/Index");
            }

            TempData["FlashMessage"] = "Agent choices saved. Selected agents remain disabled until a later explicit activation step.";
            return RedirectToPage(new { sessionId = SessionId });
        }
        catch (Exception exception) when (exception is OnboardingConcurrencyException or InvalidOperationException or ArgumentException)
        {
            TempData["FlashMessage"] = exception.Message;
            return RedirectToPage(new { sessionId = SessionId });
        }
    }
}
