using System.Text.Json;
using FounderScout.Application;
using FounderScout.Domain;
using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Domain.Agents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages.FounderScout.Operations;

/// <summary>Founder Scout account, segment, report, and raw-retention operations.</summary>
[AutoValidateAntiforgeryToken]
public sealed class IndexModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets browser accounts without profile/session paths.</summary>
    public IReadOnlyList<BrowserAccount> Accounts { get; private set; } = [];
    /// <summary>Gets configured discovery segments.</summary>
    public IReadOnlyList<DiscoverySegment> Segments { get; private set; } = [];
    /// <summary>Gets recent durable report metadata.</summary>
    public IReadOnlyList<ReportExport> Reports { get; private set; } = [];

    /// <summary>Loads account, segment, and report state.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (management.FounderScout is null) return NotFound();
        Accounts = await management.FounderScout.Queries.GetBrowserAccountsAsync(cancellationToken).ConfigureAwait(false);
        Segments = await management.FounderScout.Queries.GetDiscoverySegmentsAsync(cancellationToken).ConfigureAwait(false);
        Reports = await management.FounderScout.Queries.GetReportsAsync(100, cancellationToken).ConfigureAwait(false);
        return Page();
    }

    /// <summary>Serves only a metadata-authorized report under the Founder Scout data root.</summary>
    public async Task<IActionResult> OnGetReportAsync(Guid id, CancellationToken cancellationToken)
    {
        if (management.FounderScout is null || id == Guid.Empty) return NotFound();
        ReportExport? report = (await management.FounderScout.Queries.GetReportsAsync(500, cancellationToken).ConfigureAwait(false)).SingleOrDefault(item => item.Id == id);
        if (report is null || Path.IsPathFullyQualified(report.RelativePath)) return NotFound();
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(management.FounderScout.DataDirectory));
        string path = Path.GetFullPath(Path.Combine(root, report.RelativePath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(path)) return NotFound();
        string contentType = report.Format switch { "html" => "text/html; charset=utf-8", "markdown" => "text/markdown; charset=utf-8", "csv" => "text/csv; charset=utf-8", "json" => "application/json; charset=utf-8", _ => "application/octet-stream" };
        return PhysicalFile(path, contentType);
    }

    /// <summary>Queues interactive headed authentication in the current logged-in user session.</summary>
    public Task<IActionResult> OnPostAuthenticateAsync(string accountId, CancellationToken cancellationToken) => StartAgentAsync("authenticate", new { accountId }, "Interactive authentication", cancellationToken);

    /// <summary>Queues discovery for one account through Runner.</summary>
    public Task<IActionResult> OnPostDiscoverAsync(string accountId, CancellationToken cancellationToken) => StartAgentAsync("discover", new { accountId }, "Account discovery", cancellationToken);

    /// <summary>Queues bounded account diagnostics through Runner.</summary>
    public Task<IActionResult> OnPostDiagnoseAsync(string accountId, CancellationToken cancellationToken) => StartAgentAsync("diagnose", new { accountId }, "Account diagnostics", cancellationToken);

    /// <summary>Queues the selected report family through Runner.</summary>
    public Task<IActionResult> OnPostReportAsync(string type = "all", int top = 30, CancellationToken cancellationToken = default) => StartAgentAsync("report", new { type, top }, "Report generation", cancellationToken);

    /// <summary>Enables or pauses one browser account.</summary>
    public async Task<IActionResult> OnPostAccountEnabledAsync(string accountId, bool enabled, CancellationToken cancellationToken)
    {
        if (management.FounderScout is null) return NotFound();
        _ = await management.FounderScout.Commands.SetBrowserAccountEnabledAsync(accountId, enabled, cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = enabled ? "Browser account enabled." : "Browser account paused; its browser profile was not modified.";
        return RedirectToPage();
    }

    /// <summary>Clears reviewed account attention metadata.</summary>
    public async Task<IActionResult> OnPostClearAttentionAsync(string accountId, CancellationToken cancellationToken)
    {
        if (management.FounderScout is null) return NotFound();
        _ = await management.FounderScout.Commands.ClearBrowserAccountAttentionAsync(accountId, "local-web", cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = "Cleared the reviewed attention state.";
        return RedirectToPage();
    }

    /// <summary>Pauses or resumes one bounded discovery segment.</summary>
    public async Task<IActionResult> OnPostSegmentPausedAsync(string segmentId, bool paused, CancellationToken cancellationToken)
    {
        if (management.FounderScout is null) return NotFound();
        _ = await management.FounderScout.Commands.SetDiscoverySegmentPausedAsync(segmentId, paused, cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = paused ? "Discovery segment paused." : "Discovery segment resumed.";
        return RedirectToPage();
    }

    /// <summary>Applies configured expiry to non-current raw artifacts only.</summary>
    public async Task<IActionResult> OnPostRetentionAsync(CancellationToken cancellationToken)
    {
        if (management.FounderScout is null) return NotFound();
        FounderScoutRetentionResult result = await management.FounderScout.Commands.ApplyRawRetentionAsync((management.TimeProvider ?? TimeProvider.System).GetUtcNow(), candidateId: null, includeCurrentSnapshot: false, "local-web", cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = $"Retention considered {result.Considered}, deleted {result.Deleted}, skipped {result.SkippedActiveOrDiagnostic}, and failed {result.Failed}. Derived records remain.";
        return RedirectToPage();
    }

    private async Task<IActionResult> StartAgentAsync(string command, object arguments, string label, CancellationToken cancellationToken)
    {
        if (management.Commands is null) return NotFound();
        OccurrenceDispatchResult result = await management.Commands.RunNowAsync(new(
            FounderScoutDefaults.AgentId,
            command,
            JsonSerializer.Serialize(arguments),
            ConcurrencyPolicy.Forbid,
            RelatedScheduleId: null,
            BypassSchedulePause: false,
            "local-web",
            Guid.NewGuid()), cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = result.RunnerStarted ? $"{label} started through Runner." : $"{label} occurrence is {result.Code}.";
        return RedirectToPage("/Runs/Index", new { agentId = FounderScoutDefaults.AgentId.Value });
    }
}
