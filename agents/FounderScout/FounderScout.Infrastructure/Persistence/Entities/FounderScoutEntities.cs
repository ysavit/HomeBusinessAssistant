namespace FounderScout.Infrastructure.Persistence.Entities;

internal sealed class BrowserAccountEntity
{
    public required string Id { get; set; }
    public required string DisplayName { get; set; }
    public required string BrowserProfileRelativePath { get; set; }
    public bool Enabled { get; set; }
    public required string SessionStatus { get; set; }
    public required string AssignedSegmentIdsJson { get; set; }
    public DateTimeOffset? LastAuthenticatedAtUtc { get; set; }
    public DateTimeOffset? LastSuccessfulRunAtUtc { get; set; }
    public DateTimeOffset? LastFailedRunAtUtc { get; set; }
    public string? LastErrorReasonCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTimeOffset? SuspendedUntilUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}

internal sealed class DiscoverySegmentEntity
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public bool Enabled { get; set; }
    public int Priority { get; set; }
    public required string ConfigurationJson { get; set; }
    public string? AssignedAccountId { get; set; }
    public DateTimeOffset? LastRunAtUtc { get; set; }
    public long ViewedCount { get; set; }
    public long NewCount { get; set; }
    public long DuplicateCount { get; set; }
    public long ErrorCount { get; set; }
    public int ConsecutiveLowYieldRuns { get; set; }
    public DateTimeOffset? PausedUntilUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}

internal sealed class DiscoveryCheckpointEntity
{
    public Guid Id { get; set; }
    public required string AccountId { get; set; }
    public required string SegmentId { get; set; }
    public Guid DomainRunId { get; set; }
    public string? LastSourceProfileKey { get; set; }
    public required string StateJson { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public required string Status { get; set; }
    public required string Version { get; set; }
}

internal sealed class CandidateEntity
{
    public Guid Id { get; set; }
    public string? CurrentSourceProfileKey { get; set; }
    public string? CanonicalSourceUrl { get; set; }
    public required string DisplayName { get; set; }
    public string? NormalizedLocation { get; set; }
    public required string TechnicalStatus { get; set; }
    public required string CommitmentStatus { get; set; }
    public required string IdeaCommitmentStatus { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset FirstSeenAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public string? LastActivityText { get; set; }
    public DateTimeOffset? LastActivityAtUtc { get; set; }
    public Guid? CurrentSnapshotId { get; set; }
    public Guid? LatestEvaluationId { get; set; }
    public decimal? FounderQualityScore { get; set; }
    public decimal? OurFitScore { get; set; }
    public decimal? Confidence { get; set; }
    public decimal? ActivityScore { get; set; }
    public decimal? RiskPenalty { get; set; }
    public decimal? InvitationPriority { get; set; }
    public string? AnalysisWorkerId { get; set; }
    public DateTimeOffset? AnalysisClaimedAtUtc { get; set; }
    public DateTimeOffset? AnalysisClaimExpiresAtUtc { get; set; }
    public int AnalysisAttemptCount { get; set; }
    public string? LastAnalysisErrorCode { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long Version { get; set; }
    public Guid? MergedIntoCandidateId { get; set; }
}

internal sealed class ProfileSnapshotEntity
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public required string SourceAccountId { get; set; }
    public required string SourceSegmentId { get; set; }
    public required string SourceProfileKey { get; set; }
    public string? CanonicalSourceUrl { get; set; }
    public required string ContentHash { get; set; }
    public required string ParserVersion { get; set; }
    public required string SourceAdapterVersion { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public required string RawArtifactRelativePath { get; set; }
    public DateTimeOffset? DeleteAfterUtc { get; set; }
    public string? NormalizedProfileJson { get; set; }
    public required string RawContentHash { get; set; }
    public string? NormalizedProfileHash { get; set; }
    public string? EvaluatorInputHash { get; set; }
    public string? EvidenceJson { get; set; }
    public string? RedactionJson { get; set; }
    public decimal ExtractionCompleteness { get; set; }
    public decimal ExtractionConfidence { get; set; }
    public required string Status { get; set; }
    public string? ErrorCode { get; set; }
    public string? ProcessingWorkerId { get; set; }
    public DateTimeOffset? ProcessingClaimedAtUtc { get; set; }
    public DateTimeOffset? ProcessingClaimExpiresAtUtc { get; set; }
    public int ProcessingAttemptCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? RawArtifactDeletedAtUtc { get; set; }
    public string? RawArtifactDeletionReasonCode { get; set; }
}

internal sealed class CandidateIdentityAliasEntity
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public required string AliasType { get; set; }
    public required string NormalizedValueHash { get; set; }
    public string? SourceAccountId { get; set; }
    public decimal Confidence { get; set; }
    public bool IsStrong { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

internal sealed class ScreeningDecisionEntity
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public Guid SnapshotId { get; set; }
    public required string RulesetVersion { get; set; }
    public required string Outcome { get; set; }
    public decimal? Score { get; set; }
    public required string ReasonCodesJson { get; set; }
    public required string EvidenceJson { get; set; }
    public required string MissingEvidenceJson { get; set; }
    public required string EvaluatorInputJson { get; set; }
    public required string EvaluatorInputHash { get; set; }
    public bool IsManualOverride { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

internal sealed class CandidateIdentityConflictEntity
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public Guid? ConflictingCandidateId { get; set; }
    public required string AliasType { get; set; }
    public required string NormalizedValueHash { get; set; }
    public required string ReasonCode { get; set; }
    public decimal Confidence { get; set; }
    public bool Resolved { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public string? ResolutionAction { get; set; }
}

internal sealed class EvaluationEntity
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public Guid SnapshotId { get; set; }
    public required string ScorecardVersion { get; set; }
    public required string EvaluatorProvider { get; set; }
    public required string EvaluatorModel { get; set; }
    public required string EvaluatorDeploymentVersion { get; set; }
    public required string PromptVersion { get; set; }
    public required string InputHash { get; set; }
    public decimal FounderQualityScore { get; set; }
    public decimal OurFitScore { get; set; }
    public decimal Confidence { get; set; }
    public decimal ActivityScore { get; set; }
    public decimal BaseScore { get; set; }
    public decimal RiskPenalty { get; set; }
    public decimal FinalScore { get; set; }
    public decimal InvitationPriority { get; set; }
    public required string Recommendation { get; set; }
    public required string StructuredEvaluationJson { get; set; }
    public string? RawProviderArtifactReference { get; set; }
    public required string Status { get; set; }
    public string? ErrorCode { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset? RetryAfterUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}

internal sealed class EvaluationCategoryEntity
{
    public Guid Id { get; set; }
    public Guid EvaluationId { get; set; }
    public required string Key { get; set; }
    public decimal Score { get; set; }
    public decimal Maximum { get; set; }
    public required string EvidenceJson { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

internal sealed class EvaluationRiskEntity
{
    public Guid Id { get; set; }
    public Guid EvaluationId { get; set; }
    public required string Key { get; set; }
    public decimal Penalty { get; set; }
    public required string EvidenceJson { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

internal sealed class InvitationDraftEntity
{
    public Guid Id { get; set; }
    public Guid EvaluationId { get; set; }
    public Guid CandidateId { get; set; }
    public required string ShortDraft { get; set; }
    public required string DetailedDraft { get; set; }
    public required string FactsUsedJson { get; set; }
    public decimal Confidence { get; set; }
    public required string Status { get; set; }
    public required string ValidationErrorsJson { get; set; }
    public required string SimilarityFingerprint { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public string? ReviewedBy { get; set; }
    public bool Superseded { get; set; }
    public long Version { get; set; }
}

internal sealed class InvitationDraftRevisionEntity
{
    public Guid Id { get; set; }
    public Guid SourceDraftId { get; set; }
    public Guid CandidateId { get; set; }
    public required string ShortDraft { get; set; }
    public required string DetailedDraft { get; set; }
    public required string EditedBy { get; set; }
    public DateTimeOffset EditedAtUtc { get; set; }
    public bool ValidationPassed { get; set; }
    public required string ValidationErrorsJson { get; set; }
    public decimal MaximumObservedSimilarity { get; set; }
    public required string SimilarityFingerprint { get; set; }
    public bool IsActive { get; set; }
}

internal sealed class InvitationQueueEntryEntity
{
    public Guid Id { get; set; }
    public Guid WindowId { get; set; }
    public Guid CandidateId { get; set; }
    public required string QueueKind { get; set; }
    public int Position { get; set; }
    public bool ManuallyIncluded { get; set; }
    public required string AddedBy { get; set; }
    public DateTimeOffset AddedAtUtc { get; set; }
    public DateTimeOffset? RemovedAtUtc { get; set; }
    public string? RemovalReasonCode { get; set; }
    public long Version { get; set; }
}

internal sealed class CandidateActionEntity
{
    public Guid Id { get; set; }
    public Guid CandidateId { get; set; }
    public required string ActionType { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public required string Actor { get; set; }
    public string? Notes { get; set; }
    public required string DataJson { get; set; }
    public Guid? RelatedInvitationId { get; set; }
    public Guid? RelatedEvaluationId { get; set; }
    public Guid? RelatedRunId { get; set; }
    public required string CorrelationId { get; set; }
}

internal sealed class ManualInvitationWindowEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset StartAtUtc { get; set; }
    public DateTimeOffset EndAtUtc { get; set; }
    public int PrimaryQueueSize { get; set; }
    public int ReserveQueueSize { get; set; }
    public int SentCount { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long Version { get; set; }
}

internal sealed class ReportExportEntity
{
    public Guid Id { get; set; }
    public required string ReportType { get; set; }
    public required string Format { get; set; }
    public required string FilterSortHash { get; set; }
    public required string RelativePath { get; set; }
    public int RowCount { get; set; }
    public required string FileHash { get; set; }
    public long FileSize { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset DeleteAfterUtc { get; set; }
}

internal sealed class FounderScoutLeaseEntity
{
    public required string Resource { get; set; }
    public required string OwnerId { get; set; }
    public long FencingToken { get; set; }
    public DateTimeOffset AcquiredAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
