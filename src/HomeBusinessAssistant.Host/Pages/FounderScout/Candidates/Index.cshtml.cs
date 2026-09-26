using FounderScout.Application;
using FounderScout.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.FounderScout.Candidates;

/// <summary>Server-side founder candidate search, filter, sort, pagination, and local bulk queue actions.</summary>
[AutoValidateAntiforgeryToken]
public sealed class IndexModel(HostManagementComposition management) : PageModel
{
    private const int PageSize = 50;

    /// <summary>Gets or sets safe display-name search text.</summary>
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    /// <summary>Gets or sets one recommendation filter.</summary>
    [BindProperty(SupportsGet = true)] public string? Recommendation { get; set; }
    /// <summary>Gets or sets one lifecycle filter.</summary>
    [BindProperty(SupportsGet = true)] public CandidateStatus? Status { get; set; }
    /// <summary>Gets or sets a minimum quality/fit score.</summary>
    [BindProperty(SupportsGet = true)] public decimal? MinimumScore { get; set; }
    /// <summary>Gets or sets a maximum quality/fit score.</summary>
    [BindProperty(SupportsGet = true)] public decimal? MaximumScore { get; set; }
    /// <summary>Gets or sets a minimum evaluation confidence.</summary>
    [BindProperty(SupportsGet = true)] public decimal? MinimumConfidence { get; set; }
    /// <summary>Gets or sets technical posture.</summary>
    [BindProperty(SupportsGet = true)] public TechnicalProfileStatus? TechnicalStatus { get; set; }
    /// <summary>Gets or sets founder commitment posture.</summary>
    [BindProperty(SupportsGet = true)] public FounderCommitmentStatus? CommitmentStatus { get; set; }
    /// <summary>Gets or sets idea commitment posture.</summary>
    [BindProperty(SupportsGet = true)] public IdeaCommitmentStatus? IdeaStatus { get; set; }
    /// <summary>Gets or sets traction-evidence filtering.</summary>
    [BindProperty(SupportsGet = true)] public bool? HasTractionEvidence { get; set; }
    /// <summary>Gets or sets a normalized risk key.</summary>
    [BindProperty(SupportsGet = true)] public string? RiskKey { get; set; }
    /// <summary>Gets or sets the first snapshot-change boundary.</summary>
    [BindProperty(SupportsGet = true)] public DateTimeOffset? ChangedFromUtc { get; set; }
    /// <summary>Gets or sets the last snapshot-change boundary.</summary>
    [BindProperty(SupportsGet = true)] public DateTimeOffset? ChangedToUtc { get; set; }
    /// <summary>Gets or sets recent-activity boundary.</summary>
    [BindProperty(SupportsGet = true)] public DateTimeOffset? ActiveSinceUtc { get; set; }
    /// <summary>Gets or sets an invitation lane filter.</summary>
    [BindProperty(SupportsGet = true)] public InvitationQueueKind? QueueKind { get; set; }
    /// <summary>Gets or sets a source browser account.</summary>
    [BindProperty(SupportsGet = true)] public string? AccountId { get; set; }
    /// <summary>Gets or sets a source discovery segment.</summary>
    [BindProperty(SupportsGet = true)] public string? SegmentId { get; set; }
    /// <summary>Gets or sets attention-only filtering.</summary>
    [BindProperty(SupportsGet = true)] public bool NeedsManualReview { get; set; }
    /// <summary>Gets or sets the stable server-side sort.</summary>
    [BindProperty(SupportsGet = true)] public FounderScoutCandidateSort Sort { get; set; } = FounderScoutCandidateSort.PriorityDescending;
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

    /// <summary>Gets the total number of pages.</summary>
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)Results.TotalCount / PageSize));

    /// <summary>Loads one raw-profile-free server-side page.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (management.FounderScout is null) return NotFound();
        PageNumber = Math.Max(1, PageNumber);
        Results = await management.FounderScout.Queries.QueryCandidatesAsync(new(
            Search?.Trim(),
            string.IsNullOrWhiteSpace(Recommendation) ? null : [Recommendation],
            Status.HasValue ? [Status.Value] : null,
            MinimumScore,
            MaximumScore,
            MinimumConfidence,
            TechnicalStatus,
            CommitmentStatus,
            IdeaStatus,
            HasTractionEvidence,
            string.IsNullOrWhiteSpace(RiskKey) ? null : RiskKey.Trim(),
            ChangedFromUtc,
            ChangedToUtc,
            ActiveSinceUtc,
            QueueKind,
            string.IsNullOrWhiteSpace(AccountId) ? null : AccountId,
            string.IsNullOrWhiteSpace(SegmentId) ? null : SegmentId,
            NeedsManualReview ? true : null,
            Sort,
            (PageNumber - 1) * PageSize,
            PageSize), cancellationToken).ConfigureAwait(false);
        Accounts = await management.FounderScout.Queries.GetBrowserAccountsAsync(cancellationToken).ConfigureAwait(false);
        Segments = await management.FounderScout.Queries.GetDiscoverySegmentsAsync(cancellationToken).ConfigureAwait(false);
        if (PageNumber > TotalPages) return RedirectToPage(new { pageNumber = TotalPages, Search, Recommendation, Status, MinimumScore, MinimumConfidence, NeedsManualReview, Sort });
        return Page();
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
