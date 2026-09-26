using HomeBusinessAssistant.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HomeBusinessAssistant.Infrastructure.Persistence;

internal sealed class AgentDefinitionConfiguration : IEntityTypeConfiguration<AgentDefinitionEntity>
{
    public void Configure(EntityTypeBuilder<AgentDefinitionEntity> builder)
    {
        builder.ToTable("AgentDefinitions");
        builder.HasKey(x => x.Id).HasName("PK_AgentDefinitions");
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.Property(x => x.DisplayName).HasMaxLength(100);
        builder.Property(x => x.Description).HasMaxLength(1_000);
        builder.Property(x => x.ManifestVersion).HasMaxLength(32);
        builder.Property(x => x.InstalledVersion).HasMaxLength(128);
        builder.Property(x => x.ExecutableRelativePath).HasMaxLength(512);
        builder.Property(x => x.WorkingDirectoryRelativePath).HasMaxLength(512);
        builder.Property(x => x.CapabilitiesJson).HasMaxLength(32_768);
        builder.Property(x => x.SupportedCommandsJson).HasMaxLength(8_192);
        builder.Property(x => x.DefaultConcurrencyPolicy).HasMaxLength(32);
        builder.HasIndex(x => new { x.Enabled, x.Id }).HasDatabaseName("IX_AgentDefinitions_Enabled_Id");
    }
}

internal sealed class AgentConfigurationConfiguration : IEntityTypeConfiguration<AgentConfigurationEntity>
{
    public void Configure(EntityTypeBuilder<AgentConfigurationEntity> builder)
    {
        builder.ToTable("AgentConfigurations");
        builder.HasKey(x => x.Id).HasName("PK_AgentConfigurations");
        builder.Property(x => x.AgentId).HasMaxLength(64);
        builder.Property(x => x.SchemaVersion).HasMaxLength(32);
        builder.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(x => x.AgentId).IsUnique().HasDatabaseName("UX_AgentConfigurations_AgentId");
        builder.HasOne<AgentDefinitionEntity>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConfigurationRevisionEntity>().WithMany().HasForeignKey(x => x.CurrentRevisionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfigurationRevisionConfiguration : IEntityTypeConfiguration<ConfigurationRevisionEntity>
{
    public void Configure(EntityTypeBuilder<ConfigurationRevisionEntity> builder)
    {
        builder.ToTable("ConfigurationRevisions");
        builder.HasKey(x => x.Id).HasName("PK_ConfigurationRevisions");
        builder.Property(x => x.AgentId).HasMaxLength(64);
        builder.Property(x => x.SchemaVersion).HasMaxLength(32);
        builder.Property(x => x.CanonicalConfigurationJson).HasMaxLength(1_048_576);
        builder.Property(x => x.ConfigurationHash).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.ChangedBy).HasMaxLength(128);
        builder.Property(x => x.ChangeSummary).HasMaxLength(1_000);
        builder.HasIndex(x => new { x.AgentId, x.RevisionNumber }).IsUnique().HasDatabaseName("UX_ConfigurationRevisions_AgentId_RevisionNumber");
        builder.HasIndex(x => new { x.AgentId, x.ConfigurationHash }).IsUnique().HasDatabaseName("UX_ConfigurationRevisions_AgentId_Hash");
        builder.HasOne<AgentDefinitionEntity>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AgentScheduleConfiguration : IEntityTypeConfiguration<AgentScheduleEntity>
{
    public void Configure(EntityTypeBuilder<AgentScheduleEntity> builder)
    {
        builder.ToTable("AgentSchedules");
        builder.HasKey(x => x.Id).HasName("PK_AgentSchedules");
        builder.Property(x => x.AgentId).HasMaxLength(64);
        builder.Property(x => x.Name).HasMaxLength(100);
        builder.Property(x => x.CommandName).HasMaxLength(64);
        builder.Property(x => x.ArgumentsJson).HasMaxLength(65_536);
        builder.Property(x => x.Kind).HasMaxLength(32);
        builder.Property(x => x.DefinitionJson).HasMaxLength(65_536);
        builder.Property(x => x.TimeZoneId).HasMaxLength(128);
        builder.Property(x => x.MisfirePolicy).HasMaxLength(32);
        builder.Property(x => x.ConcurrencyPolicy).HasMaxLength(32);
        builder.Property(x => x.RetryPolicyJson).HasMaxLength(65_536);
        builder.Property(x => x.WakePolicy).HasMaxLength(32);
        builder.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(x => new { x.AgentId, x.Name }).IsUnique().HasDatabaseName("UX_AgentSchedules_AgentId_Name");
        builder.HasIndex(x => new { x.IsEnabled, x.PausedUntilUtc, x.AgentId }).HasDatabaseName("IX_AgentSchedules_Active_AgentId");
        builder.HasOne<AgentDefinitionEntity>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConfigurationRevisionEntity>().WithMany().HasForeignKey(x => x.PinnedConfigurationRevisionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ScheduleOccurrenceConfiguration : IEntityTypeConfiguration<ScheduleOccurrenceEntity>
{
    public void Configure(EntityTypeBuilder<ScheduleOccurrenceEntity> builder)
    {
        builder.ToTable("ScheduleOccurrences");
        builder.HasKey(x => x.Id).HasName("PK_ScheduleOccurrences");
        builder.Property(x => x.AgentId).HasMaxLength(64);
        builder.Property(x => x.CommandName).HasMaxLength(64);
        builder.Property(x => x.ArgumentsJson).HasMaxLength(65_536);
        builder.Property(x => x.TriggerType).HasMaxLength(32);
        builder.Property(x => x.Status).HasMaxLength(32);
        builder.Property(x => x.ClaimedBy).HasMaxLength(128);
        builder.Property(x => x.CancellationReason).HasMaxLength(1_000);
        builder.Property(x => x.TerminalReasonCode).HasMaxLength(128);
        builder.Property(x => x.TerminalMessage).HasMaxLength(1_000);
        builder.HasIndex(x => new { x.ScheduleId, x.DueAtUtc })
            .IsUnique()
            .HasFilter("ScheduleId IS NOT NULL")
            .HasDatabaseName("UX_ScheduleOccurrences_ScheduleId_DueAtUtc");
        builder.HasIndex(x => new { x.ParentOccurrenceId, x.AttemptNumber })
            .IsUnique()
            .HasFilter("ParentOccurrenceId IS NOT NULL")
            .HasDatabaseName("UX_ScheduleOccurrences_ParentOccurrenceId_AttemptNumber");
        builder.HasIndex(x => new { x.Status, x.DueAtUtc }).HasDatabaseName("IX_ScheduleOccurrences_Status_DueAtUtc");
        builder.HasIndex(x => new { x.RequiresWake, x.Status, x.DueAtUtc }).HasDatabaseName("IX_ScheduleOccurrences_RequiresWake_Status_DueAtUtc");
        builder.HasIndex(x => new { x.AgentId, x.Status }).HasDatabaseName("IX_ScheduleOccurrences_AgentId_Status");
        builder.HasIndex(x => new { x.ScheduleId, x.Status, x.DueAtUtc }).HasDatabaseName("IX_ScheduleOccurrences_ScheduleId_Status_DueAtUtc");
        builder.HasIndex(x => x.RunId).IsUnique().HasFilter("RunId IS NOT NULL").HasDatabaseName("UX_ScheduleOccurrences_RunId");
        builder.HasOne<AgentScheduleEntity>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AgentDefinitionEntity>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConfigurationRevisionEntity>().WithMany().HasForeignKey(x => x.ConfigurationRevisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ScheduleOccurrenceEntity>().WithMany().HasForeignKey(x => x.ParentOccurrenceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AgentRunConfiguration : IEntityTypeConfiguration<AgentRunEntity>
{
    public void Configure(EntityTypeBuilder<AgentRunEntity> builder)
    {
        builder.ToTable("AgentRuns");
        builder.HasKey(x => x.Id).HasName("PK_AgentRuns");
        builder.Property(x => x.AgentId).HasMaxLength(64);
        builder.Property(x => x.ConfigurationHash).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.TriggerType).HasMaxLength(32);
        builder.Property(x => x.Status).HasMaxLength(32);
        builder.Property(x => x.RunnerVersion).HasMaxLength(128);
        builder.Property(x => x.AgentVersion).HasMaxLength(128);
        builder.Property(x => x.ManifestVersion).HasMaxLength(32);
        builder.Property(x => x.ExecutableHash).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.MachineName).HasMaxLength(128);
        builder.Property(x => x.SummaryText).HasMaxLength(8_192);
        builder.Property(x => x.SummaryJson).HasMaxLength(262_144);
        builder.Property(x => x.ErrorType).HasMaxLength(256);
        builder.Property(x => x.ErrorMessage).HasMaxLength(8_192);
        builder.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(x => x.OccurrenceId).IsUnique().HasDatabaseName("UX_AgentRuns_OccurrenceId");
        builder.HasIndex(x => new { x.AgentId, x.StartedAtUtc }).HasDatabaseName("IX_AgentRuns_AgentId_StartedAtUtc");
        builder.HasIndex(x => new { x.Status, x.LastHeartbeatAtUtc }).HasDatabaseName("IX_AgentRuns_Status_LastHeartbeatAtUtc");
        builder.HasOne<ScheduleOccurrenceEntity>().WithMany().HasForeignKey(x => x.OccurrenceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AgentDefinitionEntity>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConfigurationRevisionEntity>().WithMany().HasForeignKey(x => x.ConfigurationRevisionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AgentRunEventConfiguration : IEntityTypeConfiguration<AgentRunEventEntity>
{
    public void Configure(EntityTypeBuilder<AgentRunEventEntity> builder)
    {
        builder.ToTable("AgentRunEvents");
        builder.HasKey(x => x.Id).HasName("PK_AgentRunEvents");
        builder.Property(x => x.Level).HasMaxLength(32);
        builder.Property(x => x.EventType).HasMaxLength(64);
        builder.Property(x => x.Message).HasMaxLength(4_096);
        builder.Property(x => x.DataJson).HasMaxLength(262_144);
        builder.HasIndex(x => new { x.RunId, x.Sequence }).IsUnique().HasDatabaseName("UX_AgentRunEvents_RunId_Sequence");
        builder.HasIndex(x => new { x.RunId, x.TimestampUtc }).HasDatabaseName("IX_AgentRunEvents_RunId_TimestampUtc");
        builder.HasOne<AgentRunEntity>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AgentRunMetricConfiguration : IEntityTypeConfiguration<AgentRunMetricEntity>
{
    public void Configure(EntityTypeBuilder<AgentRunMetricEntity> builder)
    {
        builder.ToTable("AgentRunMetrics");
        builder.HasKey(x => x.Id).HasName("PK_AgentRunMetrics");
        builder.Property(x => x.Name).HasMaxLength(128);
        builder.Property(x => x.TextValue).HasMaxLength(4_096);
        builder.Property(x => x.Unit).HasMaxLength(32);
        builder.Property(x => x.TagsJson).HasMaxLength(32_768);
        builder.HasIndex(x => new { x.RunId, x.Name, x.TimestampUtc }).HasDatabaseName("IX_AgentRunMetrics_RunId_Name_TimestampUtc");
        builder.HasOne<AgentRunEntity>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RunArtifactConfiguration : IEntityTypeConfiguration<RunArtifactEntity>
{
    public void Configure(EntityTypeBuilder<RunArtifactEntity> builder)
    {
        builder.ToTable("RunArtifacts");
        builder.HasKey(x => x.Id).HasName("PK_RunArtifacts");
        builder.Property(x => x.AgentId).HasMaxLength(64);
        builder.Property(x => x.ArtifactType).HasMaxLength(64);
        builder.Property(x => x.FileName).HasMaxLength(192);
        builder.Property(x => x.RelativePath).HasMaxLength(1_024);
        builder.Property(x => x.ContentType).HasMaxLength(128);
        builder.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(x => new { x.RunId, x.CreatedAtUtc }).HasDatabaseName("IX_RunArtifacts_RunId_CreatedAtUtc");
        builder.HasIndex(x => x.DeleteAfterUtc).HasDatabaseName("IX_RunArtifacts_DeleteAfterUtc");
        builder.HasOne<AgentRunEntity>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AgentDefinitionEntity>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEventEntity>
{
    public void Configure(EntityTypeBuilder<AuditEventEntity> builder)
    {
        builder.ToTable("AuditEvents");
        builder.HasKey(x => x.Id).HasName("PK_AuditEvents");
        builder.Property(x => x.ActorType).HasMaxLength(32);
        builder.Property(x => x.ActorId).HasMaxLength(128);
        builder.Property(x => x.Action).HasMaxLength(128);
        builder.Property(x => x.TargetType).HasMaxLength(64);
        builder.Property(x => x.TargetId).HasMaxLength(128);
        builder.Property(x => x.Outcome).HasMaxLength(32);
        builder.Property(x => x.DataJson).HasMaxLength(65_536);
        builder.HasIndex(x => x.TimestampUtc).HasDatabaseName("IX_AuditEvents_TimestampUtc");
        builder.HasIndex(x => new { x.TargetType, x.TargetId, x.TimestampUtc }).HasDatabaseName("IX_AuditEvents_Target_TimestampUtc");
        builder.HasIndex(x => x.CorrelationId).HasDatabaseName("IX_AuditEvents_CorrelationId");
        builder.HasIndex(x => x.RunId).HasDatabaseName("IX_AuditEvents_RunId");
        builder.HasOne<AgentRunEntity>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AgentLeaseConfiguration : IEntityTypeConfiguration<AgentLeaseEntity>
{
    public void Configure(EntityTypeBuilder<AgentLeaseEntity> builder)
    {
        builder.ToTable("AgentLeases");
        builder.HasKey(x => x.LeaseName).HasName("PK_AgentLeases");
        builder.Property(x => x.LeaseName).HasMaxLength(128);
        builder.Property(x => x.OwnerId).HasMaxLength(128);
        builder.HasIndex(x => x.ExpiresAtUtc).HasDatabaseName("IX_AgentLeases_ExpiresAtUtc");
    }
}

internal sealed class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSettingEntity>
{
    public void Configure(EntityTypeBuilder<SystemSettingEntity> builder)
    {
        builder.ToTable("SystemSettings");
        builder.HasKey(x => x.Key).HasName("PK_SystemSettings");
        builder.Property(x => x.Key).HasMaxLength(128);
        builder.Property(x => x.ValueJson).HasMaxLength(262_144);
        builder.Property(x => x.SchemaVersion).HasMaxLength(32);
        builder.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
    }
}

internal sealed class AttentionItemConfiguration : IEntityTypeConfiguration<AttentionItemEntity>
{
    public void Configure(EntityTypeBuilder<AttentionItemEntity> builder)
    {
        builder.ToTable("AttentionItems");
        builder.HasKey(x => x.Id).HasName("PK_AttentionItems");
        builder.Property(x => x.Category).HasMaxLength(64);
        builder.Property(x => x.Severity).HasMaxLength(16);
        builder.Property(x => x.Title).HasMaxLength(160);
        builder.Property(x => x.Message).HasMaxLength(1_000);
        builder.Property(x => x.AgentId).HasMaxLength(64);
        builder.Property(x => x.DedupeKey).HasMaxLength(256);
        builder.Property(x => x.Status).HasMaxLength(16);
        builder.Property(x => x.AcknowledgedBy).HasMaxLength(128);
        builder.Property(x => x.ContextJson).HasMaxLength(65_536);
        builder.HasIndex(x => x.DedupeKey).IsUnique().HasDatabaseName("UX_AttentionItems_DedupeKey");
        builder.HasIndex(x => new { x.Status, x.Severity, x.LastObservedAtUtc }).HasDatabaseName("IX_AttentionItems_Status_Severity_LastObservedAtUtc");
    }
}

internal sealed class LocalNotificationConfiguration : IEntityTypeConfiguration<LocalNotificationEntity>
{
    public void Configure(EntityTypeBuilder<LocalNotificationEntity> builder)
    {
        builder.ToTable("LocalNotifications");
        builder.HasKey(x => x.Id).HasName("PK_LocalNotifications");
        builder.Property(x => x.Title).HasMaxLength(160);
        builder.Property(x => x.Message).HasMaxLength(1_000);
        builder.Property(x => x.Severity).HasMaxLength(16);
        builder.Property(x => x.LocalPath).HasMaxLength(512);
        builder.Property(x => x.Status).HasMaxLength(16);
        builder.Property(x => x.DedupeKey).HasMaxLength(256);
        builder.Property(x => x.ThrottleKey).HasMaxLength(128);
        builder.Property(x => x.LastErrorCode).HasMaxLength(128);
        builder.HasIndex(x => x.DedupeKey).IsUnique().HasDatabaseName("UX_LocalNotifications_DedupeKey");
        builder.HasIndex(x => new { x.Status, x.NotBeforeUtc, x.CreatedAtUtc }).HasDatabaseName("IX_LocalNotifications_Status_NotBeforeUtc_CreatedAtUtc");
        builder.HasOne<AttentionItemEntity>().WithMany().HasForeignKey(x => x.AttentionItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DailySummaryConfiguration : IEntityTypeConfiguration<DailySummaryEntity>
{
    public void Configure(EntityTypeBuilder<DailySummaryEntity> builder)
    {
        builder.ToTable("DailySummaries");
        builder.HasKey(x => x.Id).HasName("PK_DailySummaries");
        builder.Property(x => x.LocalDate).HasMaxLength(10);
        builder.Property(x => x.TimeZoneId).HasMaxLength(128);
        builder.Property(x => x.SchemaVersion).HasMaxLength(16);
        builder.Property(x => x.SummaryText).HasMaxLength(8_192);
        builder.Property(x => x.SummaryJson).HasMaxLength(262_144);
        builder.Property(x => x.SourceHash).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(x => new { x.LocalDate, x.TimeZoneId, x.GenerationNumber }).IsUnique().HasDatabaseName("UX_DailySummaries_Date_TimeZone_Generation");
        builder.HasIndex(x => x.GeneratedAtUtc).HasDatabaseName("IX_DailySummaries_GeneratedAtUtc");
    }
}

internal sealed class OnboardingSessionConfiguration : IEntityTypeConfiguration<OnboardingSessionEntity>
{
    public void Configure(EntityTypeBuilder<OnboardingSessionEntity> builder)
    {
        builder.ToTable("OnboardingSessions");
        builder.HasKey(x => x.Id).HasName("PK_OnboardingSessions");
        builder.Property(x => x.SchemaVersion).HasMaxLength(16);
        builder.Property(x => x.Kind).HasMaxLength(32);
        builder.Property(x => x.Status).HasMaxLength(32);
        builder.Property(x => x.CurrentStep).HasMaxLength(64);
        builder.Property(x => x.ActorId).HasMaxLength(128);
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.Property(x => x.ActiveSlot).HasMaxLength(32);
        builder.Property(x => x.InitializationKey).HasMaxLength(32);
        builder.HasIndex(x => x.ActiveSlot)
            .IsUnique()
            .HasFilter("ActiveSlot IS NOT NULL")
            .HasDatabaseName("UX_OnboardingSessions_ActiveSlot");
        builder.HasIndex(x => x.InitializationKey)
            .IsUnique()
            .HasFilter("InitializationKey IS NOT NULL")
            .HasDatabaseName("UX_OnboardingSessions_InitializationKey");
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc })
            .HasDatabaseName("IX_OnboardingSessions_Status_CreatedAtUtc");
    }
}

internal sealed class OnboardingCheckConfiguration : IEntityTypeConfiguration<OnboardingCheckEntity>
{
    public void Configure(EntityTypeBuilder<OnboardingCheckEntity> builder)
    {
        builder.ToTable("OnboardingChecks");
        builder.HasKey(x => x.Id).HasName("PK_OnboardingChecks");
        builder.Property(x => x.Scope).HasMaxLength(32);
        builder.Property(x => x.CheckKey).HasMaxLength(128);
        builder.Property(x => x.AgentId).HasMaxLength(64);
        builder.Property(x => x.AgentScopeKey).HasMaxLength(64);
        builder.Property(x => x.Status).HasMaxLength(32);
        builder.Property(x => x.ReasonCode).HasMaxLength(128);
        builder.Property(x => x.Title).HasMaxLength(160);
        builder.Property(x => x.Message).HasMaxLength(1_000);
        builder.Property(x => x.DetailsSchemaVersion).HasMaxLength(32);
        builder.Property(x => x.DetailsJson).HasMaxLength(32_768);
        builder.Property(x => x.RemediationKey).HasMaxLength(128);
        builder.HasIndex(x => new { x.SessionId, x.Scope, x.CheckKey, x.AgentScopeKey })
            .IsUnique()
            .HasDatabaseName("UX_OnboardingChecks_LatestIdentity");
        builder.HasIndex(x => new { x.SessionId, x.Status, x.ObservedAtUtc })
            .HasDatabaseName("IX_OnboardingChecks_Session_Status_ObservedAtUtc");
        builder.HasOne<OnboardingSessionEntity>()
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AgentDefinitionEntity>()
            .WithMany()
            .HasForeignKey(x => x.AgentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OnboardingAgentSelectionConfiguration : IEntityTypeConfiguration<OnboardingAgentSelectionEntity>
{
    public void Configure(EntityTypeBuilder<OnboardingAgentSelectionEntity> builder)
    {
        builder.ToTable("OnboardingAgentSelections");
        builder.HasKey(x => new { x.SessionId, x.AgentId }).HasName("PK_OnboardingAgentSelections");
        builder.Property(x => x.AgentId).HasMaxLength(64);
        builder.Property(x => x.SelectionStatus).HasMaxLength(32);
        builder.Property(x => x.ProgressStatus).HasMaxLength(32);
        builder.Property(x => x.AdapterId).HasMaxLength(128);
        builder.Property(x => x.CurrentStepKey).HasMaxLength(128);
        builder.Property(x => x.ReviewedManifestVersion).HasMaxLength(64);
        builder.Property(x => x.ReviewedConfigurationSchemaVersion).HasMaxLength(64);
        builder.Property(x => x.StartingConfigurationHash).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.SavedConfigurationHash).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.LastReasonCode).HasMaxLength(128);
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.HasIndex(x => new { x.AgentId, x.UpdatedAtUtc })
            .HasDatabaseName("IX_OnboardingAgentSelections_Agent_UpdatedAtUtc");
        builder.HasIndex(x => new { x.SessionId, x.SelectionStatus, x.ProgressStatus })
            .HasDatabaseName("IX_OnboardingAgentSelections_Session_Status_Progress");
        builder.HasOne<OnboardingSessionEntity>()
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AgentDefinitionEntity>()
            .WithMany()
            .HasForeignKey(x => x.AgentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConfigurationRevisionEntity>()
            .WithMany()
            .HasForeignKey(x => x.StartingConfigurationRevisionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConfigurationRevisionEntity>()
            .WithMany()
            .HasForeignKey(x => x.SavedConfigurationRevisionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
