using System.Text.Json;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Application.Onboarding;

/// <summary>The owner workflow which created an onboarding session.</summary>
public enum OnboardingSessionKind
{
    /// <summary>The installation has not completed its first owner setup.</summary>
    FirstRun = 0,
    /// <summary>The owner explicitly asked to add newly installed agents.</summary>
    AddAgents = 1,
    /// <summary>The owner explicitly asked to review one agent's setup.</summary>
    ReconfigureAgent = 2,
}

/// <summary>Durable lifecycle of one onboarding session.</summary>
public enum OnboardingSessionStatus
{
    /// <summary>The session currently accepts transitions and check results.</summary>
    InProgress = 0,
    /// <summary>The owner postponed setup and may resume the same session.</summary>
    Deferred = 1,
    /// <summary>All stages in the session completed.</summary>
    Completed = 2,
    /// <summary>The owner explicitly cancelled a non-required session.</summary>
    Cancelled = 3,
}

/// <summary>Stable scope used to group readiness cards and latest-check identity.</summary>
public enum OnboardingCheckScope
{
    /// <summary>Core Host and current-owner boundary.</summary>
    Platform = 0,
    /// <summary>Central persistence and protected local state.</summary>
    Persistence = 1,
    /// <summary>Installed Runner and agent package state.</summary>
    Packages = 2,
    /// <summary>Windows Task Scheduler and power integration.</summary>
    Windows = 3,
    /// <summary>Filesystem, disk, and backup state.</summary>
    Storage = 4,
    /// <summary>A check scoped to one installed agent.</summary>
    Agent = 5,
}

/// <summary>Safe owner-facing outcome of one readiness check.</summary>
public enum OnboardingCheckStatus
{
    /// <summary>The observed prerequisite is ready.</summary>
    Passed = 0,
    /// <summary>The owner may continue after explicitly acknowledging a caveat.</summary>
    Warning = 1,
    /// <summary>Continuing would be unsafe or cannot produce a working setup.</summary>
    Blocked = 2,
    /// <summary>The check has not run, expired, timed out, or could not establish a result.</summary>
    Unknown = 3,
    /// <summary>The check does not apply to the current installation.</summary>
    NotApplicable = 4,
}

/// <summary>Allow-listed durable onboarding steps.</summary>
public static class OnboardingSteps
{
    /// <summary>Platform readiness and warnings.</summary>
    public const string Readiness = "readiness";
    /// <summary>Honest Stage 19 handoff after readiness succeeds.</summary>
    public const string AgentSelectionPending = "agent-selection-pending";
    /// <summary>One or more explicitly selected agents are being configured.</summary>
    public const string AgentConfiguration = "agent-configuration";
    /// <summary>Conservative terminal marker for installations established before onboarding existed.</summary>
    public const string LegacyInstallation = "legacy-installation";

    /// <summary>Returns whether a persisted step is part of the known state machine.</summary>
    public static bool IsAllowed(string value) => value is
        Readiness or AgentSelectionPending or AgentConfiguration or LegacyInstallation;
}

/// <summary>One durable resumable onboarding session.</summary>
public sealed record OnboardingSession(
    Guid Id,
    string SchemaVersion,
    OnboardingSessionKind Kind,
    OnboardingSessionStatus Status,
    string CurrentStep,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? DeferredAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    string ActorId,
    Guid CorrelationId,
    long Revision,
    int WarningCount,
    int AcknowledgedWarningCount,
    int CompletedStepCount);

/// <summary>The latest persisted result for one session/check/agent identity.</summary>
public sealed record OnboardingCheckRecord(
    Guid Id,
    Guid SessionId,
    OnboardingCheckScope Scope,
    string CheckKey,
    AgentId? AgentId,
    OnboardingCheckStatus Status,
    string ReasonCode,
    string Title,
    string Message,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string DetailsSchemaVersion,
    string DetailsJson,
    string? RemediationKey);

/// <summary>Safe metadata for one composable readiness check.</summary>
public sealed record OnboardingReadinessCheckDefinition(
    string Key,
    OnboardingCheckScope Scope,
    string Title,
    string Description,
    bool Required,
    bool RequiresExplicitAction,
    TimeSpan Timeout,
    string? RemediationKey = null,
    AgentId? AgentId = null);

/// <summary>A safe readiness outcome before persistence assigns its row identity.</summary>
public sealed record OnboardingReadinessCheckResult(
    OnboardingReadinessCheckDefinition Definition,
    OnboardingCheckStatus Status,
    string ReasonCode,
    string Message,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string DetailsSchemaVersion,
    JsonElement Details,
    string? RemediationKey = null);

/// <summary>One catalog item merged with its current durable result for the UI.</summary>
public sealed record OnboardingCheckView(
    OnboardingReadinessCheckDefinition Definition,
    OnboardingCheckStatus Status,
    string ReasonCode,
    string Message,
    DateTimeOffset? ObservedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string DetailsSchemaVersion,
    string DetailsJson,
    string? RemediationKey);

/// <summary>Bounded evidence used only to distinguish a fresh install from an established upgrade.</summary>
public sealed record OnboardingInstallationEvidence(
    int AgentDefinitionCount,
    bool HasUserAuthoredConfiguration,
    bool HasSchedules,
    bool HasRuns)
{
    /// <summary>An established installation is never forced through first-run onboarding.</summary>
    public bool IsEstablished => HasUserAuthoredConfiguration || HasSchedules || HasRuns;
}

/// <summary>The result of atomically ensuring the one initial baseline session.</summary>
public sealed record EnsureInitialOnboardingResult(
    OnboardingSession Session,
    bool Created,
    bool BackfilledEstablishedInstallation);

/// <summary>An optimistic state transition request.</summary>
public sealed record OnboardingTransitionRequest(
    Guid SessionId,
    long ExpectedRevision,
    OnboardingSessionStatus TargetStatus,
    string CurrentStep,
    string ActorId,
    Guid CorrelationId,
    int AcknowledgedWarningCount,
    bool IncrementCompletedStepCount);

/// <summary>Dashboard behavior determined from durable onboarding state.</summary>
public sealed record OnboardingEntryDecision(
    OnboardingSession Session,
    bool RedirectToOnboarding,
    bool ShowResumeReminder,
    bool ShowReviewInvitation,
    string ReasonCode);

/// <summary>Complete current onboarding projection for a Razor Page.</summary>
public sealed record OnboardingOverview(
    OnboardingSession Session,
    IReadOnlyList<OnboardingCheckView> Checks,
    bool CanContinue,
    int BlockedCount,
    int WarningCount,
    int UnknownRequiredCount);

/// <summary>Persistence boundary for durable onboarding sessions and latest check results.</summary>
public interface IOnboardingRepository
{
    /// <summary>Reads evidence which cannot be confused with scanner/bootstrap defaults.</summary>
    ValueTask<OnboardingInstallationEvidence> GetInstallationEvidenceAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates or returns the unique first-run baseline transactionally.</summary>
    ValueTask<EnsureInitialOnboardingResult> EnsureInitialAsync(
        bool establishedInstallation,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates or returns the one currently active explicit readiness-review session.</summary>
    ValueTask<OnboardingSession> StartReadinessReviewAsync(
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets one session.</summary>
    ValueTask<OnboardingSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Gets the most relevant active, deferred, or initial session.</summary>
    ValueTask<OnboardingSession?> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets the latest persisted results for a session.</summary>
    ValueTask<IReadOnlyList<OnboardingCheckRecord>> GetChecksAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Atomically upserts a result batch and advances the session revision.</summary>
    ValueTask<OnboardingSession> SaveCheckBatchAsync(
        Guid sessionId,
        long expectedRevision,
        IReadOnlyList<OnboardingReadinessCheckResult> results,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>Applies one allow-listed optimistic lifecycle/step transition.</summary>
    ValueTask<OnboardingSession> TransitionAsync(
        OnboardingTransitionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>One bounded, independently failing readiness adapter.</summary>
public interface IOnboardingReadinessCheck
{
    /// <summary>Gets stable metadata used for orchestration and UI placeholders.</summary>
    OnboardingReadinessCheckDefinition Definition { get; }

    /// <summary>Runs the bounded check without leaking raw output or secrets.</summary>
    ValueTask<OnboardingReadinessCheckResult> EvaluateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Runs composable readiness checks with bounded concurrency and timeout isolation.</summary>
public interface IOnboardingReadinessRunner
{
    /// <summary>Gets stable check catalog metadata in deterministic order.</summary>
    IReadOnlyList<OnboardingReadinessCheckDefinition> Definitions { get; }

    /// <summary>Runs ordinary checks or only explicitly labeled probe checks.</summary>
    ValueTask<IReadOnlyList<OnboardingReadinessCheckResult>> RunAsync(
        bool explicitChecksOnly,
        CancellationToken cancellationToken = default);
}

/// <summary>Owner-facing onboarding query and command boundary.</summary>
public interface IOnboardingService
{
    /// <summary>Ensures initial state and decides dashboard entry behavior.</summary>
    ValueTask<OnboardingEntryDecision> GetEntryDecisionAsync(string actorId, CancellationToken cancellationToken = default);

    /// <summary>Loads the current session and its catalog-complete readiness projection.</summary>
    ValueTask<OnboardingOverview> GetOverviewAsync(Guid? sessionId, string actorId, CancellationToken cancellationToken = default);

    /// <summary>Runs all non-mutating readiness checks.</summary>
    ValueTask<OnboardingOverview> RunChecksAsync(Guid sessionId, long expectedRevision, string actorId, CancellationToken cancellationToken = default);

    /// <summary>Runs only explicitly labeled readiness probes, including protected storage.</summary>
    ValueTask<OnboardingOverview> RunExplicitProbesAsync(Guid sessionId, long expectedRevision, string actorId, CancellationToken cancellationToken = default);

    /// <summary>Starts an optional readiness review for an established installation.</summary>
    ValueTask<OnboardingSession> StartReadinessReviewAsync(string actorId, CancellationToken cancellationToken = default);

    /// <summary>Defers the current session without discarding its results.</summary>
    ValueTask<OnboardingSession> DeferAsync(Guid sessionId, long expectedRevision, string actorId, CancellationToken cancellationToken = default);

    /// <summary>Resumes a previously deferred session.</summary>
    ValueTask<OnboardingSession> ResumeAsync(Guid sessionId, long expectedRevision, string actorId, CancellationToken cancellationToken = default);

    /// <summary>Cancels an optional review without changing agent or schedule state.</summary>
    ValueTask<OnboardingSession> CancelAsync(Guid sessionId, long expectedRevision, string actorId, CancellationToken cancellationToken = default);

    /// <summary>Completes readiness and advances only to the Stage 19 handoff.</summary>
    ValueTask<OnboardingSession> ContinueAsync(
        Guid sessionId,
        long expectedRevision,
        bool acknowledgeWarnings,
        string actorId,
        CancellationToken cancellationToken = default);
}

/// <summary>Raised when another request advanced an onboarding session first.</summary>
public sealed class OnboardingConcurrencyException()
    : InvalidOperationException("The onboarding session changed in another window. Reload and try again.");
