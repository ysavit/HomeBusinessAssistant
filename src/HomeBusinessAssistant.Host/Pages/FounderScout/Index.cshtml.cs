using System.Text.Json;
using FounderScout.Application;
using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Domain.Agents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.FounderScout;

/// <summary>Founder Scout decision-support overview and safe Runner launch actions.</summary>
[AutoValidateAntiforgeryToken]
public sealed class IndexModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets persisted aggregate state.</summary>
    public FounderScoutDashboard? Dashboard { get; private set; }

    /// <summary>Gets the highest-priority persisted candidate rows.</summary>
    public IReadOnlyList<FounderScoutCandidateListItem> TopCandidates { get; private set; } = [];

    /// <summary>Gets recent discovery, analysis, and report Runner records.</summary>
    public IReadOnlyList<HomeBusinessAssistant.Application.Management.ManagementRunItem> RecentRuns { get; private set; } = [];

    /// <summary>Gets a safe unavailable message when Founder Scout is not composed.</summary>
    public string? Attention { get; private set; }

    /// <summary>Loads dashboard and top-candidate projections.</summary>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (management.FounderScout is null)
        {
            Attention = "Founder Scout result services are unavailable in this Host session.";
            return;
        }

        TimeProvider time = management.TimeProvider ?? TimeProvider.System;
        Dashboard = await management.FounderScout.Queries.GetDashboardAsync(time.GetUtcNow().AddDays(-7), cancellationToken).ConfigureAwait(false);
        FounderScoutCandidateResultPage candidates = await management.FounderScout.Queries.QueryCandidatesAsync(
            FounderScoutUi.CandidateQuery(pageSize: 8), cancellationToken).ConfigureAwait(false);
        TopCandidates = candidates.Items;
        if (management.Queries is not null)
        {
            HomeBusinessAssistant.Application.Management.ManagementPage<HomeBusinessAssistant.Application.Management.ManagementRunItem> runs = await management.Queries.GetRunsAsync(new(
                FounderScoutDefaults.AgentId.Value,
                Status: null,
                Trigger: null,
                FromUtc: null,
                ThroughUtc: null,
                AttentionOnly: false,
                ActiveOnly: false,
                PageNumber: 1,
                PageSize: 8), cancellationToken).ConfigureAwait(false);
            RecentRuns = runs.Items;
        }
    }

    /// <summary>Queues a bounded discovery run through the central Runner.</summary>
    public Task<IActionResult> OnPostDiscoverAsync(CancellationToken cancellationToken) => StartAgentAsync("discover", "{}", "Discovery", cancellationToken);

    /// <summary>Queues pending screen and deep analysis work through the central Runner.</summary>
    public Task<IActionResult> OnPostAnalyzeAsync(CancellationToken cancellationToken) => StartAgentAsync("analyze", JsonSerializer.Serialize(new { phase = "all" }), "Analysis", cancellationToken);

    /// <summary>Queues generation of every Stage 13 report format through the central Runner.</summary>
    public Task<IActionResult> OnPostReportsAsync(CancellationToken cancellationToken) => StartAgentAsync("report", JsonSerializer.Serialize(new { type = "all", top = 30 }), "Report generation", cancellationToken);

    private async Task<IActionResult> StartAgentAsync(string command, string argumentsJson, string label, CancellationToken cancellationToken)
    {
        if (management.Commands is null) return NotFound();
        OccurrenceDispatchResult result = await management.Commands.RunNowAsync(new(
            FounderScoutDefaults.AgentId,
            command,
            argumentsJson,
            ConcurrencyPolicy.Forbid,
            RelatedScheduleId: null,
            BypassSchedulePause: false,
            "local-web",
            Guid.NewGuid()), cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = result.RunnerStarted ? $"{label} started through Runner." : $"{label} occurrence is {result.Code}.";
        return RedirectToPage("/Runs/Index", new { agentId = FounderScoutDefaults.AgentId.Value });
    }
}
