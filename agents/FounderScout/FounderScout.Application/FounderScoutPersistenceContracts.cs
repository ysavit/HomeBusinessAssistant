using FounderScout.Domain;

namespace FounderScout.Application;

/// <summary>A normalized identity signal supplied to candidate resolution.</summary>
public sealed record CandidateIdentityInput(
    CandidateIdentityAliasType AliasType,
    string NormalizedValueHash,
    string? SourceAccountId,
    decimal Confidence,
    bool IsStrong);

/// <summary>Input for idempotently resolving or creating a candidate.</summary>
public sealed record ResolveCandidateRequest(
    Guid ProposedCandidateId,
    string SourceProfileKey,
    string? CanonicalSourceUrl,
    string DisplayName,
    DateTimeOffset SeenAtUtc,
    IReadOnlyList<CandidateIdentityInput> Identities,
    Guid? RelatedRunId,
    string CorrelationId);

/// <summary>The result of resolving one candidate identity.</summary>
public sealed record ResolveCandidateResult(
    Candidate Candidate,
    bool Created,
    IReadOnlyList<CandidateIdentityAlias> AttachedAliases);

/// <summary>A typed strong-identity ownership conflict.</summary>
public sealed record CandidateIdentityConflict(
    string Code,
    string NormalizedValueHash,
    Guid ExistingCandidateId,
    Guid RequestedCandidateId);

/// <summary>The result of attempting to attach one identity alias.</summary>
public sealed record AttachIdentityResult(
    CandidateIdentityAlias? Alias,
    bool Created,
    CandidateIdentityConflict? Conflict);

/// <summary>Input for atomically adding a captured snapshot and queueing its candidate.</summary>
public sealed record AddProfileSnapshotRequest(
    Guid SnapshotId,
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
    decimal ExtractionCompleteness,
    decimal ExtractionConfidence,
    ProfileSnapshotStatus Status,
    Guid? RelatedRunId,
    string CorrelationId);

/// <summary>The idempotent result of attaching one captured snapshot.</summary>
public sealed record AddProfileSnapshotResult(
    ProfileSnapshot Snapshot,
    Candidate Candidate,
    bool Created);

/// <summary>One legal candidate state change with append-only action context.</summary>
public sealed record CandidateTransitionRequest(
    Guid CandidateId,
    CandidateStatus RequestedStatus,
    string ReasonCode,
    string Actor,
    string? Notes,
    string DataJson,
    Guid? RelatedInvitationId,
    Guid? RelatedEvaluationId,
    Guid? RelatedRunId,
    string CorrelationId);

/// <summary>A ranked candidate query with stable pagination.</summary>
public sealed record CandidateRankingQuery(
    IReadOnlyList<CandidateStatus>? Statuses,
    decimal? MinimumPriority,
    int Offset,
    int PageSize);

/// <summary>A stable bounded candidate page.</summary>
public sealed record CandidatePage(
    IReadOnlyList<Candidate> Items,
    int Offset,
    int PageSize,
    int TotalCount);

/// <summary>One atomically acquired analysis work item.</summary>
public sealed record FounderScoutAnalysisClaim(
    Candidate Candidate,
    string WorkerId,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset ClaimExpiresAtUtc);

/// <summary>Input for releasing an analysis claim back to the queue.</summary>
public sealed record ReleaseAnalysisClaimRequest(
    Guid CandidateId,
    string WorkerId,
    string ReasonCode,
    bool Retry,
    Guid? RelatedRunId,
    string CorrelationId,
    CandidateStatus FailureStatus = CandidateStatus.ManualReview);

/// <summary>Input for completing an analysis claim.</summary>
public sealed record FinalizeAnalysisClaimRequest(
    Guid CandidateId,
    string WorkerId,
    CandidateStatus FinalStatus,
    Guid? EvaluationId,
    decimal? FounderQualityScore,
    decimal? OurFitScore,
    decimal? Confidence,
    decimal? ActivityScore,
    decimal? RiskPenalty,
    decimal? InvitationPriority,
    string ReasonCode,
    Guid? RelatedRunId,
    string CorrelationId);

/// <summary>A complete evaluation aggregate ready for persistence.</summary>
public sealed record EvaluationAggregate(
    Evaluation Evaluation,
    IReadOnlyList<EvaluationCategory> Categories,
    IReadOnlyList<EvaluationRisk> Risks);

/// <summary>A bounded invitation queue projection.</summary>
public sealed record InvitationQueuePage(
    IReadOnlyList<Candidate> Primary,
    IReadOnlyList<Candidate> Reserve,
    ManualInvitationWindow? Window);

/// <summary>Deterministic Founder Scout aggregate counts.</summary>
public sealed record FounderScoutDomainCounts(
    int BrowserAccounts,
    int EnabledBrowserAccounts,
    int DiscoverySegments,
    int Candidates,
    int Snapshots,
    int ScreeningDecisions,
    int Evaluations,
    int InvitationDrafts,
    int CandidateActions,
    int PendingAnalysis,
    int QueuedForInvite,
    int ReportExports,
    IReadOnlyDictionary<CandidateStatus, int> CandidatesByStatus);

/// <summary>One atomically acquired raw-snapshot processing item.</summary>
public sealed record FounderScoutProcessingClaim(
    Candidate Candidate,
    ProfileSnapshot Snapshot,
    string? CurrentNormalizedProfileJson,
    string? CurrentNormalizedProfileHash,
    string? CurrentEvaluatorInputHash,
    string WorkerId,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset ClaimExpiresAtUtc);

/// <summary>All deterministic output committed atomically for one claimed snapshot.</summary>
public sealed record CompleteFounderProfileProcessingRequest(
    Guid CandidateId,
    Guid SnapshotId,
    string WorkerId,
    string NormalizedProfileJson,
    string NormalizedProfileHash,
    string EvaluatorInputJson,
    string EvaluatorInputHash,
    string EvidenceJson,
    string RedactionJson,
    NormalizedFounderProfile Profile,
    FounderScreeningResult Screening,
    IReadOnlyList<CandidateIdentityInput> DerivedIdentities,
    IReadOnlyList<FounderProfileChange> Changes,
    Guid RelatedRunId,
    string CorrelationId);

/// <summary>Durable outcome of one processing commit.</summary>
public sealed record CompleteFounderProfileProcessingResult(
    Guid CandidateId,
    Guid SnapshotId,
    bool NormalizedChanged,
    bool ScreeningCreated,
    CandidateStatus CandidateStatus,
    int IdentityConflictsCreated);

/// <summary>Bounded processing failure release.</summary>
public sealed record FailFounderProfileProcessingRequest(
    Guid SnapshotId,
    string WorkerId,
    string ReasonCode,
    bool Retry,
    Guid RelatedRunId,
    string CorrelationId);

/// <summary>One deterministic screening manual override.</summary>
public sealed record ScreeningOverrideRequest(
    Guid CandidateId,
    Guid SnapshotId,
    ScreeningOutcome Outcome,
    string ReasonCode,
    string Actor,
    Guid? RelatedRunId,
    string CorrelationId);

/// <summary>Database-atomic raw-snapshot processing work queue.</summary>
#pragma warning disable CA1711 // The stage contract intentionally names a database work queue.
public interface IFounderScoutProcessingQueue
{
    /// <summary>Claims the next unprocessed raw snapshot with an expiring lease.</summary>
    ValueTask<FounderScoutProcessingClaim?> ClaimPendingProcessingAsync(string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    /// <summary>Atomically commits normalization, identities, screening, state, and actions.</summary>
    ValueTask<CompleteFounderProfileProcessingResult> CompleteProcessingAsync(CompleteFounderProfileProcessingRequest request, CancellationToken cancellationToken = default);
    /// <summary>Releases or permanently fails a processing claim owned by the worker.</summary>
    ValueTask<bool> FailProcessingAsync(FailFounderProfileProcessingRequest request, CancellationToken cancellationToken = default);
    /// <summary>Marks a browser account and segment unhealthy after repeated parser drift.</summary>
    ValueTask MarkParserFailureAsync(string accountId, string segmentId, string reasonCode, CancellationToken cancellationToken = default);
    /// <summary>Appends a reversible human screening override and audit action.</summary>
    ValueTask<ScreeningDecision> OverrideScreeningAsync(ScreeningOverrideRequest request, CancellationToken cancellationToken = default);
}
#pragma warning restore CA1711

/// <summary>Review and explicit reconciliation of candidate identity ambiguity.</summary>
public interface ICandidateIdentityConflictRepository
{
    /// <summary>Lists bounded unresolved identity ambiguities.</summary>
    ValueTask<IReadOnlyList<CandidateIdentityConflictRecord>> ListOpenAsync(int maximumResults, CancellationToken cancellationToken = default);
    /// <summary>Merges one candidate aggregate into another without discarding dependent evidence.</summary>
    ValueTask<Guid> ManualMergeAsync(Guid sourceCandidateId, Guid targetCandidateId, string actor, string reasonCode, Guid? relatedRunId, string correlationId, CancellationToken cancellationToken = default);
}

/// <summary>Focused persistence for browser account health and assignment.</summary>
public interface IBrowserAccountRepository
{
    /// <summary>Gets one browser account.</summary>
    ValueTask<BrowserAccount?> GetAsync(string accountId, CancellationToken cancellationToken = default);
    /// <summary>Lists all browser accounts in stable order.</summary>
    ValueTask<IReadOnlyList<BrowserAccount>> ListAsync(CancellationToken cancellationToken = default);
    /// <summary>Creates or updates one browser account.</summary>
    ValueTask<BrowserAccount> UpsertAsync(BrowserAccount account, CancellationToken cancellationToken = default);
    /// <summary>Applies one legal session-health transition.</summary>
    ValueTask<BrowserAccount> TransitionHealthAsync(
        string accountId,
        BrowserSessionStatus requested,
        string reasonCode,
        string? boundedMessage,
        DateTimeOffset? suspendedUntilUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>Focused persistence for configured discovery segments.</summary>
public interface IDiscoverySegmentRepository
{
    /// <summary>Gets one discovery segment.</summary>
    ValueTask<DiscoverySegment?> GetAsync(string segmentId, CancellationToken cancellationToken = default);
    /// <summary>Lists enabled non-paused discovery segments.</summary>
    ValueTask<IReadOnlyList<DiscoverySegment>> ListEnabledAsync(CancellationToken cancellationToken = default);
    /// <summary>Creates or updates one discovery segment.</summary>
    ValueTask<DiscoverySegment> UpsertAsync(DiscoverySegment segment, CancellationToken cancellationToken = default);
}

/// <summary>Append/read persistence for bounded discovery checkpoints.</summary>
public interface IDiscoveryCheckpointRepository
{
    /// <summary>Appends one immutable checkpoint.</summary>
    ValueTask AppendAsync(DiscoveryCheckpoint checkpoint, CancellationToken cancellationToken = default);
    /// <summary>Gets the newest checkpoint for an account and segment.</summary>
    ValueTask<DiscoveryCheckpoint?> GetLatestAsync(
        string accountId,
        string segmentId,
        CancellationToken cancellationToken = default);
}

/// <summary>Focused persistence for candidates and ranked pages.</summary>
public interface ICandidateRepository
{
    /// <summary>Gets one candidate.</summary>
    ValueTask<Candidate?> GetAsync(Guid candidateId, CancellationToken cancellationToken = default);
    /// <summary>Resolves or creates a candidate using strong identity aliases.</summary>
    ValueTask<ResolveCandidateResult> ResolveAsync(ResolveCandidateRequest request, CancellationToken cancellationToken = default);
    /// <summary>Applies one legal candidate transition and appends its action.</summary>
    ValueTask<Candidate> TransitionAsync(CandidateTransitionRequest request, CancellationToken cancellationToken = default);
    /// <summary>Returns one stable ranked candidate page.</summary>
    ValueTask<CandidatePage> QueryRankedAsync(CandidateRankingQuery query, CancellationToken cancellationToken = default);
}

/// <summary>Focused immutable snapshot persistence.</summary>
public interface IProfileSnapshotRepository
{
    /// <summary>Adds a snapshot only when its candidate/source-key/content identity is new.</summary>
    ValueTask<AddProfileSnapshotResult> AddIfNewAsync(
        AddProfileSnapshotRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>Gets one profile snapshot.</summary>
    ValueTask<ProfileSnapshot?> GetAsync(Guid snapshotId, CancellationToken cancellationToken = default);
    /// <summary>Lists bounded snapshot history for a candidate.</summary>
    ValueTask<IReadOnlyList<ProfileSnapshot>> ListForCandidateAsync(
        Guid candidateId,
        int maximumResults,
        CancellationToken cancellationToken = default);
}

/// <summary>Focused alias ownership persistence.</summary>
public interface ICandidateIdentityRepository
{
    /// <summary>Attaches an alias or returns a typed strong-ownership conflict.</summary>
    ValueTask<AttachIdentityResult> AttachAsync(
        Guid candidateId,
        CandidateIdentityInput identity,
        CancellationToken cancellationToken = default);
}

/// <summary>Append/read persistence for deterministic screening decisions.</summary>
public interface IScreeningRepository
{
    /// <summary>Appends one immutable screening decision.</summary>
    ValueTask AppendAsync(ScreeningDecision decision, CancellationToken cancellationToken = default);
    /// <summary>Gets the latest screening decision for a candidate.</summary>
    ValueTask<ScreeningDecision?> GetLatestAsync(Guid candidateId, CancellationToken cancellationToken = default);
}

/// <summary>Focused persistence for evaluation aggregates and state.</summary>
public interface IEvaluationRepository
{
    /// <summary>Adds one evaluation and its normalized children.</summary>
    ValueTask<EvaluationAggregate> AddAsync(EvaluationAggregate aggregate, CancellationToken cancellationToken = default);
    /// <summary>Gets one evaluation aggregate.</summary>
    ValueTask<EvaluationAggregate?> GetAsync(Guid evaluationId, CancellationToken cancellationToken = default);
    /// <summary>Gets one completed valid evaluation for the candidate/cache identity.</summary>
    ValueTask<EvaluationAggregate?> GetCompletedByInputHashAsync(
        Guid candidateId,
        string inputHash,
        CancellationToken cancellationToken = default);
    /// <summary>Applies one legal evaluation transition.</summary>
    ValueTask<Evaluation> TransitionAsync(
        Guid evaluationId,
        EvaluationStatus requested,
        string reasonCode,
        CancellationToken cancellationToken = default);
}

/// <summary>Focused persistence for human-review-only invitation drafts.</summary>
public interface IInvitationDraftRepository
{
    /// <summary>Creates a draft and supersedes earlier active drafts for the candidate.</summary>
    ValueTask<InvitationDraft> CreateAndSupersedePreviousAsync(
        InvitationDraft draft,
        CancellationToken cancellationToken = default);
    /// <summary>Gets a draft already persisted for one evaluation.</summary>
    ValueTask<InvitationDraft?> GetForEvaluationAsync(
        Guid evaluationId,
        CancellationToken cancellationToken = default);
    /// <summary>Lists bounded recent active drafts for cross-candidate similarity validation.</summary>
    ValueTask<IReadOnlyList<InvitationDraft>> ListRecentActiveAsync(
        int maximumResults,
        CancellationToken cancellationToken = default);
    /// <summary>Applies one legal validation or human-review transition.</summary>
    ValueTask<InvitationDraft> TransitionAsync(
        Guid draftId,
        InvitationDraftStatus requested,
        string reasonCode,
        string actor,
        CancellationToken cancellationToken = default);
}

/// <summary>Append-only candidate timeline writer.</summary>
public interface ICandidateActionWriter
{
    /// <summary>Appends one immutable candidate action.</summary>
    ValueTask AppendAsync(CandidateAction action, CancellationToken cancellationToken = default);
    /// <summary>Lists bounded recent actions for a candidate.</summary>
    ValueTask<IReadOnlyList<CandidateAction>> ListAsync(
        Guid candidateId,
        int maximumResults,
        CancellationToken cancellationToken = default);
}

/// <summary>Focused manual invitation queue/window persistence.</summary>
public interface IInvitationQueueRepository
{
    /// <summary>Creates or updates a user-managed invitation window.</summary>
    ValueTask<ManualInvitationWindow> SaveWindowAsync(
        ManualInvitationWindow window,
        CancellationToken cancellationToken = default);
    /// <summary>Gets the user-managed window containing the requested instant.</summary>
    ValueTask<ManualInvitationWindow?> GetCurrentWindowAsync(
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default);
    /// <summary>Conditionally increments the manually recorded send count.</summary>
    ValueTask<bool> TryIncrementSentCountAsync(
        Guid windowId,
        long expectedVersion,
        CancellationToken cancellationToken = default);
    /// <summary>Returns the primary and reserve invitation queues.</summary>
    ValueTask<InvitationQueuePage> QueryAsync(
        int primarySize,
        int reserveSize,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>Focused durable report metadata persistence.</summary>
public interface IReportExportRepository
{
    /// <summary>Appends immutable report metadata.</summary>
    ValueTask AppendAsync(ReportExport report, CancellationToken cancellationToken = default);
    /// <summary>Lists bounded recent report metadata.</summary>
    ValueTask<IReadOnlyList<ReportExport>> ListRecentAsync(
        int maximumResults,
        CancellationToken cancellationToken = default);
}

/// <summary>Database-atomic Founder Scout analysis work queue.</summary>
#pragma warning disable CA1711 // The Stage 09 public contract explicitly names IFounderScoutWorkQueue.
public interface IFounderScoutWorkQueue
{
    /// <summary>Claims the next pending candidate with a bounded lease.</summary>
    ValueTask<FounderScoutAnalysisClaim?> ClaimPendingAnalysisAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);
    /// <summary>Releases a claim only when the worker still owns it.</summary>
    ValueTask<bool> ReleaseAnalysisClaimAsync(
        ReleaseAnalysisClaimRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>Finalizes a claim only when the worker still owns it.</summary>
    ValueTask<bool> FinalizeAnalysisClaimAsync(
        FinalizeAnalysisClaimRequest request,
        CancellationToken cancellationToken = default);
}
#pragma warning restore CA1711

/// <summary>Bounded aggregate metric reads for diagnostics and generated reports.</summary>
public interface IFounderScoutMetricsReader
{
    /// <summary>Reads bounded deterministic aggregate counts.</summary>
    ValueTask<FounderScoutDomainCounts> GetCountsAsync(CancellationToken cancellationToken = default);
}
