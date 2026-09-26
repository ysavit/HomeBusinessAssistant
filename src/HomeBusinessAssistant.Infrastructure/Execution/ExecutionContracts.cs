using System.Diagnostics;
using HomeBusinessAssistant.AgentSdk.Manifest;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using Serilog;

namespace HomeBusinessAssistant.Infrastructure.Execution;

/// <summary>Stable process exit codes returned by the central Runner CLI.</summary>
public static class RunnerExitCode
{
    /// <summary>The requested operation completed successfully.</summary>
    public const int Success = 0;
    /// <summary>The Runner command line was invalid.</summary>
    public const int InvalidArguments = 2;
    /// <summary>The occurrence was absent, not runnable, or already owned.</summary>
    public const int NotRunnable = 3;
    /// <summary>Bootstrap, manifest, configuration, or executable validation failed.</summary>
    public const int InvalidExecutionEnvironment = 4;
    /// <summary>The child was cancelled.</summary>
    public const int Cancelled = 20;
    /// <summary>The child exceeded its timeout.</summary>
    public const int TimedOut = 21;
    /// <summary>The child or its protocol failed.</summary>
    public const int ExecutionFailed = 30;
    /// <summary>Recovery or an unexpected Runner operation failed.</summary>
    public const int RunnerFailure = 70;
}

/// <summary>Validated bootstrap paths used by one Runner invocation.</summary>
public sealed record RunnerBootstrapSettings(
    string DataDirectory,
    string AgentDirectory,
    string ManifestDirectory,
    string DatabaseFileName = "assistant.db",
    TimeSpan? BusyTimeout = null);

/// <summary>Bounded supervision, ingestion, and recovery policy.</summary>
public sealed record RunnerSupervisionOptions(
    TimeSpan ClaimDuration,
    TimeSpan ExecutionLeaseDuration,
    TimeSpan HeartbeatInterval,
    TimeSpan CancellationPollInterval,
    TimeSpan TerminationGracePeriod,
    TimeSpan ForcedExitWait,
    TimeSpan StaleRunThreshold,
    TimeSpan StaleTemporaryFileAge,
    int MaximumProtocolViolations,
    int MaximumStderrEvents,
    int MaximumRecoveryResults,
    long MaximumAgentArtifactBytes)
{
    /// <summary>Gets conservative defaults for one local Windows workstation.</summary>
    public static RunnerSupervisionOptions Default { get; } = new(
        ClaimDuration: TimeSpan.FromMinutes(1),
        ExecutionLeaseDuration: TimeSpan.FromSeconds(30),
        HeartbeatInterval: TimeSpan.FromSeconds(5),
        CancellationPollInterval: TimeSpan.FromSeconds(1),
        TerminationGracePeriod: TimeSpan.FromSeconds(3),
        ForcedExitWait: TimeSpan.FromSeconds(10),
        StaleRunThreshold: TimeSpan.FromMinutes(2),
        StaleTemporaryFileAge: TimeSpan.FromHours(24),
        MaximumProtocolViolations: 5,
        MaximumStderrEvents: 100,
        MaximumRecoveryResults: 100,
        MaximumAgentArtifactBytes: 64L * 1024 * 1024);

    /// <summary>Rejects unsafe or unbounded supervision policy.</summary>
    public void Validate()
    {
        if (ClaimDuration < TimeSpan.FromSeconds(5) || ClaimDuration > TimeSpan.FromHours(1)
            || ExecutionLeaseDuration < TimeSpan.FromSeconds(5) || ExecutionLeaseDuration > TimeSpan.FromHours(24)
            || HeartbeatInterval < TimeSpan.FromMilliseconds(100) || HeartbeatInterval > TimeSpan.FromMinutes(1)
            || HeartbeatInterval >= ExecutionLeaseDuration
            || CancellationPollInterval < TimeSpan.FromMilliseconds(100) || CancellationPollInterval > TimeSpan.FromMinutes(1)
            || TerminationGracePeriod < TimeSpan.Zero || TerminationGracePeriod > TimeSpan.FromMinutes(5)
            || ForcedExitWait < TimeSpan.FromSeconds(1) || ForcedExitWait > TimeSpan.FromMinutes(5)
            || StaleRunThreshold < TimeSpan.FromSeconds(5) || StaleRunThreshold > TimeSpan.FromDays(1)
            || StaleTemporaryFileAge < TimeSpan.FromMinutes(1) || StaleTemporaryFileAge > TimeSpan.FromDays(30)
            || MaximumProtocolViolations is < 1 or > 100
            || MaximumStderrEvents is < 1 or > 10_000
            || MaximumRecoveryResults is < 1 or > 1_000
            || MaximumAgentArtifactBytes is < 1 or > (1024L * 1024 * 1024))
        {
            throw new ArgumentOutOfRangeException(nameof(RunnerSupervisionOptions), "Runner supervision options are outside supported bounds.");
        }
    }
}

/// <summary>The validated executable, manifest, and integrity snapshot used for launch.</summary>
public sealed record ValidatedAgentExecutable(
    AgentManifest Manifest,
    string ManifestPath,
    string WorkingDirectory,
    string ExecutablePath,
    string ExecutableSha256,
    long ExecutableSizeBytes,
    DateTimeOffset ExecutableLastWriteAtUtc);

/// <summary>Validates an installed agent's on-disk process boundary.</summary>
public interface IExecutableIntegrityService
{
    /// <summary>Resolves, validates, and hashes the exact executable and manifest.</summary>
    ValueTask<ValidatedAgentExecutable> ValidateAsync(
        AgentDefinitionRecord definition,
        string commandName,
        CancellationToken cancellationToken = default);

    /// <summary>Fails when the executable changed after its integrity snapshot.</summary>
    ValueTask EnsureUnchangedAsync(
        ValidatedAgentExecutable executable,
        CancellationToken cancellationToken = default);
}

/// <summary>The complete shell-free child-process invocation.</summary>
public sealed record AgentProcessStartRequest(
    ValidatedAgentExecutable Executable,
    AgentRunId RunId,
    OccurrenceId OccurrenceId,
    AgentId AgentId,
    string CommandName,
    string ConfigurationFile,
    string AgentDataDirectory,
    string ArtifactDirectory);

/// <summary>Owns one launched child and its redirected raw streams.</summary>
public sealed class AgentProcessHandle(Process process, DateTimeOffset processStartedAtUtc) : IAsyncDisposable
{
    /// <summary>Gets the underlying process for bounded supervision.</summary>
    public Process Process { get; } = process;

    /// <summary>Gets the process start identity captured from Windows.</summary>
    public DateTimeOffset ProcessStartedAtUtc { get; } = processStartedAtUtc;

    /// <summary>Gets raw standard output for strict UTF-8 line framing.</summary>
    public Stream StandardOutput => Process.StandardOutput.BaseStream;

    /// <summary>Gets raw standard error for bounded diagnostics.</summary>
    public Stream StandardError => Process.StandardError.BaseStream;

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Process.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Launches agents without a shell.</summary>
public interface IAgentProcessLauncher
{
    /// <summary>Starts one redirected child using argument-list separation.</summary>
    ValueTask<AgentProcessHandle> LaunchAsync(
        AgentProcessStartRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>The bounded trusted information collected from stdout.</summary>
public sealed record AgentProtocolIngestionResult(
    int AcceptedEvents,
    int ProtocolViolations,
    bool FatalProtocolIssue,
    string? FatalReasonCode,
    string? SummaryText,
    string? SummaryJson,
    AgentRunStatus? ReportedSummaryStatus,
    int? ReportedCompletedExitCode,
    bool CompletedEventSeen);

/// <summary>The bounded diagnostic result collected from stderr.</summary>
public sealed record StderrIngestionResult(
    int LinesObserved,
    int EventsPersisted,
    int LinesSuppressed);

/// <summary>Reads and persists redirected agent output.</summary>
public interface IAgentProtocolReader
{
    /// <summary>Consumes stdout as bounded protocol 1.x records.</summary>
    Task<AgentProtocolIngestionResult> ReadStandardOutputAsync(
        Stream output,
        AgentRunId runId,
        AgentId agentId,
        string artifactDirectory,
        RunnerEventSequence runnerSequences,
        ILogger logger,
        CancellationToken cancellationToken = default);

    /// <summary>Consumes stderr as redacted, bounded diagnostics and rolling logs.</summary>
    Task<StderrIngestionResult> ReadStandardErrorAsync(
        Stream errorStream,
        AgentRunId runId,
        RunnerEventSequence runnerSequences,
        ILogger logger,
        CancellationToken cancellationToken = default);
}

/// <summary>Allocates the reserved negative sequence space for concurrent runner diagnostics.</summary>
public sealed class RunnerEventSequence
{
    private long current;

    /// <summary>Returns the next negative sequence value.</summary>
    public long Next() => Interlocked.Decrement(ref current);
}

/// <summary>The authoritative child lifecycle observed by the supervisor.</summary>
public sealed record ProcessSupervisionResult(
    int? ExitCode,
    bool TimedOut,
    bool CancellationRequested,
    bool LeaseLost,
    bool ProcessTreeKilled,
    string? FailureReason);

/// <summary>Monitors persisted cancellation, runner heartbeats, timeout, and lease renewal.</summary>
public interface IRunCancellationMonitor
{
    /// <summary>Supervises one already-running child until exit or enforced termination.</summary>
    Task<ProcessSupervisionResult> SuperviseAsync(
        AgentProcessHandle process,
        AgentRunId runId,
        OccurrenceId occurrenceId,
        TimeSpan timeout,
        AgentLeaseRecord? executionLease,
        ILogger logger,
        CancellationToken cancellationToken = default);
}

/// <summary>Inputs required for authoritative durable finalization.</summary>
public sealed record RunFinalizationRequest(
    AgentRunId RunId,
    ProcessSupervisionResult Supervision,
    AgentProtocolIngestionResult Protocol,
    string? RunnerFailureType,
    string? RunnerFailureMessage,
    AgentRunStatus? ForcedStatus = null);

/// <summary>The durable result of idempotent finalization.</summary>
public sealed record RunFinalizationResult(
    AgentRunRecord Run,
    bool FixedDelayApplicable,
    OccurrenceId? NextFixedDelayOccurrenceId,
    OccurrenceId? RetryOccurrenceId);

/// <summary>Finalizes run/occurrence state and downstream durable scheduling hooks.</summary>
public interface IRunFinalizer
{
    /// <summary>Applies authoritative precedence and idempotently finalizes one run.</summary>
    ValueTask<RunFinalizationResult> FinalizeAsync(
        RunFinalizationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>The result returned by one durable occurrence execution attempt.</summary>
public sealed record OccurrenceExecutionResult(
    int RunnerExitCode,
    string OutcomeCode,
    OccurrenceId OccurrenceId,
    AgentRunId? RunId,
    AgentRunStatus? RunStatus,
    int? AgentExitCode,
    string Message);

/// <summary>The single authoritative durable occurrence execution use case.</summary>
public interface IOccurrenceExecutor
{
    /// <summary>Claims and executes one existing occurrence.</summary>
    ValueTask<OccurrenceExecutionResult> ExecuteAsync(
        OccurrenceId occurrenceId,
        CancellationToken cancellationToken = default);
}

/// <summary>A bounded recovery summary.</summary>
public sealed record StaleRunRecoveryResult(
    int StaleClaimsAbandoned,
    int StaleRunsAbandoned,
    int LiveMatchingProcessesLeftRunning,
    int TemporaryFilesDeleted,
    int Errors);

/// <summary>Recovers stale durable execution state without killing ambiguous PIDs.</summary>
public interface IStaleRunRecoveryService
{
    /// <summary>Audits and repairs one bounded page of stale state.</summary>
    ValueTask<StaleRunRecoveryResult> RecoverAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates and cleans runner-owned temporary configuration and artifact staging paths.</summary>
public interface IRunTemporaryFileManager
{
    /// <summary>Writes immutable configuration and occurrence arguments to a per-run private input file.</summary>
    ValueTask<string> WriteExecutionInputAsync(
        AgentRunId runId,
        string canonicalConfigurationJson,
        string occurrenceArgumentsJson,
        CancellationToken cancellationToken = default);
    /// <summary>Writes immutable input plus short-lived resolved secret values to the same private file.</summary>
    ValueTask<string> WriteExecutionInputAsync(
        AgentRunId runId,
        string canonicalConfigurationJson,
        string occurrenceArgumentsJson,
        IReadOnlyDictionary<string, string> resolvedSecrets,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a clean agent-writable artifact staging directory for one run.</summary>
    string CreateArtifactDirectory(AgentRunId runId);

    /// <summary>Returns the root-confined agent data directory.</summary>
    string GetAgentDataDirectory(AgentId agentId);

    /// <summary>Deletes one run's temporary config and staging directory.</summary>
    void CleanupRun(AgentRunId runId);

    /// <summary>Deletes stale Runner-owned temporary files and directories.</summary>
    int CleanupStale(DateTimeOffset olderThanUtc);
}
