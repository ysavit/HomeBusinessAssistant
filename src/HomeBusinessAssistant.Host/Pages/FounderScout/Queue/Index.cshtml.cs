using FounderScout.Application;
using FounderScout.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.FounderScout.Queue;

/// <summary>Configurable explicit primary/reserve manual invitation planning queue.</summary>
[AutoValidateAntiforgeryToken]
public sealed class IndexModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets the current invitation window and lanes.</summary>
    public FounderScoutInvitationQueueView? Queue { get; private set; }

    /// <summary>Loads the active invitation window or an unsaved suggested window.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (management.FounderScout is null) return NotFound();
        Queue = await management.FounderScout.Queries.GetInvitationQueueAsync((management.TimeProvider ?? TimeProvider.System).GetUtcNow(), 15, 15, .55m, cancellationToken).ConfigureAwait(false);
        return Page();
    }

    /// <summary>Creates a user-managed planning window without assuming an external reset schedule.</summary>
    public async Task<IActionResult> OnPostCreateWindowAsync(DateTimeOffset startAtLocal, DateTimeOffset endAtLocal, int primarySize, int reserveSize, string? notes, CancellationToken cancellationToken)
    {
        if (management.FounderScout is null) return NotFound();
        try
        {
            _ = await management.FounderScout.Commands.CreateInvitationWindowAsync(startAtLocal.ToUniversalTime(), endAtLocal.ToUniversalTime(), primarySize, reserveSize, notes, cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = "Created a local manual invitation planning window.";
        }
        catch (ArgumentException)
        {
            TempData["FlashMessage"] = "The invitation window dates or capacities are invalid.";
        }
        return RedirectToPage();
    }

    /// <summary>Adds one ranked suggestion to a local lane.</summary>
    public async Task<IActionResult> OnPostAddAsync(Guid candidateId, InvitationQueueKind queueKind, bool manualOverride, CancellationToken cancellationToken)
    {
        if (management.FounderScout is null) return NotFound();
        FounderScoutInvitationQueueView queue = await management.FounderScout.Queries.GetInvitationQueueAsync((management.TimeProvider ?? TimeProvider.System).GetUtcNow(), 15, 15, .55m, cancellationToken).ConfigureAwait(false);
        if (queue.Window.Id == Guid.Empty) return RedirectToPage();
        try
        {
            _ = await management.FounderScout.Commands.AddToQueueAsync(new(queue.Window.Id, candidateId, queueKind, manualOverride, "local-web"), .55m, 120, cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = $"Added the candidate to the {queueKind.ToString().ToLowerInvariant()} queue.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["FlashMessage"] = $"Candidate was not queued: {exception.Message}.";
        }
        return RedirectToPage();
    }

    /// <summary>Removes an active entry while preserving its queue history.</summary>
    public async Task<IActionResult> OnPostRemoveAsync(Guid entryId, CancellationToken cancellationToken)
    {
        if (management.FounderScout is null) return NotFound();
        await management.FounderScout.Commands.RemoveFromQueueAsync(entryId, "local-web", "ui.queue.removed", cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = "Removed the candidate from the active queue; history was preserved.";
        return RedirectToPage();
    }

    /// <summary>Marks a generated queue draft reviewed without sending it.</summary>
    public async Task<IActionResult> OnPostReviewDraftAsync(Guid draftId, CancellationToken cancellationToken)
    {
        if (management.FounderScout is null) return NotFound();
        try
        {
            _ = await management.FounderScout.Commands.MarkDraftReviewedAsync(draftId, "local-web", cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = "Draft marked reviewed. Nothing was sent.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["FlashMessage"] = $"Draft was not marked reviewed: {exception.Message}.";
        }
        return RedirectToPage();
    }

    /// <summary>Records that a human sent an invitation outside Founder Scout.</summary>
    public async Task<IActionResult> OnPostManuallySentAsync(Guid candidateId, string? notes, CancellationToken cancellationToken)
    {
        if (management.FounderScout is null) return NotFound();
        FounderScoutCandidateDetail? detail = await management.FounderScout.Queries.GetCandidateAsync(candidateId, cancellationToken).ConfigureAwait(false);
        if (detail is null) return NotFound();
        try
        {
            _ = await management.FounderScout.Commands.RecordOutcomeAsync(new(
                candidateId,
                CandidateStatus.ManuallySent,
                "ui.outcome.manually-sent",
                notes,
                detail.Drafts.FirstOrDefault(draft => !draft.Superseded)?.Id,
                detail.InvitationWindow?.Id,
                detail.InvitationWindow?.Version,
                (management.TimeProvider ?? TimeProvider.System).GetUtcNow(),
                "local-web",
                Guid.NewGuid().ToString("D")), cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = "Recorded the human-confirmed manual send time. Founder Scout did not transmit anything.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["FlashMessage"] = $"Manual send was not recorded: {exception.Message}. Review the draft and lifecycle state first.";
        }
        return RedirectToPage();
    }

    /// <summary>Moves an entry one position within its current lane using a complete order replacement.</summary>
    public async Task<IActionResult> OnPostMoveAsync(Guid entryId, InvitationQueueKind queueKind, int direction, CancellationToken cancellationToken)
    {
        if (management.FounderScout is null || direction is not (-1 or 1)) return BadRequest();
        FounderScoutInvitationQueueView queue = await management.FounderScout.Queries.GetInvitationQueueAsync((management.TimeProvider ?? TimeProvider.System).GetUtcNow(), 15, 15, .55m, cancellationToken).ConfigureAwait(false);
        List<Guid> order = (queueKind == InvitationQueueKind.Primary ? queue.Primary : queue.Reserve).Select(item => item.Entry.Id).ToList();
        int current = order.IndexOf(entryId);
        int target = current + direction;
        if (current >= 0 && target >= 0 && target < order.Count)
        {
            (order[current], order[target]) = (order[target], order[current]);
            await management.FounderScout.Commands.ReorderQueueAsync(new(queue.Window.Id, queueKind, order, "local-web"), cancellationToken).ConfigureAwait(false);
            TempData["FlashMessage"] = "Updated the explicit queue order.";
        }
        return RedirectToPage();
    }
}
