namespace FounderScout.Domain;

/// <summary>One dedicated, manually authenticated browser account.</summary>
public sealed record BrowserAccount(
    string Id,
    string DisplayName,
    string BrowserProfileRelativePath,
    bool Enabled,
    BrowserSessionStatus SessionStatus,
    IReadOnlyList<string> AssignedSegmentIds,
    DateTimeOffset? LastAuthenticatedAtUtc,
    DateTimeOffset? LastSuccessfulRunAtUtc,
    DateTimeOffset? LastFailedRunAtUtc,
    string? LastErrorReasonCode,
    string? LastErrorMessage,
    DateTimeOffset? SuspendedUntilUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);

/// <summary>One bounded source-filter/navigation segment.</summary>
public sealed record DiscoverySegment(
    string Id,
    string Name,
    bool Enabled,
    int Priority,
    string ConfigurationJson,
    string? AssignedAccountId,
    DateTimeOffset? LastRunAtUtc,
    long ViewedCount,
    long NewCount,
    long DuplicateCount,
    long ErrorCount,
    int ConsecutiveLowYieldRuns,
    DateTimeOffset? PausedUntilUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);

/// <summary>Minimal resumable discovery metadata without authentication state or whole pages.</summary>
public sealed record DiscoveryCheckpoint(
    Guid Id,
    string AccountId,
    string SegmentId,
    Guid DomainRunId,
    string? LastSourceProfileKey,
    string StateJson,
    DateTimeOffset CapturedAtUtc,
    string Status,
    string Version);

/// <summary>One normalized founder candidate without protected demographic attributes.</summary>
public sealed record Candidate(
    Guid Id,
    string? CurrentSourceProfileKey,
    string? CanonicalSourceUrl,
    string DisplayName,
    string? NormalizedLocation,
    TechnicalProfileStatus TechnicalStatus,
    FounderCommitmentStatus CommitmentStatus,
    IdeaCommitmentStatus IdeaCommitmentStatus,
    CandidateStatus Status,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc,
    string? LastActivityText,
    DateTimeOffset? LastActivityAtUtc,
    Guid? CurrentSnapshotId,
    Guid? LatestEvaluationId,
    decimal? FounderQualityScore,
    decimal? OurFitScore,
    decimal? Confidence,
    decimal? ActivityScore,
    decimal? RiskPenalty,
    decimal? InvitationPriority,
    string? AnalysisWorkerId,
    DateTimeOffset? AnalysisClaimedAtUtc,
    DateTimeOffset? AnalysisClaimExpiresAtUtc,
    int AnalysisAttemptCount,
    string? LastAnalysisErrorCode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version,
    Guid? MergedIntoCandidateId = null);

/// <summary>One immutable captured profile version.</summary>
public sealed record ProfileSnapshot(
    Guid Id,
    Guid CandidateId,
    string SourceAccountId,
    string SourceSegmentId,
    string SourceProfileKey,
    string? CanonicalSourceUrl,
    string ContentHash,
    string ParserVersion,
    string SourceAdapterVersion,
    DateTimeOffset CapturedAtUtc,
    string RawArtifactRelativePath,
    DateTimeOffset? DeleteAfterUtc,
    string? NormalizedProfileJson,
    string RawContentHash,
    string? NormalizedProfileHash,
    string? EvaluatorInputHash,
    string? EvidenceJson,
    string? RedactionJson,
    decimal ExtractionCompleteness,
    decimal ExtractionConfidence,
    ProfileSnapshotStatus Status,
    string? ErrorCode,
    string? ProcessingWorkerId,
    DateTimeOffset? ProcessingClaimedAtUtc,
    DateTimeOffset? ProcessingClaimExpiresAtUtc,
    int ProcessingAttemptCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? RawArtifactDeletedAtUtc = null,
    string? RawArtifactDeletionReasonCode = null);

/// <summary>One normalized candidate identity signal.</summary>
public sealed record CandidateIdentityAlias(
    Guid Id,
    Guid CandidateId,
    CandidateIdentityAliasType AliasType,
    string NormalizedValueHash,
    string? SourceAccountId,
    decimal Confidence,
    bool IsStrong,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);

/// <summary>One deterministic fast-screen decision.</summary>
public sealed record ScreeningDecision(
    Guid Id,
    Guid CandidateId,
    Guid SnapshotId,
    string RulesetVersion,
    ScreeningOutcome Outcome,
    decimal? Score,
    string ReasonCodesJson,
    string EvidenceJson,
    string MissingEvidenceJson,
    string EvaluatorInputJson,
    string EvaluatorInputHash,
    bool IsManualOverride,
    DateTimeOffset CreatedAtUtc);

/// <summary>An additive, reviewable ambiguity between candidate identity signals.</summary>
public sealed record CandidateIdentityConflictRecord(
    Guid Id,
    Guid CandidateId,
    Guid? ConflictingCandidateId,
    CandidateIdentityAliasType AliasType,
    string NormalizedValueHash,
    string ReasonCode,
    decimal Confidence,
    bool Resolved,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    string? ResolutionAction);

/// <summary>One versioned structured evaluation; Stage 12 populates its behavior.</summary>
public sealed record Evaluation(
    Guid Id,
    Guid CandidateId,
    Guid SnapshotId,
    string ScorecardVersion,
    string EvaluatorProvider,
    string EvaluatorModel,
    string EvaluatorDeploymentVersion,
    string PromptVersion,
    string InputHash,
    decimal FounderQualityScore,
    decimal OurFitScore,
    decimal Confidence,
    decimal ActivityScore,
    decimal BaseScore,
    decimal RiskPenalty,
    decimal FinalScore,
    decimal InvitationPriority,
    string Recommendation,
    string StructuredEvaluationJson,
    string? RawProviderArtifactReference,
    EvaluationStatus Status,
    string? ErrorCode,
    int RetryCount,
    DateTimeOffset? RetryAfterUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);

/// <summary>One normalized category child of an evaluation.</summary>
public sealed record EvaluationCategory(
    Guid Id,
    Guid EvaluationId,
    string Key,
    decimal Score,
    decimal Maximum,
    string EvidenceJson,
    string? Reason,
    DateTimeOffset CreatedAtUtc);

/// <summary>One normalized risk child of an evaluation.</summary>
public sealed record EvaluationRisk(
    Guid Id,
    Guid EvaluationId,
    string Key,
    decimal Penalty,
    string EvidenceJson,
    string? Reason,
    DateTimeOffset CreatedAtUtc);

/// <summary>One human-review-only invitation draft.</summary>
public sealed record InvitationDraft(
    Guid Id,
    Guid EvaluationId,
    Guid CandidateId,
    string ShortDraft,
    string DetailedDraft,
    string FactsUsedJson,
    decimal Confidence,
    InvitationDraftStatus Status,
    string ValidationErrorsJson,
    string SimilarityFingerprint,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReviewedAtUtc,
    string? ReviewedBy,
    bool Superseded,
    long Version);

/// <summary>One immutable human edit derived from a generated invitation draft.</summary>
public sealed record InvitationDraftRevision(
    Guid Id,
    Guid SourceDraftId,
    Guid CandidateId,
    string ShortDraft,
    string DetailedDraft,
    string EditedBy,
    DateTimeOffset EditedAtUtc,
    bool ValidationPassed,
    string ValidationErrorsJson,
    decimal MaximumObservedSimilarity,
    string SimilarityFingerprint,
    bool IsActive);

/// <summary>One explicit, window-scoped local queue placement.</summary>
public sealed record InvitationQueueEntry(
    Guid Id,
    Guid WindowId,
    Guid CandidateId,
    InvitationQueueKind QueueKind,
    int Position,
    bool ManuallyIncluded,
    string AddedBy,
    DateTimeOffset AddedAtUtc,
    DateTimeOffset? RemovedAtUtc,
    string? RemovalReasonCode,
    long Version);

/// <summary>One append-only candidate timeline entry.</summary>
public sealed record CandidateAction(
    Guid Id,
    Guid CandidateId,
    CandidateActionType ActionType,
    DateTimeOffset OccurredAtUtc,
    string Actor,
    string? Notes,
    string DataJson,
    Guid? RelatedInvitationId,
    Guid? RelatedEvaluationId,
    Guid? RelatedRunId,
    string CorrelationId);

/// <summary>A user-managed invitation tracking window with no external reset-time assumption.</summary>
public sealed record ManualInvitationWindow(
    Guid Id,
    DateTimeOffset StartAtUtc,
    DateTimeOffset EndAtUtc,
    int PrimaryQueueSize,
    int ReserveQueueSize,
    int SentCount,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Version);

/// <summary>Metadata for one durable Founder Scout report file.</summary>
public sealed record ReportExport(
    Guid Id,
    string ReportType,
    string Format,
    string FilterSortHash,
    string RelativePath,
    int RowCount,
    string FileHash,
    long FileSize,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset DeleteAfterUtc);
