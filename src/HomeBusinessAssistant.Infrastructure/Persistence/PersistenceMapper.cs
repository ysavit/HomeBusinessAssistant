using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;
using HomeBusinessAssistant.Infrastructure.Persistence.Entities;

namespace HomeBusinessAssistant.Infrastructure.Persistence;

internal static class PersistenceMapper
{
    public static AgentDefinitionRecord Map(AgentDefinitionEntity entity) => new(
        AgentId.Parse(entity.Id),
        entity.DisplayName,
        entity.Description,
        entity.ManifestVersion,
        entity.InstalledVersion,
        entity.ExecutableRelativePath,
        entity.WorkingDirectoryRelativePath,
        entity.CapabilitiesJson,
        entity.SupportedCommandsJson,
        ParseEnum<ConcurrencyPolicy>(entity.DefaultConcurrencyPolicy),
        entity.SupportsScheduling,
        entity.SupportsManualRun,
        entity.RequiresInteractiveUserSession,
        entity.Enabled,
        entity.CreatedAtUtc,
        entity.UpdatedAtUtc);

    public static ConfigurationRevisionRecord Map(ConfigurationRevisionEntity entity) => new(
        entity.Id,
        AgentId.Parse(entity.AgentId),
        entity.RevisionNumber,
        entity.SchemaVersion,
        entity.CanonicalConfigurationJson,
        entity.ConfigurationHash,
        entity.ChangedBy,
        entity.ChangeSummary,
        entity.CreatedAtUtc);

    public static AgentScheduleRecord Map(AgentScheduleEntity entity) => new(
        entity.Id,
        AgentId.Parse(entity.AgentId),
        entity.Name,
        entity.CommandName,
        entity.ArgumentsJson,
        ParseEnum<ScheduleKind>(entity.Kind),
        entity.DefinitionJson,
        entity.TimeZoneId,
        ParseEnum<MisfirePolicy>(entity.MisfirePolicy),
        ParseEnum<ConcurrencyPolicy>(entity.ConcurrencyPolicy),
        TimeSpan.FromSeconds(entity.TimeoutSeconds),
        TimeSpan.FromSeconds(entity.MisfireGracePeriodSeconds),
        entity.RetryPolicyJson,
        ParseEnum<WakePolicy>(entity.WakePolicy),
        entity.PinnedConfigurationRevisionId,
        entity.AllowDisabledAgent,
        entity.IsEnabled,
        entity.IsPaused,
        entity.PausedUntilUtc,
        entity.CreatedAtUtc,
        entity.UpdatedAtUtc,
        entity.ConcurrencyToken);

    public static ScheduleOccurrenceRecord Map(ScheduleOccurrenceEntity entity) => new(
        OccurrenceId.FromGuid(entity.Id),
        entity.ScheduleId,
        AgentId.Parse(entity.AgentId),
        entity.CommandName,
        entity.ArgumentsJson,
        entity.ConfigurationRevisionId,
        entity.DueAtUtc,
        ParseEnum<TriggerType>(entity.TriggerType),
        ParseEnum<OccurrenceStatus>(entity.Status),
        entity.RequiresWake,
        entity.KeepSystemAwake,
        entity.KeepDisplayOn,
        entity.AttemptNumber,
        entity.ParentOccurrenceId is Guid parent ? OccurrenceId.FromGuid(parent) : null,
        entity.ClaimedBy,
        entity.ClaimedAtUtc,
        entity.ClaimExpiresAtUtc,
        entity.StartedAtUtc,
        entity.CompletedAtUtc,
        entity.CancellationRequestedAtUtc,
        entity.CancellationReason,
        entity.TerminalReasonCode,
        entity.TerminalMessage,
        entity.RunId is Guid run ? AgentRunId.FromGuid(run) : null,
        entity.CreatedAtUtc,
        entity.UpdatedAtUtc,
        entity.AllowDisabledAgent);

    public static AgentRunRecord Map(AgentRunEntity entity) => new(
        AgentRunId.FromGuid(entity.Id),
        OccurrenceId.FromGuid(entity.OccurrenceId),
        AgentId.Parse(entity.AgentId),
        entity.ConfigurationRevisionId,
        entity.ConfigurationHash,
        ParseEnum<TriggerType>(entity.TriggerType),
        ParseEnum<AgentRunStatus>(entity.Status),
        entity.RunnerVersion,
        entity.AgentVersion,
        entity.ManifestVersion,
        entity.ExecutableHash,
        entity.MachineName,
        entity.ProcessId,
        entity.ProcessStartedAtUtc,
        entity.StartedAtUtc,
        entity.LastHeartbeatAtUtc,
        entity.CompletedAtUtc,
        entity.DurationMilliseconds,
        entity.ExitCode,
        entity.SummaryText,
        entity.SummaryJson,
        entity.ErrorType,
        entity.ErrorMessage,
        entity.CreatedAtUtc,
        entity.UpdatedAtUtc,
        entity.ConcurrencyToken);

    public static RunArtifactRecord Map(RunArtifactEntity entity) => new(
        entity.Id,
        AgentRunId.FromGuid(entity.RunId),
        AgentId.Parse(entity.AgentId),
        entity.ArtifactType,
        entity.FileName,
        entity.RelativePath,
        entity.ContentType,
        entity.SizeBytes,
        entity.Sha256,
        entity.CreatedAtUtc,
        entity.DeleteAfterUtc);

    public static AuditEventRecord Map(AuditEventEntity entity) => new(
        entity.Id,
        entity.TimestampUtc,
        ParseEnum<AuditActorType>(entity.ActorType),
        entity.ActorId,
        entity.Action,
        entity.TargetType,
        entity.TargetId,
        ParseEnum<AuditOutcome>(entity.Outcome),
        entity.CorrelationId,
        entity.RunId is Guid runId ? AgentRunId.FromGuid(runId) : null,
        entity.DataJson);

    public static AgentLeaseRecord Map(AgentLeaseEntity entity) => new(
        entity.LeaseName,
        entity.OwnerId,
        entity.AcquiredAtUtc,
        entity.ExpiresAtUtc,
        entity.FencingToken);

    public static SystemSettingRecord Map(SystemSettingEntity entity) => new(
        entity.Key,
        entity.ValueJson,
        entity.SchemaVersion,
        entity.UpdatedAtUtc,
        entity.ConcurrencyToken);

    private static T ParseEnum<T>(string value)
        where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: false, out T parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException("A persisted enum value is invalid.");
}
