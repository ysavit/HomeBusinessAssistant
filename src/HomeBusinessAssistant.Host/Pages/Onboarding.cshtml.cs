using HomeBusinessAssistant.Application.Onboarding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages;

/// <summary>Durable first-run readiness, warning acknowledgement, defer, and resume UI.</summary>
[AutoValidateAntiforgeryToken]
public sealed class OnboardingModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets or sets the durable session selected by the route.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? SessionId { get; set; }

    /// <summary>Gets or sets the displayed optimistic session revision.</summary>
    [BindProperty]
    public long ExpectedRevision { get; set; }

    /// <summary>Gets or sets explicit acknowledgement of the displayed warning set.</summary>
    [BindProperty]
    public bool AcknowledgeWarnings { get; set; }

    /// <summary>Gets the current catalog-complete onboarding view.</summary>
    public OnboardingOverview? Overview { get; private set; }

    /// <summary>Loads the current or explicitly selected session without running checks on GET.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (management.Onboarding is null)
        {
            return NotFound();
        }

        Overview = await management.Onboarding.GetOverviewAsync(SessionId, "local-web", cancellationToken).ConfigureAwait(false);
        SessionId = Overview.Session.Id;
        ExpectedRevision = Overview.Session.Revision;
        if (management.AgentOnboarding is not null
            && Overview.Session.Status == OnboardingSessionStatus.InProgress
            && Overview.Session.CurrentStep is OnboardingSteps.AgentSelectionPending or OnboardingSteps.AgentConfiguration)
        {
            return RedirectToPage("/Onboarding/Agents", new { sessionId = Overview.Session.Id });
        }

        return Page();
    }

    /// <summary>Runs the normal bounded read-only readiness batch.</summary>
    public Task<IActionResult> OnPostRunChecksAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(
            service => service.RunChecksAsync(RequireSessionId(), ExpectedRevision, "local-web", cancellationToken).AsTask(),
            "Readiness checks completed.");

    /// <summary>Runs only explicitly labeled probes, including temporary protected storage.</summary>
    public Task<IActionResult> OnPostProbeAsync(CancellationToken cancellationToken) =>
        ExecuteAsync(
            service => service.RunExplicitProbesAsync(RequireSessionId(), ExpectedRevision, "local-web", cancellationToken).AsTask(),
            "Protected-storage probe completed and cleanup was attempted.");

    /// <summary>Defers the same session without discarding results.</summary>
    public Task<IActionResult> OnPostDeferAsync(CancellationToken cancellationToken) =>
        ExecuteSessionAsync(
            service => service.DeferAsync(RequireSessionId(), ExpectedRevision, "local-web", cancellationToken).AsTask(),
            "Setup was saved for later.",
            redirectDashboard: true);

    /// <summary>Resumes a deferred session with optimistic concurrency.</summary>
    public Task<IActionResult> OnPostResumeAsync(CancellationToken cancellationToken) =>
        ExecuteSessionAsync(
            service => service.ResumeAsync(RequireSessionId(), ExpectedRevision, "local-web", cancellationToken).AsTask(),
            "Setup resumed.");

    /// <summary>Cancels only an optional readiness review.</summary>
    public Task<IActionResult> OnPostCancelAsync(CancellationToken cancellationToken) =>
        ExecuteSessionAsync(
            service => service.CancelAsync(RequireSessionId(), ExpectedRevision, "local-web", cancellationToken).AsTask(),
            "Optional readiness review cancelled.",
            redirectDashboard: true);

    /// <summary>Advances only to the explicit Stage 19 selection handoff.</summary>
    public Task<IActionResult> OnPostContinueAsync(CancellationToken cancellationToken) =>
        ExecuteSessionAsync(
            service => service.ContinueAsync(
                RequireSessionId(),
                ExpectedRevision,
                AcknowledgeWarnings,
                "local-web",
                cancellationToken).AsTask(),
            "Platform readiness is complete. Agent selection remains pending.");

    /// <summary>Starts an optional readiness review for an established installation.</summary>
    public Task<IActionResult> OnPostStartReviewAsync(CancellationToken cancellationToken) =>
        ExecuteSessionAsync(
            service => service.StartReadinessReviewAsync("local-web", cancellationToken).AsTask(),
            "Readiness review started.");

    private async Task<IActionResult> ExecuteAsync(
        Func<IOnboardingService, Task<OnboardingOverview>> operation,
        string successMessage)
    {
        if (management.Onboarding is null)
        {
            return NotFound();
        }

        try
        {
            OnboardingOverview result = await operation(management.Onboarding).ConfigureAwait(false);
            TempData["FlashMessage"] = successMessage;
            return RedirectToPage(new { sessionId = result.Session.Id });
        }
        catch (Exception exception) when (exception is OnboardingConcurrencyException or InvalidOperationException or ArgumentException)
        {
            TempData["FlashMessage"] = exception.Message;
            return RedirectToPage(new { sessionId = SessionId });
        }
    }

    private async Task<IActionResult> ExecuteSessionAsync(
        Func<IOnboardingService, Task<OnboardingSession>> operation,
        string successMessage,
        bool redirectDashboard = false)
    {
        if (management.Onboarding is null)
        {
            return NotFound();
        }

        try
        {
            OnboardingSession result = await operation(management.Onboarding).ConfigureAwait(false);
            TempData["FlashMessage"] = successMessage;
            return redirectDashboard ? RedirectToPage("/Index") : RedirectToPage(new { sessionId = result.Id });
        }
        catch (Exception exception) when (exception is OnboardingConcurrencyException or InvalidOperationException or ArgumentException)
        {
            TempData["FlashMessage"] = exception.Message;
            return RedirectToPage(new { sessionId = SessionId });
        }
    }

    private Guid RequireSessionId() => SessionId is Guid id && id != Guid.Empty
        ? id
        : throw new ArgumentException("An onboarding session is required.");
}
