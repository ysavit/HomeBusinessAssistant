using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace HomeBusinessAssistant.Windows.Desktop;

/// <summary>Allow-listed commands accepted from another launch of the desktop Host.</summary>
public enum HostInstanceCommand
{
    /// <summary>Open the primary instance's local dashboard.</summary>
    OpenDashboard = 0,
    /// <summary>Show a bounded primary-instance status notification.</summary>
    ShowStatus = 1,
    /// <summary>Request a graceful application-owned shutdown for maintenance.</summary>
    Shutdown = 2,
}

/// <summary>Bounded single-line IPC encoding for desktop activation commands.</summary>
public static class HostInstanceCommandCodec
{
    /// <summary>The maximum accepted UTF-8 command payload.</summary>
    public const int MaximumMessageBytes = 64;

    /// <summary>Encodes one allow-listed command.</summary>
    public static byte[] Encode(HostInstanceCommand command)
    {
        if (!Enum.IsDefined(command))
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        return Encoding.UTF8.GetBytes(command + "\n");
    }

    /// <summary>Parses one complete command and rejects extra or arbitrary input.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> payload, out HostInstanceCommand command)
    {
        command = default;
        if (payload.Length is 0 or > MaximumMessageBytes)
        {
            return false;
        }

        string value;
        try
        {
            value = new UTF8Encoding(false, true).GetString(payload).TrimEnd('\r', '\n');
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        return value.Length > 0
            && value.All(character => !char.IsControl(character))
            && Enum.TryParse(value, ignoreCase: false, out command)
            && Enum.IsDefined(command);
    }
}

/// <summary>Per-user, per-session mutex and CurrentUserOnly named-pipe activation transport.</summary>
public sealed class WindowsSingleInstanceCoordinator : IAsyncDisposable
{
    private readonly string mutexName;
    private readonly string pipeName;
    private readonly TimeSpan signalTimeout;
    private readonly CancellationTokenSource listenerCancellation = new();
    private Mutex? mutex;
    private Task? listenerTask;
    private bool ownsMutex;
    private int disposed;

    /// <summary>Creates default names isolated to the current interactive Windows user/session.</summary>
    public WindowsSingleInstanceCoordinator(TimeSpan? signalTimeout = null, string? capabilityDirectory = null)
        : this(CreateIdentitySuffix(capabilityDirectory), signalTimeout)
    {
    }

    /// <summary>Creates an isolated coordinator; the explicit suffix is primarily for tests.</summary>
    public WindowsSingleInstanceCoordinator(string identitySuffix, TimeSpan? signalTimeout = null)
    {
        if (string.IsNullOrWhiteSpace(identitySuffix)
            || identitySuffix.Length > 96
            || identitySuffix.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new ArgumentException("A safe single-instance identity suffix is required.", nameof(identitySuffix));
        }

        this.signalTimeout = signalTimeout ?? TimeSpan.FromSeconds(3);
        if (this.signalTimeout < TimeSpan.FromMilliseconds(100) || this.signalTimeout > TimeSpan.FromSeconds(15))
        {
            throw new ArgumentOutOfRangeException(nameof(signalTimeout));
        }

        mutexName = $"Local\\HomeBusinessAssistant.Host.{identitySuffix}";
        pipeName = $"HomeBusinessAssistant.Host.{identitySuffix}";
    }

    /// <summary>Attempts to own the primary-instance mutex on the calling STA thread.</summary>
    public bool TryAcquirePrimary()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (mutex is not null)
        {
            throw new InvalidOperationException("The single-instance mutex was already inspected.");
        }

        mutex = new Mutex(initiallyOwned: false, mutexName);
        try
        {
            ownsMutex = mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        return ownsMutex;
    }

    /// <summary>Sends one bounded command to the primary instance, retrying until the startup timeout.</summary>
    public async ValueTask<bool> SignalPrimaryAsync(
        HostInstanceCommand command,
        CancellationToken cancellationToken = default)
    {
        byte[] payload = HostInstanceCommandCodec.Encode(command);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(signalTimeout);
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                await using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.Out,
                    PipeOptions.Asynchronous);
                await client.ConnectAsync(250, timeout.Token).ConfigureAwait(false);
                await client.WriteAsync(payload, timeout.Token).ConfigureAwait(false);
                await client.FlushAsync(timeout.Token).ConfigureAwait(false);
                return true;
            }
            catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                if (timeout.IsCancellationRequested)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(75), timeout.Token).ConfigureAwait(false);
            }
        }

        return false;
    }

    /// <summary>Starts the sequential allow-listed command listener for the primary instance.</summary>
    public void StartListening(Func<HostInstanceCommand, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!ownsMutex || listenerTask is not null)
        {
            throw new InvalidOperationException("Only the acquired primary instance may start one listener.");
        }

        listenerTask = Task.Run(() => ListenAsync(handler, listenerCancellation.Token));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        if (ownsMutex)
        {
            mutex?.ReleaseMutex();
            ownsMutex = false;
        }

        mutex?.Dispose();
        mutex = null;
        await listenerCancellation.CancelAsync().ConfigureAwait(false);
        if (listenerTask is not null)
        {
            try
            {
                await listenerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during primary-instance shutdown.
            }
        }

        listenerCancellation.Dispose();
    }

    private async Task ListenAsync(
        Func<HostInstanceCommand, CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using NamedPipeServerStream server = CreateActivationPipe();
            await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            byte[] buffer = new byte[HostInstanceCommandCodec.MaximumMessageBytes + 1];
            var length = 0;
            while (length < buffer.Length)
            {
                int read = await server.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                length += read;
                if (buffer.AsSpan(0, length).Contains((byte)'\n'))
                {
                    break;
                }
            }

            if (HostInstanceCommandCodec.TryDecode(buffer.AsSpan(0, length), out HostInstanceCommand command))
            {
                try
                {
                    await handler(command, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A bad activation handler must not terminate the listener.
                }
            }
        }
    }

    private NamedPipeServerStream CreateActivationPipe()
        => new(
            pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            inBufferSize: 256,
            outBufferSize: 0);

    private static string CreateIdentitySuffix(string? capabilityDirectory)
    {
        string identity = $"{Environment.UserDomainName}\\{Environment.UserName}:{GetSessionId()}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        string identitySuffix = Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
        return $"{identitySuffix}-{GetOrCreateCapabilitySuffix(identitySuffix, capabilityDirectory)}";
    }

    private static string GetOrCreateCapabilitySuffix(string identitySuffix, string? capabilityDirectory)
    {
        string root = capabilityDirectory is null
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HomeBusinessAssistant",
                "ipc")
            : Path.Combine(Path.GetFullPath(capabilityDirectory), "ipc");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, $"host-{identitySuffix}.capability");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (File.Exists(path))
            {
                string existing = File.ReadAllText(path, Encoding.ASCII).Trim();
                if (existing.Length == 48 && existing.All(char.IsAsciiHexDigit))
                {
                    return existing.ToLowerInvariant();
                }

                throw new InvalidDataException("The Host IPC capability file is invalid.");
            }

            string created = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(Encoding.ASCII.GetBytes(created));
                stream.Flush(flushToDisk: true);
                return created;
            }
            catch (IOException) when (attempt < 2)
            {
                // Another process may have won first-launch capability creation.
            }
        }

        throw new IOException("The Host IPC capability file could not be initialized.");
    }

    private static int GetSessionId()
    {
        try
        {
            return System.Diagnostics.Process.GetCurrentProcess().SessionId;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }
}
