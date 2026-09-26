using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Application.Management;

/// <summary>A stable bounded page returned by local management queries.</summary>
public sealed record ManagementPage<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount)
{
    /// <summary>Gets the total number of pages.</summary>
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

/// <summary>One installed-agent row with current operational context.</summary>
public sealed record ManagementAgentItem(
    AgentDefinitionRecord Definition,
    long? ConfigurationRevision,
    string? ConfigurationHash,
    DateTimeOffset? NextOccurrenceAtUtc,
    OccurrenceStatus? NextOccurrenceStatus,
    AgentRunId? ActiveRunId,
    AgentRunStatus? ActiveRunStatus,
    AgentRunId? LastRunId,
    AgentRunStatus? LastRunStatus,
    DateTimeOffset? LastRunAtUtc,
    string? AttentionReason);

/// <summary>Complete bounded agent-detail projection.</summary>
public sealed record ManagementAgentDetail(
    ManagementAgentItem Agent,
    AgentConfigurationRecord? Configuration,
    IReadOnlyList<ConfigurationRevisionRecord> ConfigurationHistory,
    IReadOnlyList<AgentScheduleRecord> Schedules,
    IReadOnlyList<AgentRunRecord> RecentRuns);

/// <summary>One schedule row with latest durable occurrence/run context.</summary>
public sealed record ManagementScheduleItem(
    AgentScheduleRecord Schedule,
    string AgentDisplayName,
    DateTimeOffset? NextDueAtUtc,
    OccurrenceStatus? NextStatus,
    AgentRunStatus? LastRunStatus,
    DateTimeOffset? LastRunAtUtc);

/// <summary>One schedule and bounded future/history state.</summary>
public sealed record ManagementScheduleDetail(
    ManagementScheduleItem Schedule,
    IReadOnlyList<ScheduleOccurrenceRecord> FutureOccurrences,
    IReadOnlyList<AgentRunRecord> RecentRuns);

/// <summary>Server-side filters for run history.</summary>
public sealed record ManagementRunQuery(
    string? AgentId,
    AgentRunStatus? Status,
    TriggerType? Trigger,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ThroughUtc,
    bool AttentionOnly,
    bool ActiveOnly,
    int PageNumber = 1,
    int PageSize = 25);

/// <summary>A safe run-list row.</summary>
public sealed record ManagementRunItem(
    AgentRunRecord Run,
    string AgentDisplayName,
    string CommandName,
    DateTimeOffset DueAtUtc,
    int AttemptNumber,
    Guid? ScheduleId,
    OccurrenceStatus OccurrenceStatus);

/// <summary>Bounded run detail with encoded/redacted child records.</summary>
public sealed record ManagementRunDetail(
    ManagementRunItem Run,
    ScheduleOccurrenceRecord Occurrence,
    ConfigurationRevisionRecord? ConfigurationRevision,
    IReadOnlyList<AgentRunEventRecord> Events,
    IReadOnlyList<AgentRunMetricRecord> Metrics,
    IReadOnlyList<RunArtifactRecord> Artifacts,
    IReadOnlyList<AuditEventRecord> AuditEvents);

/// <summary>Server-side filters for append-only audit history.</summary>
public sealed record ManagementAuditQuery(
    DateTimeOffset? FromUtc,
    DateTimeOffset? ThroughUtc,
    string? Actor,
    string? Action,
    string? TargetType,
    string? Outcome,
    string? AgentId,
    AgentRunId? RunId,
    int PageNumber = 1,
    int PageSize = 25);

/// <summary>Safe details about the central database and local storage.</summary>
public sealed record ManagementDatabaseStatus(
    string DatabaseFileName,
    string Migration,
    string JournalMode,
    bool ForeignKeysEnabled,
    int BusyTimeoutMilliseconds,
    long DatabaseSizeBytes,
    long AvailableDiskBytes);

/// <summary>Read-only bounded query surface used by the local management UI.</summary>
public interface IManagementQueryService
{
    /// <summary>Gets all installed agents with bounded operational context.</summary>
    ValueTask<IReadOnlyList<ManagementAgentItem>> GetAgentsAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one installed agent with bounded related data.</summary>
    ValueTask<ManagementAgentDetail?> GetAgentAsync(AgentId agentId, CancellationToken cancellationToken = default);

    /// <summary>Gets all schedules with latest context.</summary>
    ValueTask<IReadOnlyList<ManagementScheduleItem>> GetSchedulesAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one schedule with bounded future and run history.</summary>
    ValueTask<ManagementScheduleDetail?> GetScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default);

    /// <summary>Gets a filtered stable page of runs.</summary>
    ValueTask<ManagementPage<ManagementRunItem>> GetRunsAsync(ManagementRunQuery query, CancellationToken cancellationToken = default);

    /// <summary>Gets one run and bounded child rows.</summary>
    ValueTask<ManagementRunDetail?> GetRunAsync(AgentRunId runId, CancellationToken cancellationToken = default);

    /// <summary>Gets a filtered stable page of append-only audit rows.</summary>
    ValueTask<ManagementPage<AuditEventRecord>> GetAuditAsync(ManagementAuditQuery query, CancellationToken cancellationToken = default);

    /// <summary>Gets one append-only audit row.</summary>
    ValueTask<AuditEventRecord?> GetAuditEventAsync(Guid auditId, CancellationToken cancellationToken = default);

    /// <summary>Gets a bounded read-only central database health snapshot.</summary>
    ValueTask<ManagementDatabaseStatus> GetDatabaseStatusAsync(CancellationToken cancellationToken = default);
}
