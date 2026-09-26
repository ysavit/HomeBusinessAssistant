using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.Wake;
using HomeBusinessAssistant.Host.Health;

namespace HomeBusinessAssistant.Host.Dashboard;

/// <summary>Safe managed wake-task state for local presentation.</summary>
public sealed record DashboardWakeStatus(
    bool Exists,
    bool IsManaged,
    string? OccurrenceId,
    DateTimeOffset? DueAtUtc,
    string? ErrorCode);

/// <summary>One coherent dashboard snapshot from durable and in-memory platform state.</summary>
public sealed record HostDashboardSnapshot(
    bool IsReady,
    IReadOnlyList<string> ReadinessReasonCodes,
    HostPlatformStatus Platform,
    GlobalSchedulePauseState GlobalPause,
    DashboardWakeStatus Wake,
    IReadOnlyList<HostLoopHealth> Loops);

/// <summary>Builds the local dashboard model without exposing sensitive payloads.</summary>
public interface IHostDashboardService
{
    /// <summary>Reads one bounded dashboard snapshot.</summary>
    ValueTask<HostDashboardSnapshot> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Production dashboard composition over central read models and wake state.</summary>
public sealed class HostDashboardService(
    IHostPlatformStatusReader statusReader,
    IGlobalScheduleControlService globalPause,
    IWakeTaskSchedulerBridge wakeBridge,
    IHostReadinessEvaluator readiness,
    HostHealthState healthState) : IHostDashboardService
{
    /// <inheritdoc />
    public async ValueTask<HostDashboardSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        HostPlatformStatus platform = await statusReader.ReadAsync(10, cancellationToken).ConfigureAwait(false);
        GlobalSchedulePauseState pause = await globalPause.GetStateAsync(cancellationToken).ConfigureAwait(false);
        HostReadinessResult ready = await readiness.EvaluateAsync(cancellationToken).ConfigureAwait(false);
        WakeTaskState wake;
        try
        {
            wake = await wakeBridge.GetStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            wake = new(false, false, null, null, null, new(
                "wake.state-unavailable",
                WakeTaskErrorCategory.CommandFailed,
                "The managed wake-task state is unavailable."));
        }

        return new(
            ready.IsReady,
            ready.ReasonCodes,
            platform,
            pause,
            new(
                wake.Exists,
                wake.IsManaged,
                wake.OccurrenceId?.ToString(),
                wake.DueAtUtc,
                wake.Error?.Code),
            healthState.Loops);
    }
}

internal sealed class EmptyHostDashboardService : IHostDashboardService
{
    public ValueTask<HostDashboardSnapshot> GetAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new HostDashboardSnapshot(
            IsReady: true,
            ReadinessReasonCodes: [],
            new([], [], [], [], null),
            new(false, []),
            new(false, false, null, null, null),
            []));
}
