using FounderScout.Application;
using FounderScout.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.FounderScout.Candidates;

/// <summary>Evidence-rich candidate review with local-only queue, draft, retention, and outcome actions.</summary>
[AutoValidateAntiforgeryToken]
public sealed class DetailsModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets the selected bounded candidate detail.</summary>
    public FounderScoutCandidateDetail? Detail { get; private set; }

    /// <summary>Gets supported human-confirmed outcomes.</summary>
    public static IReadOnlyList<CandidateStatus> Outcomes { get; } =
    [
        CandidateStatus.ManuallySent,
        CandidateStatus.Accepted,
        CandidateStatus.Declined,
        CandidateStatus.NoResponse,
        CandidateStatus.CallScheduled,
        CandidateStatus.CallCompleted,
        CandidateStatus.SecondCall,
        CandidateStatus.PassedAfterCall,
        CandidateStatus.TrialProject,
        CandidateStatus.Selected,
    ];

    /// <summary>Loads one candidate or returns 404.</summary>
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (management.FounderScout is null || id == Guid.Empty) return NotFound();
        Detail = await management.FounderScout.Queries.GetCandidateAsync(id, cancellationToken).ConfigureAwait(false);
        return Detail is null ? NotFound() : Page();
    }

    /// <summary>Creates an immutable human-edited draft revision after validation.</summary>
    public async Task<IActionResult> OnPostSaveDraftAsync(Guid id, Guid sourceDraftId, string shortDraft, string detailedDraft, CancellationToken cancellationToken)
    {
        if (management.FounderScout is null || id == Guid.Empty || sourceDraftId == Guid.Empty) return NotFound();
        try
        {
            InvitationDraftRevision revision = await management.FounderScout.Commands.SaveDraftRevisionAsync(new(
                sourceDraftId,
                shortDraft ?? string.Empty,
                detailedDraft ?? string.Empty,
                "local-web",
                MaximumCharacters: 2_000,
                SimilarityThreshold: .88m), cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = revision.ValidationPassed
                ? "Saved a validated immutable draft revision."
                : "Saved the revision, but validation still requires review before manual use.";
        }
        catch (ArgumentException)
        {
            TempData["FlashMessage"] = "The edited draft did not meet the configured length or content rules.";
        }

        return RedirectToPage(new { id });
    }

    /// <summary>Marks a generated draft reviewed; this does not send it.</summary>
    public async Task<IActionResult> OnPostReviewDraftAsync(Guid id, Guid draftId, CancellationToken cancellationToken)
    {
        if (management.FounderScout is null) return NotFound();
        try
        {
            _ = await management.FounderScout.Commands.MarkDraftReviewedAsync(draftId, "local-web", cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = "Draft marked reviewed for manual use. Nothing was sent.";
        }
        catch (InvalidOperationException)
        {
            TempData["FlashMessage"] = "That draft cannot move to reviewed from its current validation state.";
        }
        return RedirectToPage(new { id });
    }

    /// <summary>Adds the candidate to one explicit local planning lane.</summary>
    public async Task<IActionResult> OnPostQueueAsync(Guid id, InvitationQueueKind queueKind, bool manualOverride, CancellationToken cancellationToken)
    {
        if (management.FounderScout is null) return NotFound();
        FounderScoutInvitationQueueView queue = await management.FounderScout.Queries.GetInvitationQueueAsync((management.TimeProvider ?? TimeProvider.System).GetUtcNow(), 15, 15, .55m, cancellationToken).ConfigureAwait(false);
        if (queue.Window.Id == Guid.Empty)
        {
            TempData["FlashMessage"] = "Create an active invitation window first.";
            return RedirectToPage("/FounderScout/Queue/Index");
        }
        try
        {
            _ = await management.FounderScout.Commands.AddToQueueAsync(new(queue.Window.Id, id, queueKind, manualOverride, "local-web"), .55m, 120, cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = $"Added to the {queueKind.ToString().ToLowerInvariant()} planning queue. Nothing was sent.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["FlashMessage"] = $"The candidate was not queued: {exception.Message}.";
        }
        return RedirectToPage(new { id });
    }

    /// <summary>Records a legal human-confirmed lifecycle outcome.</summary>
    public async Task<IActionResult> OnPostOutcomeAsync(Guid id, CandidateStatus outcome, string? notes, CancellationToken cancellationToken)
    {
        if (management.FounderScout is null || !Outcomes.Contains(outcome)) return BadRequest();
        FounderScoutCandidateDetail? detail = await management.FounderScout.Queries.GetCandidateAsync(id, cancellationToken).ConfigureAwait(false);
        if (detail is null) return NotFound();
        try
        {
            _ = await management.FounderScout.Commands.RecordOutcomeAsync(new(
                id,
                outcome,
                $"ui.outcome.{outcome.ToString().ToLowerInvariant()}",
                notes,
                detail.Drafts.FirstOrDefault(draft => !draft.Superseded)?.Id,
                detail.InvitationWindow?.Id,
                detail.InvitationWindow?.Version,
                (management.TimeProvider ?? TimeProvider.System).GetUtcNow(),
                "local-web",
                Guid.NewGuid().ToString("D")), cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = $"Recorded {outcome} as a human-confirmed outcome.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["FlashMessage"] = $"Outcome was not recorded: {exception.Message}.";
        }
        return RedirectToPage(new { id });
    }

    /// <summary>Deletes only raw artifacts for this candidate after confirmation.</summary>
    public async Task<IActionResult> OnPostDeleteRawAsync(Guid id, CancellationToken cancellationToken)
    {
        if (management.FounderScout is null) return NotFound();
        FounderScoutRetentionResult result = await management.FounderScout.Commands.ApplyRawRetentionAsync(
            (management.TimeProvider ?? TimeProvider.System).GetUtcNow(), id, includeCurrentSnapshot: true, "local-web", cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = $"Raw retention deleted {result.Deleted} artifact(s), skipped {result.SkippedActiveOrDiagnostic}, and preserved all derived evaluation/history records.";
        return RedirectToPage(new { id });
    }
}
