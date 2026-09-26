using HomeBusinessAssistant.Application.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.Attention;

/// <summary>Shows and acknowledges durable local operational attention.</summary>
[AutoValidateAntiforgeryToken]
public sealed class IndexModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets current and recently resolved conditions.</summary>
    public IReadOnlyList<AttentionItem> Items { get; private set; } = [];

    /// <summary>Gets recent daily operational digests.</summary>
    public IReadOnlyList<DailySummary> DailySummaries { get; private set; } = [];

    /// <summary>Loads bounded operational history.</summary>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (management.Operations is null) return;
        Items = await management.Operations.GetAttentionAsync(includeResolved: true, 100, cancellationToken).ConfigureAwait(false);
        DailySummaries = await management.Operations.GetDailySummariesAsync(30, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Acknowledges awareness without claiming that the condition recovered.</summary>
    public async Task<IActionResult> OnPostAcknowledgeAsync(Guid id, CancellationToken cancellationToken)
    {
        if (management.Operations is null) return NotFound();
        AttentionItem? item = await management.Operations.AcknowledgeAsync(id, "local-web", cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = item is null ? "Attention item was not found." : "Attention acknowledged. It remains open until the detector observes recovery.";
        return RedirectToPage();
    }

    /// <summary>Runs the detector explicitly.</summary>
    public async Task<IActionResult> OnPostRefreshAsync(CancellationToken cancellationToken)
    {
        if (management.Operations is null) return NotFound();
        AttentionScanResult result = await management.Operations.DetectAsync(cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = $"Attention scan observed {result.Observed}, created {result.Created}, updated {result.Updated}, and resolved {result.Resolved}.";
        return RedirectToPage();
    }
}
