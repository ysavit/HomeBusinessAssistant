using HomeBusinessAssistant.Application.Power;

namespace WakeRemote.Application;

/// <summary>One conservative local network-readiness observation.</summary>
public sealed record NetworkReadinessAttempt(bool Ready, string ReasonCode);

/// <summary>Performs one bounded local network-readiness observation.</summary>
public interface INetworkReadinessProbe
{
    /// <summary>Checks local interface state and configured optional probes once.</summary>
    ValueTask<NetworkReadinessAttempt> ProbeAsync(
        WakeRemoteConfiguration configuration,
        CancellationToken cancellationToken = default);
}

/// <summary>Typed readiness states returned by a remote-provider probe.</summary>
public enum RemoteProviderReadiness
{
    /// <summary>The configured provider appears locally ready.</summary>
    Ready,
    /// <summary>The configured provider is present but does not appear ready.</summary>
    NotReady,
    /// <summary>No usable local provider evidence was configured.</summary>
    NotConfigured,
    /// <summary>The provider could not be inspected safely.</summary>
    Unknown,
}

/// <summary>Safe, bounded provider observation without process/service details.</summary>
public sealed record RemoteProviderProbeResult(
    RemoteProviderKind Provider,
    RemoteProviderReadiness Readiness,
    string ReasonCode);

/// <summary>Performs one read-only local remote-provider health check.</summary>
public interface IRemoteAccessProviderProbe
{
    /// <summary>Checks the configured provider without mutating local state.</summary>
    ValueTask<RemoteProviderProbeResult> ProbeAsync(
        RemoteProviderSettings configuration,
        CancellationToken cancellationToken = default);
}

/// <summary>Abstracts time delays so availability windows do not make tests wait in real time.</summary>
public interface IWakeRemoteDelay
{
    /// <summary>Waits for a bounded duration or cancellation.</summary>
    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);
}

/// <summary>Production delay based on the same clock used by orchestration.</summary>
public sealed class TimeProviderWakeRemoteDelay(TimeProvider timeProvider) : IWakeRemoteDelay
{
    /// <inheritdoc />
    public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default) =>
        await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
}

/// <summary>Protocol-neutral telemetry emitted by Wake Remote orchestration.</summary>
public interface IWakeRemoteEventSink
{
    /// <summary>Emits one liveness heartbeat.</summary>
    ValueTask HeartbeatAsync(string phase, string message, CancellationToken cancellationToken = default);

    /// <summary>Emits one bounded progress record.</summary>
    ValueTask ProgressAsync(string phase, string message, CancellationToken cancellationToken = default);

    /// <summary>Emits one numeric metric.</summary>
    ValueTask NumericMetricAsync(string name, double value, string? unit = null, CancellationToken cancellationToken = default);

    /// <summary>Emits one textual metric.</summary>
    ValueTask TextMetricAsync(string name, string value, CancellationToken cancellationToken = default);

    /// <summary>Emits one non-terminal warning.</summary>
    ValueTask WarningAsync(string code, string message, CancellationToken cancellationToken = default);
}

/// <summary>Deterministic summary persisted in the terminal protocol event.</summary>
public sealed record WakeRemoteSummary(
    DateTimeOffset ScheduledWakeAtUtc,
    DateTimeOffset AgentStartedAtUtc,
    double WakeDelaySeconds,
    bool NetworkReady,
    double NetworkReadySeconds,
    int NetworkAttempts,
    RemoteProviderKind RemoteProvider,
    bool RemoteProviderRequired,
    bool RemoteProviderReady,
    DateTimeOffset? MachineReadyAtUtc,
    DateTimeOffset AvailableUntilUtc,
    double KeepAwakeSeconds,
    string Result,
    string ReasonCode);

/// <summary>Stable workflow results mapped to Agent SDK statuses/exit codes by the executable.</summary>
public enum WakeRemoteResultKind
{
    /// <summary>The readiness checks passed and the window reached its configured deadline.</summary>
    Completed,
    /// <summary>The immutable deadline had already elapsed before power acquisition.</summary>
    WindowExpired,
    /// <summary>Local network readiness did not arrive within the bounded timeout.</summary>
    NetworkTimeout,
    /// <summary>A required remote provider was not locally ready.</summary>
    RemoteProviderUnavailable,
}

/// <summary>One Wake Remote workflow result.</summary>
public sealed record WakeRemoteWorkflowResult(WakeRemoteResultKind Kind, WakeRemoteSummary Summary);

/// <summary>Runs one bounded Wake Remote availability window.</summary>
public sealed class WakeRemoteWorkflow(
    IPowerRequestService powerRequests,
    INetworkReadinessProbe network,
    IRemoteAccessProviderProbe provider,
    IWakeRemoteDelay delay,
    TimeProvider timeProvider)
{
    /// <summary>Executes readiness and availability while reliably owning the power handle.</summary>
    public async ValueTask<WakeRemoteWorkflowResult> RunAsync(
        WakeRemoteConfiguration configuration,
        WakeRemoteOccurrenceArguments occurrence,
        IWakeRemoteEventSink events,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentNullException.ThrowIfNull(events);
        DateTimeOffset startedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        double wakeDelaySeconds = Math.Max(0, (startedAtUtc - occurrence.ScheduledWakeAtUtc).TotalSeconds);
        if (occurrence.AvailableUntilUtc <= startedAtUtc)
        {
            return Result(
                WakeRemoteResultKind.WindowExpired,
                configuration,
                occurrence,
                startedAtUtc,
                wakeDelaySeconds,
                networkReady: false,
                networkReadySeconds: 0,
                networkAttempts: 0,
                remoteReady: false,
                machineReadyAtUtc: null,
                keepAwakeSeconds: 0,
                "wakeRemote.window.expired");
        }

        IAsyncDisposable? powerHandle = null;
        try
        {
            powerHandle = await powerRequests.AcquireSystemRequiredAsync(
                $"Wake Remote availability window {occurrence.WindowInstanceId}",
                configuration.KeepDisplayOn,
                cancellationToken).ConfigureAwait(false);
            await events.TextMetricAsync("scheduled_wake_at_utc", occurrence.ScheduledWakeAtUtc.ToString("O"), cancellationToken).ConfigureAwait(false);
            await events.TextMetricAsync("agent_started_at_utc", startedAtUtc.ToString("O"), cancellationToken).ConfigureAwait(false);
            await events.NumericMetricAsync("wake_delay_seconds", wakeDelaySeconds, "seconds", cancellationToken).ConfigureAwait(false);

            NetworkWaitResult networkResult = await WaitForNetworkAsync(
                configuration,
                occurrence.AvailableUntilUtc,
                events,
                cancellationToken).ConfigureAwait(false);
            await events.NumericMetricAsync("network_probe_attempts", networkResult.Attempts, cancellationToken: cancellationToken).ConfigureAwait(false);
            await events.NumericMetricAsync("network_ready_seconds", networkResult.Elapsed.TotalSeconds, "seconds", cancellationToken).ConfigureAwait(false);
            await events.TextMetricAsync("network_ready", networkResult.Ready ? "true" : "false", cancellationToken).ConfigureAwait(false);
            if (!networkResult.Ready)
            {
                return Result(
                    WakeRemoteResultKind.NetworkTimeout,
                    configuration,
                    occurrence,
                    startedAtUtc,
                    wakeDelaySeconds,
                    networkReady: false,
                    networkResult.Elapsed.TotalSeconds,
                    networkResult.Attempts,
                    remoteReady: false,
                    machineReadyAtUtc: null,
                    keepAwakeSeconds: ElapsedSince(startedAtUtc),
                    networkResult.ReasonCode);
            }

            await events.TextMetricAsync("network.ready", "true", cancellationToken).ConfigureAwait(false);
            RemoteProviderProbeResult remote = await provider.ProbeAsync(
                configuration.RemoteProvider,
                cancellationToken).ConfigureAwait(false);
            bool remoteReady = remote.Readiness == RemoteProviderReadiness.Ready;
            await events.TextMetricAsync("remote_provider", remote.Provider.ToString(), cancellationToken).ConfigureAwait(false);
            await events.TextMetricAsync("remote.ready", remoteReady ? "true" : "false", cancellationToken).ConfigureAwait(false);
            if (!remoteReady && occurrence.RemoteProviderRequired)
            {
                return Result(
                    WakeRemoteResultKind.RemoteProviderUnavailable,
                    configuration,
                    occurrence,
                    startedAtUtc,
                    wakeDelaySeconds,
                    networkReady: true,
                    networkResult.Elapsed.TotalSeconds,
                    networkResult.Attempts,
                    remoteReady: false,
                    machineReadyAtUtc: null,
                    keepAwakeSeconds: ElapsedSince(startedAtUtc),
                    remote.ReasonCode);
            }

            if (!remoteReady)
            {
                await events.WarningAsync(
                    "wakeRemote.remote.optionalUnavailable",
                    "The optional remote provider is not ready; the availability window will remain active.",
                    cancellationToken).ConfigureAwait(false);
            }

            DateTimeOffset machineReadyAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
            await events.TextMetricAsync("machine.ready", "true", cancellationToken).ConfigureAwait(false);
            await RemainAvailableAsync(
                configuration,
                occurrence.AvailableUntilUtc,
                events,
                cancellationToken).ConfigureAwait(false);
            return Result(
                WakeRemoteResultKind.Completed,
                configuration,
                occurrence,
                startedAtUtc,
                wakeDelaySeconds,
                networkReady: true,
                networkResult.Elapsed.TotalSeconds,
                networkResult.Attempts,
                remoteReady,
                machineReadyAtUtc,
                keepAwakeSeconds: ElapsedSince(startedAtUtc),
                "wakeRemote.completed");
        }
        finally
        {
            if (powerHandle is not null)
            {
                await powerHandle.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<NetworkWaitResult> WaitForNetworkAsync(
        WakeRemoteConfiguration configuration,
        DateTimeOffset availableUntilUtc,
        IWakeRemoteEventSink events,
        CancellationToken cancellationToken)
    {
        DateTimeOffset beganAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        DateTimeOffset timeoutAtUtc = beganAtUtc.AddSeconds(configuration.NetworkReadyTimeoutSeconds);
        DateTimeOffset stopAtUtc = timeoutAtUtc < availableUntilUtc ? timeoutAtUtc : availableUntilUtc;
        var attempts = 0;
        string reason = "network.not-ready";
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;
            NetworkReadinessAttempt attempt = await network.ProbeAsync(configuration, cancellationToken).ConfigureAwait(false);
            reason = attempt.ReasonCode;
            DateTimeOffset observedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
            if (attempt.Ready)
            {
                return new(true, attempts, observedAtUtc - beganAtUtc, reason);
            }

            await events.ProgressAsync("network", "Waiting for local network readiness.", cancellationToken).ConfigureAwait(false);
            await events.HeartbeatAsync("network", reason, cancellationToken).ConfigureAwait(false);
            TimeSpan remaining = stopAtUtc - observedAtUtc;
            if (remaining <= TimeSpan.Zero)
            {
                return new(false, attempts, observedAtUtc - beganAtUtc, reason);
            }

            TimeSpan interval = TimeSpan.FromSeconds(configuration.NetworkProbeIntervalSeconds);
            await delay.DelayAsync(interval < remaining ? interval : remaining, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask RemainAvailableAsync(
        WakeRemoteConfiguration configuration,
        DateTimeOffset availableUntilUtc,
        IWakeRemoteEventSink events,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
            TimeSpan remaining = availableUntilUtc - nowUtc;
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            await events.ProgressAsync("availability-window", "The machine is available for remote access.", cancellationToken).ConfigureAwait(false);
            await events.HeartbeatAsync("availability-window", "Keep-awake request is active.", cancellationToken).ConfigureAwait(false);
            TimeSpan heartbeat = TimeSpan.FromSeconds(configuration.WindowHeartbeatIntervalSeconds);
            await delay.DelayAsync(heartbeat < remaining ? heartbeat : remaining, cancellationToken).ConfigureAwait(false);
        }
    }

    private static WakeRemoteWorkflowResult Result(
        WakeRemoteResultKind kind,
        WakeRemoteConfiguration configuration,
        WakeRemoteOccurrenceArguments occurrence,
        DateTimeOffset startedAtUtc,
        double wakeDelaySeconds,
        bool networkReady,
        double networkReadySeconds,
        int networkAttempts,
        bool remoteReady,
        DateTimeOffset? machineReadyAtUtc,
        double keepAwakeSeconds,
        string reasonCode) => new(kind, new(
            occurrence.ScheduledWakeAtUtc,
            startedAtUtc,
            wakeDelaySeconds,
            networkReady,
            networkReadySeconds,
            networkAttempts,
            configuration.RemoteProvider.Kind,
            occurrence.RemoteProviderRequired,
            remoteReady,
            machineReadyAtUtc,
            occurrence.AvailableUntilUtc,
            keepAwakeSeconds,
            kind.ToString(),
            reasonCode));

    private double ElapsedSince(DateTimeOffset startedAtUtc) =>
        Math.Max(0, (timeProvider.GetUtcNow().ToUniversalTime() - startedAtUtc).TotalSeconds);

    private sealed record NetworkWaitResult(bool Ready, int Attempts, TimeSpan Elapsed, string ReasonCode);
}
