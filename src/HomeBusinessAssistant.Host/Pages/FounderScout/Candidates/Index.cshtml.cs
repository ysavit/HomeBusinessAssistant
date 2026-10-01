using System.Text.Json;
using FounderScout.Application;
using FounderScout.Domain;
using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.FounderScout.Candidates;

/// <summary>Server-side founder candidate search, filter, sort, pagination, and local bulk queue actions.</summary>
[AutoValidateAntiforgeryToken]
public sealed class IndexModel(HostManagementComposition management) : PageModel
{
    private const int PageSize = 10;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Gets or sets safe display-name search text.</summary>
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    /// <summary>Gets or sets one-based page number.</summary>
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    /// <summary>Gets or sets explicitly selected candidates for a local queue action.</summary>
    [BindProperty] public List<Guid> SelectedCandidateIds { get; set; } = [];

    /// <summary>Gets the bounded result page.</summary>
    public FounderScoutCandidateResultPage Results { get; private set; } = new([], 0, PageSize, 0);
    /// <summary>Gets available safe account filters.</summary>
    public IReadOnlyList<BrowserAccount> Accounts { get; private set; } = [];
    /// <summary>Gets available segment filters.</summary>
    public IReadOnlyList<DiscoverySegment> Segments { get; private set; } = [];
    /// <summary>Gets the newest Founder Scout run, when available.</summary>
    public ManagementRunItem? LatestRun { get; private set; }
    /// <summary>Gets the newest AI run even when discovery ran afterward.</summary>
    public ManagementRunItem? LatestAnalysisRun { get; private set; }
    /// <summary>Gets the newest search run even when AI ran afterward.</summary>
    public ManagementRunItem? LatestScoutRun { get; private set; }
    /// <summary>Gets bounded viewed/saved counts and the stop reason for that search.</summary>
    public string? LatestScoutOutcome => FounderScoutUi.GetDiscoveryOutcome(LatestScoutRun);
    /// <summary>Gets whether the local AI setup is complete.</summary>
    public bool AnalysisReady { get; private set; }
    /// <summary>Gets the configured maximum size of one bulk AI run.</summary>
    public int AnalysisBatchSize { get; private set; }
    /// <summary>Gets persisted aggregate state for live progress.</summary>
    public FounderScoutDashboard? Dashboard { get; private set; }
    /// <summary>Gets the newest persisted protocol progress for the latest run.</summary>
    internal FounderScoutRunProgress? RunProgress { get; private set; }
    /// <summary>Gets whether discovery is currently being dispatched or executed.</summary>
    public bool IsRunActive => LatestRun?.Run.Status is AgentRunStatus.Pending
        or AgentRunStatus.Claimed
        or AgentRunStatus.Starting
        or AgentRunStatus.Running;

    /// <summary>Gets the total number of pages.</summary>
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)Results.TotalCount / PageSize));

    /// <summary>Gets a bounded window of page numbers around the current page.</summary>
    public IReadOnlyList<int> VisiblePageNumbers
    {
        get
        {
            const int maximumLinks = 5;
            int start = Math.Max(1, PageNumber - 2);
            int end = Math.Min(TotalPages, start + maximumLinks - 1);
            start = Math.Max(1, end - maximumLinks + 1);
            return Enumerable.Range(start, end - start + 1).ToArray();
        }
    }

    /// <summary>Loads one raw-profile-free server-side page.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (management.FounderScout is null) return NotFound();
        PageNumber = Math.Max(1, PageNumber);
        Results = await management.FounderScout.Queries.QueryCandidatesAsync(
            FounderScoutUi.CandidateQuery(
                search: Search?.Trim(),
                sort: FounderScoutCandidateSort.LastCapturedDescending,
                offset: (PageNumber - 1) * PageSize,
                pageSize: PageSize),
            cancellationToken).ConfigureAwait(false);
        Dashboard = await management.FounderScout.Queries.GetDashboardAsync(
            (management.TimeProvider ?? TimeProvider.System).GetUtcNow().AddDays(-7),
            cancellationToken).ConfigureAwait(false);
        Accounts = await management.FounderScout.Queries.GetBrowserAccountsAsync(cancellationToken).ConfigureAwait(false);
        Segments = await management.FounderScout.Queries.GetDiscoverySegmentsAsync(cancellationToken).ConfigureAwait(false);
        FounderScoutConfiguration? analysisConfiguration = await GetAnalysisConfigurationAsync(cancellationToken).ConfigureAwait(false);
        AnalysisBatchSize = analysisConfiguration?.Analysis.BatchSize ?? 0;
        AnalysisReady = analysisConfiguration is not null;
        if (management.Queries is not null)
        {
            ManagementPage<ManagementRunItem> runs = await management.Queries.GetRunsAsync(new(
                FounderScoutDefaults.AgentId.Value,
                Status: null,
                Trigger: null,
                FromUtc: null,
                ThroughUtc: null,
                AttentionOnly: false,
                ActiveOnly: false,
                PageNumber: 1,
                PageSize: 20), cancellationToken).ConfigureAwait(false);
            LatestRun = runs.Items.Count == 0 ? null : runs.Items[0];
            LatestAnalysisRun = runs.Items.FirstOrDefault(item => item.CommandName is "analyze" or "analyze-candidate");
            LatestScoutRun = runs.Items.FirstOrDefault(item => item.CommandName is "start" or "discover");
            if (LatestRun is not null)
            {
                ManagementRunDetail? detail = await management.Queries
                    .GetRunAsync(LatestRun.Run.Id, cancellationToken)
                    .ConfigureAwait(false);
                RunProgress = FounderScoutUi.GetRunProgress(detail);
            }
        }
        if (PageNumber > TotalPages) return RedirectToPage(new { pageNumber = TotalPages, Search });
        return Page();
    }

    /// <summary>Dispatches a refresh and AI evaluation for exactly one selected candidate.</summary>
    public async Task<IActionResult> OnPostAnalyzeCandidateAsync(Guid candidateId, CancellationToken cancellationToken)
    {
        if (management.FounderScout is null || management.Commands is null) return NotFound();
        if (candidateId == Guid.Empty || await management.FounderScout.Queries.GetCandidateAsync(candidateId, cancellationToken).ConfigureAwait(false) is null)
            return NotFound();
        if (await GetAnalysisConfigurationAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            TempData["FlashMessage"] = "Save a model and protected OpenAI API key in AI settings before analysis.";
            return RedirectToPage();
        }

        try
        {
            OccurrenceDispatchResult dispatch = await management.Commands.RunNowAsync(new(
                FounderScoutDefaults.AgentId, "analyze-candidate", JsonSerializer.Serialize(new { candidateId }),
                ConcurrencyPolicy.Forbid, RelatedScheduleId: null, BypassSchedulePause: false,
                "local-web", Guid.NewGuid()), cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = dispatch.RunnerStarted
                ? "Candidate refresh and AI analysis started. Watch the progress on this page."
                : $"Candidate analysis could not start: {dispatch.Code}.";
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("already has active work", StringComparison.Ordinal))
        {
            TempData["FlashMessage"] = "Founder Scout is already running. Wait for that run to finish before analyzing a candidate.";
        }
        return RedirectToPage();
    }

    /// <summary>Queues screened, unevaluated stored candidates and dispatches one bounded AI run.</summary>
    public async Task<IActionResult> OnPostAnalyzeAllAsync(CancellationToken cancellationToken)
    {
        if (management.FounderScout is null || management.Commands is null) return NotFound();
        FounderScoutConfiguration? configuration = await GetAnalysisConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (configuration is null)
        {
            TempData["FlashMessage"] = "Save a model and protected OpenAI API key in AI settings before analysis.";
            return RedirectToPage();
        }

        FounderScoutAnalysisQueueResult queued = await management.FounderScout.Commands
            .QueueUnanalyzedForAnalysisAsync(configuration.Analysis.BatchSize, "local-web", cancellationToken)
            .ConfigureAwait(false);
        if (queued.Queued == 0 && queued.AlreadyPending == 0)
        {
            TempData["FlashMessage"] = "No screened candidates are waiting for AI analysis. Use Analyze on a row to refresh and evaluate it again.";
            return RedirectToPage();
        }
        try
        {
            OccurrenceDispatchResult dispatch = await management.Commands.RunNowAsync(new(
                FounderScoutDefaults.AgentId, "analyze",
                JsonSerializer.Serialize(new { phase = "deep", max = configuration.Analysis.BatchSize }),
                ConcurrencyPolicy.Forbid, RelatedScheduleId: null, BypassSchedulePause: false,
                "local-web", Guid.NewGuid()), cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = dispatch.RunnerStarted
                ? $"AI analysis started. {queued.Queued} candidate(s) newly queued; {queued.AlreadyPending} already pending. Watch progress here."
                : $"AI analysis could not start: {dispatch.Code}. Queued candidates remain pending.";
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("already has active work", StringComparison.Ordinal))
        {
            TempData["FlashMessage"] = "Founder Scout is already running. Queued candidates remain pending for a later analysis run.";
        }
        return RedirectToPage();
    }

    private async ValueTask<FounderScoutConfiguration?> GetAnalysisConfigurationAsync(CancellationToken cancellationToken)
    {
        if (management.Configurations is null || management.Secrets is null) return null;
        AgentConfigurationRecord? current = await management.Configurations
            .GetCurrentAsync(FounderScoutDefaults.AgentId, cancellationToken).ConfigureAwait(false);
        if (current is null) return null;
        FounderScoutConfiguration? configuration = JsonSerializer.Deserialize<FounderScoutConfiguration>(
            current.CurrentRevision.CanonicalConfigurationJson, JsonOptions);
        if (configuration is null || !configuration.Analysis.Enabled
            || !string.Equals(configuration.Ai.Provider, "OpenAI", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(configuration.Ai.Deployment)
            || !await management.Secrets.ExistsAsync(FounderScoutDefaults.ApiKeyReference, cancellationToken).ConfigureAwait(false))
            return null;
        return configuration;
    }

    /// <summary>Adds selected candidates to an explicit local queue lane. No network send occurs.</summary>
    public async Task<IActionResult> OnPostQueueAsync(InvitationQueueKind queueKind, CancellationToken cancellationToken)
    {
        if (management.FounderScout is null) return NotFound();
        if (SelectedCandidateIds.Count is < 1 or > 100 || SelectedCandidateIds.Distinct().Count() != SelectedCandidateIds.Count)
        {
            TempData["FlashMessage"] = "Select between 1 and 100 unique candidates.";
            return RedirectToPage();
        }

        FounderScoutInvitationQueueView queue = await management.FounderScout.Queries.GetInvitationQueueAsync(
            (management.TimeProvider ?? TimeProvider.System).GetUtcNow(), 15, 15, .55m, cancellationToken).ConfigureAwait(false);
        if (queue.Window.Id == Guid.Empty)
        {
            TempData["FlashMessage"] = "Create an active invitation window before adding candidates.";
            return RedirectToPage("/FounderScout/Queue/Index");
        }

        var added = 0;
        var rejected = 0;
        foreach (Guid candidateId in SelectedCandidateIds)
        {
            try
            {
                _ = await management.FounderScout.Commands.AddToQueueAsync(new(queue.Window.Id, candidateId, queueKind, ManuallyIncluded: false, "local-web"), .55m, 120, cancellationToken).ConfigureAwait(false);
                added++;
            }
            catch (InvalidOperationException)
            {
                rejected++;
            }
        }

        TempData["FlashMessage"] = $"Added {added} candidate(s) to the {queueKind.ToString().ToLowerInvariant()} queue; {rejected} did not meet current eligibility or capacity rules.";
        return RedirectToPage("/FounderScout/Queue/Index");
    }
}
