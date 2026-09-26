using System.Text.Json;
using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Application.Power;
using HomeBusinessAssistant.Application.Wake;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WakeRemote.Application;

namespace HomeBusinessAssistant.Host.Pages.WakeRemote;

/// <summary>Displays and controls harmless Wake &amp; Remote readiness workflows.</summary>
[AutoValidateAntiforgeryToken]
public sealed class IndexModel(HostManagementComposition management) : PageModel
{
    /// <summary>Gets current managed wake-task state.</summary>
    public WakeTaskState WakeTask { get; private set; } = WakeTaskState.Absent;

    /// <summary>Gets current read-only power diagnostics.</summary>
    public PowerDiagnosticsSnapshot? Diagnostics { get; private set; }

    /// <summary>Gets bounded Wake Remote agent context.</summary>
    public ManagementAgentDetail? Agent { get; private set; }

    /// <summary>Gets the current manual keep-awake state.</summary>
    public Desktop.ManualKeepAwakeState KeepAwake { get; private set; } = new(false, null);

    /// <summary>Gets a safe attention message when a diagnostic cannot be read.</summary>
    public string? Attention { get; private set; }

    /// <summary>Loads current wake, power, configuration, schedule, and run state.</summary>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (management.Queries is not null)
        {
            Agent = await management.Queries.GetAgentAsync(WakeRemoteDefaults.AgentId, cancellationToken).ConfigureAwait(false);
        }

        if (management.KeepAwake is not null)
        {
            KeepAwake = management.KeepAwake.GetState();
        }

        try
        {
            if (management.WakeBridge is not null)
            {
                WakeTask = await management.WakeBridge.GetStateAsync(cancellationToken).ConfigureAwait(false);
            }

            if (management.PowerDiagnostics is not null)
            {
                Diagnostics = await management.PowerDiagnostics.CaptureAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Attention = "One or more Windows wake diagnostics are unavailable in the current user session.";
        }
    }

    /// <summary>Creates and launches a read-only agent diagnostics occurrence.</summary>
    public async Task<IActionResult> OnPostRunDiagnosticsAsync(CancellationToken cancellationToken)
    {
        if (management.Commands is null)
        {
            return NotFound();
        }

        var result = await management.Commands.RunNowAsync(new(
            WakeRemoteDefaults.AgentId,
            "diagnose",
            "{}",
            ConcurrencyPolicy.Forbid,
            RelatedScheduleId: null,
            BypassSchedulePause: false,
            "local-web",
            Guid.NewGuid()), cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = result.RunnerStarted ? "Wake Remote diagnostics started through Runner." : $"Diagnostic occurrence is {result.Code}.";
        return RedirectToPage("/Runs/Index", new { agentId = WakeRemoteDefaults.AgentId.Value });
    }

    /// <summary>Creates a harmless future wake test without sleeping the machine.</summary>
    public async Task<IActionResult> OnPostPrepareWakeTestAsync(int minutes = 5, CancellationToken cancellationToken = default)
    {
        if (management.WakeTests is null || minutes is < 3 or > 1_440)
        {
            TempData["FlashMessage"] = "Wake-test delay must be between 3 minutes and 24 hours.";
            return RedirectToPage();
        }

        WakeTestPreparationResult result = await management.WakeTests.PrepareAsync(
            new(minutes, "local-web", Guid.NewGuid()),
            cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = $"Wake test {result.OccurrenceId} is prepared for {result.ExpectedWakeAtUtc.ToLocalTime():g}. The computer was not put to sleep.";
        return RedirectToPage();
    }

    /// <summary>Reconciles schedule and managed wake state.</summary>
    public async Task<IActionResult> OnPostReconcileAsync(CancellationToken cancellationToken)
    {
        if (management.Commands is null)
        {
            return NotFound();
        }

        ManagementReconciliationResult result = await management.Commands.ReconcileAsync(cancellationToken).ConfigureAwait(false);
        TempData["FlashMessage"] = result.WakeErrorCode is null
            ? $"Wake reconciliation completed; managed task was {(result.WakeChanged ? "updated" : "already current")}."
            : $"Wake reconciliation reported {result.WakeErrorCode}.";
        return RedirectToPage();
    }

    /// <summary>Starts or extends one bounded Host-owned keep-awake request.</summary>
    public async Task<IActionResult> OnPostKeepAwakeAsync(int minutes = 30, CancellationToken cancellationToken = default)
    {
        if (management.KeepAwake is null || minutes is < 1 or > 1_440)
        {
            TempData["FlashMessage"] = "Keep-awake duration must be between 1 minute and 24 hours.";
            return RedirectToPage();
        }

        Desktop.ManualKeepAwakeState state = await management.KeepAwake.StartAsync(TimeSpan.FromMinutes(minutes), cancellationToken).ConfigureAwait(false);
        if (management.Audit is not null)
        {
            _ = await management.Audit.WriteAsync(new(
                AuditActorType.User,
                "local-web",
                "power.keep-awake-started",
                "power-request",
                "host-manual",
                AuditOutcome.Succeeded,
                Guid.NewGuid(),
                RunId: null,
                JsonSerializer.SerializeToElement(new { minutes, state.ExpiresAtUtc })), cancellationToken).ConfigureAwait(false);
        }

        TempData["FlashMessage"] = $"This Host will keep the system awake until {state.ExpiresAtUtc?.ToLocalTime():g}. The display is not forced on.";
        return RedirectToPage();
    }

    /// <summary>Releases only the Host-owned manual keep-awake handle.</summary>
    public async Task<IActionResult> OnPostReleaseKeepAwakeAsync(CancellationToken cancellationToken)
    {
        if (management.KeepAwake is null)
        {
            return NotFound();
        }

        await management.KeepAwake.ReleaseAsync().ConfigureAwait(false);
        if (management.Audit is not null)
        {
            _ = await management.Audit.WriteAsync(new(
                AuditActorType.User,
                "local-web",
                "power.keep-awake-released",
                "power-request",
                "host-manual",
                AuditOutcome.Succeeded,
                Guid.NewGuid(),
                RunId: null,
                JsonSerializer.SerializeToElement(new { released = true })), cancellationToken).ConfigureAwait(false);
        }

        TempData["FlashMessage"] = "The manual keep-awake request was released. Normal Windows power policy resumes.";
        return RedirectToPage();
    }
}
