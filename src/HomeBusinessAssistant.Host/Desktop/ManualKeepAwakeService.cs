using HomeBusinessAssistant.Application.Power;

namespace HomeBusinessAssistant.Host.Desktop;

/// <summary>Current bounded manual keep-awake state.</summary>
public sealed record ManualKeepAwakeState(bool IsActive, DateTimeOffset? ExpiresAtUtc);

/// <summary>Creates, extends, and releases one manual system-required session.</summary>
public interface IManualKeepAwakeService : IAsyncDisposable
{
    /// <summary>Gets current in-memory session state.</summary>
    ManualKeepAwakeState GetState();

    /// <summary>Starts or extends the session; a shorter request never reduces an existing hold.</summary>
    ValueTask<ManualKeepAwakeState> StartAsync(
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    /// <summary>Releases the active hold immediately.</summary>
    ValueTask ReleaseAsync();
}

/// <summary>Reference-safe manual power session with deterministic time and cleanup.</summary>
public sealed class ManualKeepAwakeService(
    IPowerRequestService powerRequests,
    TimeProvider timeProvider) : IManualKeepAwakeService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object stateLock = new();
    private ManualKeepAwakeState state = new(false, null);
    private IAsyncDisposable? handle;
    private CancellationTokenSource? expiryCancellation;
    private long generation;
    private int disposed;

    /// <inheritdoc />
    public ManualKeepAwakeState GetState()
    {
        lock (stateLock)
        {
            return state;
        }
    }

    /// <inheritdoc />
    public async ValueTask<ManualKeepAwakeState> StartAsync(
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (duration < TimeSpan.FromMinutes(1) || duration > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Manual keep-awake must be between one minute and 24 hours.");
        }

        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        DateTimeOffset expiresAtUtc = timeProvider.GetUtcNow().ToUniversalTime().Add(duration);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        IAsyncDisposable? priorHandle = null;
        CancellationTokenSource? priorCancellation = null;
        long currentGeneration;
        try
        {
            ManualKeepAwakeState current = GetState();
            if (current is { IsActive: true, ExpiresAtUtc: not null }
                && current.ExpiresAtUtc.Value >= expiresAtUtc)
            {
                return current;
            }

            IAsyncDisposable acquired = await powerRequests.AcquireSystemRequiredAsync(
                "Manual keep-awake session from the Home Business Assistant tray.",
                keepDisplayOn: false,
                cancellationToken).ConfigureAwait(false);
            priorHandle = handle;
            priorCancellation = expiryCancellation;
            handle = acquired;
            expiryCancellation = new CancellationTokenSource();
            currentGeneration = Interlocked.Increment(ref generation);
            lock (stateLock)
            {
                state = new(true, expiresAtUtc);
            }

            _ = ExpireAsync(currentGeneration, expiresAtUtc, expiryCancellation.Token);
        }
        finally
        {
            gate.Release();
        }

        if (priorCancellation is not null)
        {
            await priorCancellation.CancelAsync().ConfigureAwait(false);
            priorCancellation.Dispose();
        }

        if (priorHandle is not null)
        {
            await priorHandle.DisposeAsync().ConfigureAwait(false);
        }

        return GetState();
    }

    /// <inheritdoc />
    public async ValueTask ReleaseAsync() => await ReleaseGenerationAsync(expectedGeneration: null).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await ReleaseGenerationAsync(expectedGeneration: null).ConfigureAwait(false);
        gate.Dispose();
    }

    private async Task ExpireAsync(long expectedGeneration, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken)
    {
        try
        {
            TimeSpan delay = expiresAtUtc - timeProvider.GetUtcNow().ToUniversalTime();
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
            }

            await ReleaseGenerationAsync(expectedGeneration).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A replacement or explicit release owns cleanup.
        }
    }

    private async ValueTask ReleaseGenerationAsync(long? expectedGeneration)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        IAsyncDisposable? releaseHandle;
        CancellationTokenSource? releaseCancellation;
        try
        {
            if (expectedGeneration.HasValue && expectedGeneration.Value != Volatile.Read(ref generation))
            {
                return;
            }

            releaseHandle = handle;
            releaseCancellation = expiryCancellation;
            handle = null;
            expiryCancellation = null;
            _ = Interlocked.Increment(ref generation);
            lock (stateLock)
            {
                state = new(false, null);
            }
        }
        finally
        {
            gate.Release();
        }

        if (releaseCancellation is not null)
        {
            await releaseCancellation.CancelAsync().ConfigureAwait(false);
            releaseCancellation.Dispose();
        }

        if (releaseHandle is not null)
        {
            await releaseHandle.DisposeAsync().ConfigureAwait(false);
        }
    }
}
