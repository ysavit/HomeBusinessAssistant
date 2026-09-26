using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Host.Orchestration;

namespace HomeBusinessAssistant.Host.Desktop;

/// <summary>Testable tray/menu state independent of direct Windows Forms controls.</summary>
public sealed record TrayControllerState(
    HostPlatformStatus Platform,
    GlobalSchedulePauseState GlobalPause,
    ManualKeepAwakeState KeepAwake)
{
    /// <summary>Gets whether graceful exit should ask for confirmation.</summary>
    public bool RequiresExitConfirmation => Platform.ActiveRuns.Count > 0 || KeepAwake.IsActive;
}

/// <summary>Application-facing tray command controller; UI handlers only schedule these async operations.</summary>
public sealed class TrayController(
    IHostPlatformStatusReader statusReader,
    IManualAgentRunLauncher manualRuns,
    IGlobalScheduleControlService globalPause,
    IManualKeepAwakeService keepAwake,
    ILocalDashboardLauncher dashboardLauncher,
    HostNotificationHub notifications)
{
    /// <summary>Reads persisted menu state and current manual power state.</summary>
    public async ValueTask<TrayControllerState> GetStateAsync(CancellationToken cancellationToken = default) => new(
        await statusReader.ReadAsync(10, cancellationToken).ConfigureAwait(false),
        await globalPause.GetStateAsync(cancellationToken).ConfigureAwait(false),
        keepAwake.GetState());

    /// <summary>Opens the exact validated dashboard URL.</summary>
    public async ValueTask OpenDashboardAsync(CancellationToken cancellationToken = default)
    {
        if (!await dashboardLauncher.OpenAsync(cancellationToken).ConfigureAwait(false))
        {
            notifications.Publish(new(
                "dashboard.open-failed",
                "Dashboard could not be opened",
                "Open the configured 127.0.0.1 dashboard URL in a browser.",
                IsHighPriority: true));
        }
    }

    /// <summary>Opens a validated local notification destination.</summary>
    public async ValueTask OpenLocalPathAsync(string localPath, CancellationToken cancellationToken = default)
    {
        if (!await dashboardLauncher.OpenPathAsync(localPath, cancellationToken).ConfigureAwait(false))
        {
            await OpenDashboardAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Shows a bounded local system-status notification.</summary>
    public async ValueTask ShowStatusAsync(CancellationToken cancellationToken = default)
    {
        TrayControllerState current = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        notifications.Publish(new(
            "host.status",
            "Home Business Assistant status",
            $"{current.Platform.ActiveRuns.Count} active run(s), {current.Platform.NextOccurrences.Count} upcoming occurrence(s).",
            IsHighPriority: false));
    }

    /// <summary>Creates a Founder Scout manual occurrence and starts Runner when eligible.</summary>
    public ValueTask<OccurrenceDispatchResult> RunFounderScoutAsync(CancellationToken cancellationToken = default) =>
        RunAgentAsync(AgentId.Parse("founder-scout"), "run", cancellationToken);

    /// <summary>Creates a Wake Remote diagnostic occurrence and starts Runner when eligible.</summary>
    public ValueTask<OccurrenceDispatchResult> RunWakeRemoteDiagnosticAsync(CancellationToken cancellationToken = default) =>
        RunAgentAsync(AgentId.Parse("wake-remote"), "diagnose", cancellationToken);

    /// <summary>Starts or extends a bounded manual keep-awake session.</summary>
    public async ValueTask<ManualKeepAwakeState> KeepAwakeForAsync(
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ManualKeepAwakeState result = await keepAwake.StartAsync(duration, cancellationToken).ConfigureAwait(false);
        notifications.Publish(new(
            "power.manual-started",
            "Keep-awake active",
            $"The system-required request is active until {result.ExpiresAtUtc:u}.",
            IsHighPriority: false));
        return result;
    }

    /// <summary>Releases the current manual keep-awake request.</summary>
    public async ValueTask ReleaseKeepAwakeAsync()
    {
        await keepAwake.ReleaseAsync().ConfigureAwait(false);
        notifications.Publish(new(
            "power.manual-released",
            "Keep-awake released",
            "Normal Windows idle power policy has resumed.",
            IsHighPriority: false));
    }

    /// <summary>Persists an audited global pause.</summary>
    public async ValueTask PauseAllAsync(CancellationToken cancellationToken = default)
    {
        _ = await globalPause.PauseAllAsync("tray", Guid.NewGuid(), cancellationToken).ConfigureAwait(false);
        notifications.Publish(new("schedules.paused", "Agents paused", "All active schedules are paused.", false));
    }

    /// <summary>Resumes schedules changed by the current global pause.</summary>
    public async ValueTask ResumeAllAsync(CancellationToken cancellationToken = default)
    {
        _ = await globalPause.ResumeAllAsync("tray", Guid.NewGuid(), cancellationToken).ConfigureAwait(false);
        notifications.Publish(new("schedules.resumed", "Agents resumed", "Globally paused schedules are active again.", false));
    }

    /// <summary>Shows the latest persisted bounded summary, or a real empty state.</summary>
    public async ValueTask ViewLatestSummaryAsync(CancellationToken cancellationToken = default)
    {
        HostRunStatus? latest = (await statusReader.ReadAsync(10, cancellationToken).ConfigureAwait(false)).LatestSummary;
        notifications.Publish(latest is null
            ? new("summary.empty", "No run summary", "No completed run has published a summary yet.", false)
            : new(
                "summary.latest",
                $"Latest summary: {latest.AgentDisplayName}",
                latest.SummaryText ?? "The run completed without summary text.",
                false));
    }

    private async ValueTask<OccurrenceDispatchResult> RunAgentAsync(
        AgentId agentId,
        string commandName,
        CancellationToken cancellationToken)
    {
        OccurrenceDispatchResult result = await manualRuns.CreateAndLaunchAsync(new(
            agentId,
            commandName,
            "{}",
            TriggerType.TrayMenu,
            ConcurrencyPolicy.Forbid,
            "tray",
            Guid.NewGuid()), cancellationToken).ConfigureAwait(false);
        notifications.Publish(new(
            result.RunnerStarted ? "runner.started" : result.Queued ? "occurrence.queued" : "runner.not-started",
            result.RunnerStarted ? "Agent run started" : result.Queued ? "Agent run queued" : "Agent run could not start",
            result.RunnerStarted
                ? $"Runner process {result.RunnerProcessId} accepted the occurrence."
                : $"Occurrence {result.OccurrenceId} has status code {result.Code}.",
            IsHighPriority: !result.RunnerStarted && !result.Queued));
        return result;
    }
}
