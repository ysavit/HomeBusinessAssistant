using System.Collections.Concurrent;
using HomeBusinessAssistant.Application.Power;

namespace HomeBusinessAssistant.Windows.Power;

/// <summary>A safe acquisition/release diagnostic emitted without request content.</summary>
public sealed record PowerRequestDiagnostic(
    DateTimeOffset TimestampUtc,
    string EventCode,
    int ActiveHandles,
    int DisplayHandles);

/// <summary>
/// Reference-counts execution-state requests on one dedicated thread, as required by the thread-scoped Win32 API.
/// </summary>
public sealed class WindowsPowerRequestService : IPowerRequestService, IAsyncDisposable
{
    private readonly IWindowsExecutionStateNativeApi nativeApi;
    private readonly TimeProvider timeProvider;
    private readonly BlockingCollection<PowerCommand> commands = new();
    private readonly Thread worker;
    private int disposed;

    /// <summary>Creates and starts the dedicated native-call thread.</summary>
    public WindowsPowerRequestService(
        IWindowsExecutionStateNativeApi nativeApi,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.nativeApi = nativeApi;
        this.timeProvider = timeProvider;
        worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "HomeBusinessAssistant.PowerRequest",
        };
        worker.Start();
    }

    /// <summary>Raised after bounded acquisition/release state changes.</summary>
    public event EventHandler<PowerRequestDiagnostic>? DiagnosticRecorded;

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> AcquireSystemRequiredAsync(
        string reason,
        bool keepDisplayOn,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 256 || reason.Any(char.IsControl))
        {
            throw new ArgumentException("A bounded power-request reason is required.", nameof(reason));
        }

        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var completion = NewCompletion<IAsyncDisposable>();
        try
        {
            commands.Add(new AcquireCommand(keepDisplayOn, completion), cancellationToken);
        }
        catch (InvalidOperationException)
        {
            ThrowIfDisposed();
            throw;
        }

        return await completion.Task.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        var completion = NewCompletion<bool>();
        try
        {
            commands.Add(new ShutdownCommand(completion));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        _ = await completion.Task.ConfigureAwait(false);
        commands.CompleteAdding();
        await Task.Run(worker.Join).ConfigureAwait(false);
        commands.Dispose();
    }

    private void Run()
    {
        var activeHandles = 0;
        var displayHandles = 0;
        foreach (PowerCommand command in commands.GetConsumingEnumerable())
        {
            switch (command)
            {
                case AcquireCommand acquire:
                    {
                        int nextActive = activeHandles + 1;
                        int nextDisplay = displayHandles + (acquire.KeepDisplayOn ? 1 : 0);
                        if (!nativeApi.TrySet(BuildState(nextActive, nextDisplay)))
                        {
                            acquire.Completion.TrySetException(new PowerRequestException(
                                "power.acquire-failed",
                                "Windows did not accept the required system-awake request."));
                            Record("power.acquire-failed", activeHandles, displayHandles);
                            break;
                        }

                        activeHandles = nextActive;
                        displayHandles = nextDisplay;
                        var handle = new PowerRequestHandle(this, acquire.KeepDisplayOn);
                        acquire.Completion.TrySetResult(handle);
                        Record("power.acquired", activeHandles, displayHandles);
                        break;
                    }

                case ReleaseCommand release:
                    {
                        int nextActive = Math.Max(0, activeHandles - 1);
                        int nextDisplay = Math.Max(0, displayHandles - (release.KeptDisplayOn ? 1 : 0));
                        bool released = nativeApi.TrySet(BuildState(nextActive, nextDisplay));
                        activeHandles = nextActive;
                        displayHandles = nextDisplay;
                        Record(released ? "power.released" : "power.release-failed", activeHandles, displayHandles);
                        release.Completion.TrySetResult(released);
                        break;
                    }

                case ShutdownCommand shutdown:
                    {
                        bool released = activeHandles == 0
                            || nativeApi.TrySet(WindowsExecutionState.Continuous);
                        activeHandles = 0;
                        displayHandles = 0;
                        Record(released ? "power.shutdown-released" : "power.shutdown-release-failed", 0, 0);
                        shutdown.Completion.TrySetResult(released);
                        return;
                    }
            }
        }
    }

    private async ValueTask ReleaseAsync(bool keptDisplayOn)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        var completion = NewCompletion<bool>();
        try
        {
            commands.Add(new ReleaseCommand(keptDisplayOn, completion));
            _ = await completion.Task.ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (Volatile.Read(ref disposed) != 0)
        {
            // Service shutdown performs the final ES_CONTINUOUS clear.
        }
    }

    private void Record(string eventCode, int activeHandles, int displayHandles)
    {
        try
        {
            DiagnosticRecorded?.Invoke(this, new(
                timeProvider.GetUtcNow().ToUniversalTime(),
                eventCode,
                activeHandles,
                displayHandles));
        }
        catch (Exception)
        {
            // Diagnostics cannot terminate the thread that owns the native execution-state request.
        }
    }

    private static WindowsExecutionState BuildState(int activeHandles, int displayHandles)
    {
        WindowsExecutionState state = WindowsExecutionState.Continuous;
        if (activeHandles > 0)
        {
            state |= WindowsExecutionState.SystemRequired;
        }

        if (displayHandles > 0)
        {
            state |= WindowsExecutionState.DisplayRequired;
        }

        return state;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

    private static TaskCompletionSource<T> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private abstract record PowerCommand;

    private sealed record AcquireCommand(
        bool KeepDisplayOn,
        TaskCompletionSource<IAsyncDisposable> Completion) : PowerCommand;

    private sealed record ReleaseCommand(
        bool KeptDisplayOn,
        TaskCompletionSource<bool> Completion) : PowerCommand;

    private sealed record ShutdownCommand(TaskCompletionSource<bool> Completion) : PowerCommand;

    private sealed class PowerRequestHandle(
        WindowsPowerRequestService owner,
        bool keptDisplayOn) : IAsyncDisposable
    {
        private int disposed;

        public ValueTask DisposeAsync() => Interlocked.Exchange(ref disposed, 1) == 0
            ? owner.ReleaseAsync(keptDisplayOn)
            : ValueTask.CompletedTask;
    }
}
