namespace FounderScout.Domain;

/// <summary>The durable health of one manually authenticated browser account.</summary>
public enum BrowserSessionStatus
{
    /// <summary>No health observation has been recorded.</summary>
    Unknown = 0,
    /// <summary>The manually authenticated session is healthy.</summary>
    Healthy = 1,
    /// <summary>Manual reauthentication is required.</summary>
    ReauthenticationRequired = 2,
    /// <summary>The source denied access.</summary>
    AccessDenied = 3,
    /// <summary>The source reported throttling.</summary>
    Throttled = 4,
    /// <summary>An access challenge was detected.</summary>
    ChallengeDetected = 5,
    /// <summary>The source adapter failed its parser-health check.</summary>
    ParserFailure = 6,
    /// <summary>The account is explicitly disabled.</summary>
    Disabled = 7,
}

/// <summary>The durable lifecycle of one founder candidate.</summary>
public enum CandidateStatus
{
    /// <summary>The identity was first observed.</summary>
    Discovered = 0,
    /// <summary>Raw profile content was durably captured.</summary>
    Captured = 1,
    /// <summary>The captured profile was parsed.</summary>
    Parsed = 2,
    /// <summary>Deterministic screening excluded the candidate.</summary>
    FilteredOut = 3,
    /// <summary>The candidate is waiting for analysis.</summary>
    PendingAnalysis = 4,
    /// <summary>A worker currently holds the analysis claim.</summary>
    Analyzing = 5,
    /// <summary>Analysis completed.</summary>
    Analyzed = 6,
    /// <summary>The candidate is shortlisted.</summary>
    Shortlisted = 7,
    /// <summary>The candidate remains under observation.</summary>
    Monitor = 8,
    /// <summary>The candidate was passed over.</summary>
    Passed = 9,
    /// <summary>The candidate is in the manual invitation queue.</summary>
    QueuedForInvite = 10,
    /// <summary>A human reviewed the invitation message.</summary>
    MessageReviewed = 11,
    /// <summary>A human recorded that the invitation was sent.</summary>
    ManuallySent = 12,
    /// <summary>The candidate accepted the invitation.</summary>
    Accepted = 13,
    /// <summary>The candidate declined the invitation.</summary>
    Declined = 14,
    /// <summary>No response was recorded.</summary>
    NoResponse = 15,
    /// <summary>A call was scheduled.</summary>
    CallScheduled = 16,
    /// <summary>The candidate was passed over after a call.</summary>
    PassedAfterCall = 17,
    /// <summary>A trial project is active.</summary>
    TrialProject = 18,
    /// <summary>The candidate was selected.</summary>
    Selected = 19,
    /// <summary>Deterministic processing requires a human identity, redaction, or content review.</summary>
    ManualReview = 20,
    /// <summary>A human recorded that the first founder conversation completed.</summary>
    CallCompleted = 21,
    /// <summary>A human recorded that a second founder conversation is planned or completed.</summary>
    SecondCall = 22,
}

/// <summary>Normalized technical-founder posture without demographic attributes.</summary>
public enum TechnicalProfileStatus
{
    /// <summary>The profile did not establish technical posture.</summary>
    Unknown = 0,
    /// <summary>The profile is technical.</summary>
    Technical = 1,
    /// <summary>The profile is non-technical.</summary>
    NonTechnical = 2,
    /// <summary>The profile reports mixed technical posture.</summary>
    Mixed = 3,
}

/// <summary>Normalized founder commitment posture.</summary>
public enum FounderCommitmentStatus
{
    /// <summary>Commitment was not established.</summary>
    Unknown = 0,
    /// <summary>Full-time founder commitment was reported.</summary>
    FullTime = 1,
    /// <summary>Part-time founder commitment was reported.</summary>
    PartTime = 2,
    /// <summary>The founder is open to full-time commitment.</summary>
    OpenToFullTime = 3,
    /// <summary>The profile states no founder commitment.</summary>
    NotCommitted = 4,
}

/// <summary>Normalized idea commitment posture.</summary>
public enum IdeaCommitmentStatus
{
    /// <summary>Idea posture was not established.</summary>
    Unknown = 0,
    /// <summary>The founder is committed to a current idea.</summary>
    Committed = 1,
    /// <summary>The founder is open to other ideas.</summary>
    OpenToIdeas = 2,
    /// <summary>The founder reports no current idea.</summary>
    NoIdea = 3,
}

/// <summary>The durable processing state of a captured profile snapshot.</summary>
public enum ProfileSnapshotStatus
{
    /// <summary>Raw content was captured.</summary>
    Captured = 0,
    /// <summary>Normalized parsing completed.</summary>
    Parsed = 1,
    /// <summary>The snapshot is awaiting analysis.</summary>
    PendingAnalysis = 2,
    /// <summary>Analysis completed for the snapshot.</summary>
    AnalysisCompleted = 3,
    /// <summary>Snapshot processing failed.</summary>
    Failed = 4,
    /// <summary>A later snapshot superseded this version.</summary>
    Superseded = 5,
}

/// <summary>The durable lifecycle of a structured evaluation.</summary>
public enum EvaluationStatus
{
    /// <summary>The evaluation is pending.</summary>
    Pending = 0,
    /// <summary>A worker holds the evaluation claim.</summary>
    Claimed = 1,
    /// <summary>A bounded retry is pending.</summary>
    RetryPending = 2,
    /// <summary>The evaluation completed.</summary>
    Completed = 3,
    /// <summary>The evaluation failed permanently.</summary>
    FailedPermanent = 4,
    /// <summary>The structured output is retained but deterministic validation requires a human.</summary>
    NeedsReview = 5,
}

/// <summary>The durable validation and human-review state of an invitation draft.</summary>
public enum InvitationDraftStatus
{
    /// <summary>The draft has not been validated.</summary>
    Draft = 0,
    /// <summary>The draft passed deterministic validation.</summary>
    Valid = 1,
    /// <summary>The draft failed deterministic validation.</summary>
    Invalid = 2,
    /// <summary>A human reviewed the draft.</summary>
    Reviewed = 3,
    /// <summary>A human recorded that the draft was sent.</summary>
    ManuallySent = 4,
    /// <summary>A newer draft superseded this draft.</summary>
    Superseded = 5,
    /// <summary>The draft is retained for explicit human review after deterministic validation failed.</summary>
    NeedsReview = 6,
}

/// <summary>The explicit local invitation planning lane.</summary>
public enum InvitationQueueKind
{
    /// <summary>The user-managed primary review queue.</summary>
    Primary = 0,
    /// <summary>The user-managed reserve queue.</summary>
    Reserve = 1,
}

/// <summary>The strength and origin of one candidate identity alias.</summary>
public enum CandidateIdentityAliasType
{
    /// <summary>A stable source-provided profile key.</summary>
    SourceKey = 0,
    /// <summary>A normalized canonical profile URL.</summary>
    CanonicalUrl = 1,
    /// <summary>A weaker derived fingerprint.</summary>
    Fingerprint = 2,
    /// <summary>An explicit user-confirmed merge identity.</summary>
    ManualMerge = 3,
    /// <summary>A stable source-specific identifier extracted from trusted source metadata.</summary>
    TrustedSourceId = 4,
}

/// <summary>The deterministic screening result for one snapshot.</summary>
public enum ScreeningOutcome
{
    /// <summary>No screening decision exists yet.</summary>
    Pending = 0,
    /// <summary>The snapshot passed screening.</summary>
    Passed = 1,
    /// <summary>The snapshot was filtered out.</summary>
    FilteredOut = 2,
    /// <summary>The snapshot requires human review.</summary>
    NeedsReview = 3,
    /// <summary>The snapshot should proceed to deep analysis.</summary>
    DeepAnalyze = 4,
    /// <summary>The snapshot is retained for later observation without deep analysis.</summary>
    Monitor = 5,
    /// <summary>The snapshot requires human review before automated routing.</summary>
    ManualReview = 6,
}

/// <summary>Stable action names for the append-only candidate timeline.</summary>
public enum CandidateActionType
{
    /// <summary>A candidate was first created.</summary>
    CandidateDiscovered = 0,
    /// <summary>An identity alias was attached.</summary>
    IdentityAttached = 1,
    /// <summary>A raw snapshot was captured.</summary>
    SnapshotCaptured = 2,
    /// <summary>The candidate lifecycle changed.</summary>
    StateTransitioned = 3,
    /// <summary>An analysis worker acquired the candidate.</summary>
    AnalysisClaimed = 4,
    /// <summary>An analysis worker released the candidate.</summary>
    AnalysisReleased = 5,
    /// <summary>Candidate analysis completed.</summary>
    AnalysisCompleted = 6,
    /// <summary>An invitation draft was created.</summary>
    InvitationDraftCreated = 7,
    /// <summary>A human reviewed an invitation draft.</summary>
    InvitationDraftReviewed = 8,
    /// <summary>A human recorded an invitation send.</summary>
    InvitationMarkedSent = 9,
    /// <summary>A post-invitation outcome was recorded.</summary>
    OutcomeRecorded = 10,
    /// <summary>A user explicitly merged identities.</summary>
    ManualMerge = 11,
    /// <summary>A captured snapshot was normalized.</summary>
    ProfileNormalized = 12,
    /// <summary>Relevant normalized profile fields changed.</summary>
    ProfileChanged = 13,
    /// <summary>A deterministic screening decision was recorded.</summary>
    ScreeningCompleted = 14,
    /// <summary>An ambiguous identity signal was recorded for review.</summary>
    IdentityConflictRecorded = 15,
    /// <summary>A user overrode a reversible screening decision.</summary>
    ScreeningOverridden = 16,
    /// <summary>A candidate was added to a local invitation queue.</summary>
    InvitationQueueAdded = 17,
    /// <summary>A candidate was removed from a local invitation queue.</summary>
    InvitationQueueRemoved = 18,
    /// <summary>A local invitation queue was explicitly reordered.</summary>
    InvitationQueueReordered = 19,
    /// <summary>A human created an immutable edited draft revision.</summary>
    InvitationDraftRevisionCreated = 20,
    /// <summary>Raw snapshot evidence was deleted under a retention policy.</summary>
    RawProfileDeleted = 21,
    /// <summary>A browser account state was explicitly reviewed by a user.</summary>
    BrowserAccountReviewed = 22,
    /// <summary>A discovery segment state or safe configuration changed.</summary>
    DiscoverySegmentChanged = 23,
}

/// <summary>A typed, stable rejection of a requested domain transition.</summary>
public sealed record FounderScoutTransitionError(
    string Code,
    string Message,
    string From,
    string To);

/// <summary>The result of validating one requested domain transition.</summary>
public sealed record FounderScoutTransitionResult<TState>(
    bool IsAllowed,
    TState State,
    string ReasonCode,
    FounderScoutTransitionError? Error)
    where TState : struct, Enum;

/// <summary>Central candidate lifecycle transition policy.</summary>
public static class CandidateLifecycle
{
    private static readonly Dictionary<CandidateStatus, CandidateStatus[]> AllowedTransitions =
        new Dictionary<CandidateStatus, CandidateStatus[]>
        {
            [CandidateStatus.Discovered] = [CandidateStatus.Captured],
            [CandidateStatus.Captured] = [CandidateStatus.Parsed, CandidateStatus.FilteredOut, CandidateStatus.Monitor, CandidateStatus.ManualReview, CandidateStatus.PendingAnalysis],
            [CandidateStatus.Parsed] = [CandidateStatus.PendingAnalysis, CandidateStatus.FilteredOut, CandidateStatus.Monitor, CandidateStatus.ManualReview],
            [CandidateStatus.FilteredOut] = [CandidateStatus.PendingAnalysis, CandidateStatus.ManualReview],
            [CandidateStatus.PendingAnalysis] = [CandidateStatus.Analyzing, CandidateStatus.FilteredOut, CandidateStatus.Monitor, CandidateStatus.ManualReview],
            [CandidateStatus.Analyzing] = [CandidateStatus.PendingAnalysis, CandidateStatus.Analyzed, CandidateStatus.Shortlisted, CandidateStatus.Monitor, CandidateStatus.Passed, CandidateStatus.FilteredOut, CandidateStatus.ManualReview],
            [CandidateStatus.Analyzed] = [CandidateStatus.Shortlisted, CandidateStatus.Monitor, CandidateStatus.Passed, CandidateStatus.PendingAnalysis],
            [CandidateStatus.Shortlisted] = [CandidateStatus.QueuedForInvite, CandidateStatus.Monitor, CandidateStatus.Passed, CandidateStatus.PendingAnalysis],
            [CandidateStatus.Monitor] = [CandidateStatus.PendingAnalysis, CandidateStatus.Shortlisted, CandidateStatus.Passed, CandidateStatus.ManualReview],
            [CandidateStatus.Passed] = [CandidateStatus.PendingAnalysis, CandidateStatus.Shortlisted],
            [CandidateStatus.QueuedForInvite] = [CandidateStatus.MessageReviewed, CandidateStatus.Shortlisted, CandidateStatus.PendingAnalysis],
            [CandidateStatus.MessageReviewed] = [CandidateStatus.ManuallySent, CandidateStatus.QueuedForInvite, CandidateStatus.PendingAnalysis],
            [CandidateStatus.ManuallySent] = [CandidateStatus.Accepted, CandidateStatus.Declined, CandidateStatus.NoResponse, CandidateStatus.CallScheduled],
            [CandidateStatus.Accepted] = [CandidateStatus.CallScheduled],
            [CandidateStatus.NoResponse] = [CandidateStatus.CallScheduled],
            [CandidateStatus.CallScheduled] = [CandidateStatus.CallCompleted, CandidateStatus.PassedAfterCall, CandidateStatus.TrialProject, CandidateStatus.Selected, CandidateStatus.Passed],
            [CandidateStatus.CallCompleted] = [CandidateStatus.SecondCall, CandidateStatus.PassedAfterCall, CandidateStatus.TrialProject, CandidateStatus.Selected, CandidateStatus.Passed],
            [CandidateStatus.SecondCall] = [CandidateStatus.PassedAfterCall, CandidateStatus.TrialProject, CandidateStatus.Selected, CandidateStatus.Passed],
            [CandidateStatus.PassedAfterCall] = [CandidateStatus.TrialProject, CandidateStatus.Selected, CandidateStatus.Passed],
            [CandidateStatus.TrialProject] = [CandidateStatus.Selected, CandidateStatus.Passed],
            [CandidateStatus.ManualReview] = [CandidateStatus.PendingAnalysis, CandidateStatus.FilteredOut, CandidateStatus.Monitor, CandidateStatus.Parsed],
            [CandidateStatus.Declined] = [],
            [CandidateStatus.Selected] = [],
        };

    /// <summary>Validates one requested candidate transition.</summary>
    public static FounderScoutTransitionResult<CandidateStatus> Validate(
        CandidateStatus current,
        CandidateStatus requested,
        string reasonCode)
    {
        ValidateReason(reasonCode);
        return AllowedTransitions.TryGetValue(current, out CandidateStatus[]? allowed)
            && allowed.Contains(requested)
            ? Allowed(requested, reasonCode)
            : Rejected(current, requested, reasonCode, "candidate.transition.illegal");
    }

    private static FounderScoutTransitionResult<CandidateStatus> Allowed(CandidateStatus state, string reasonCode) =>
        new(true, state, reasonCode, null);

    private static FounderScoutTransitionResult<CandidateStatus> Rejected(
        CandidateStatus current,
        CandidateStatus requested,
        string reasonCode,
        string code) =>
        new(false, current, reasonCode, new(code, $"Candidate cannot transition from {current} to {requested}.", current.ToString(), requested.ToString()));

    private static void ValidateReason(string reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode) || reasonCode.Length > 128)
        {
            throw new ArgumentException("A bounded transition reason code is required.", nameof(reasonCode));
        }
    }
}

/// <summary>Central browser-account health transition policy.</summary>
public static class BrowserAccountLifecycle
{
    /// <summary>Validates one requested browser account transition.</summary>
    public static FounderScoutTransitionResult<BrowserSessionStatus> Validate(
        BrowserSessionStatus current,
        BrowserSessionStatus requested,
        string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        bool allowed = current == requested
            || current != BrowserSessionStatus.Disabled
            || requested == BrowserSessionStatus.Unknown;
        return allowed
            ? new(true, requested, reasonCode, null)
            : new(false, current, reasonCode, new(
                "browserAccount.transition.illegal",
                "A disabled browser account must be explicitly re-enabled before another health state is recorded.",
                current.ToString(),
                requested.ToString()));
    }
}

/// <summary>Central snapshot processing transition policy.</summary>
public static class ProfileSnapshotLifecycle
{
    private static readonly Dictionary<ProfileSnapshotStatus, ProfileSnapshotStatus[]> Allowed =
        new Dictionary<ProfileSnapshotStatus, ProfileSnapshotStatus[]>
        {
            [ProfileSnapshotStatus.Captured] = [ProfileSnapshotStatus.Parsed, ProfileSnapshotStatus.Failed, ProfileSnapshotStatus.Superseded],
            [ProfileSnapshotStatus.Parsed] = [ProfileSnapshotStatus.PendingAnalysis, ProfileSnapshotStatus.Failed, ProfileSnapshotStatus.Superseded],
            [ProfileSnapshotStatus.PendingAnalysis] = [ProfileSnapshotStatus.AnalysisCompleted, ProfileSnapshotStatus.Failed, ProfileSnapshotStatus.Superseded],
            [ProfileSnapshotStatus.AnalysisCompleted] = [ProfileSnapshotStatus.Superseded],
            [ProfileSnapshotStatus.Failed] = [ProfileSnapshotStatus.PendingAnalysis, ProfileSnapshotStatus.Superseded],
            [ProfileSnapshotStatus.Superseded] = [],
        };

    /// <summary>Validates one requested snapshot transition.</summary>
    public static FounderScoutTransitionResult<ProfileSnapshotStatus> Validate(
        ProfileSnapshotStatus current,
        ProfileSnapshotStatus requested,
        string reasonCode) =>
        ValidateMapped(Allowed, current, requested, reasonCode, "snapshot.transition.illegal");

    private static FounderScoutTransitionResult<ProfileSnapshotStatus> ValidateMapped(
        Dictionary<ProfileSnapshotStatus, ProfileSnapshotStatus[]> map,
        ProfileSnapshotStatus current,
        ProfileSnapshotStatus requested,
        string reasonCode,
        string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        return map.TryGetValue(current, out ProfileSnapshotStatus[]? allowed) && allowed.Contains(requested)
            ? new(true, requested, reasonCode, null)
            : new(false, current, reasonCode, new(code, $"State cannot transition from {current} to {requested}.", current.ToString(), requested.ToString()));
    }
}

/// <summary>Central evaluation processing transition policy.</summary>
public static class EvaluationLifecycle
{
    /// <summary>Validates one requested evaluation transition.</summary>
    public static FounderScoutTransitionResult<EvaluationStatus> Validate(
        EvaluationStatus current,
        EvaluationStatus requested,
        string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        bool allowed = (current, requested) switch
        {
            (EvaluationStatus.Pending, EvaluationStatus.Claimed) => true,
            (EvaluationStatus.Claimed, EvaluationStatus.Completed or EvaluationStatus.RetryPending or EvaluationStatus.FailedPermanent) => true,
            (EvaluationStatus.RetryPending, EvaluationStatus.Claimed or EvaluationStatus.FailedPermanent) => true,
            _ => false,
        };
        return allowed
            ? new(true, requested, reasonCode, null)
            : new(false, current, reasonCode, new("evaluation.transition.illegal", $"Evaluation cannot transition from {current} to {requested}.", current.ToString(), requested.ToString()));
    }
}

/// <summary>Central invitation review and manual-send transition policy.</summary>
public static class InvitationLifecycle
{
    /// <summary>Validates one requested invitation transition.</summary>
    public static FounderScoutTransitionResult<InvitationDraftStatus> Validate(
        InvitationDraftStatus current,
        InvitationDraftStatus requested,
        string reasonCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);
        bool allowed = (current, requested) switch
        {
            (InvitationDraftStatus.Draft, InvitationDraftStatus.Valid or InvitationDraftStatus.Invalid or InvitationDraftStatus.NeedsReview or InvitationDraftStatus.Superseded) => true,
            (InvitationDraftStatus.Valid, InvitationDraftStatus.Reviewed or InvitationDraftStatus.Superseded) => true,
            (InvitationDraftStatus.Invalid, InvitationDraftStatus.Valid or InvitationDraftStatus.NeedsReview or InvitationDraftStatus.Superseded) => true,
            (InvitationDraftStatus.NeedsReview, InvitationDraftStatus.Valid or InvitationDraftStatus.Reviewed or InvitationDraftStatus.Superseded) => true,
            (InvitationDraftStatus.Reviewed, InvitationDraftStatus.ManuallySent or InvitationDraftStatus.Superseded) => true,
            _ => false,
        };
        return allowed
            ? new(true, requested, reasonCode, null)
            : new(false, current, reasonCode, new("invitation.transition.illegal", $"Invitation cannot transition from {current} to {requested}.", current.ToString(), requested.ToString()));
    }
}
