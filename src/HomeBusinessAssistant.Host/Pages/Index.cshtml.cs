using System.Text.Json;
using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Application.Operations;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Audit;
using HomeBusinessAssistant.Host.Dashboard;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace HomeBusinessAssistant.Host.Pages;

/// <summary>
/// Renders the real bounded platform dashboard and its global schedule controls.
/// </summary>
[AutoValidateAntiforgeryToken]
public sealed class IndexModel(
    IHostDashboardService dashboard,
    IGlobalScheduleControlService globalControls,
    HostManagementComposition management) : PageModel
{
    private const string DismissedReminderCookie = "hba.onboarding-reminder-dismissed";

    /// <summary>Gets or sets the reminder session targeted by the session-cookie dismissal.</summary>
    [BindProperty]
    public Guid OnboardingSessionId { get; set; }

    /// <summary>Gets the current safe dashboard snapshot.</summary>
    public HostDashboardSnapshot Snapshot { get; private set; } = new(
        true,
        [],
        new([], [], [], [], null),
        new(false, []),
        new(false, false, null, null, null),
        []);

    /// <summary>Gets richer installed-agent rows when production management is composed.</summary>
    public IReadOnlyList<ManagementAgentItem> Agents { get; private set; } = [];

    /// <summary>Gets durable active attention.</summary>
    public IReadOnlyList<AttentionItem> Attention { get; private set; } = [];

    /// <summary>Gets the non-redirecting onboarding reminder or review invitation.</summary>
    public OnboardingEntryDecision? Onboarding { get; private set; }

    /// <summary>Gets non-blocking newly available/pending agent status.</summary>
    public AgentOnboardingDashboardStatus? AgentOnboarding { get; private set; }

    /// <summary>Gets current local time.</summary>
    public DateTimeOffset LocalNow { get; private set; }

    /// <summary>Gets current UTC time.</summary>
    public DateTimeOffset UtcNow { get; private set; }

    /// <summary>Gets the local time-zone name.</summary>
    public string TimeZoneId => TimeZoneInfo.Local.Id;

    /// <summary>Gets the Host uptime.</summary>
    public TimeSpan Uptime => HostProcessInfo.GetUptime(management.TimeProvider);

    /// <summary>Gets the current manual keep-awake state.</summary>
    public Desktop.ManualKeepAwakeState KeepAwake => management.KeepAwake?.GetState() ?? new(false, null);

    /// <summary>Loads current local platform state.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (management.Onboarding is not null)
        {
            Onboarding = await management.Onboarding.GetEntryDecisionAsync("local-web", cancellationToken).ConfigureAwait(false);
            if (Onboarding.RedirectToOnboarding)
            {
                return RedirectToPage("/Onboarding", new { sessionId = Onboarding.Session.Id });
            }

            if (Onboarding.ShowResumeReminder
                && Request.Cookies.TryGetValue(DismissedReminderCookie, out string? dismissed)
                && string.Equals(dismissed, Onboarding.Session.Id.ToString("N"), StringComparison.Ordinal))
            {
                Onboarding = Onboarding with { ShowResumeReminder = false };
            }
        }

        Snapshot = await dashboard.GetAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = (management.TimeProvider ?? TimeProvider.System).GetUtcNow();
        UtcNow = now.ToUniversalTime();
        LocalNow = now.ToLocalTime();
        if (management.Queries is not null)
        {
            Agents = await management.Queries.GetAgentsAsync(cancellationToken).ConfigureAwait(false);
        }
        if (management.AgentOnboarding is not null)
        {
            AgentOnboarding = await management.AgentOnboarding.GetDashboardStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        if (management.Operations is not null)
        {
            Attention = await management.Operations.GetAttentionAsync(false, 20, cancellationToken).ConfigureAwait(false);
        }

        return Page();
    }

    /// <summary>Antiforgery-protected global pause action.</summary>
    public async Task<IActionResult> OnPostPauseAllAsync(CancellationToken cancellationToken)
    {
        _ = await globalControls.PauseAllAsync("local-web", Guid.NewGuid(), cancellationToken).ConfigureAwait(false);
        return RedirectToPage();
    }

    /// <summary>Antiforgery-protected global resume action.</summary>
    public async Task<IActionResult> OnPostResumeAllAsync(CancellationToken cancellationToken)
    {
        _ = await globalControls.ResumeAllAsync("local-web", Guid.NewGuid(), cancellationToken).ConfigureAwait(false);
        return RedirectToPage();
    }

    /// <summary>Dismisses only the current deferred/review reminder until this browser session ends.</summary>
    public async Task<IActionResult> OnPostDismissOnboardingAsync(CancellationToken cancellationToken)
    {
        if (management.Onboarding is null || OnboardingSessionId == Guid.Empty)
        {
            return NotFound();
        }

        OnboardingEntryDecision current = await management.Onboarding.GetEntryDecisionAsync("local-web", cancellationToken).ConfigureAwait(false);
        if (!current.ShowResumeReminder || current.Session.Id != OnboardingSessionId)
        {
            return RedirectToPage();
        }

        Guid correlationId = Guid.NewGuid();
        if (management.Audit is not null)
        {
            _ = await management.Audit.WriteAsync(new WriteAuditEventRequest(
                AuditActorType.User,
                "local-web",
                "onboarding.reminder-dismissed-for-session",
                "onboarding-session",
                current.Session.Id.ToString("D"),
                AuditOutcome.Succeeded,
                correlationId,
                RunId: null,
                JsonSerializer.SerializeToElement(new { browserSessionOnly = true })), cancellationToken).ConfigureAwait(false);
        }

        Response.Cookies.Append(DismissedReminderCookie, current.Session.Id.ToString("N"), new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Strict,
            Secure = Request.IsHttps,
            Path = "/",
        });
        return RedirectToPage();
    }

    /// <summary>Starts or joins the explicit add/configure agents workflow.</summary>
    public async Task<IActionResult> OnPostStartAgentOnboardingAsync(CancellationToken cancellationToken)
    {
        if (management.AgentOnboarding is null)
        {
            return NotFound();
        }

        try
        {
            OnboardingSession session = await management.AgentOnboarding.StartAddAgentsAsync(
                "local-web",
                cancellationToken).ConfigureAwait(false);
            return RedirectToPage("/Onboarding/Agents", new { sessionId = session.Id });
        }
        catch (InvalidOperationException exception)
        {
            TempData["FlashMessage"] = exception.Message;
            return RedirectToPage();
        }
    }

    /// <summary>Runs an explicit schedule and wake reconciliation.</summary>
    public async Task<IActionResult> OnPostReconcileAsync(CancellationToken cancellationToken)
    {
        if (management.Commands is null)
        {
            return NotFound();
        }

        ManagementReconciliationResult result = await management.Commands.ReconcileAsync(cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = $"Reconciled: {result.OccurrencesCreated} occurrence(s), {result.ScheduleErrors} error(s), wake {(result.WakeChanged ? "updated" : "unchanged")}.";
        return RedirectToPage();
    }
}
