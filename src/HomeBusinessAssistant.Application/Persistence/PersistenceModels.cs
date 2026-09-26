using System.Text.Json;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;

namespace HomeBusinessAssistant.Application.Persistence;

/// <summary>A persisted installed-agent definition sourced from a versioned manifest.</summary>
public sealed record AgentDefinitionRecord(
    AgentId Id,
    string DisplayName,
    string Description,
    string ManifestVersion,
    string InstalledVersion,
    string ExecutableRelativePath,
    string WorkingDirectoryRelativePath,
    string CapabilitiesJson,
    string SupportedCommandsJson,
    ConcurrencyPolicy DefaultConcurrencyPolicy,
    bool SupportsScheduling,
    bool SupportsManualRun,
    bool RequiresInteractiveUserSession,
    bool Enabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>An immutable configuration revision.</summary>
public sealed record ConfigurationRevisionRecord(
    Guid Id,
    AgentId AgentId,
    long RevisionNumber,
    string SchemaVersion,
    string CanonicalConfigurationJson,
    string ConfigurationHash,
    string ChangedBy,
    string ChangeSummary,
    DateTimeOffset CreatedAtUtc);

/// <summary>The current configuration pointer and its immutable revision.</summary>
public sealed record AgentConfigurationRecord(
    Guid Id,
    AgentId AgentId,
    Guid CurrentRevisionId,
    string SchemaVersion,
    ConfigurationRevisionRecord CurrentRevision,
    DateTimeOffset UpdatedAtUtc,
    long ConcurrencyToken);

/// <summary>A request to validate, persist, and promote an agent configuration.</summary>
public sealed record SaveConfigurationRequest(
    AgentId AgentId,
    string SchemaVersion,
    JsonElement Configuration,
    string ChangedBy,
    string ChangeSummary,
    Guid CorrelationId,
    long? ExpectedCurrentRevisionNumber = null,
    string? ExpectedCurrentHash = null);

/// <summary>The result of a configuration save or idempotent promotion.</summary>
public sealed record SaveConfigurationResult(
    ConfigurationRevisionRecord Revision,
    bool Created,
    bool CurrentChanged);

/// <summary>A durable schedule definition; recurrence behavior is implemented in Stage 03.</summary>
public sealed record AgentScheduleRecord(
    Guid Id,
    AgentId AgentId,
    string Name,
    string CommandName,
    string ArgumentsJson,
    ScheduleKind Kind,
    string DefinitionJson,
    string TimeZoneId,
    MisfirePolicy MisfirePolicy,
    ConcurrencyPolicy ConcurrencyPolicy,
    TimeSpan Timeout,
    TimeSpan MisfireGracePeriod,
    string RetryPolicyJson,
    WakePolicy WakePolicy,
    Guid? PinnedConfigurationRevisionId,
    bool AllowDisabledAgent,
    bool IsEnabled,
    bool IsPaused,
    DateTimeOffset? PausedUntilUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long ConcurrencyToken);

/// <summary>A durable scheduled, manual, or retry occurrence.</summary>
public sealed record ScheduleOccurrenceRecord(
    OccurrenceId Id,
    Guid? ScheduleId,
    AgentId AgentId,
    string CommandName,
    string ArgumentsJson,
    Guid ConfigurationRevisionId,
    DateTimeOffset DueAtUtc,
    TriggerType TriggerType,
    OccurrenceStatus Status,
    bool RequiresWake,
    bool KeepSystemAwake,
    bool KeepDisplayOn,
    int AttemptNumber,
    OccurrenceId? ParentOccurrenceId,
    string? ClaimedBy,
    DateTimeOffset? ClaimedAtUtc,
    DateTimeOffset? ClaimExpiresAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? CancellationRequestedAtUtc,
    string? CancellationReason,
    string? TerminalReasonCode,
    string? TerminalMessage,
    AgentRunId? RunId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool AllowDisabledAgent = false);

/// <summary>The immutable inputs required to create an occurrence.</summary>
public sealed record CreateOccurrenceRequest(
    OccurrenceId Id,
    Guid? ScheduleId,
    AgentId AgentId,
    string CommandName,
    string ArgumentsJson,
    Guid ConfigurationRevisionId,
    DateTimeOffset DueAtUtc,
    TriggerType TriggerType,
    int AttemptNumber,
    OccurrenceId? ParentOccurrenceId,
    OccurrenceStatus InitialStatus,
    bool RequiresWake = false,
    bool KeepSystemAwake = false,
    bool KeepDisplayOn = false,
    bool AllowDisabledAgent = false);

/// <summary>An atomic legal transition request for a scheduler-owned occurrence.</summary>
public sealed record OccurrenceTransitionRequest(
    OccurrenceId OccurrenceId,
    IReadOnlyCollection<OccurrenceStatus> ExpectedStatuses,
    OccurrenceStatus TargetStatus,
    DateTimeOffset TransitionedAtUtc,
    string? ReasonCode,
    string? Message);

/// <summary>A durable platform run record.</summary>
public sealed record AgentRunRecord(
    AgentRunId Id,
    OccurrenceId OccurrenceId,
    AgentId AgentId,
    Guid ConfigurationRevisionId,
    string ConfigurationHash,
    TriggerType TriggerType,
    AgentRunStatus Status,
    string RunnerVersion,
    string AgentVersion,
    string ManifestVersion,
    string ExecutableHash,
    string MachineName,
    int? ProcessId,
    DateTimeOffset? ProcessStartedAtUtc,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? LastHeartbeatAtUtc,
    DateTimeOffset? CompletedAtUtc,
    long? DurationMilliseconds,
    int? ExitCode,
    string? SummaryText,
    string? SummaryJson,
    string? ErrorType,
    string? ErrorMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long ConcurrencyToken);

/// <summary>A bounded page of active runs whose runner heartbeat is stale.</summary>
public sealed record StaleRunQuery(
    DateTimeOffset HeartbeatBeforeUtc,
    int MaximumResults);

/// <summary>An append-only run event persisted from runner or agent protocol output.</summary>
public sealed record AgentRunEventRecord(
    Guid Id,
    AgentRunId RunId,
    long Sequence,
    DateTimeOffset TimestampUtc,
    string Level,
    string EventType,
    string Message,
    string DataJson);

/// <summary>An append-only numeric metric for a run.</summary>
public sealed record AgentRunMetricRecord(
    Guid Id,
    AgentRunId RunId,
    string Name,
    double? NumericValue,
    string? TextValue,
    string? Unit,
    string TagsJson,
    DateTimeOffset TimestampUtc);

/// <summary>Metadata for a durable diagnostic or result artifact stored on disk.</summary>
public sealed record RunArtifactRecord(
    Guid Id,
    AgentRunId RunId,
    AgentId AgentId,
    string ArtifactType,
    string FileName,
    string RelativePath,
    string ContentType,
    long SizeBytes,
    string Sha256,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DeleteAfterUtc);

/// <summary>A request to atomically write and register one run artifact.</summary>
public sealed record ArtifactWriteRequest(
    AgentRunId RunId,
    AgentId AgentId,
    string ArtifactType,
    string FileName,
    string ContentType,
    Stream Content,
    DateTimeOffset? DeleteAfterUtc);

/// <summary>An append-only, redacted platform audit event.</summary>
public sealed record AuditEventRecord(
    Guid Id,
    DateTimeOffset TimestampUtc,
    AuditActorType ActorType,
    string ActorId,
    string Action,
    string TargetType,
    string TargetId,
    AuditOutcome Outcome,
    Guid CorrelationId,
    AgentRunId? RunId,
    string DataJson);

/// <summary>A request to append a bounded and redacted audit event.</summary>
public sealed record WriteAuditEventRequest(
    AuditActorType ActorType,
    string ActorId,
    string Action,
    string TargetType,
    string TargetId,
    AuditOutcome Outcome,
    Guid CorrelationId,
    AgentRunId? RunId,
    JsonElement Data);

/// <summary>A time-bounded exclusive lease with a monotonically increasing fencing token.</summary>
public sealed record AgentLeaseRecord(
    string LeaseName,
    string OwnerId,
    DateTimeOffset AcquiredAtUtc,
    DateTimeOffset ExpiresAtUtc,
    long FencingToken);

/// <summary>A JSON-valued platform setting with optimistic concurrency.</summary>
public sealed record SystemSettingRecord(
    string Key,
    string ValueJson,
    string SchemaVersion,
    DateTimeOffset UpdatedAtUtc,
    long ConcurrencyToken);
