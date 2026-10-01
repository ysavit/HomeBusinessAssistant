using FounderScout.Domain;

namespace FounderScout.Application;

/// <summary>Stable server-side candidate sort choices.</summary>
public enum FounderScoutCandidateSort
{
    /// <summary>Invitation priority, confidence, activity, name, and ID descending/ascending tie-break order.</summary>
    PriorityDescending = 0,
    /// <summary>Founder quality first.</summary>
    FounderQualityDescending = 1,
    /// <summary>Local founder fit first.</summary>
    FitDescending = 2,
    /// <summary>Evaluation confidence first.</summary>
    ConfidenceDescending = 3,
    /// <summary>Activity score first.</summary>
    ActivityDescending = 4,
    /// <summary>Most recently captured first.</summary>
    LastCapturedDescending = 5,
    /// <summary>Display name and stable ID ascending.</summary>
    NameAscending = 6,
}

/// <summary>Bounded filters for raw-text-free candidate result projections.</summary>
public sealed record FounderScoutCandidateQuery(
    string? Search,
    IReadOnlyList<string>? Recommendations,
    IReadOnlyList<CandidateStatus>? Statuses,
    decimal? MinimumScore,
    decimal? MaximumScore,
    decimal? MinimumConfidence,
    TechnicalProfileStatus? TechnicalStatus,
    FounderCommitmentStatus? CommitmentStatus,
    IdeaCommitmentStatus? IdeaStatus,
    bool? HasTractionEvidence,
    string? RiskKey,
    DateTimeOffset? ChangedFromUtc,
    DateTimeOffset? ChangedToUtc,
    DateTimeOffset? ActiveSinceUtc,
    InvitationQueueKind? QueueKind,
    string? AccountId,
    string? SegmentId,
    bool? NeedsManualReview,
    FounderScoutCandidateSort Sort,
    int Offset,
    int PageSize);

/// <summary>One bounded candidate list row with no raw profile or model payload.</summary>
public sealed record FounderScoutCandidateListItem(
    int Rank,
    Guid CandidateId,
    string DisplayName,
    CandidateStatus Status,
    string? Recommendation,
    decimal? FounderQualityScore,
    decimal? OurFitScore,
    decimal? Confidence,
    decimal? ActivityScore,
    decimal? RiskPenalty,
    decimal? InvitationPriority,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset? LastActivityAtUtc,
    string? SourceAccountId,
    string? SourceSegmentId,
    InvitationQueueKind? QueueKind,
    int? QueuePosition,
    bool ProfileChanged,
    bool NeedsManualReview,
    bool HasTractionEvidence);

/// <summary>One stable bounded candidate result page.</summary>
public sealed record FounderScoutCandidateResultPage(
    IReadOnlyList<FounderScoutCandidateListItem> Items,
    int Offset,
    int PageSize,
    int TotalCount);

/// <summary>Safe dashboard metrics from persisted Founder Scout data.</summary>
public sealed record FounderScoutDashboard(
    int Candidates,
    int NewOrUpdated,
    int PendingScreening,
    int PendingAnalysis,
    int Analyzed,
    IReadOnlyDictionary<string, int> Recommendations,
    int DraftsReady,
    int DraftsNeedReview,
    int PrimaryQueue,
    int ReserveQueue,
    int ManuallySent,
    int Accepted,
    int CallsScheduled,
    int AttentionItems,
    IReadOnlyList<BrowserAccount> Accounts,
    IReadOnlyList<DiscoverySegment> Segments,
    IReadOnlyList<ReportExport> LatestReports);

/// <summary>One snapshot history item with a safe relevant-field-only diff.</summary>
public sealed record FounderScoutSnapshotHistoryItem(
    ProfileSnapshot Snapshot,
    IReadOnlyList<FounderProfileChange> Changes,
    bool RawArtifactAvailable);

/// <summary>One evaluation history entry and its normalized children.</summary>
public sealed record FounderScoutEvaluationHistoryItem(
    Evaluation Evaluation,
    IReadOnlyList<EvaluationCategory> Categories,
    IReadOnlyList<EvaluationRisk> Risks,
    IReadOnlyList<string> PositiveSignals,
    IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string> PriorityQuestions,
    IReadOnlyList<string> TopicsToValidate,
    string RecommendationRationale);

/// <summary>Complete bounded candidate detail projection.</summary>
public sealed record FounderScoutCandidateDetail(
    Candidate Candidate,
    NormalizedFounderProfile? Profile,
    string Summary,
    ScreeningDecision? LatestScreening,
    IReadOnlyList<FounderScoutSnapshotHistoryItem> Snapshots,
    IReadOnlyList<FounderScoutEvaluationHistoryItem> Evaluations,
    IReadOnlyList<InvitationDraft> Drafts,
    IReadOnlyList<InvitationDraftRevision> DraftRevisions,
    InvitationQueueEntry? QueueEntry,
    ManualInvitationWindow? InvitationWindow,
    IReadOnlyList<CandidateAction> Timeline,
    IReadOnlyList<CandidateIdentityConflictRecord> IdentityConflicts);

/// <summary>One queue row with its review-ready candidate and draft state.</summary>
public sealed record FounderScoutInvitationQueueItem(
    InvitationQueueEntry Entry,
    FounderScoutCandidateListItem Candidate,
    string? SourceUrl,
    InvitationDraft? GeneratedDraft,
    InvitationDraftRevision? ActiveRevision,
    bool Eligible,
    IReadOnlyList<string> EligibilityReasons);

/// <summary>One complete explicit queue/window projection.</summary>
public sealed record FounderScoutInvitationQueueView(
    ManualInvitationWindow Window,
    IReadOnlyList<FounderScoutInvitationQueueItem> Primary,
    IReadOnlyList<FounderScoutInvitationQueueItem> Reserve,
    IReadOnlyList<FounderScoutCandidateListItem> Suggestions);

/// <summary>Input for creating an immutable edited draft revision.</summary>
public sealed record SaveInvitationDraftRevisionRequest(
    Guid SourceDraftId,
    string ShortDraft,
    string DetailedDraft,
    string Actor,
    int MaximumCharacters,
    decimal SimilarityThreshold);

/// <summary>Input for an explicit local queue addition.</summary>
public sealed record AddInvitationQueueEntryRequest(
    Guid WindowId,
    Guid CandidateId,
    InvitationQueueKind QueueKind,
    bool ManuallyIncluded,
    string Actor);

/// <summary>Input for replacing one lane's complete queue order.</summary>
public sealed record ReorderInvitationQueueRequest(
    Guid WindowId,
    InvitationQueueKind QueueKind,
    IReadOnlyList<Guid> EntryIds,
    string Actor);

/// <summary>Input for one explicit manual outcome or internal note.</summary>
public sealed record RecordFounderScoutOutcomeRequest(
    Guid CandidateId,
    CandidateStatus Outcome,
    string ReasonCode,
    string? Notes,
    Guid? DraftId,
    Guid? WindowId,
    long? WindowVersion,
    DateTimeOffset OccurredAtUtc,
    string Actor,
    string CorrelationId);

/// <summary>Result of applying raw profile retention.</summary>
public sealed record FounderScoutRetentionResult(
    int Considered,
    int Deleted,
    int AlreadyMissing,
    int SkippedActiveOrDiagnostic,
    int Failed,
    IReadOnlyList<string> FailureCodes);

/// <summary>Result of explicitly queueing locally stored, screened candidates for AI evaluation.</summary>
public sealed record FounderScoutAnalysisQueueResult(int Queued, int AlreadyPending, int Eligible);

/// <summary>One report candidate shared by every top-candidate export format.</summary>
public sealed record FounderScoutReportCandidate(
    FounderScoutCandidateListItem Candidate,
    string Summary,
    IReadOnlyList<EvaluationCategory> Categories,
    IReadOnlyList<EvaluationRisk> Risks,
    IReadOnlyList<string> PositiveSignals,
    IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<string> PriorityQuestions,
    string? ShortDraft,
    string? DetailedDraft,
    string? SourceUrl,
    InvitationQueueKind? QueueKind,
    int? QueuePosition,
    string EvaluationVersion);

/// <summary>One canonical selected candidate set used by all report renderers.</summary>
public sealed record FounderScoutReportModel(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset GeneratedAtLocal,
    string FilterSortHash,
    string ScorecardVersions,
    int ReviewedCount,
    IReadOnlyList<FounderScoutReportCandidate> Candidates,
    IReadOnlyList<FounderScoutCandidateListItem> Attention,
    IReadOnlyDictionary<string, int> PositiveReasonDistribution,
    IReadOnlyDictionary<string, int> RiskReasonDistribution,
    IReadOnlyList<BrowserAccount> Accounts,
    IReadOnlyList<DiscoverySegment> Segments,
    FounderScoutInvitationQueueView? InvitationQueue);

/// <summary>Bounded Founder Scout read boundary for Host pages and reports.</summary>
public interface IFounderScoutResultsQuery
{
    /// <summary>Loads dashboard counts and attention state since the requested UTC boundary.</summary>
    ValueTask<FounderScoutDashboard> GetDashboardAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);
    /// <summary>Returns one stable raw-text-free result page.</summary>
    ValueTask<FounderScoutCandidateResultPage> QueryCandidatesAsync(FounderScoutCandidateQuery query, CancellationToken cancellationToken = default);
    /// <summary>Loads one evidence-rich bounded candidate detail projection.</summary>
    ValueTask<FounderScoutCandidateDetail?> GetCandidateAsync(Guid candidateId, CancellationToken cancellationToken = default);
    /// <summary>Loads the current invitation window, explicit lanes, and ranked suggestions.</summary>
    ValueTask<FounderScoutInvitationQueueView> GetInvitationQueueAsync(DateTimeOffset atUtc, int defaultPrimarySize, int defaultReserveSize, decimal minimumConfidence, CancellationToken cancellationToken = default);
    /// <summary>Lists browser accounts without session material.</summary>
    ValueTask<IReadOnlyList<BrowserAccount>> GetBrowserAccountsAsync(CancellationToken cancellationToken = default);
    /// <summary>Lists discovery segments and aggregate yield.</summary>
    ValueTask<IReadOnlyList<DiscoverySegment>> GetDiscoverySegmentsAsync(CancellationToken cancellationToken = default);
    /// <summary>Lists bounded durable report metadata.</summary>
    ValueTask<IReadOnlyList<ReportExport>> GetReportsAsync(int maximumResults, CancellationToken cancellationToken = default);
    /// <summary>Builds the one canonical ordered report model.</summary>
    ValueTask<FounderScoutReportModel> BuildReportModelAsync(int top, DateTimeOffset atUtc, decimal minimumConfidence, CancellationToken cancellationToken = default);
}

/// <summary>Focused local Founder Scout mutations. This interface intentionally has no send method.</summary>
public interface IFounderScoutResultsCommands
{
    /// <summary>
    /// Queues a bounded number of stored candidates that have a current normalized snapshot and screening
    /// decision but no AI evaluation. This explicit local action may include deterministic monitor/filter rows.
    /// </summary>
    ValueTask<FounderScoutAnalysisQueueResult> QueueUnanalyzedForAnalysisAsync(
        int maximumCandidates,
        string actor,
        CancellationToken cancellationToken = default);
    /// <summary>Explicitly queues one screened candidate, including a requested repeat evaluation.</summary>
    ValueTask<bool> QueueCandidateForAnalysisAsync(Guid candidateId, string actor, CancellationToken cancellationToken = default);
    /// <summary>Appends and activates one human-edited draft revision.</summary>
    ValueTask<InvitationDraftRevision> SaveDraftRevisionAsync(SaveInvitationDraftRevisionRequest request, CancellationToken cancellationToken = default);
    /// <summary>Marks a generated draft reviewed for manual use.</summary>
    ValueTask<InvitationDraft> MarkDraftReviewedAsync(Guid draftId, string actor, CancellationToken cancellationToken = default);
    /// <summary>Creates a new user-managed invitation window without deleting history.</summary>
    ValueTask<ManualInvitationWindow> CreateInvitationWindowAsync(DateTimeOffset startAtUtc, DateTimeOffset endAtUtc, int primarySize, int reserveSize, string? notes, CancellationToken cancellationToken = default);
    /// <summary>Adds one explicit primary or reserve entry after eligibility validation.</summary>
    ValueTask<InvitationQueueEntry> AddToQueueAsync(AddInvitationQueueEntryRequest request, decimal minimumConfidence, int staleAfterDays, CancellationToken cancellationToken = default);
    /// <summary>Removes one active entry while preserving history.</summary>
    ValueTask RemoveFromQueueAsync(Guid entryId, string actor, string reasonCode, CancellationToken cancellationToken = default);
    /// <summary>Replaces one lane's complete explicit order.</summary>
    ValueTask ReorderQueueAsync(ReorderInvitationQueueRequest request, CancellationToken cancellationToken = default);
    /// <summary>Records one legal human-confirmed lifecycle outcome.</summary>
    ValueTask<Candidate> RecordOutcomeAsync(RecordFounderScoutOutcomeRequest request, CancellationToken cancellationToken = default);
    /// <summary>Enables or disables one browser account without touching its profile files.</summary>
    ValueTask<BrowserAccount> SetBrowserAccountEnabledAsync(string accountId, bool enabled, CancellationToken cancellationToken = default);
    /// <summary>Clears bounded attention metadata after explicit manual review.</summary>
    ValueTask<BrowserAccount> ClearBrowserAccountAttentionAsync(string accountId, string actor, CancellationToken cancellationToken = default);
    /// <summary>Pauses or resumes one discovery segment.</summary>
    ValueTask<DiscoverySegment> SetDiscoverySegmentPausedAsync(string segmentId, bool paused, CancellationToken cancellationToken = default);
    /// <summary>Deletes eligible raw files while retaining every derived record.</summary>
    ValueTask<FounderScoutRetentionResult> ApplyRawRetentionAsync(DateTimeOffset nowUtc, Guid? candidateId, bool includeCurrentSnapshot, string actor, CancellationToken cancellationToken = default);
}

/// <summary>A bounded cross-process lease for one report writer.</summary>
public sealed record FounderScoutReportLease(string Resource, string OwnerId, long FencingToken, DateTimeOffset ExpiresAtUtc);

/// <summary>Serializes report writers across Host/Runner processes.</summary>
public interface IFounderScoutReportLeaseRepository
{
    /// <summary>Attempts to acquire the single bounded report writer lease.</summary>
    ValueTask<FounderScoutReportLease?> TryAcquireAsync(string ownerId, TimeSpan duration, CancellationToken cancellationToken = default);
    /// <summary>Releases the lease only when its owner and fencing token still match.</summary>
    ValueTask ReleaseAsync(FounderScoutReportLease lease, CancellationToken cancellationToken = default);
}
