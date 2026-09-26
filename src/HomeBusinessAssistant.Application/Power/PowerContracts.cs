using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Application.Power;

/// <summary>A typed failure to establish or release a Windows execution-state request.</summary>
public sealed class PowerRequestException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>Gets a stable health/audit code that contains no native command text.</summary>
    public string Code { get; } = code;
}

/// <summary>Acquires bounded system/display execution-state requests.</summary>
public interface IPowerRequestService
{
    /// <summary>Holds a continuous system-required request until the returned handle is disposed.</summary>
    ValueTask<IAsyncDisposable> AcquireSystemRequiredAsync(
        string reason,
        bool keepDisplayOn,
        CancellationToken cancellationToken = default);
}

/// <summary>The explicit durable policy passed from one occurrence to its process lifetime.</summary>
public sealed record ExecutionLifetimeRequest(
    AgentRunId RunId,
    OccurrenceId OccurrenceId,
    AgentId AgentId,
    bool KeepSystemAwake,
    bool KeepDisplayOn,
    string Reason);

/// <summary>Optional host-specific resource held across one child-process lifetime.</summary>
public interface IExecutionLifetimeHook
{
    /// <summary>Acquires the resources required by the durable occurrence policy.</summary>
    ValueTask<IAsyncDisposable> AcquireAsync(
        ExecutionLifetimeRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>No-op execution hook for non-Windows hosts and tests that do not request power integration.</summary>
public sealed class NoOpExecutionLifetimeHook : IExecutionLifetimeHook
{
    /// <inheritdoc />
    public ValueTask<IAsyncDisposable> AcquireAsync(
        ExecutionLifetimeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IAsyncDisposable>(NoOpHandle.Instance);
    }

    private sealed class NoOpHandle : IAsyncDisposable
    {
        public static NoOpHandle Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>One supported read-only power diagnostic.</summary>
public enum PowerDiagnosticKind
{
    /// <summary>Sleep states reported by <c>powercfg /a</c>.</summary>
    AvailableSleepStates = 0,
    /// <summary>Active wake timers reported by <c>powercfg /waketimers</c>.</summary>
    WakeTimers = 1,
    /// <summary>The most recent wake source reported by <c>powercfg /lastwake</c>.</summary>
    LastWake = 2,
    /// <summary>Devices armed to wake the system.</summary>
    WakeArmedDevices = 3,
}

/// <summary>Bounded and redacted output from one read-only power command.</summary>
public sealed record PowerDiagnosticResult(
    PowerDiagnosticKind Kind,
    int ExitCode,
    bool TimedOut,
    string Output,
    string Error,
    bool IndicatesNoActiveItems);

/// <summary>A local health snapshot which never changes power policy.</summary>
public sealed record PowerDiagnosticsSnapshot(
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<PowerDiagnosticResult> Results,
    bool SleepStatesQuerySucceeded,
    bool WakeTimersQuerySucceeded,
    bool HasReportedWakeTimer,
    bool HasArmedWakeDevice);

/// <summary>Captures read-only Windows power diagnostics.</summary>
public interface IPowerDiagnosticsService
{
    /// <summary>Runs the bounded supported diagnostic commands.</summary>
    Task<PowerDiagnosticsSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}
