using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Application.Persistence;

/// <summary>Persists and queries installed-agent definitions.</summary>
public interface IAgentDefinitionRepository
{
    /// <summary>Gets one agent definition, or null when it is not installed.</summary>
    ValueTask<AgentDefinitionRecord?> GetAsync(AgentId agentId, CancellationToken cancellationToken = default);

    /// <summary>Gets all installed definition records in stable identifier order.</summary>
    ValueTask<IReadOnlyList<AgentDefinitionRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets enabled definitions in stable identifier order.</summary>
    ValueTask<IReadOnlyList<AgentDefinitionRecord>> GetEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates or updates manifest-owned fields without overwriting the enabled state.</summary>
    ValueTask<AgentDefinitionRecord> UpsertManifestAsync(AgentDefinitionRecord definition, CancellationToken cancellationToken = default);

    /// <summary>Updates whether an installed agent may be scheduled or run.</summary>
    ValueTask<bool> SetEnabledAsync(AgentId agentId, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>Updates enabled state for a system reconciliation; the caller owns its specific audit record.</summary>
    ValueTask<bool> SetEnabledBySystemAsync(AgentId agentId, bool enabled, CancellationToken cancellationToken = default);
}

/// <summary>Validates and atomically versions mutable agent configuration.</summary>
public interface IAgentConfigurationService
{
    /// <summary>Gets the current immutable revision for an agent.</summary>
    ValueTask<AgentConfigurationRecord?> GetCurrentAsync(AgentId agentId, CancellationToken cancellationToken = default);

    /// <summary>Gets revision history in descending revision order.</summary>
    ValueTask<IReadOnlyList<ConfigurationRevisionRecord>> GetHistoryAsync(AgentId agentId, CancellationToken cancellationToken = default);

    /// <summary>Gets one immutable revision by identifier.</summary>
    ValueTask<ConfigurationRevisionRecord?> GetRevisionAsync(Guid revisionId, CancellationToken cancellationToken = default);

    /// <summary>Validates, canonicalizes, saves, and atomically promotes a revision.</summary>
    ValueTask<SaveConfigurationResult> SaveAsync(SaveConfigurationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Persists schedule definitions without calculating future occurrences.</summary>
public interface IScheduleRepository
{
    /// <summary>Gets one schedule by identifier.</summary>
    ValueTask<AgentScheduleRecord?> GetAsync(Guid scheduleId, CancellationToken cancellationToken = default);

    /// <summary>Gets enabled schedules, including paused schedules, in stable identifier order.</summary>
    ValueTask<IReadOnlyList<AgentScheduleRecord>> GetEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets enabled schedules whose pause has elapsed in stable identifier order.</summary>
    ValueTask<IReadOnlyList<AgentScheduleRecord>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a schedule with the same agent and name, excluding an optional schedule.</summary>
    ValueTask<AgentScheduleRecord?> GetByAgentAndNameAsync(
        AgentId agentId,
        string name,
        Guid? excludingScheduleId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces a schedule using optimistic concurrency.</summary>
    ValueTask<AgentScheduleRecord> SaveAsync(AgentScheduleRecord schedule, long? expectedConcurrencyToken, CancellationToken cancellationToken = default);
}

/// <summary>Persists durable occurrences and provides atomic claim operations.</summary>
public interface IOccurrenceRepository
{
    /// <summary>Creates an occurrence or returns the existing schedule/due/attempt identity.</summary>
    ValueTask<ScheduleOccurrenceRecord> CreateIfAbsentAsync(CreateOccurrenceRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gets one occurrence by identifier.</summary>
    ValueTask<ScheduleOccurrenceRecord?> GetAsync(OccurrenceId occurrenceId, CancellationToken cancellationToken = default);

    /// <summary>Gets a bounded chronological slice for one schedule.</summary>
    ValueTask<IReadOnlyList<ScheduleOccurrenceRecord>> GetForScheduleAsync(
        Guid scheduleId,
        DateTimeOffset? dueFromUtc,
        DateTimeOffset? dueThroughUtc,
        int maximumResults,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the chronologically latest occurrence for one schedule.</summary>
    ValueTask<ScheduleOccurrenceRecord?> GetLatestForScheduleAsync(
        Guid scheduleId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets bounded active work for one agent and optional schedule.</summary>
    ValueTask<IReadOnlyList<ScheduleOccurrenceRecord>> GetActiveAsync(
        AgentId agentId,
        Guid? scheduleId,
        int maximumResults,
        CancellationToken cancellationToken = default);

    /// <summary>Gets bounded planned or ready work for one agent and optional schedule.</summary>
    ValueTask<IReadOnlyList<ScheduleOccurrenceRecord>> GetPendingAsync(
        AgentId agentId,
        Guid? scheduleId,
        int maximumResults,
        CancellationToken cancellationToken = default);

    /// <summary>Gets ready due work in stable due/identifier order.</summary>
    ValueTask<IReadOnlyList<ScheduleOccurrenceRecord>> GetDueAsync(
        DateTimeOffset nowUtc,
        int maximumResults,
        CancellationToken cancellationToken = default);

    /// <summary>Gets expired claimed occurrences that have no linked run.</summary>
    ValueTask<IReadOnlyList<ScheduleOccurrenceRecord>> GetStaleClaimsAsync(
        DateTimeOffset nowUtc,
        int maximumResults,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the earliest enabled, effectively unpaused occurrence requiring wake.</summary>
    ValueTask<ScheduleOccurrenceRecord?> GetEarliestWakeAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically claims one pending occurrence for a bounded duration.</summary>
    ValueTask<ScheduleOccurrenceRecord?> TryClaimAsync(OccurrenceId occurrenceId, string ownerId, DateTimeOffset nowUtc, TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>Applies one legal occurrence transition atomically.</summary>
    ValueTask<ScheduleOccurrenceRecord?> TryTransitionAsync(
        OccurrenceTransitionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Records a bounded cancellation request without pretending the occurrence is terminal.</summary>
    ValueTask<bool> RequestCancellationAsync(OccurrenceId occurrenceId, DateTimeOffset requestedAtUtc, string reason, CancellationToken cancellationToken = default);
}

/// <summary>Persists runs and their append-only events and metrics.</summary>
public interface IAgentRunRepository
{
    /// <summary>Creates one run and links its claimed occurrence.</summary>
    ValueTask<AgentRunRecord> CreateAsync(AgentRunRecord run, CancellationToken cancellationToken = default);

    /// <summary>Gets one run by identifier.</summary>
    ValueTask<AgentRunRecord?> GetAsync(AgentRunId runId, CancellationToken cancellationToken = default);

    /// <summary>Gets the run linked to one occurrence, or null when execution has not initialized.</summary>
    ValueTask<AgentRunRecord?> GetForOccurrenceAsync(OccurrenceId occurrenceId, CancellationToken cancellationToken = default);

    /// <summary>Gets a bounded oldest-heartbeat-first set of stale starting or running runs.</summary>
    ValueTask<IReadOnlyList<AgentRunRecord>> GetStaleAsync(
        StaleRunQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Atomically records the launched child identity and moves the run and occurrence to Running.</summary>
    ValueTask<AgentRunRecord?> MarkRunningAsync(
        AgentRunId runId,
        int processId,
        DateTimeOffset processStartedAtUtc,
        DateTimeOffset heartbeatAtUtc,
        long expectedConcurrencyToken,
        CancellationToken cancellationToken = default);

    /// <summary>Updates the runner-owned liveness timestamp for one active run.</summary>
    ValueTask<AgentRunRecord?> UpdateHeartbeatAsync(
        AgentRunId runId,
        DateTimeOffset heartbeatAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Appends one uniquely sequenced run event.</summary>
    ValueTask AppendEventAsync(AgentRunEventRecord runEvent, CancellationToken cancellationToken = default);

    /// <summary>Appends one run metric.</summary>
    ValueTask AppendMetricAsync(AgentRunMetricRecord metric, CancellationToken cancellationToken = default);

    /// <summary>Atomically and idempotently marks a run and its occurrence terminal.</summary>
    ValueTask<AgentRunRecord?> MarkTerminalAsync(
        AgentRunId runId,
        AgentRunStatus terminalStatus,
        DateTimeOffset completedAtUtc,
        int? exitCode,
        string? summaryText,
        string? summaryJson,
        string? errorType,
        string? errorMessage,
        long expectedConcurrencyToken,
        CancellationToken cancellationToken = default);
}

/// <summary>Appends immutable, bounded, redacted audit events.</summary>
public interface IAuditWriter
{
    /// <summary>Redacts and writes one audit event.</summary>
    ValueTask<AuditEventRecord> WriteAsync(WriteAuditEventRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Stores artifact bytes outside SQLite and persists their integrity metadata.</summary>
public interface IArtifactStore
{
    /// <summary>Atomically writes and registers an artifact.</summary>
    ValueTask<RunArtifactRecord> WriteAsync(ArtifactWriteRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gets artifact metadata by identifier.</summary>
    ValueTask<RunArtifactRecord?> GetMetadataAsync(Guid artifactId, CancellationToken cancellationToken = default);

    /// <summary>Opens a read-only artifact stream after root-containment checks.</summary>
    ValueTask<Stream> OpenReadAsync(Guid artifactId, CancellationToken cancellationToken = default);
}

/// <summary>Coordinates local exclusive work using expiring database leases.</summary>
public interface ILeaseManager
{
    /// <summary>Acquires an absent or expired lease and returns its fencing token.</summary>
    ValueTask<AgentLeaseRecord?> TryAcquireAsync(string leaseName, string ownerId, DateTimeOffset nowUtc, TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>Renews a lease only when owner and fencing token still match.</summary>
    ValueTask<AgentLeaseRecord?> TryRenewAsync(AgentLeaseRecord lease, DateTimeOffset nowUtc, TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>Releases a lease only when owner and fencing token still match.</summary>
    ValueTask<bool> ReleaseAsync(AgentLeaseRecord lease, CancellationToken cancellationToken = default);
}

/// <summary>Persists JSON-valued system settings with optimistic concurrency.</summary>
public interface ISystemSettingRepository
{
    /// <summary>Gets one setting by key.</summary>
    ValueTask<SystemSettingRecord?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates one canonical JSON setting.</summary>
    ValueTask<SystemSettingRecord> SaveAsync(SystemSettingRecord setting, long? expectedConcurrencyToken, CancellationToken cancellationToken = default);
}
