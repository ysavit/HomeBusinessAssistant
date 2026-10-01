using FounderScout.Application;
using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Domain.Agents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.Agents;

/// <summary>Lists installed agents with persisted operational context.</summary>
[AutoValidateAntiforgeryToken]
public sealed class IndexModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets installed agent rows.</summary>
    public IReadOnlyList<ManagementAgentItem> Agents { get; private set; } = [];

    /// <summary>Gets whether production management services are available.</summary>
    public bool IsAvailable => management.Queries is not null && management.Commands is not null;

    /// <summary>Gets agent IDs which currently require onboarding review.</summary>
    public IReadOnlySet<AgentId> PendingAgentIds { get; private set; } = new HashSet<AgentId>();

    /// <summary>Loads all installed agents.</summary>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (management.Queries is not null)
        {
            Agents = await management.Queries.GetAgentsAsync(cancellationToken).ConfigureAwait(false);
        }
        if (management.AgentOnboarding is not null)
        {
            AgentOnboardingDashboardStatus status = await management.AgentOnboarding.GetDashboardStatusAsync(cancellationToken).ConfigureAwait(false);
            PendingAgentIds = status.PendingAgentIds.ToHashSet();
        }
    }

    /// <summary>Changes enablement without terminating active work.</summary>
    public async Task<IActionResult> OnPostSetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken)
    {
        if (management.Commands is null || !AgentId.TryParse(id, out AgentId agentId))
        {
            return NotFound();
        }

        if (agentId == FounderScoutDefaults.AgentId)
        {
            TempData["FlashMessage"] = "Founder Scout is enabled automatically in simple mode.";
            return RedirectToPage();
        }

        bool changed = await management.Commands.SetAgentEnabledAsync(agentId, enabled, cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = changed
            ? $"{id} was {(enabled ? "enabled" : "disabled")}. Active runs were not cancelled."
            : $"{id} was already {(enabled ? "enabled" : "disabled")}.";
        return RedirectToPage();
    }

    /// <summary>Starts or rejoins safe setup for one agent without changing runtime state.</summary>
    public async Task<IActionResult> OnPostSetupAsync(string id, CancellationToken cancellationToken)
    {
        if (management.AgentOnboarding is null || !AgentId.TryParse(id, out AgentId agentId))
        {
            return NotFound();
        }

        if (agentId == FounderScoutDefaults.AgentId)
        {
            return RedirectToPage("/Agents/Configuration", new { id });
        }

        try
        {
            OnboardingSession session = await management.AgentOnboarding.StartReconfigureAgentAsync(
                agentId,
                "local-web",
                cancellationToken).ConfigureAwait(false);
            return RedirectToPage("/Onboarding/Agents", new { sessionId = session.Id });
        }
        catch (InvalidOperationException exception)
        {
            TempData["FlashMessage"] = exception.Message;
            return RedirectToPage();
        }
    }
}
