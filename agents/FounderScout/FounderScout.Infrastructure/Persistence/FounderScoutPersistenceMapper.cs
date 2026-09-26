using System.Text.Json;
using FounderScout.Application;
using FounderScout.Domain;
using FounderScout.Infrastructure.Persistence.Entities;

namespace FounderScout.Infrastructure.Persistence;

internal static class FounderScoutPersistenceMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static BrowserAccount Map(BrowserAccountEntity entity) => new(
        entity.Id,
        entity.DisplayName,
        entity.BrowserProfileRelativePath,
        entity.Enabled,
        Parse<BrowserSessionStatus>(entity.SessionStatus),
        JsonSerializer.Deserialize<string[]>(entity.AssignedSegmentIdsJson, JsonOptions) ?? [],
        entity.LastAuthenticatedAtUtc,
        entity.LastSuccessfulRunAtUtc,
        entity.LastFailedRunAtUtc,
        entity.LastErrorReasonCode,
        entity.LastErrorMessage,
        entity.SuspendedUntilUtc,
        entity.CreatedAtUtc,
        entity.UpdatedAtUtc,
        entity.Version);

    public static BrowserAccountEntity Map(BrowserAccount account) => new()
    {
        Id = account.Id,
        DisplayName = account.DisplayName.Trim(),
        BrowserProfileRelativePath = account.BrowserProfileRelativePath,
        Enabled = account.Enabled,
        SessionStatus = account.SessionStatus.ToString(),
        AssignedSegmentIdsJson = JsonSerializer.Serialize(account.AssignedSegmentIds, JsonOptions),
        LastAuthenticatedAtUtc = Utc(account.LastAuthenticatedAtUtc),
        LastSuccessfulRunAtUtc = Utc(account.LastSuccessfulRunAtUtc),
        LastFailedRunAtUtc = Utc(account.LastFailedRunAtUtc),
        LastErrorReasonCode = account.LastErrorReasonCode,
        LastErrorMessage = account.LastErrorMessage,
        SuspendedUntilUtc = Utc(account.SuspendedUntilUtc),
        CreatedAtUtc = account.CreatedAtUtc.ToUniversalTime(),
        UpdatedAtUtc = account.UpdatedAtUtc.ToUniversalTime(),
        Version = account.Version <= 0 ? 1 : account.Version,
    };

    public static DiscoverySegment Map(DiscoverySegmentEntity entity) => new(
        entity.Id,
        entity.Name,
        entity.Enabled,
        entity.Priority,
        entity.ConfigurationJson,
        entity.AssignedAccountId,
        entity.LastRunAtUtc,
        entity.ViewedCount,
        entity.NewCount,
        entity.DuplicateCount,
        entity.ErrorCount,
        entity.ConsecutiveLowYieldRuns,
        entity.PausedUntilUtc,
        entity.CreatedAtUtc,
        entity.UpdatedAtUtc,
        entity.Version);

    public static DiscoverySegmentEntity Map(DiscoverySegment segment) => new()
    {
        Id = segment.Id,
        Name = segment.Name.Trim(),
        Enabled = segment.Enabled,
        Priority = segment.Priority,
        ConfigurationJson = segment.ConfigurationJson,
        AssignedAccountId = segment.AssignedAccountId,
        LastRunAtUtc = Utc(segment.LastRunAtUtc),
        ViewedCount = segment.ViewedCount,
        NewCount = segment.NewCount,
        DuplicateCount = segment.DuplicateCount,
        ErrorCount = segment.ErrorCount,
        ConsecutiveLowYieldRuns = segment.ConsecutiveLowYieldRuns,
        PausedUntilUtc = Utc(segment.PausedUntilUtc),
        CreatedAtUtc = segment.CreatedAtUtc.ToUniversalTime(),
        UpdatedAtUtc = segment.UpdatedAtUtc.ToUniversalTime(),
        Version = segment.Version <= 0 ? 1 : segment.Version,
    };

    public static DiscoveryCheckpoint Map(DiscoveryCheckpointEntity entity) => new(
        entity.Id,
        entity.AccountId,
        entity.SegmentId,
        entity.DomainRunId,
        entity.LastSourceProfileKey,
        entity.StateJson,
        entity.CapturedAtUtc,
        entity.Status,
        entity.Version);

    public static DiscoveryCheckpointEntity Map(DiscoveryCheckpoint checkpoint) => new()
    {
        Id = checkpoint.Id,
        AccountId = checkpoint.AccountId,
        SegmentId = checkpoint.SegmentId,
        DomainRunId = checkpoint.DomainRunId,
        LastSourceProfileKey = checkpoint.LastSourceProfileKey,
        StateJson = checkpoint.StateJson,
        CapturedAtUtc = checkpoint.CapturedAtUtc.ToUniversalTime(),
        Status = checkpoint.Status,
        Version = checkpoint.Version,
    };

    public static Candidate Map(CandidateEntity entity) => new(
        entity.Id,
        entity.CurrentSourceProfileKey,
        entity.CanonicalSourceUrl,
        entity.DisplayName,
        entity.NormalizedLocation,
        Parse<TechnicalProfileStatus>(entity.TechnicalStatus),
        Parse<FounderCommitmentStatus>(entity.CommitmentStatus),
        Parse<IdeaCommitmentStatus>(entity.IdeaCommitmentStatus),
        Parse<CandidateStatus>(entity.Status),
        entity.FirstSeenAtUtc,
        entity.LastSeenAtUtc,
        entity.LastActivityText,
        entity.LastActivityAtUtc,
        entity.CurrentSnapshotId,
        entity.LatestEvaluationId,
        entity.FounderQualityScore,
        entity.OurFitScore,
        entity.Confidence,
        entity.ActivityScore,
        entity.RiskPenalty,
        entity.InvitationPriority,
        entity.AnalysisWorkerId,
        entity.AnalysisClaimedAtUtc,
        entity.AnalysisClaimExpiresAtUtc,
        entity.AnalysisAttemptCount,
        entity.LastAnalysisErrorCode,
        entity.CreatedAtUtc,
        entity.UpdatedAtUtc,
        entity.Version,
        entity.MergedIntoCandidateId);

    public static ProfileSnapshot Map(ProfileSnapshotEntity entity) => new(
        entity.Id,
        entity.CandidateId,
        entity.SourceAccountId,
        entity.SourceSegmentId,
        entity.SourceProfileKey,
        entity.CanonicalSourceUrl,
        entity.ContentHash,
        entity.ParserVersion,
        entity.SourceAdapterVersion,
        entity.CapturedAtUtc,
        entity.RawArtifactRelativePath,
        entity.DeleteAfterUtc,
        entity.NormalizedProfileJson,
        entity.RawContentHash,
        entity.NormalizedProfileHash,
        entity.EvaluatorInputHash,
        entity.EvidenceJson,
        entity.RedactionJson,
        entity.ExtractionCompleteness,
        entity.ExtractionConfidence,
        Parse<ProfileSnapshotStatus>(entity.Status),
        entity.ErrorCode,
        entity.ProcessingWorkerId,
        entity.ProcessingClaimedAtUtc,
        entity.ProcessingClaimExpiresAtUtc,
        entity.ProcessingAttemptCount,
        entity.CreatedAtUtc,
        entity.RawArtifactDeletedAtUtc,
        entity.RawArtifactDeletionReasonCode);

    public static ProfileSnapshotEntity Map(AddProfileSnapshotRequest request) => new()
    {
        Id = request.SnapshotId,
        CandidateId = request.CandidateId,
        SourceAccountId = request.SourceAccountId,
        SourceSegmentId = request.SourceSegmentId,
        SourceProfileKey = request.SourceProfileKey,
        CanonicalSourceUrl = request.CanonicalSourceUrl,
        ContentHash = request.ContentHash,
        ParserVersion = request.ParserVersion,
        SourceAdapterVersion = request.SourceAdapterVersion,
        CapturedAtUtc = request.CapturedAtUtc.ToUniversalTime(),
        RawArtifactRelativePath = request.RawArtifactRelativePath,
        DeleteAfterUtc = Utc(request.DeleteAfterUtc),
        NormalizedProfileJson = request.NormalizedProfileJson,
        RawContentHash = request.ContentHash,
        ExtractionCompleteness = request.ExtractionCompleteness,
        ExtractionConfidence = request.ExtractionConfidence,
        Status = request.Status.ToString(),
        CreatedAtUtc = request.CapturedAtUtc.ToUniversalTime(),
    };

    public static CandidateIdentityAlias Map(CandidateIdentityAliasEntity entity) => new(
        entity.Id,
        entity.CandidateId,
        Parse<CandidateIdentityAliasType>(entity.AliasType),
        entity.NormalizedValueHash,
        entity.SourceAccountId,
        entity.Confidence,
        entity.IsStrong,
        entity.IsActive,
        entity.CreatedAtUtc);

    public static ScreeningDecision Map(ScreeningDecisionEntity entity) => new(
        entity.Id,
        entity.CandidateId,
        entity.SnapshotId,
        entity.RulesetVersion,
        Parse<ScreeningOutcome>(entity.Outcome),
        entity.Score,
        entity.ReasonCodesJson,
        entity.EvidenceJson,
        entity.MissingEvidenceJson,
        entity.EvaluatorInputJson,
        entity.EvaluatorInputHash,
        entity.IsManualOverride,
        entity.CreatedAtUtc);

    public static ScreeningDecisionEntity Map(ScreeningDecision decision) => new()
    {
        Id = decision.Id,
        CandidateId = decision.CandidateId,
        SnapshotId = decision.SnapshotId,
        RulesetVersion = decision.RulesetVersion,
        Outcome = decision.Outcome.ToString(),
        Score = decision.Score,
        ReasonCodesJson = decision.ReasonCodesJson,
        EvidenceJson = decision.EvidenceJson,
        MissingEvidenceJson = decision.MissingEvidenceJson,
        EvaluatorInputJson = decision.EvaluatorInputJson,
        EvaluatorInputHash = decision.EvaluatorInputHash,
        IsManualOverride = decision.IsManualOverride,
        CreatedAtUtc = decision.CreatedAtUtc.ToUniversalTime(),
    };

    public static CandidateIdentityConflictRecord Map(CandidateIdentityConflictEntity entity) => new(
        entity.Id,
        entity.CandidateId,
        entity.ConflictingCandidateId,
        Parse<CandidateIdentityAliasType>(entity.AliasType),
        entity.NormalizedValueHash,
        entity.ReasonCode,
        entity.Confidence,
        entity.Resolved,
        entity.CreatedAtUtc,
        entity.ResolvedAtUtc,
        entity.ResolutionAction);

    public static Evaluation Map(EvaluationEntity entity) => new(
        entity.Id,
        entity.CandidateId,
        entity.SnapshotId,
        entity.ScorecardVersion,
        entity.EvaluatorProvider,
        entity.EvaluatorModel,
        entity.EvaluatorDeploymentVersion,
        entity.PromptVersion,
        entity.InputHash,
        entity.FounderQualityScore,
        entity.OurFitScore,
        entity.Confidence,
        entity.ActivityScore,
        entity.BaseScore,
        entity.RiskPenalty,
        entity.FinalScore,
        entity.InvitationPriority,
        entity.Recommendation,
        entity.StructuredEvaluationJson,
        entity.RawProviderArtifactReference,
        Parse<EvaluationStatus>(entity.Status),
        entity.ErrorCode,
        entity.RetryCount,
        entity.RetryAfterUtc,
        entity.CreatedAtUtc,
        entity.UpdatedAtUtc,
        entity.Version);

    public static EvaluationEntity Map(Evaluation evaluation) => new()
    {
        Id = evaluation.Id,
        CandidateId = evaluation.CandidateId,
        SnapshotId = evaluation.SnapshotId,
        ScorecardVersion = evaluation.ScorecardVersion,
        EvaluatorProvider = evaluation.EvaluatorProvider,
        EvaluatorModel = evaluation.EvaluatorModel,
        EvaluatorDeploymentVersion = evaluation.EvaluatorDeploymentVersion,
        PromptVersion = evaluation.PromptVersion,
        InputHash = evaluation.InputHash,
        FounderQualityScore = evaluation.FounderQualityScore,
        OurFitScore = evaluation.OurFitScore,
        Confidence = evaluation.Confidence,
        ActivityScore = evaluation.ActivityScore,
        BaseScore = evaluation.BaseScore,
        RiskPenalty = evaluation.RiskPenalty,
        FinalScore = evaluation.FinalScore,
        InvitationPriority = evaluation.InvitationPriority,
        Recommendation = evaluation.Recommendation,
        StructuredEvaluationJson = evaluation.StructuredEvaluationJson,
        RawProviderArtifactReference = evaluation.RawProviderArtifactReference,
        Status = evaluation.Status.ToString(),
        ErrorCode = evaluation.ErrorCode,
        RetryCount = evaluation.RetryCount,
        RetryAfterUtc = Utc(evaluation.RetryAfterUtc),
        CreatedAtUtc = evaluation.CreatedAtUtc.ToUniversalTime(),
        UpdatedAtUtc = evaluation.UpdatedAtUtc.ToUniversalTime(),
        Version = evaluation.Version <= 0 ? 1 : evaluation.Version,
    };

    public static EvaluationCategory Map(EvaluationCategoryEntity entity) => new(
        entity.Id,
        entity.EvaluationId,
        entity.Key,
        entity.Score,
        entity.Maximum,
        entity.EvidenceJson,
        entity.Reason,
        entity.CreatedAtUtc);

    public static EvaluationCategoryEntity Map(EvaluationCategory category) => new()
    {
        Id = category.Id,
        EvaluationId = category.EvaluationId,
        Key = category.Key,
        Score = category.Score,
        Maximum = category.Maximum,
        EvidenceJson = category.EvidenceJson,
        Reason = category.Reason,
        CreatedAtUtc = category.CreatedAtUtc.ToUniversalTime(),
    };

    public static EvaluationRisk Map(EvaluationRiskEntity entity) => new(
        entity.Id,
        entity.EvaluationId,
        entity.Key,
        entity.Penalty,
        entity.EvidenceJson,
        entity.Reason,
        entity.CreatedAtUtc);

    public static EvaluationRiskEntity Map(EvaluationRisk risk) => new()
    {
        Id = risk.Id,
        EvaluationId = risk.EvaluationId,
        Key = risk.Key,
        Penalty = risk.Penalty,
        EvidenceJson = risk.EvidenceJson,
        Reason = risk.Reason,
        CreatedAtUtc = risk.CreatedAtUtc.ToUniversalTime(),
    };

    public static InvitationDraft Map(InvitationDraftEntity entity) => new(
        entity.Id,
        entity.EvaluationId,
        entity.CandidateId,
        entity.ShortDraft,
        entity.DetailedDraft,
        entity.FactsUsedJson,
        entity.Confidence,
        Parse<InvitationDraftStatus>(entity.Status),
        entity.ValidationErrorsJson,
        entity.SimilarityFingerprint,
        entity.CreatedAtUtc,
        entity.ReviewedAtUtc,
        entity.ReviewedBy,
        entity.Superseded,
        entity.Version);

    public static InvitationDraftEntity Map(InvitationDraft draft) => new()
    {
        Id = draft.Id,
        EvaluationId = draft.EvaluationId,
        CandidateId = draft.CandidateId,
        ShortDraft = draft.ShortDraft,
        DetailedDraft = draft.DetailedDraft,
        FactsUsedJson = draft.FactsUsedJson,
        Confidence = draft.Confidence,
        Status = draft.Status.ToString(),
        ValidationErrorsJson = draft.ValidationErrorsJson,
        SimilarityFingerprint = draft.SimilarityFingerprint,
        CreatedAtUtc = draft.CreatedAtUtc.ToUniversalTime(),
        ReviewedAtUtc = Utc(draft.ReviewedAtUtc),
        ReviewedBy = draft.ReviewedBy,
        Superseded = draft.Superseded,
        Version = draft.Version <= 0 ? 1 : draft.Version,
    };

    public static InvitationDraftRevision Map(InvitationDraftRevisionEntity entity) => new(
        entity.Id,
        entity.SourceDraftId,
        entity.CandidateId,
        entity.ShortDraft,
        entity.DetailedDraft,
        entity.EditedBy,
        entity.EditedAtUtc,
        entity.ValidationPassed,
        entity.ValidationErrorsJson,
        entity.MaximumObservedSimilarity,
        entity.SimilarityFingerprint,
        entity.IsActive);

    public static InvitationQueueEntry Map(InvitationQueueEntryEntity entity) => new(
        entity.Id,
        entity.WindowId,
        entity.CandidateId,
        Parse<InvitationQueueKind>(entity.QueueKind),
        entity.Position,
        entity.ManuallyIncluded,
        entity.AddedBy,
        entity.AddedAtUtc,
        entity.RemovedAtUtc,
        entity.RemovalReasonCode,
        entity.Version);

    public static CandidateAction Map(CandidateActionEntity entity) => new(
        entity.Id,
        entity.CandidateId,
        Parse<CandidateActionType>(entity.ActionType),
        entity.OccurredAtUtc,
        entity.Actor,
        entity.Notes,
        entity.DataJson,
        entity.RelatedInvitationId,
        entity.RelatedEvaluationId,
        entity.RelatedRunId,
        entity.CorrelationId);

    public static CandidateActionEntity Map(CandidateAction action) => new()
    {
        Id = action.Id,
        CandidateId = action.CandidateId,
        ActionType = action.ActionType.ToString(),
        OccurredAtUtc = action.OccurredAtUtc.ToUniversalTime(),
        Actor = action.Actor,
        Notes = action.Notes,
        DataJson = action.DataJson,
        RelatedInvitationId = action.RelatedInvitationId,
        RelatedEvaluationId = action.RelatedEvaluationId,
        RelatedRunId = action.RelatedRunId,
        CorrelationId = action.CorrelationId,
    };

    public static ManualInvitationWindow Map(ManualInvitationWindowEntity entity) => new(
        entity.Id,
        entity.StartAtUtc,
        entity.EndAtUtc,
        entity.PrimaryQueueSize,
        entity.ReserveQueueSize,
        entity.SentCount,
        entity.Notes,
        entity.CreatedAtUtc,
        entity.UpdatedAtUtc,
        entity.Version);

    public static ManualInvitationWindowEntity Map(ManualInvitationWindow window) => new()
    {
        Id = window.Id,
        StartAtUtc = window.StartAtUtc.ToUniversalTime(),
        EndAtUtc = window.EndAtUtc.ToUniversalTime(),
        PrimaryQueueSize = window.PrimaryQueueSize,
        ReserveQueueSize = window.ReserveQueueSize,
        SentCount = window.SentCount,
        Notes = window.Notes,
        CreatedAtUtc = window.CreatedAtUtc.ToUniversalTime(),
        UpdatedAtUtc = window.UpdatedAtUtc.ToUniversalTime(),
        Version = window.Version <= 0 ? 1 : window.Version,
    };

    public static ReportExport Map(ReportExportEntity entity) => new(
        entity.Id,
        entity.ReportType,
        entity.Format,
        entity.FilterSortHash,
        entity.RelativePath,
        entity.RowCount,
        entity.FileHash,
        entity.FileSize,
        entity.CreatedAtUtc,
        entity.DeleteAfterUtc);

    public static ReportExportEntity Map(ReportExport report) => new()
    {
        Id = report.Id,
        ReportType = report.ReportType,
        Format = report.Format,
        FilterSortHash = report.FilterSortHash,
        RelativePath = report.RelativePath,
        RowCount = report.RowCount,
        FileHash = report.FileHash,
        FileSize = report.FileSize,
        CreatedAtUtc = report.CreatedAtUtc.ToUniversalTime(),
        DeleteAfterUtc = report.DeleteAfterUtc.ToUniversalTime(),
    };

    private static TEnum Parse<TEnum>(string value)
        where TEnum : struct, Enum =>
        Enum.TryParse(value, ignoreCase: false, out TEnum parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException("Founder Scout persisted an unsupported enum value.");

    private static DateTimeOffset? Utc(DateTimeOffset? value) => value?.ToUniversalTime();
}
