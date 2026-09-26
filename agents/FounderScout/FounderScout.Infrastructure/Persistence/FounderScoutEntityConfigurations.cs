using FounderScout.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FounderScout.Infrastructure.Persistence;

internal sealed class BrowserAccountConfiguration : IEntityTypeConfiguration<BrowserAccountEntity>
{
    public void Configure(EntityTypeBuilder<BrowserAccountEntity> builder)
    {
        builder.ToTable("BrowserAccounts");
        builder.HasKey(x => x.Id).HasName("PK_BrowserAccounts");
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.Property(x => x.DisplayName).HasMaxLength(200);
        builder.Property(x => x.BrowserProfileRelativePath).HasMaxLength(512);
        builder.Property(x => x.SessionStatus).HasMaxLength(64);
        builder.Property(x => x.AssignedSegmentIdsJson).HasMaxLength(32_768);
        builder.Property(x => x.LastErrorReasonCode).HasMaxLength(128);
        builder.Property(x => x.LastErrorMessage).HasMaxLength(1_000);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.Enabled, x.SessionStatus, x.Id }).HasDatabaseName("IX_BrowserAccounts_Enabled_Status_Id");
    }
}

internal sealed class DiscoverySegmentConfiguration : IEntityTypeConfiguration<DiscoverySegmentEntity>
{
    public void Configure(EntityTypeBuilder<DiscoverySegmentEntity> builder)
    {
        builder.ToTable("DiscoverySegments");
        builder.HasKey(x => x.Id).HasName("PK_DiscoverySegments");
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.ConfigurationJson).HasMaxLength(65_536);
        builder.Property(x => x.AssignedAccountId).HasMaxLength(64);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.Enabled, x.PausedUntilUtc, x.Priority, x.Id }).HasDatabaseName("IX_DiscoverySegments_Active_Priority_Id");
        builder.HasOne<BrowserAccountEntity>().WithMany().HasForeignKey(x => x.AssignedAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DiscoveryCheckpointConfiguration : IEntityTypeConfiguration<DiscoveryCheckpointEntity>
{
    public void Configure(EntityTypeBuilder<DiscoveryCheckpointEntity> builder)
    {
        builder.ToTable("DiscoveryCheckpoints");
        builder.HasKey(x => x.Id).HasName("PK_DiscoveryCheckpoints");
        builder.Property(x => x.AccountId).HasMaxLength(64);
        builder.Property(x => x.SegmentId).HasMaxLength(64);
        builder.Property(x => x.LastSourceProfileKey).HasMaxLength(512);
        builder.Property(x => x.StateJson).HasMaxLength(65_536);
        builder.Property(x => x.Status).HasMaxLength(64);
        builder.Property(x => x.Version).HasMaxLength(32);
        builder.HasIndex(x => new { x.AccountId, x.SegmentId, x.CapturedAtUtc }).HasDatabaseName("IX_DiscoveryCheckpoints_Account_Segment_Captured");
        builder.HasIndex(x => new { x.DomainRunId, x.Id }).HasDatabaseName("IX_DiscoveryCheckpoints_DomainRun_Id");
        builder.HasOne<BrowserAccountEntity>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DiscoverySegmentEntity>().WithMany().HasForeignKey(x => x.SegmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CandidateConfiguration : IEntityTypeConfiguration<CandidateEntity>
{
    public void Configure(EntityTypeBuilder<CandidateEntity> builder)
    {
        builder.ToTable("Candidates");
        builder.HasKey(x => x.Id).HasName("PK_Candidates");
        builder.Property(x => x.CurrentSourceProfileKey).HasMaxLength(512);
        builder.Property(x => x.CanonicalSourceUrl).HasMaxLength(2_048);
        builder.Property(x => x.DisplayName).HasMaxLength(200);
        builder.Property(x => x.NormalizedLocation).HasMaxLength(300);
        builder.Property(x => x.TechnicalStatus).HasMaxLength(64);
        builder.Property(x => x.CommitmentStatus).HasMaxLength(64);
        builder.Property(x => x.IdeaCommitmentStatus).HasMaxLength(64);
        builder.Property(x => x.Status).HasMaxLength(64);
        builder.Property(x => x.LastActivityText).HasMaxLength(1_000);
        builder.Property(x => x.AnalysisWorkerId).HasMaxLength(128);
        builder.Property(x => x.LastAnalysisErrorCode).HasMaxLength(128);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.Status, x.AnalysisClaimExpiresAtUtc, x.InvitationPriority, x.Id })
            .HasDatabaseName("IX_Candidates_AnalysisQueue_Priority_Id");
        builder.HasIndex(x => new { x.Status, x.InvitationPriority, x.Id })
            .HasDatabaseName("IX_Candidates_Status_Priority_Id");
        builder.HasIndex(x => x.CurrentSnapshotId).HasDatabaseName("IX_Candidates_CurrentSnapshotId");
        builder.HasIndex(x => x.LatestEvaluationId).HasDatabaseName("IX_Candidates_LatestEvaluationId");
        builder.HasIndex(x => new { x.LastSeenAtUtc, x.Id }).HasDatabaseName("IX_Candidates_LastSeen_Id");
        builder.HasIndex(x => x.MergedIntoCandidateId).HasDatabaseName("IX_Candidates_MergedIntoCandidateId");
        builder.HasOne<CandidateEntity>().WithMany().HasForeignKey(x => x.MergedIntoCandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProfileSnapshotConfiguration : IEntityTypeConfiguration<ProfileSnapshotEntity>
{
    public void Configure(EntityTypeBuilder<ProfileSnapshotEntity> builder)
    {
        builder.ToTable("ProfileSnapshots");
        builder.HasKey(x => x.Id).HasName("PK_ProfileSnapshots");
        builder.Property(x => x.SourceAccountId).HasMaxLength(64);
        builder.Property(x => x.SourceSegmentId).HasMaxLength(64);
        builder.Property(x => x.SourceProfileKey).HasMaxLength(512);
        builder.Property(x => x.CanonicalSourceUrl).HasMaxLength(2_048);
        builder.Property(x => x.ContentHash).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.ParserVersion).HasMaxLength(128);
        builder.Property(x => x.SourceAdapterVersion).HasMaxLength(128);
        builder.Property(x => x.RawArtifactRelativePath).HasMaxLength(1_024);
        builder.Property(x => x.NormalizedProfileJson).HasMaxLength(262_144);
        builder.Property(x => x.RawContentHash).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.NormalizedProfileHash).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.EvaluatorInputHash).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.EvidenceJson).HasMaxLength(262_144);
        builder.Property(x => x.RedactionJson).HasMaxLength(32_768);
        builder.Property(x => x.Status).HasMaxLength(64);
        builder.Property(x => x.ErrorCode).HasMaxLength(128);
        builder.Property(x => x.ProcessingWorkerId).HasMaxLength(128);
        builder.Property(x => x.RawArtifactDeletionReasonCode).HasMaxLength(128);
        builder.HasIndex(x => new { x.CandidateId, x.SourceProfileKey, x.ContentHash })
            .IsUnique()
            .HasDatabaseName("UX_ProfileSnapshots_Candidate_SourceKey_ContentHash");
        builder.HasIndex(x => new { x.CandidateId, x.CapturedAtUtc }).HasDatabaseName("IX_ProfileSnapshots_Candidate_Captured");
        builder.HasIndex(x => new { x.DeleteAfterUtc, x.Id }).HasDatabaseName("IX_ProfileSnapshots_DeleteAfter_Id");
        builder.HasIndex(x => new { x.Status, x.ProcessingClaimExpiresAtUtc, x.CapturedAtUtc, x.Id }).HasDatabaseName("IX_ProfileSnapshots_ProcessingQueue");
        builder.HasOne<CandidateEntity>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CandidateIdentityAliasConfiguration : IEntityTypeConfiguration<CandidateIdentityAliasEntity>
{
    public void Configure(EntityTypeBuilder<CandidateIdentityAliasEntity> builder)
    {
        builder.ToTable("CandidateIdentityAliases");
        builder.HasKey(x => x.Id).HasName("PK_CandidateIdentityAliases");
        builder.Property(x => x.AliasType).HasMaxLength(64);
        builder.Property(x => x.NormalizedValueHash).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.SourceAccountId).HasMaxLength(64);
        builder.HasIndex(x => new { x.NormalizedValueHash, x.IsStrong, x.IsActive })
            .IsUnique()
            .HasFilter("IsStrong = 1 AND IsActive = 1")
            .HasDatabaseName("UX_CandidateIdentityAliases_StrongActiveHash");
        builder.HasIndex(x => new { x.CandidateId, x.AliasType, x.NormalizedValueHash })
            .IsUnique()
            .HasDatabaseName("UX_CandidateIdentityAliases_Candidate_Type_Hash");
        builder.HasOne<CandidateEntity>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ScreeningDecisionConfiguration : IEntityTypeConfiguration<ScreeningDecisionEntity>
{
    public void Configure(EntityTypeBuilder<ScreeningDecisionEntity> builder)
    {
        builder.ToTable("ScreeningDecisions");
        builder.HasKey(x => x.Id).HasName("PK_ScreeningDecisions");
        builder.Property(x => x.RulesetVersion).HasMaxLength(128);
        builder.Property(x => x.Outcome).HasMaxLength(64);
        builder.Property(x => x.ReasonCodesJson).HasMaxLength(32_768);
        builder.Property(x => x.EvidenceJson).HasMaxLength(262_144);
        builder.Property(x => x.MissingEvidenceJson).HasMaxLength(32_768);
        builder.Property(x => x.EvaluatorInputJson).HasMaxLength(262_144);
        builder.Property(x => x.EvaluatorInputHash).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(x => new { x.CandidateId, x.CreatedAtUtc }).HasDatabaseName("IX_ScreeningDecisions_Candidate_Created");
        builder.HasIndex(x => new { x.SnapshotId, x.RulesetVersion }).IsUnique().HasDatabaseName("UX_ScreeningDecisions_Snapshot_Ruleset");
        builder.HasOne<CandidateEntity>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProfileSnapshotEntity>().WithMany().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CandidateIdentityConflictConfiguration : IEntityTypeConfiguration<CandidateIdentityConflictEntity>
{
    public void Configure(EntityTypeBuilder<CandidateIdentityConflictEntity> builder)
    {
        builder.ToTable("CandidateIdentityConflicts");
        builder.HasKey(x => x.Id).HasName("PK_CandidateIdentityConflicts");
        builder.Property(x => x.AliasType).HasMaxLength(64);
        builder.Property(x => x.NormalizedValueHash).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.ReasonCode).HasMaxLength(128);
        builder.Property(x => x.ResolutionAction).HasMaxLength(128);
        builder.HasIndex(x => new { x.Resolved, x.CreatedAtUtc, x.Id }).HasDatabaseName("IX_CandidateIdentityConflicts_Open_Created");
        builder.HasIndex(x => new { x.CandidateId, x.ConflictingCandidateId, x.NormalizedValueHash, x.Resolved }).HasDatabaseName("IX_CandidateIdentityConflicts_Candidates_Hash");
        builder.HasOne<CandidateEntity>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CandidateEntity>().WithMany().HasForeignKey(x => x.ConflictingCandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EvaluationConfiguration : IEntityTypeConfiguration<EvaluationEntity>
{
    public void Configure(EntityTypeBuilder<EvaluationEntity> builder)
    {
        builder.ToTable("Evaluations");
        builder.HasKey(x => x.Id).HasName("PK_Evaluations");
        builder.Property(x => x.ScorecardVersion).HasMaxLength(128);
        builder.Property(x => x.EvaluatorProvider).HasMaxLength(128);
        builder.Property(x => x.EvaluatorModel).HasMaxLength(128);
        builder.Property(x => x.EvaluatorDeploymentVersion).HasMaxLength(128);
        builder.Property(x => x.PromptVersion).HasMaxLength(128);
        builder.Property(x => x.InputHash).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.Recommendation).HasMaxLength(128);
        builder.Property(x => x.StructuredEvaluationJson).HasMaxLength(1_048_576);
        builder.Property(x => x.RawProviderArtifactReference).HasMaxLength(1_024);
        builder.Property(x => x.Status).HasMaxLength(64);
        builder.Property(x => x.ErrorCode).HasMaxLength(128);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.CandidateId, x.InputHash }).IsUnique().HasDatabaseName("UX_Evaluations_Candidate_InputHash");
        builder.HasIndex(x => new { x.Status, x.RetryAfterUtc, x.CreatedAtUtc }).HasDatabaseName("IX_Evaluations_Status_RetryAfter_Created");
        builder.HasIndex(x => new { x.CandidateId, x.CreatedAtUtc }).HasDatabaseName("IX_Evaluations_Candidate_Created");
        builder.HasOne<CandidateEntity>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProfileSnapshotEntity>().WithMany().HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EvaluationCategoryConfiguration : IEntityTypeConfiguration<EvaluationCategoryEntity>
{
    public void Configure(EntityTypeBuilder<EvaluationCategoryEntity> builder)
    {
        builder.ToTable("EvaluationCategories");
        builder.HasKey(x => x.Id).HasName("PK_EvaluationCategories");
        builder.Property(x => x.Key).HasMaxLength(128);
        builder.Property(x => x.EvidenceJson).HasMaxLength(262_144);
        builder.Property(x => x.Reason).HasMaxLength(1_000);
        builder.HasIndex(x => new { x.EvaluationId, x.Key }).IsUnique().HasDatabaseName("UX_EvaluationCategories_Evaluation_Key");
        builder.HasOne<EvaluationEntity>().WithMany().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EvaluationRiskConfiguration : IEntityTypeConfiguration<EvaluationRiskEntity>
{
    public void Configure(EntityTypeBuilder<EvaluationRiskEntity> builder)
    {
        builder.ToTable("EvaluationRisks");
        builder.HasKey(x => x.Id).HasName("PK_EvaluationRisks");
        builder.Property(x => x.Key).HasMaxLength(128);
        builder.Property(x => x.EvidenceJson).HasMaxLength(262_144);
        builder.Property(x => x.Reason).HasMaxLength(1_000);
        builder.HasIndex(x => new { x.EvaluationId, x.Key }).IsUnique().HasDatabaseName("UX_EvaluationRisks_Evaluation_Key");
        builder.HasOne<EvaluationEntity>().WithMany().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InvitationDraftConfiguration : IEntityTypeConfiguration<InvitationDraftEntity>
{
    public void Configure(EntityTypeBuilder<InvitationDraftEntity> builder)
    {
        builder.ToTable("InvitationDrafts");
        builder.HasKey(x => x.Id).HasName("PK_InvitationDrafts");
        builder.Property(x => x.ShortDraft).HasMaxLength(5_000);
        builder.Property(x => x.DetailedDraft).HasMaxLength(20_000);
        builder.Property(x => x.FactsUsedJson).HasMaxLength(65_536);
        builder.Property(x => x.Status).HasMaxLength(64);
        builder.Property(x => x.ValidationErrorsJson).HasMaxLength(65_536);
        builder.Property(x => x.SimilarityFingerprint).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.ReviewedBy).HasMaxLength(128);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.CandidateId, x.Superseded, x.CreatedAtUtc }).HasDatabaseName("IX_InvitationDrafts_Candidate_Active_Created");
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc }).HasDatabaseName("IX_InvitationDrafts_Status_Created");
        builder.HasOne<CandidateEntity>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EvaluationEntity>().WithMany().HasForeignKey(x => x.EvaluationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InvitationDraftRevisionConfiguration : IEntityTypeConfiguration<InvitationDraftRevisionEntity>
{
    public void Configure(EntityTypeBuilder<InvitationDraftRevisionEntity> builder)
    {
        builder.ToTable("InvitationDraftRevisions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ShortDraft).HasMaxLength(5_000);
        builder.Property(x => x.DetailedDraft).HasMaxLength(20_000);
        builder.Property(x => x.EditedBy).HasMaxLength(128);
        builder.Property(x => x.ValidationErrorsJson).HasMaxLength(65_536);
        builder.Property(x => x.SimilarityFingerprint).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(x => new { x.SourceDraftId, x.EditedAtUtc, x.Id }).HasDatabaseName("IX_InvitationDraftRevisions_Source_Edited");
        builder.HasIndex(x => new { x.CandidateId, x.IsActive, x.EditedAtUtc }).HasDatabaseName("IX_InvitationDraftRevisions_Candidate_Active_Edited");
        builder.HasOne<InvitationDraftEntity>().WithMany().HasForeignKey(x => x.SourceDraftId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CandidateEntity>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InvitationQueueEntryConfiguration : IEntityTypeConfiguration<InvitationQueueEntryEntity>
{
    public void Configure(EntityTypeBuilder<InvitationQueueEntryEntity> builder)
    {
        builder.ToTable("InvitationQueueEntries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.QueueKind).HasMaxLength(32);
        builder.Property(x => x.AddedBy).HasMaxLength(128);
        builder.Property(x => x.RemovalReasonCode).HasMaxLength(128);
        builder.HasIndex(x => new { x.WindowId, x.CandidateId })
            .IsUnique()
            .HasFilter("\"RemovedAtUtc\" IS NULL")
            .HasDatabaseName("UX_InvitationQueueEntries_Window_Candidate_Active");
        builder.HasIndex(x => new { x.WindowId, x.QueueKind, x.Position, x.Id }).HasDatabaseName("IX_InvitationQueueEntries_Window_Kind_Position");
        builder.HasOne<ManualInvitationWindowEntity>().WithMany().HasForeignKey(x => x.WindowId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CandidateEntity>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CandidateActionConfiguration : IEntityTypeConfiguration<CandidateActionEntity>
{
    public void Configure(EntityTypeBuilder<CandidateActionEntity> builder)
    {
        builder.ToTable("CandidateActions");
        builder.HasKey(x => x.Id).HasName("PK_CandidateActions");
        builder.Property(x => x.ActionType).HasMaxLength(64);
        builder.Property(x => x.Actor).HasMaxLength(128);
        builder.Property(x => x.Notes).HasMaxLength(2_000);
        builder.Property(x => x.DataJson).HasMaxLength(262_144);
        builder.Property(x => x.CorrelationId).HasMaxLength(128);
        builder.HasIndex(x => new { x.CandidateId, x.OccurredAtUtc, x.Id }).HasDatabaseName("IX_CandidateActions_Candidate_Occurred_Id");
        builder.HasIndex(x => new { x.RelatedRunId, x.Id }).HasDatabaseName("IX_CandidateActions_RelatedRun_Id");
        builder.HasIndex(x => new { x.CorrelationId, x.Id }).HasDatabaseName("IX_CandidateActions_Correlation_Id");
        builder.HasOne<CandidateEntity>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ManualInvitationWindowConfiguration : IEntityTypeConfiguration<ManualInvitationWindowEntity>
{
    public void Configure(EntityTypeBuilder<ManualInvitationWindowEntity> builder)
    {
        builder.ToTable("ManualInvitationWindows");
        builder.HasKey(x => x.Id).HasName("PK_ManualInvitationWindows");
        builder.Property(x => x.Notes).HasMaxLength(2_000);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.StartAtUtc, x.EndAtUtc }).HasDatabaseName("IX_ManualInvitationWindows_Start_End");
    }
}

internal sealed class ReportExportConfiguration : IEntityTypeConfiguration<ReportExportEntity>
{
    public void Configure(EntityTypeBuilder<ReportExportEntity> builder)
    {
        builder.ToTable("ReportExports");
        builder.HasKey(x => x.Id).HasName("PK_ReportExports");
        builder.Property(x => x.ReportType).HasMaxLength(128);
        builder.Property(x => x.Format).HasMaxLength(32);
        builder.Property(x => x.FilterSortHash).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.RelativePath).HasMaxLength(1_024);
        builder.Property(x => x.FileHash).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(x => new { x.CreatedAtUtc, x.Id }).HasDatabaseName("IX_ReportExports_Created_Id");
        builder.HasIndex(x => new { x.DeleteAfterUtc, x.Id }).HasDatabaseName("IX_ReportExports_DeleteAfter_Id");
    }
}

internal sealed class FounderScoutLeaseConfiguration : IEntityTypeConfiguration<FounderScoutLeaseEntity>
{
    public void Configure(EntityTypeBuilder<FounderScoutLeaseEntity> builder)
    {
        builder.ToTable("FounderScoutLeases");
        builder.HasKey(x => x.Resource);
        builder.Property(x => x.Resource).HasMaxLength(128);
        builder.Property(x => x.OwnerId).HasMaxLength(128);
        builder.HasIndex(x => x.ExpiresAtUtc).HasDatabaseName("IX_FounderScoutLeases_Expires");
    }
}
