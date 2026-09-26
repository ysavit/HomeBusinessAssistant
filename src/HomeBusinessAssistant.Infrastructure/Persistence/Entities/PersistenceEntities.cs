namespace HomeBusinessAssistant.Infrastructure.Persistence.Entities;

internal sealed class AgentDefinitionEntity
{
    public required string Id { get; set; }
    public required string DisplayName { get; set; }
    public required string Description { get; set; }
    public required string ManifestVersion { get; set; }
    public required string InstalledVersion { get; set; }
    public required string ExecutableRelativePath { get; set; }
    public required string WorkingDirectoryRelativePath { get; set; }
    public required string CapabilitiesJson { get; set; }
    public required string SupportedCommandsJson { get; set; }
    public required string DefaultConcurrencyPolicy { get; set; }
    public bool SupportsScheduling { get; set; }
    public bool SupportsManualRun { get; set; }
    public bool RequiresInteractiveUserSession { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class AgentConfigurationEntity
{
    public Guid Id { get; set; }
    public required string AgentId { get; set; }
    public Guid CurrentRevisionId { get; set; }
    public required string SchemaVersion { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long ConcurrencyToken { get; set; }
}

internal sealed class ConfigurationRevisionEntity
{
    public Guid Id { get; set; }
    public required string AgentId { get; set; }
    public long RevisionNumber { get; set; }
    public required string SchemaVersion { get; set; }
    public required string CanonicalConfigurationJson { get; set; }
    public required string ConfigurationHash { get; set; }
    public required string ChangedBy { get; set; }
    public required string ChangeSummary { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

internal sealed class AgentScheduleEntity
{
    public Guid Id { get; set; }
    public required string AgentId { get; set; }
    public required string Name { get; set; }
    public required string CommandName { get; set; }
    public required string ArgumentsJson { get; set; }
    public required string Kind { get; set; }
    public required string DefinitionJson { get; set; }
    public required string TimeZoneId { get; set; }
    public required string MisfirePolicy { get; set; }
    public required string ConcurrencyPolicy { get; set; }
    public int TimeoutSeconds { get; set; }
    public int MisfireGracePeriodSeconds { get; set; }
    public required string RetryPolicyJson { get; set; }
    public required string WakePolicy { get; set; }
    public Guid? PinnedConfigurationRevisionId { get; set; }
    public bool AllowDisabledAgent { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsPaused { get; set; }
    public DateTimeOffset? PausedUntilUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long ConcurrencyToken { get; set; }
}

internal sealed class ScheduleOccurrenceEntity
{
    public Guid Id { get; set; }
    public Guid? ScheduleId { get; set; }
    public required string AgentId { get; set; }
    public required string CommandName { get; set; }
    public required string ArgumentsJson { get; set; }
    public Guid ConfigurationRevisionId { get; set; }
    public DateTimeOffset DueAtUtc { get; set; }
    public required string TriggerType { get; set; }
    public required string Status { get; set; }
    public bool RequiresWake { get; set; }
    public bool KeepSystemAwake { get; set; }
    public bool KeepDisplayOn { get; set; }
    public bool AllowDisabledAgent { get; set; }
    public int AttemptNumber { get; set; }
    public Guid? ParentOccurrenceId { get; set; }
    public string? ClaimedBy { get; set; }
    public DateTimeOffset? ClaimedAtUtc { get; set; }
    public DateTimeOffset? ClaimExpiresAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? CancellationRequestedAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public string? TerminalReasonCode { get; set; }
    public string? TerminalMessage { get; set; }
    public Guid? RunId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

internal sealed class AgentRunEntity
{
    public Guid Id { get; set; }
    public Guid OccurrenceId { get; set; }
    public required string AgentId { get; set; }
    public Guid ConfigurationRevisionId { get; set; }
    public required string ConfigurationHash { get; set; }
    public required string TriggerType { get; set; }
    public required string Status { get; set; }
    public required string RunnerVersion { get; set; }
    public required string AgentVersion { get; set; }
    public required string ManifestVersion { get; set; }
    public required string ExecutableHash { get; set; }
    public required string MachineName { get; set; }
    public int? ProcessId { get; set; }
    public DateTimeOffset? ProcessStartedAtUtc { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public long? DurationMilliseconds { get; set; }
    public int? ExitCode { get; set; }
    public string? SummaryText { get; set; }
    public string? SummaryJson { get; set; }
    public string? ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long ConcurrencyToken { get; set; }
}

internal sealed class AgentRunEventEntity
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public long Sequence { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public required string Level { get; set; }
    public required string EventType { get; set; }
    public required string Message { get; set; }
    public required string DataJson { get; set; }
}

internal sealed class AgentRunMetricEntity
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public required string Name { get; set; }
    public double? NumericValue { get; set; }
    public string? TextValue { get; set; }
    public string? Unit { get; set; }
    public required string TagsJson { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
}

internal sealed class RunArtifactEntity
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public required string AgentId { get; set; }
    public required string ArtifactType { get; set; }
    public required string FileName { get; set; }
    public required string RelativePath { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string Sha256 { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? DeleteAfterUtc { get; set; }
}

internal sealed class AuditEventEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public required string ActorType { get; set; }
    public required string ActorId { get; set; }
    public required string Action { get; set; }
    public required string TargetType { get; set; }
    public required string TargetId { get; set; }
    public required string Outcome { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid? RunId { get; set; }
    public required string DataJson { get; set; }
}

internal sealed class AgentLeaseEntity
{
    public required string LeaseName { get; set; }
    public required string OwnerId { get; set; }
    public DateTimeOffset AcquiredAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public long FencingToken { get; set; }
}

internal sealed class SystemSettingEntity
{
    public required string Key { get; set; }
    public required string ValueJson { get; set; }
    public required string SchemaVersion { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long ConcurrencyToken { get; set; }
}

internal sealed class AttentionItemEntity
{
    public Guid Id { get; set; }
    public required string Category { get; set; }
    public required string Severity { get; set; }
    public required string Title { get; set; }
    public required string Message { get; set; }
    public string? AgentId { get; set; }
    public Guid? RunId { get; set; }
    public Guid? ScheduleId { get; set; }
    public required string DedupeKey { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset FirstObservedAtUtc { get; set; }
    public DateTimeOffset LastObservedAtUtc { get; set; }
    public int OccurrenceCount { get; set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public required string ContextJson { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
}

internal sealed class LocalNotificationEntity
{
    public Guid Id { get; set; }
    public Guid? AttentionItemId { get; set; }
    public required string Title { get; set; }
    public required string Message { get; set; }
    public required string Severity { get; set; }
    public string? LocalPath { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset NotBeforeUtc { get; set; }
    public DateTimeOffset? DeliveredAtUtc { get; set; }
    public int DeliveryAttempts { get; set; }
    public required string Status { get; set; }
    public required string DedupeKey { get; set; }
    public required string ThrottleKey { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
}

internal sealed class DailySummaryEntity
{
    public Guid Id { get; set; }
    public required string LocalDate { get; set; }
    public required string TimeZoneId { get; set; }
    public required string SchemaVersion { get; set; }
    public required string SummaryText { get; set; }
    public required string SummaryJson { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public DateTimeOffset PeriodStartUtc { get; set; }
    public DateTimeOffset PeriodEndUtc { get; set; }
    public int GenerationNumber { get; set; }
    public required string SourceHash { get; set; }
}

internal sealed class OnboardingSessionEntity
{
    public Guid Id { get; set; }
    public required string SchemaVersion { get; set; }
    public required string Kind { get; set; }
    public required string Status { get; set; }
    public required string CurrentStep { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? DeferredAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? CancelledAtUtc { get; set; }
    public required string ActorId { get; set; }
    public Guid CorrelationId { get; set; }
    public long Revision { get; set; }
    public int WarningCount { get; set; }
    public int AcknowledgedWarningCount { get; set; }
    public int CompletedStepCount { get; set; }
    public string? ActiveSlot { get; set; }
    public string? InitializationKey { get; set; }
}

internal sealed class OnboardingCheckEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public required string Scope { get; set; }
    public required string CheckKey { get; set; }
    public string? AgentId { get; set; }
    public required string AgentScopeKey { get; set; }
    public required string Status { get; set; }
    public required string ReasonCode { get; set; }
    public required string Title { get; set; }
    public required string Message { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public required string DetailsSchemaVersion { get; set; }
    public required string DetailsJson { get; set; }
    public string? RemediationKey { get; set; }
}

internal sealed class OnboardingAgentSelectionEntity
{
    public Guid SessionId { get; set; }
    public required string AgentId { get; set; }
    public required string SelectionStatus { get; set; }
    public required string ProgressStatus { get; set; }
    public required string AdapterId { get; set; }
    public required string CurrentStepKey { get; set; }
    public string? ReviewedManifestVersion { get; set; }
    public string? ReviewedConfigurationSchemaVersion { get; set; }
    public Guid? StartingConfigurationRevisionId { get; set; }
    public string? StartingConfigurationHash { get; set; }
    public Guid? SavedConfigurationRevisionId { get; set; }
    public string? SavedConfigurationHash { get; set; }
    public required string LastReasonCode { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public long Revision { get; set; }
}
