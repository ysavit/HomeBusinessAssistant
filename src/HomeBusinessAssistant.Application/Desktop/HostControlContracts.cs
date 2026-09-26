using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Application.Desktop;

/// <summary>The bounded result of starting the central Runner process.</summary>
public sealed record RunnerProcessLaunchResult(bool Started, int? ProcessId, string Code);

/// <summary>Starts the central Runner for one already-durable occurrence.</summary>
public interface IRunnerProcessLauncher
{
    /// <summary>Starts <c>Runner execute</c> without awaiting the agent's completion.</summary>
    ValueTask<RunnerProcessLaunchResult> LaunchAsync(
        OccurrenceId occurrenceId,
        CancellationToken cancellationToken = default);
}

/// <summary>The result of dispatching one occurrence to Runner.</summary>
public sealed record OccurrenceDispatchResult(
    OccurrenceId OccurrenceId,
    bool Created,
    bool Queued,
    bool RunnerStarted,
    int? RunnerProcessId,
    string Code);

/// <summary>Creates a durable tray/UI occurrence and dispatches eligible work to Runner.</summary>
public interface IManualAgentRunLauncher
{
    /// <summary>Creates one manual occurrence using normal concurrency policy, then starts Runner when ready.</summary>
    ValueTask<OccurrenceDispatchResult> CreateAndLaunchAsync(
        ManualAgentLaunchRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Dispatches an existing due occurrence through the central Runner.</summary>
public interface IOccurrenceRunnerDispatcher
{
    /// <summary>Starts Runner when the occurrence remains ready, or reports its current durable state.</summary>
    ValueTask<OccurrenceDispatchResult> DispatchAsync(
        OccurrenceId occurrenceId,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default);
}

/// <summary>A tray or local-UI request for one installed agent command.</summary>
public sealed record ManualAgentLaunchRequest(
    AgentId AgentId,
    string CommandName,
    string ArgumentsJson,
    TriggerType TriggerType,
    ConcurrencyPolicy ConcurrencyPolicy,
    string ActorId,
    Guid CorrelationId);

/// <summary>Opens only the validated local Home Business Assistant dashboard.</summary>
public interface ILocalDashboardLauncher
{
    /// <summary>Opens the configured loopback dashboard with the default Windows shell.</summary>
    ValueTask<bool> OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a validated application-relative route on the configured loopback origin.</summary>
    ValueTask<bool> OpenPathAsync(string localPath, CancellationToken cancellationToken = default) => OpenAsync(cancellationToken);
}

/// <summary>The persisted global schedule-pause state.</summary>
public sealed record GlobalSchedulePauseState(bool IsPaused, IReadOnlyList<Guid> ChangedScheduleIds);

/// <summary>Applies an audited global pause without clearing unrelated per-schedule pauses.</summary>
public interface IGlobalScheduleControlService
{
    /// <summary>Reads the persisted global pause state.</summary>
    ValueTask<GlobalSchedulePauseState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Indefinitely pauses every currently active enabled schedule.</summary>
    ValueTask<GlobalSchedulePauseState> PauseAllAsync(
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>Resumes only schedules changed by the preceding global pause.</summary>
    ValueTask<GlobalSchedulePauseState> ResumeAllAsync(
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default);
}

/// <summary>A safe installed-agent item for the local dashboard and tray.</summary>
public sealed record HostAgentStatus(
    AgentId AgentId,
    string DisplayName,
    string Description,
    bool Enabled,
    bool SupportsManualRun,
    bool HasConfiguration);

/// <summary>A safe pending occurrence item for the local dashboard and tray.</summary>
public sealed record HostOccurrenceStatus(
    OccurrenceId OccurrenceId,
    AgentId AgentId,
    string AgentDisplayName,
    string CommandName,
    DateTimeOffset DueAtUtc,
    OccurrenceStatus Status,
    bool RequiresWake);

/// <summary>A safe run item for the local dashboard.</summary>
public sealed record HostRunStatus(
    AgentRunId RunId,
    AgentId AgentId,
    string AgentDisplayName,
    AgentRunStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? SummaryText,
    string? ErrorType);

/// <summary>One bounded read-only snapshot of central platform state.</summary>
public sealed record HostPlatformStatus(
    IReadOnlyList<HostAgentStatus> Agents,
    IReadOnlyList<HostOccurrenceStatus> NextOccurrences,
    IReadOnlyList<HostRunStatus> ActiveRuns,
    IReadOnlyList<HostRunStatus> RecentFailures,
    HostRunStatus? LatestSummary);

/// <summary>Reads bounded dashboard/notification data from central persistence.</summary>
public interface IHostPlatformStatusReader
{
    /// <summary>Reads current state without exposing configuration or diagnostic payloads.</summary>
    ValueTask<HostPlatformStatus> ReadAsync(
        int maximumItems,
        CancellationToken cancellationToken = default);
}
