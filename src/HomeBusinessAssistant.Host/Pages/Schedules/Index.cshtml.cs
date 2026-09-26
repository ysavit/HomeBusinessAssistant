using HomeBusinessAssistant.Application.Management;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.Schedules;

/// <summary>Lists and controls durable schedules.</summary>
[AutoValidateAntiforgeryToken]
public sealed class IndexModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets schedule rows.</summary>
    public IReadOnlyList<ManagementScheduleItem> Schedules { get; private set; } = [];

    /// <summary>Loads all schedules.</summary>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (management.Queries is not null)
        {
            Schedules = await management.Queries.GetSchedulesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Enables or disables one schedule.</summary>
    public async Task<IActionResult> OnPostSetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken) =>
        await MutateAsync(id, enabled ? "enabled" : "disabled", () => management.Commands!.SetScheduleEnabledAsync(id, enabled, "local-web", Guid.NewGuid(), cancellationToken));

    /// <summary>Pauses one schedule indefinitely.</summary>
    public async Task<IActionResult> OnPostPauseAsync(Guid id, CancellationToken cancellationToken) =>
        await MutateAsync(id, "paused indefinitely", () => management.Commands!.PauseScheduleAsync(id, null, "local-web", Guid.NewGuid(), cancellationToken));

    /// <summary>Resumes one schedule.</summary>
    public async Task<IActionResult> OnPostResumeAsync(Guid id, CancellationToken cancellationToken) =>
        await MutateAsync(id, "resumed", () => management.Commands!.ResumeScheduleAsync(id, "local-web", Guid.NewGuid(), cancellationToken));

    /// <summary>Reconciles schedules and the managed wake task.</summary>
    public async Task<IActionResult> OnPostReconcileAsync(CancellationToken cancellationToken)
    {
        if (management.Commands is null)
        {
            return NotFound();
        }

        ManagementReconciliationResult result = await management.Commands.ReconcileAsync(cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = $"Reconciliation completed: {result.OccurrencesCreated} occurrence(s) created, {result.ScheduleErrors} bounded error(s), wake {(result.WakeChanged ? "updated" : "unchanged")}.";
        return RedirectToPage();
    }

    private async Task<IActionResult> MutateAsync(
        Guid id,
        string outcome,
        Func<ValueTask<HomeBusinessAssistant.Application.Persistence.AgentScheduleRecord>> action)
    {
        if (management.Commands is null)
        {
            return NotFound();
        }

        _ = await action().ConfigureAwait(false);
        TempData["FlashMessage"] = $"Schedule {outcome}; durable occurrence and wake state were reconciled.";
        return RedirectToPage();
    }
}
