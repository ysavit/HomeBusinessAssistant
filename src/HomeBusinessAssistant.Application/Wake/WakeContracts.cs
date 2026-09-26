using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Application.Wake;

/// <summary>The one Windows Task Scheduler object owned by V1.</summary>
public static class ManagedWakeTask
{
    /// <summary>Gets the stable folder-qualified task name.</summary>
    public const string Name = @"\HomeBusinessAssistant\NextWake";
}

/// <summary>The supported V1 task principal policy.</summary>
public enum WakeTaskUserSessionPolicy
{
    /// <summary>Run as the current user only while that user remains signed in.</summary>
    CurrentInteractiveUser = 0,
}

/// <summary>Typed categories suitable for local health display.</summary>
public enum WakeTaskErrorCategory
{
    /// <summary>The operating system denied task access.</summary>
    PermissionDenied = 0,
    /// <summary>A bounded task command failed.</summary>
    CommandFailed = 1,
    /// <summary>The registered task is not the expected managed shape.</summary>
    InvalidTask = 2,
    /// <summary>The request or local path is invalid.</summary>
    InvalidRequest = 3,
}

/// <summary>A safe wake-task error with no raw command line.</summary>
public sealed record WakeTaskError(
    string Code,
    WakeTaskErrorCategory Category,
    string Message,
    int? NativeExitCode = null);

/// <summary>A typed bridge failure.</summary>
public sealed class WakeTaskBridgeException(WakeTaskError error, Exception? innerException = null)
    : Exception(error.Message, innerException)
{
    /// <summary>Gets the safe structured error.</summary>
    public WakeTaskError Error { get; } = error;
}

/// <summary>Validated bootstrap paths generated into the managed Runner action.</summary>
public sealed record WakeRunnerBootstrap(
    string DataDirectory,
    string AgentDirectory,
    string ManifestDirectory,
    string DatabaseFileName);

/// <summary>All immutable data needed to render one managed wake task.</summary>
public sealed record WakeTaskRequest(
    OccurrenceId OccurrenceId,
    Guid ConfigurationRevisionId,
    DateTimeOffset DueAtUtc,
    DateTimeOffset DueLocal,
    string TimeZoneId,
    string RunnerExecutablePath,
    string RunnerWorkingDirectory,
    WakeRunnerBootstrap Bootstrap,
    TimeSpan ExecutionTimeout,
    string CurrentUserId,
    WakeTaskUserSessionPolicy UserSessionPolicy,
    bool StartWhenAvailable,
    bool AllowStartOnBatteries,
    bool StopIfGoingOnBatteries,
    Guid CorrelationId)
{
    /// <summary>Builds a stable identity over every setting that affects task behavior.</summary>
    public string GetFingerprint()
    {
        string canonical = string.Join('\n',
            "wake-task/1.0",
            OccurrenceId.ToString(),
            ConfigurationRevisionId.ToString("D"),
            DueAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DueLocal.ToString("O", CultureInfo.InvariantCulture),
            TimeZoneId,
            RunnerExecutablePath,
            RunnerWorkingDirectory,
            Bootstrap.DataDirectory,
            Bootstrap.AgentDirectory,
            Bootstrap.ManifestDirectory,
            Bootstrap.DatabaseFileName,
            ((long)ExecutionTimeout.TotalSeconds).ToString(CultureInfo.InvariantCulture),
            CurrentUserId,
            UserSessionPolicy.ToString(),
            StartWhenAvailable.ToString(CultureInfo.InvariantCulture),
            AllowStartOnBatteries.ToString(CultureInfo.InvariantCulture),
            StopIfGoingOnBatteries.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

/// <summary>The observed state of the managed task.</summary>
public sealed record WakeTaskState(
    bool Exists,
    bool IsManaged,
    OccurrenceId? OccurrenceId,
    DateTimeOffset? DueAtUtc,
    string? Fingerprint,
    WakeTaskError? Error)
{
    /// <summary>Gets the healthy absent state.</summary>
    public static WakeTaskState Absent { get; } = new(false, false, null, null, null, null);
}

/// <summary>Windows Task Scheduler bridge kept behind the Application boundary.</summary>
public interface IWakeTaskSchedulerBridge
{
    /// <summary>Queries the one managed task.</summary>
    Task<WakeTaskState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates, replaces, or removes the managed task to match the request.</summary>
    Task ReconcileAsync(WakeTaskRequest? nextWake, CancellationToken cancellationToken = default);

    /// <summary>Idempotently removes the managed task.</summary>
    Task RemoveAsync(CancellationToken cancellationToken = default);
}

/// <summary>Options required to create safe task requests and fence reconciliation.</summary>
public sealed record WakeTaskReconciliationOptions(
    string ApplicationRoot,
    string RunnerExecutablePath,
    string RunnerWorkingDirectory,
    WakeRunnerBootstrap Bootstrap,
    string CurrentUserId,
    string DiagnosticTimeZoneId,
    TimeSpan LeaseDuration,
    TimeSpan MaximumStaleness,
    TimeSpan TaskTimeoutPadding,
    bool StartWhenAvailable,
    bool AllowStartOnBatteries,
    bool StopIfGoingOnBatteries)
{
    /// <summary>Validates bounded paths, identifiers, and timing policy before side effects.</summary>
    public void Validate()
    {
        string root = NormalizeDirectory(ApplicationRoot, nameof(ApplicationRoot));
        string runner = NormalizeFile(RunnerExecutablePath, nameof(RunnerExecutablePath));
        _ = NormalizeDirectory(RunnerWorkingDirectory, nameof(RunnerWorkingDirectory));
        _ = NormalizeDirectory(Bootstrap.DataDirectory, nameof(Bootstrap.DataDirectory));
        _ = NormalizeDirectory(Bootstrap.AgentDirectory, nameof(Bootstrap.AgentDirectory));
        _ = NormalizeDirectory(Bootstrap.ManifestDirectory, nameof(Bootstrap.ManifestDirectory));
        if (!IsContained(root, runner)
            || string.IsNullOrWhiteSpace(Bootstrap.DatabaseFileName)
            || Bootstrap.DatabaseFileName != Path.GetFileName(Bootstrap.DatabaseFileName)
            || Bootstrap.DatabaseFileName.Length > 128
            || string.IsNullOrWhiteSpace(CurrentUserId)
            || CurrentUserId.Length > 256
            || CurrentUserId.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(DiagnosticTimeZoneId)
            || DiagnosticTimeZoneId.Length > 128
            || LeaseDuration < TimeSpan.FromSeconds(5)
            || LeaseDuration > TimeSpan.FromMinutes(5)
            || MaximumStaleness < TimeSpan.Zero
            || MaximumStaleness > TimeSpan.FromDays(31)
            || TaskTimeoutPadding < TimeSpan.Zero
            || TaskTimeoutPadding > TimeSpan.FromHours(1))
        {
            throw new ArgumentException("Wake-task reconciliation options are invalid.");
        }
    }

    private static string NormalizeDirectory(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A wake-task directory is required.", name);
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }

    private static string NormalizeFile(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A wake-task file is required.", name);
        }

        return Path.GetFullPath(value);
    }

    private static bool IsContained(string root, string candidate) =>
        candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The observable result of one lease-fenced reconciliation.</summary>
public sealed record WakeTaskReconciliationResult(
    bool LeaseAcquired,
    bool Changed,
    bool Removed,
    OccurrenceId? RegisteredOccurrenceId,
    DateTimeOffset? RegisteredDueAtUtc,
    string? Fingerprint,
    WakeTaskError? Error);

/// <summary>Reconciles the managed task to the earliest durable wake occurrence.</summary>
public interface IWakeTaskReconciler
{
    /// <summary>Acquires the wake lease and reconciles one task.</summary>
    ValueTask<WakeTaskReconciliationResult> ReconcileAsync(
        string ownerId,
        CancellationToken cancellationToken = default);
}

/// <summary>A request to prepare a harmless future wake test.</summary>
public sealed record WakeTestRequest(
    int MinutesInFuture,
    string ActorId,
    Guid CorrelationId);

/// <summary>The prepared diagnostic occurrence and managed-task state.</summary>
public sealed record WakeTestPreparationResult(
    OccurrenceId OccurrenceId,
    DateTimeOffset ExpectedWakeAtUtc,
    WakeTaskReconciliationResult Reconciliation);

/// <summary>Persisted wake-test timing derived from occurrence and run state.</summary>
public sealed record WakeTestResult(
    OccurrenceId OccurrenceId,
    DateTimeOffset ExpectedWakeAtUtc,
    DateTimeOffset? ActualRunnerStartAtUtc,
    TimeSpan? WakeDelay,
    OccurrenceStatus Status,
    WakeTaskState TaskState,
    string ResultCode);

/// <summary>Prepares and reads harmless wake-test occurrences.</summary>
public interface IWakeTestService
{
    /// <summary>Creates a wake-test occurrence and reconciles it into the managed task.</summary>
    ValueTask<WakeTestPreparationResult> PrepareAsync(
        WakeTestRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads expected/actual timing and task state without changing the machine.</summary>
    ValueTask<WakeTestResult> GetResultAsync(
        OccurrenceId occurrenceId,
        CancellationToken cancellationToken = default);
}
