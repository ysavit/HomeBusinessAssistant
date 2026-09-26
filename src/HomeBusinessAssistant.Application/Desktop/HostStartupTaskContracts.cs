using System.Security.Cryptography;
using System.Text;

namespace HomeBusinessAssistant.Application.Desktop;

/// <summary>Stable identity of the application-managed at-logon task.</summary>
public static class ManagedHostStartupTask
{
    /// <summary>The only Task Scheduler path owned for Host startup.</summary>
    public const string Name = "\\HomeBusinessAssistant\\HostAtLogon";
}

/// <summary>Immutable current-user startup task definition.</summary>
public sealed record HostStartupTaskRequest(
    string HostExecutablePath,
    string WorkingDirectory,
    string BootstrapConfigurationPath,
    string CurrentUserId,
    TimeSpan Delay)
{
    /// <summary>Returns a stable fingerprint over behavior-affecting fields.</summary>
    public string GetFingerprint()
    {
        string canonical = string.Join('\n',
            "host-at-logon/1.0",
            Path.GetFullPath(HostExecutablePath),
            Path.GetFullPath(WorkingDirectory),
            Path.GetFullPath(BootstrapConfigurationPath),
            CurrentUserId,
            ((long)Delay.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

/// <summary>Observed semantic state of the managed startup task.</summary>
public sealed record HostStartupTaskState(
    bool Exists,
    bool IsManaged,
    string? Fingerprint,
    string? HostExecutablePath,
    string? ErrorCode)
{
    /// <summary>Healthy absent state.</summary>
    public static HostStartupTaskState Absent { get; } = new(false, false, null, null, null);
}

/// <summary>Creates, verifies, queries, and removes the current-user Host startup task.</summary>
public interface IHostStartupTaskScheduler
{
    /// <summary>Queries semantic task state without changing it.</summary>
    ValueTask<HostStartupTaskState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Idempotently creates or updates the managed task.</summary>
    ValueTask<HostStartupTaskState> ReconcileAsync(HostStartupTaskRequest request, CancellationToken cancellationToken = default);

    /// <summary>Idempotently removes the managed task.</summary>
    ValueTask RemoveAsync(CancellationToken cancellationToken = default);
}
