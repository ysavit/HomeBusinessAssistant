using System.Text.Json;
using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Application.Power;
using HomeBusinessAssistant.Application.Wake;

namespace HomeBusinessAssistant.Windows.Onboarding;

/// <summary>Queries both reserved Task Scheduler objects without registering, updating, or deleting either task.</summary>
public sealed class TaskSchedulerOnboardingCheck(
    IHostStartupTaskScheduler startupTasks,
    IWakeTaskSchedulerBridge wakeTasks,
    TimeProvider timeProvider) : IOnboardingReadinessCheck
{
    /// <inheritdoc />
    public OnboardingReadinessCheckDefinition Definition { get; } = new(
        "windows.task-scheduler",
        OnboardingCheckScope.Windows,
        "Windows Task Scheduler",
        "Reads ownership and availability for the reserved Host-at-logon and next-wake tasks. This check never creates, changes, or removes a task.",
        Required: true,
        RequiresExplicitAction: false,
        Timeout: TimeSpan.FromSeconds(30),
        RemediationKey: "task-scheduler-help");

    /// <inheritdoc />
    public async ValueTask<OnboardingReadinessCheckResult> EvaluateAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset observedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        try
        {
            HostStartupTaskState startup = await startupTasks.GetStateAsync(cancellationToken).ConfigureAwait(false);
            WakeTaskState wake = await wakeTasks.GetStateAsync(cancellationToken).ConfigureAwait(false);
            bool ownershipConflict = startup.Exists && !startup.IsManaged || wake.Exists && !wake.IsManaged;
            bool queryWarning = startup.ErrorCode is not null || wake.Error is not null;
            string queryReason = ClassifyQueryWarning(startup.ErrorCode, wake.Error);
            OnboardingCheckStatus status = ownershipConflict
                ? OnboardingCheckStatus.Blocked
                : !startup.Exists || queryWarning
                    ? OnboardingCheckStatus.Warning
                    : OnboardingCheckStatus.Passed;
            string reasonCode = ownershipConflict ? "task-scheduler.ownership-conflict"
                : queryWarning ? queryReason
                : !startup.Exists ? "task-scheduler.host-at-logon-absent"
                : "task-scheduler.ready";
            string message = ownershipConflict
                ? "A reserved task name is occupied by an unmanaged or invalid task. Repair must resolve ownership before automatic startup or wake is configured."
                : queryWarning
                    ? QueryWarningMessage(queryReason)
                    : !startup.Exists
                        ? "The managed Host-at-logon task is not installed. The Host must be started manually until installation or repair registers it."
                        : "The managed Host-at-logon task is healthy. The next-wake task is read-only here and may be absent when no wake occurrence is due.";
            return new(
                Definition,
                status,
                reasonCode,
                message,
                observedAtUtc,
                observedAtUtc.AddHours(1),
                "1.0",
                JsonSerializer.SerializeToElement(new
                {
                    hostAtLogonExists = startup.Exists,
                    hostAtLogonManaged = startup.IsManaged,
                    hostAtLogonErrorCode = startup.ErrorCode,
                    nextWakeExists = wake.Exists,
                    nextWakeManaged = wake.IsManaged,
                    nextWakeErrorCode = wake.Error?.Code,
                    mutationAttempted = false,
                }),
                Definition.RemediationKey);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(
                Definition,
                OnboardingCheckStatus.Warning,
                "task-scheduler.query-unavailable",
                "Task Scheduler could not be queried with the current user's access. Manual Host startup remains available; automatic startup and wake require repair or Windows policy changes.",
                observedAtUtc,
                observedAtUtc.AddMinutes(15),
                "1.0",
                JsonSerializer.SerializeToElement(new { querySucceeded = false, mutationAttempted = false }),
                Definition.RemediationKey);
        }
    }

    private static string ClassifyQueryWarning(string? startupErrorCode, WakeTaskError? wakeError)
    {
        string combined = $"{startupErrorCode}|{wakeError?.Code}";
        if (wakeError?.Category == WakeTaskErrorCategory.PermissionDenied
            || combined.Contains("permission", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("access-denied", StringComparison.OrdinalIgnoreCase))
        {
            return "task-scheduler.permission-denied";
        }

        if (combined.Contains("folder", StringComparison.OrdinalIgnoreCase))
        {
            return "task-scheduler.folder-unavailable";
        }

        if (combined.Contains("service", StringComparison.OrdinalIgnoreCase))
        {
            return "task-scheduler.service-unavailable";
        }

        if (combined.Contains("policy", StringComparison.OrdinalIgnoreCase))
        {
            return "task-scheduler.policy-denied";
        }

        return "task-scheduler.query-unavailable";
    }

    private static string QueryWarningMessage(string reasonCode) => reasonCode switch
    {
        "task-scheduler.permission-denied" => "Windows denied access to Task Scheduler. Setup may continue, but automatic startup or wake requires the current user to have task access.",
        "task-scheduler.folder-unavailable" => "The Home Business Assistant Task Scheduler folder is unavailable. Manual Host startup remains available until installation or repair restores it.",
        "task-scheduler.service-unavailable" => "The Windows Task Scheduler service is unavailable. Manual Host startup remains available until the service is restored.",
        "task-scheduler.policy-denied" => "Windows policy prevents the required Task Scheduler access. Manual Host startup remains available until policy permits managed tasks.",
        _ => "Task Scheduler returned a bounded query warning. Setup may continue, but automatic startup or wake may be unavailable.",
    };
}

/// <summary>Projects bounded read-only power diagnostics into conservative wake caveats.</summary>
public sealed class PowerAndWakeOnboardingCheck(
    IPowerDiagnosticsService diagnostics,
    TimeProvider timeProvider) : IOnboardingReadinessCheck
{
    /// <inheritdoc />
    public OnboardingReadinessCheckDefinition Definition { get; } = new(
        "windows.power-and-wake",
        OnboardingCheckScope.Windows,
        "Power and wake capability",
        "Runs only the supported read-only power diagnostics. Firmware, sleep mode, battery policy, permissions, and wake hardware can still limit real wake behavior.",
        Required: false,
        RequiresExplicitAction: false,
        Timeout: TimeSpan.FromSeconds(75),
        RemediationKey: "power-wake-help");

    /// <inheritdoc />
    public async ValueTask<OnboardingReadinessCheckResult> EvaluateAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset observedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        try
        {
            PowerDiagnosticsSnapshot snapshot = await diagnostics.CaptureAsync(cancellationToken).ConfigureAwait(false);
            int failed = snapshot.Results.Count(item => item.ExitCode != 0 || item.TimedOut);
            bool warning = !snapshot.SleepStatesQuerySucceeded
                || !snapshot.WakeTimersQuerySucceeded
                || !snapshot.HasArmedWakeDevice
                || failed > 0;
            return new(
                Definition,
                warning ? OnboardingCheckStatus.Warning : OnboardingCheckStatus.Passed,
                warning ? "power-wake.caveat" : "power-wake.diagnostics-ready",
                warning
                    ? "Windows reported one or more wake limitations or inaccessible diagnostics. Scheduled work can still run while awake, but waking from sleep is not guaranteed."
                    : "Read-only Windows power diagnostics completed. Real wake still depends on firmware, sleep mode, battery state, and Windows policy.",
                snapshot.CapturedAtUtc,
                observedAtUtc.AddHours(1),
                "1.0",
                JsonSerializer.SerializeToElement(new
                {
                    snapshot.SleepStatesQuerySucceeded,
                    snapshot.WakeTimersQuerySucceeded,
                    snapshot.HasReportedWakeTimer,
                    snapshot.HasArmedWakeDevice,
                    failedDiagnosticCount = failed,
                    diagnosticCount = snapshot.Results.Count,
                    mutationAttempted = false,
                }),
                Definition.RemediationKey);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(
                Definition,
                OnboardingCheckStatus.Warning,
                "power-wake.diagnostics-unavailable",
                "Read-only Windows power diagnostics were unavailable. Continue only if you understand that automatic wake cannot be confirmed on this machine.",
                observedAtUtc,
                observedAtUtc.AddMinutes(15),
                "1.0",
                JsonSerializer.SerializeToElement(new { diagnosticsSucceeded = false, mutationAttempted = false }),
                Definition.RemediationKey);
        }
    }
}
