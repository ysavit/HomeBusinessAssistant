using System.Text.Json;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Audit;

namespace HomeBusinessAssistant.Application.Onboarding;

/// <summary>Application-owned onboarding state, entry, readiness, warning, and audit orchestration.</summary>
public sealed class OnboardingService(
    IOnboardingRepository repository,
    IOnboardingReadinessRunner readiness,
    IAuditWriter audit,
    TimeProvider timeProvider) : IOnboardingService
{
    /// <inheritdoc />
    public async ValueTask<OnboardingEntryDecision> GetEntryDecisionAsync(
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId);
        OnboardingInstallationEvidence evidence = await repository.GetInstallationEvidenceAsync(cancellationToken).ConfigureAwait(false);
        Guid correlationId = Guid.NewGuid();
        EnsureInitialOnboardingResult ensured = await repository.EnsureInitialAsync(
            evidence.IsEstablished,
            actorId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        if (ensured.Created)
        {
            await WriteAuditAsync(
                ensured.BackfilledEstablishedInstallation ? "onboarding.legacy-backfilled" : "onboarding.started",
                ensured.Session,
                actorId,
                correlationId,
                new
                {
                    ensured.Session.Kind,
                    ensured.Session.Status,
                    ensured.Session.CurrentStep,
                    established = evidence.IsEstablished,
                    evidence.AgentDefinitionCount,
                    evidence.HasUserAuthoredConfiguration,
                    evidence.HasSchedules,
                    evidence.HasRuns,
                },
                cancellationToken).ConfigureAwait(false);
            if (ensured.BackfilledEstablishedInstallation)
            {
                await WriteAuditAsync(
                    "onboarding.completed",
                    ensured.Session,
                    actorId,
                    correlationId,
                    new { reason = "legacy-installation-backfill" },
                    cancellationToken).ConfigureAwait(false);
            }
        }

        OnboardingSession session = await repository.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
            ?? ensured.Session;
        bool freshInProgress = session.Kind == OnboardingSessionKind.FirstRun
            && session.Status == OnboardingSessionStatus.InProgress
            && session.CurrentStep is OnboardingSteps.Readiness
                or OnboardingSteps.AgentSelectionPending
                or OnboardingSteps.AgentConfiguration;
        bool deferred = session.Status == OnboardingSessionStatus.Deferred;
        bool optionalReviewInProgress = session.Status == OnboardingSessionStatus.InProgress
            && session.Kind != OnboardingSessionKind.FirstRun;
        bool legacy = session.Status == OnboardingSessionStatus.Completed
            && session.CurrentStep == OnboardingSteps.LegacyInstallation;
        return new(
            session,
            RedirectToOnboarding: freshInProgress,
            ShowResumeReminder: deferred || optionalReviewInProgress,
            ShowReviewInvitation: legacy,
            freshInProgress ? "onboarding.first-run-required"
                : deferred ? "onboarding.deferred"
                : optionalReviewInProgress ? "onboarding.review-in-progress"
                : legacy ? "onboarding.legacy-review-available"
                : "onboarding.not-required");
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingOverview> GetOverviewAsync(
        Guid? sessionId,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId);
        OnboardingSession? session = sessionId is Guid id
            ? await repository.GetAsync(id, cancellationToken).ConfigureAwait(false)
            : await repository.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            _ = await GetEntryDecisionAsync(actorId, cancellationToken).ConfigureAwait(false);
            session = await repository.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        }

        return await BuildOverviewAsync(
            session ?? throw new InvalidOperationException("The onboarding session could not be initialized."),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask<OnboardingOverview> RunChecksAsync(
        Guid sessionId,
        long expectedRevision,
        string actorId,
        CancellationToken cancellationToken = default) =>
        RunBatchAsync(sessionId, expectedRevision, actorId, explicitChecksOnly: false, cancellationToken);

    /// <inheritdoc />
    public ValueTask<OnboardingOverview> RunExplicitProbesAsync(
        Guid sessionId,
        long expectedRevision,
        string actorId,
        CancellationToken cancellationToken = default) =>
        RunBatchAsync(sessionId, expectedRevision, actorId, explicitChecksOnly: true, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<OnboardingSession> StartReadinessReviewAsync(
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId);
        Guid correlationId = Guid.NewGuid();
        OnboardingSession session = await repository.StartReadinessReviewAsync(
            actorId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync(
            "onboarding.review-started",
            session,
            actorId,
            correlationId,
            new { session.Kind, session.CurrentStep },
            cancellationToken).ConfigureAwait(false);
        return session;
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingSession> DeferAsync(
        Guid sessionId,
        long expectedRevision,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId);
        Guid correlationId = Guid.NewGuid();
        OnboardingSession current = await RequireSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        OnboardingSession updated = await repository.TransitionAsync(new(
            sessionId,
            expectedRevision,
            OnboardingSessionStatus.Deferred,
            current.CurrentStep,
            actorId,
            correlationId,
            current.AcknowledgedWarningCount,
            IncrementCompletedStepCount: false), cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync("onboarding.deferred", updated, actorId, correlationId, new { updated.CurrentStep }, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingSession> ResumeAsync(
        Guid sessionId,
        long expectedRevision,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId);
        Guid correlationId = Guid.NewGuid();
        OnboardingSession current = await RequireSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (current.Status != OnboardingSessionStatus.Deferred)
        {
            throw new InvalidOperationException("Only a deferred onboarding session can be resumed.");
        }

        OnboardingSession updated = await repository.TransitionAsync(new(
            sessionId,
            expectedRevision,
            OnboardingSessionStatus.InProgress,
            current.CurrentStep,
            actorId,
            correlationId,
            current.AcknowledgedWarningCount,
            IncrementCompletedStepCount: false), cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync("onboarding.resumed", updated, actorId, correlationId, new { updated.CurrentStep }, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingSession> CancelAsync(
        Guid sessionId,
        long expectedRevision,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId);
        Guid correlationId = Guid.NewGuid();
        OnboardingSession current = await RequireSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (current.Kind == OnboardingSessionKind.FirstRun)
        {
            throw new InvalidOperationException("First-run onboarding can be deferred, but not cancelled.");
        }

        OnboardingSession updated = await repository.TransitionAsync(new(
            sessionId,
            expectedRevision,
            OnboardingSessionStatus.Cancelled,
            current.CurrentStep,
            actorId,
            correlationId,
            current.AcknowledgedWarningCount,
            IncrementCompletedStepCount: false), cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync("onboarding.cancelled", updated, actorId, correlationId, new { updated.CurrentStep }, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingSession> ContinueAsync(
        Guid sessionId,
        long expectedRevision,
        bool acknowledgeWarnings,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actorId);
        OnboardingOverview overview = await GetOverviewAsync(sessionId, actorId, cancellationToken).ConfigureAwait(false);
        if (overview.Session.Status != OnboardingSessionStatus.InProgress
            || overview.Session.CurrentStep != OnboardingSteps.Readiness)
        {
            throw new InvalidOperationException("The session is not accepting a readiness completion.");
        }

        if (overview.BlockedCount > 0 || overview.UnknownRequiredCount > 0)
        {
            throw new InvalidOperationException("Resolve or rerun every required readiness check before continuing.");
        }

        if (overview.WarningCount > 0 && !acknowledgeWarnings)
        {
            throw new InvalidOperationException("Acknowledge the current readiness warnings before continuing.");
        }

        Guid correlationId = Guid.NewGuid();
        int acknowledged = acknowledgeWarnings ? overview.WarningCount : 0;
        OnboardingSession updated = await repository.TransitionAsync(new(
            sessionId,
            expectedRevision,
            OnboardingSessionStatus.InProgress,
            OnboardingSteps.AgentSelectionPending,
            actorId,
            correlationId,
            acknowledged,
            IncrementCompletedStepCount: true), cancellationToken).ConfigureAwait(false);
        if (acknowledged > 0)
        {
            await WriteAuditAsync(
                "onboarding.warning-acknowledged",
                updated,
                actorId,
                correlationId,
                new { warningCount = acknowledged },
                cancellationToken).ConfigureAwait(false);
        }

        await WriteAuditAsync(
            "onboarding.readiness-completed",
            updated,
            actorId,
            correlationId,
            new { warningsAcknowledged = acknowledged, nextStep = updated.CurrentStep },
            cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private async ValueTask<OnboardingOverview> RunBatchAsync(
        Guid sessionId,
        long expectedRevision,
        string actorId,
        bool explicitChecksOnly,
        CancellationToken cancellationToken)
    {
        ValidateActor(actorId);
        OnboardingSession current = await RequireSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (current.Status != OnboardingSessionStatus.InProgress || current.CurrentStep != OnboardingSteps.Readiness)
        {
            throw new InvalidOperationException("Readiness checks can run only during the active readiness step.");
        }

        Guid correlationId = Guid.NewGuid();
        await WriteAuditAsync(
            "onboarding.readiness-requested",
            current,
            actorId,
            correlationId,
            new { explicitChecksOnly },
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<OnboardingReadinessCheckResult> results = await readiness.RunAsync(
            explicitChecksOnly,
            cancellationToken).ConfigureAwait(false);
        OnboardingSession updated = await repository.SaveCheckBatchAsync(
            sessionId,
            expectedRevision,
            results,
            actorId,
            correlationId,
            cancellationToken).ConfigureAwait(false);
        await WriteAuditAsync(
            explicitChecksOnly ? "onboarding.explicit-probes-completed" : "onboarding.readiness-checked",
            updated,
            actorId,
            correlationId,
            new
            {
                checks = results.Select(item => new
                {
                    item.Definition.Key,
                    scope = item.Definition.Scope.ToString(),
                    status = item.Status.ToString(),
                    item.ReasonCode,
                }).ToArray(),
            },
            cancellationToken).ConfigureAwait(false);
        OnboardingReadinessCheckResult? protectedStorage = results.SingleOrDefault(
            item => item.Definition.Key == "persistence.protected-storage");
        if (protectedStorage is not null)
        {
            string probeAction = protectedStorage.ReasonCode == "protected-storage.cleanup-failed"
                ? "onboarding.protected-storage-cleanup-failed"
                : protectedStorage.Status == OnboardingCheckStatus.Passed
                    ? "onboarding.protected-storage-probe-succeeded"
                    : "onboarding.protected-storage-probe-failed";
            await WriteAuditAsync(
                probeAction,
                updated,
                actorId,
                correlationId,
                new { protectedStorage.Status, protectedStorage.ReasonCode },
                cancellationToken).ConfigureAwait(false);
        }

        return await BuildOverviewAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<OnboardingOverview> BuildOverviewAsync(
        OnboardingSession session,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OnboardingCheckRecord> records = await repository.GetChecksAsync(session.Id, cancellationToken).ConfigureAwait(false);
        Dictionary<string, OnboardingCheckRecord> latest = records.ToDictionary(GetIdentity, StringComparer.Ordinal);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        OnboardingCheckView[] views = readiness.Definitions.Select(definition =>
        {
            latest.TryGetValue(GetIdentity(definition), out OnboardingCheckRecord? record);
            bool expired = record?.ExpiresAtUtc is DateTimeOffset expiry && expiry <= nowUtc;
            return record is null || expired
                ? new OnboardingCheckView(
                    definition,
                    OnboardingCheckStatus.Unknown,
                    expired ? "readiness.result-expired" : definition.RequiresExplicitAction ? "readiness.explicit-probe-required" : "readiness.not-run",
                    expired ? "This result expired. Run the check again." : definition.RequiresExplicitAction ? "Run this explicitly labeled probe when you are ready." : "This check has not run yet.",
                    record?.ObservedAtUtc,
                    record?.ExpiresAtUtc,
                    record?.DetailsSchemaVersion ?? "1.0",
                    record?.DetailsJson ?? "{}",
                    definition.RemediationKey)
                : new OnboardingCheckView(
                    definition,
                    record.Status,
                    record.ReasonCode,
                    record.Message,
                    record.ObservedAtUtc,
                    record.ExpiresAtUtc,
                    record.DetailsSchemaVersion,
                    record.DetailsJson,
                    record.RemediationKey ?? definition.RemediationKey);
        }).ToArray();
        int blocked = views.Count(item => item.Status == OnboardingCheckStatus.Blocked);
        int warnings = views.Count(item => item.Status == OnboardingCheckStatus.Warning);
        int unknownRequired = views.Count(item => item.Definition.Required && item.Status == OnboardingCheckStatus.Unknown);
        bool canContinue = session.Status == OnboardingSessionStatus.InProgress
            && session.CurrentStep == OnboardingSteps.Readiness
            && blocked == 0
            && unknownRequired == 0;
        return new(session, views, canContinue, blocked, warnings, unknownRequired);
    }

    private async ValueTask<OnboardingSession> RequireSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await repository.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException("The onboarding session was not found.");

    private async ValueTask WriteAuditAsync(
        string action,
        OnboardingSession session,
        string actorId,
        Guid correlationId,
        object data,
        CancellationToken cancellationToken)
    {
        _ = await audit.WriteAsync(new WriteAuditEventRequest(
            AuditActorType.User,
            actorId,
            action,
            "onboarding-session",
            session.Id.ToString("D"),
            AuditOutcome.Succeeded,
            correlationId,
            RunId: null,
            JsonSerializer.SerializeToElement(data)), cancellationToken).ConfigureAwait(false);
    }

    private static string GetIdentity(OnboardingCheckRecord item) =>
        $"{item.Scope}:{item.CheckKey}:{item.AgentId?.Value ?? string.Empty}";

    private static string GetIdentity(OnboardingReadinessCheckDefinition item) =>
        $"{item.Scope}:{item.Key}:{item.AgentId?.Value ?? string.Empty}";

    private static void ValidateActor(string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId) || actorId.Length > 128)
        {
            throw new ArgumentException("A bounded onboarding actor is required.", nameof(actorId));
        }
    }
}
