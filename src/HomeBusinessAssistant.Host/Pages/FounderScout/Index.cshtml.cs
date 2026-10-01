using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FounderScout.Application;
using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Domain.Agents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.FounderScout;

/// <summary>Founder Scout decision-support overview and safe Runner launch actions.</summary>
[AutoValidateAntiforgeryToken]
public sealed class IndexModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets or sets the delay applied after each explored profile.</summary>
    [BindProperty]
    [Range(1, 60, ErrorMessage = "Choose a discovery delay from 1 to 60 seconds.")]
    public int DiscoveryDelaySeconds { get; set; } = 5;

    /// <summary>Gets persisted aggregate state.</summary>
    public FounderScoutDashboard? Dashboard { get; private set; }

    /// <summary>Gets the highest-priority persisted candidate rows.</summary>
    public IReadOnlyList<FounderScoutCandidateListItem> TopCandidates { get; private set; } = [];

    /// <summary>Gets recent discovery, analysis, and report Runner records.</summary>
    public IReadOnlyList<HomeBusinessAssistant.Application.Management.ManagementRunItem> RecentRuns { get; private set; } = [];

    /// <summary>Gets the newest persisted run, when one exists.</summary>
    public ManagementRunItem? LatestRun => RecentRuns.Count == 0 ? null : RecentRuns[0];

    /// <summary>Gets the newest persisted protocol progress for the latest run.</summary>
    internal FounderScoutRunProgress? RunProgress { get; private set; }

    /// <summary>Gets whether the newest run is still being dispatched or executed.</summary>
    public bool IsRunActive => LatestRun?.Run.Status is AgentRunStatus.Pending
        or AgentRunStatus.Claimed
        or AgentRunStatus.Starting
        or AgentRunStatus.Running;

    /// <summary>Gets whether the newest run is an AI evaluation run rather than discovery.</summary>
    public bool IsAnalysisRun => LatestRun?.CommandName is "analyze" or "analyze-candidate";

    /// <summary>Gets whether the Runner can accept a cancellation request for the newest run.</summary>
    public bool CanStopRun => LatestRun?.Run.Status is AgentRunStatus.Starting or AgentRunStatus.Running;

    /// <summary>Gets a safe unavailable message when Founder Scout is not composed.</summary>
    public string? Attention { get; private set; }

    /// <summary>Loads dashboard and top-candidate projections.</summary>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Starts one interactive authentication, discovery, and screening run.</summary>
    public async Task<IActionResult> OnPostStartAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken).ConfigureAwait(false);
            return Page();
        }

        return await StartAgentAsync(
            "start",
            JsonSerializer.Serialize(new
            {
                accountId = FounderScoutSimpleMode.BrowserAccountId,
                segmentId = FounderScoutSimpleMode.DiscoverySegmentId,
                discoveryDelaySeconds = DiscoveryDelaySeconds,
            }),
            "Founder Scout",
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Requests safe cancellation of the current Founder Scout child process.</summary>
    public async Task<IActionResult> OnPostStopAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (management.Commands is null || management.Queries is null || runId == Guid.Empty) return NotFound();

        ManagementRunDetail? detail = await management.Queries
            .GetRunAsync(AgentRunId.FromGuid(runId), cancellationToken)
            .ConfigureAwait(false);
        if (detail?.Run.Run.AgentId != FounderScoutDefaults.AgentId) return NotFound();

        try
        {
            bool changed = await management.Commands.CancelRunAsync(
                detail.Run.Run.Id,
                "local-web",
                Guid.NewGuid(),
                cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = changed
                ? "Stop requested. Founder Scout will exit safely; candidates already saved remain available."
                : "Founder Scout had already stopped before the request was applied.";
        }
        catch (InvalidOperationException)
        {
            TempData["FlashMessage"] = "Founder Scout is no longer in a state that can be stopped.";
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
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
            if (LatestRun is not null)
            {
                ManagementRunDetail? detail = await management.Queries
                    .GetRunAsync(LatestRun.Run.Id, cancellationToken)
                    .ConfigureAwait(false);
                RunProgress = FounderScoutUi.GetRunProgress(detail);
            }
        }
    }

    private async Task<IActionResult> StartAgentAsync(string command, string argumentsJson, string label, CancellationToken cancellationToken)
    {
        if (management.Commands is null) return NotFound();
        try
        {
            OccurrenceDispatchResult result = await management.Commands.RunNowAsync(new(
                FounderScoutDefaults.AgentId,
                command,
                argumentsJson,
                ConcurrencyPolicy.Forbid,
                RelatedScheduleId: null,
                BypassSchedulePause: false,
                "local-web",
                Guid.NewGuid()), cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = result.RunnerStarted
                ? $"{label} started. This page refreshes while it browses and saves profiles; discovery continues automatically after any required Startup School sign-in."
                : $"{label} could not start: {result.Code}.";
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("already has active work", StringComparison.Ordinal))
        {
            TempData["FlashMessage"] = "Founder Scout is already running. Wait for the current discovery or AI analysis run to finish before starting another.";
        }
        return RedirectToPage();
    }
}
