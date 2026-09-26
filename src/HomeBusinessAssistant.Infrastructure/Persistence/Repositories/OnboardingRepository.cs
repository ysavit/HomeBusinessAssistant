using HomeBusinessAssistant.Application.Audit;
using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Persistence.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Persistence.Repositories;

/// <summary>SQLite-backed sessions, first-run evidence, transitions, and latest readiness results.</summary>
public sealed class OnboardingRepository(
    AssistantDbContextFactory contextFactory,
    TimeProvider timeProvider) : IOnboardingRepository, IOnboardingAgentSelectionRepository
{
    private const string InitialKey = "first-run";
    private const string ActiveSlot = "platform-onboarding";

    /// <inheritdoc />
    public async ValueTask<OnboardingInstallationEvidence> GetInstallationEvidenceAsync(
        CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int agentCount = await context.Set<AgentDefinitionEntity>().AsNoTracking().CountAsync(cancellationToken).ConfigureAwait(false);
        bool userConfiguration = await context.Set<ConfigurationRevisionEntity>().AsNoTracking()
            .AnyAsync(
                item => item.ChangedBy != "system-bootstrap"
                    && item.ChangedBy != "agent-registry-scanner",
                cancellationToken).ConfigureAwait(false);
        bool schedules = await context.Set<AgentScheduleEntity>().AsNoTracking().AnyAsync(cancellationToken).ConfigureAwait(false);
        bool runs = await context.Set<AgentRunEntity>().AsNoTracking().AnyAsync(cancellationToken).ConfigureAwait(false);
        return new(agentCount, userConfiguration, schedules, runs);
    }

    /// <inheritdoc />
    public async ValueTask<EnsureInitialOnboardingResult> EnsureInitialAsync(
        bool establishedInstallation,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        ValidateActorAndCorrelation(actorId, correlationId);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: false);
        _ = await context.Database.UseTransactionAsync(transaction, cancellationToken).ConfigureAwait(false);
        OnboardingSessionEntity? existing = await context.Set<OnboardingSessionEntity>()
            .SingleOrDefaultAsync(item => item.InitializationKey == InitialKey, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(Map(existing), Created: false, existing.CurrentStep == OnboardingSteps.LegacyInstallation);
        }

        var entity = new OnboardingSessionEntity
        {
            Id = Guid.NewGuid(),
            SchemaVersion = "1.0",
            Kind = nameof(OnboardingSessionKind.FirstRun),
            Status = establishedInstallation ? nameof(OnboardingSessionStatus.Completed) : nameof(OnboardingSessionStatus.InProgress),
            CurrentStep = establishedInstallation ? OnboardingSteps.LegacyInstallation : OnboardingSteps.Readiness,
            CreatedAtUtc = nowUtc,
            StartedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CompletedAtUtc = establishedInstallation ? nowUtc : null,
            ActorId = actorId,
            CorrelationId = correlationId,
            Revision = 1,
            WarningCount = 0,
            AcknowledgedWarningCount = 0,
            CompletedStepCount = establishedInstallation ? 1 : 0,
            ActiveSlot = establishedInstallation ? null : ActiveSlot,
            InitializationKey = InitialKey,
        };
        context.Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(Map(entity), Created: true, establishedInstallation);
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingSession> StartReadinessReviewAsync(
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        ValidateActorAndCorrelation(actorId, correlationId);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: false);
        _ = await context.Database.UseTransactionAsync(transaction, cancellationToken).ConfigureAwait(false);
        OnboardingSessionEntity? active = await context.Set<OnboardingSessionEntity>()
            .SingleOrDefaultAsync(item => item.ActiveSlot == ActiveSlot, cancellationToken).ConfigureAwait(false);
        if (active is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Map(active);
        }

        var entity = new OnboardingSessionEntity
        {
            Id = Guid.NewGuid(),
            SchemaVersion = "1.0",
            Kind = nameof(OnboardingSessionKind.ReconfigureAgent),
            Status = nameof(OnboardingSessionStatus.InProgress),
            CurrentStep = OnboardingSteps.Readiness,
            CreatedAtUtc = nowUtc,
            StartedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            ActorId = actorId,
            CorrelationId = correlationId,
            Revision = 1,
            WarningCount = 0,
            AcknowledgedWarningCount = 0,
            CompletedStepCount = 0,
            ActiveSlot = ActiveSlot,
        };
        context.Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingSession> StartAgentSelectionSessionAsync(
        OnboardingSessionKind kind,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        if (kind is not OnboardingSessionKind.AddAgents and not OnboardingSessionKind.ReconfigureAgent)
        {
            throw new ArgumentException("An agent-selection session kind is required.", nameof(kind));
        }

        ValidateActorAndCorrelation(actorId, correlationId);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: false);
        _ = await context.Database.UseTransactionAsync(transaction, cancellationToken).ConfigureAwait(false);
        OnboardingSessionEntity? active = await context.Set<OnboardingSessionEntity>()
            .SingleOrDefaultAsync(item => item.ActiveSlot == ActiveSlot, cancellationToken).ConfigureAwait(false);
        if (active is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Map(active);
        }

        var entity = new OnboardingSessionEntity
        {
            Id = Guid.NewGuid(),
            SchemaVersion = "1.0",
            Kind = kind.ToString(),
            Status = nameof(OnboardingSessionStatus.InProgress),
            CurrentStep = OnboardingSteps.AgentSelectionPending,
            CreatedAtUtc = nowUtc,
            StartedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            ActorId = actorId,
            CorrelationId = correlationId,
            Revision = 1,
            WarningCount = 0,
            AcknowledgedWarningCount = 0,
            CompletedStepCount = 0,
            ActiveSlot = ActiveSlot,
        };
        context.Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (sessionId == Guid.Empty)
        {
            return null;
        }

        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        OnboardingSessionEntity? entity = await context.Set<OnboardingSessionEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken).ConfigureAwait(false);
        return entity is null ? null : Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingSession?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        OnboardingSessionEntity? entity = await context.Set<OnboardingSessionEntity>().AsNoTracking()
            .OrderBy(item => item.Status == nameof(OnboardingSessionStatus.InProgress) ? 0
                : item.Status == nameof(OnboardingSessionStatus.Deferred) ? 1 : 2)
            .ThenByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return entity is null ? null : Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<OnboardingCheckRecord>> GetChecksAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<OnboardingCheckEntity> entities = await context.Set<OnboardingCheckEntity>().AsNoTracking()
            .Where(item => item.SessionId == sessionId)
            .OrderBy(item => item.Scope)
            .ThenBy(item => item.CheckKey)
            .ThenBy(item => item.AgentScopeKey)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return entities.Select(Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<OnboardingAgentSelection>> GetSelectionsAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<OnboardingAgentSelectionEntity> entities = await context.Set<OnboardingAgentSelectionEntity>().AsNoTracking()
            .Where(item => item.SessionId == sessionId)
            .OrderBy(item => item.AgentId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return entities.Select(Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<AgentId, OnboardingAgentSelection>> GetLatestSelectionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<OnboardingAgentSelectionEntity> entities = await context.Set<OnboardingAgentSelectionEntity>().AsNoTracking()
            .OrderByDescending(item => item.UpdatedAtUtc)
            .ThenByDescending(item => item.SessionId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return entities
            .Select(Map)
            .GroupBy(item => item.AgentId)
            .ToDictionary(group => group.Key, group => group.First());
    }

    /// <inheritdoc />
    public async ValueTask<SaveOnboardingAgentChoicesResult> SaveChoicesAsync(
        Guid sessionId,
        long expectedSessionRevision,
        IReadOnlyList<SaveOnboardingAgentChoice> choices,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ValidateActorAndCorrelation(actorId, correlationId);
        if (choices.Count > 128 || choices.Select(item => item.AgentId).Distinct().Count() != choices.Count)
        {
            throw new ArgumentException("The onboarding agent choices are invalid.", nameof(choices));
        }

        foreach (SaveOnboardingAgentChoice choice in choices)
        {
            ValidateChoice(choice);
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: false);
        _ = await context.Database.UseTransactionAsync(transaction, cancellationToken).ConfigureAwait(false);
        OnboardingSessionEntity session = await RequireMutableSessionAsync(
            context,
            sessionId,
            expectedSessionRevision,
            cancellationToken).ConfigureAwait(false);
        if (session.Status != nameof(OnboardingSessionStatus.InProgress)
            || session.CurrentStep is not OnboardingSteps.AgentSelectionPending and not OnboardingSteps.AgentConfiguration)
        {
            throw new InvalidOperationException("The onboarding session is not accepting agent choices.");
        }

        var changed = new List<AgentId>();
        foreach (SaveOnboardingAgentChoice choice in choices)
        {
            OnboardingAgentSelectionEntity? entity = await context.Set<OnboardingAgentSelectionEntity>()
                .SingleOrDefaultAsync(
                    item => item.SessionId == sessionId && item.AgentId == choice.AgentId.Value,
                    cancellationToken).ConfigureAwait(false);
            if (entity is null)
            {
                entity = new OnboardingAgentSelectionEntity
                {
                    SessionId = sessionId,
                    AgentId = choice.AgentId.Value,
                    SelectionStatus = choice.SelectionStatus.ToString(),
                    ProgressStatus = choice.ProgressStatus.ToString(),
                    AdapterId = choice.AdapterId,
                    CurrentStepKey = choice.CurrentStepKey,
                    StartingConfigurationRevisionId = choice.StartingConfigurationRevisionId,
                    StartingConfigurationHash = choice.StartingConfigurationHash,
                    LastReasonCode = choice.ReasonCode,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc,
                    Revision = 1,
                };
                context.Add(entity);
                changed.Add(choice.AgentId);
                continue;
            }

            bool differs = entity.SelectionStatus != choice.SelectionStatus.ToString()
                || entity.ProgressStatus != choice.ProgressStatus.ToString()
                || entity.AdapterId != choice.AdapterId
                || entity.CurrentStepKey != choice.CurrentStepKey
                || entity.LastReasonCode != choice.ReasonCode;
            if (!differs)
            {
                continue;
            }

            entity.SelectionStatus = choice.SelectionStatus.ToString();
            entity.ProgressStatus = choice.ProgressStatus.ToString();
            entity.AdapterId = choice.AdapterId;
            entity.CurrentStepKey = choice.CurrentStepKey;
            entity.LastReasonCode = choice.ReasonCode;
            entity.UpdatedAtUtc = nowUtc;
            entity.CompletedAtUtc = choice.ProgressStatus is OnboardingAgentProgressStatus.Completed or OnboardingAgentProgressStatus.Skipped
                ? nowUtc : null;
            entity.Revision++;
            changed.Add(choice.AgentId);
        }

        string nextStep = choices.Any(item => item.SelectionStatus == OnboardingAgentSelectionStatus.Selected)
            ? OnboardingSteps.AgentConfiguration
            : OnboardingSteps.AgentSelectionPending;
        if (changed.Count > 0 || session.CurrentStep != nextStep)
        {
            session.CurrentStep = nextStep;
            session.ActorId = actorId;
            session.CorrelationId = correlationId;
            session.UpdatedAtUtc = nowUtc;
            session.Revision++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        List<OnboardingAgentSelectionEntity> saved = await context.Set<OnboardingAgentSelectionEntity>()
            .Where(item => item.SessionId == sessionId)
            .OrderBy(item => item.AgentId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(Map(session), saved.Select(Map).ToArray(), changed);
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingAgentSelection> UpdateProgressAsync(
        UpdateOnboardingAgentProgressRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateProgressRequest(request);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: false);
        _ = await context.Database.UseTransactionAsync(transaction, cancellationToken).ConfigureAwait(false);
        OnboardingAgentSelectionEntity entity = await context.Set<OnboardingAgentSelectionEntity>()
            .SingleOrDefaultAsync(
                item => item.SessionId == request.SessionId && item.AgentId == request.AgentId.Value,
                cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The onboarding agent selection was not found.");
        if (request.ExpectedRevision <= 0 || entity.Revision != request.ExpectedRevision)
        {
            throw new OnboardingConcurrencyException();
        }

        entity.SelectionStatus = request.SelectionStatus.ToString();
        entity.ProgressStatus = request.ProgressStatus.ToString();
        entity.AdapterId = request.AdapterId;
        entity.CurrentStepKey = request.CurrentStepKey;
        entity.ReviewedManifestVersion = request.ReviewedManifestVersion;
        entity.ReviewedConfigurationSchemaVersion = request.ReviewedConfigurationSchemaVersion;
        entity.SavedConfigurationRevisionId = request.SavedConfigurationRevisionId;
        entity.SavedConfigurationHash = request.SavedConfigurationHash;
        entity.LastReasonCode = request.ReasonCode;
        entity.UpdatedAtUtc = nowUtc;
        entity.CompletedAtUtc = request.CompletedAtUtc?.ToUniversalTime();
        entity.Revision++;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentId>> ReconcileUnavailableAsync(
        Guid sessionId,
        IReadOnlyDictionary<AgentId, bool> unavailableAgents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unavailableAgents);
        if (unavailableAgents.Count == 0)
        {
            return [];
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: false);
        _ = await context.Database.UseTransactionAsync(transaction, cancellationToken).ConfigureAwait(false);
        string[] ids = unavailableAgents.Keys.Select(item => item.Value).ToArray();
        List<OnboardingAgentSelectionEntity> selections = await context.Set<OnboardingAgentSelectionEntity>()
            .Where(item => item.SessionId == sessionId && ids.Contains(item.AgentId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var changed = new List<AgentId>();
        foreach (OnboardingAgentSelectionEntity selection in selections)
        {
            AgentId id = AgentId.Parse(selection.AgentId);
            string target = unavailableAgents[id]
                ? nameof(OnboardingAgentSelectionStatus.Removed)
                : nameof(OnboardingAgentSelectionStatus.Unavailable);
            if (selection.SelectionStatus == target)
            {
                continue;
            }

            selection.SelectionStatus = target;
            selection.ProgressStatus = nameof(OnboardingAgentProgressStatus.NeedsAttention);
            selection.CurrentStepKey = "agent.unavailable";
            selection.LastReasonCode = unavailableAgents[id] ? "agent.package-removed" : "agent.package-unavailable";
            selection.UpdatedAtUtc = nowUtc;
            selection.CompletedAtUtc = null;
            selection.Revision++;
            changed.Add(id);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return changed;
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingSession> SaveCheckBatchAsync(
        Guid sessionId,
        long expectedRevision,
        IReadOnlyList<OnboardingReadinessCheckResult> results,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(results);
        ValidateActorAndCorrelation(actorId, correlationId);
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: false);
        _ = await context.Database.UseTransactionAsync(transaction, cancellationToken).ConfigureAwait(false);
        OnboardingSessionEntity session = await RequireMutableSessionAsync(context, sessionId, expectedRevision, cancellationToken).ConfigureAwait(false);
        if (session.Status != nameof(OnboardingSessionStatus.InProgress)
            || session.CurrentStep is not OnboardingSteps.Readiness and not OnboardingSteps.AgentConfiguration)
        {
            throw new InvalidOperationException("The onboarding session is not accepting readiness results.");
        }

        foreach (OnboardingReadinessCheckResult result in results)
        {
            ValidateResult(result);
            string agentScopeKey = result.Definition.AgentId?.Value ?? string.Empty;
            string scope = result.Definition.Scope.ToString();
            OnboardingCheckEntity? entity = await context.Set<OnboardingCheckEntity>().SingleOrDefaultAsync(
                item => item.SessionId == sessionId
                    && item.Scope == scope
                    && item.CheckKey == result.Definition.Key
                    && item.AgentScopeKey == agentScopeKey,
                cancellationToken).ConfigureAwait(false);
            string detailsJson = AuditRedactor.Redact(result.Details);
            if (detailsJson.Length > 32_768)
            {
                detailsJson = "{\"truncated\":true}";
            }

            if (entity is null)
            {
                entity = new OnboardingCheckEntity
                {
                    Id = Guid.NewGuid(),
                    SessionId = sessionId,
                    Scope = scope,
                    CheckKey = result.Definition.Key,
                    AgentId = result.Definition.AgentId?.Value,
                    AgentScopeKey = agentScopeKey,
                    Status = string.Empty,
                    ReasonCode = string.Empty,
                    Title = string.Empty,
                    Message = string.Empty,
                    DetailsSchemaVersion = string.Empty,
                    DetailsJson = "{}",
                };
                context.Add(entity);
            }

            entity.Status = result.Status.ToString();
            entity.ReasonCode = result.ReasonCode;
            entity.Title = AuditRedactor.RedactText(result.Definition.Title, 160);
            entity.Message = AuditRedactor.RedactText(result.Message, 1_000);
            entity.ObservedAtUtc = result.ObservedAtUtc.ToUniversalTime();
            entity.ExpiresAtUtc = result.ExpiresAtUtc?.ToUniversalTime();
            entity.DetailsSchemaVersion = result.DetailsSchemaVersion;
            entity.DetailsJson = detailsJson;
            entity.RemediationKey = ValidateRemediationKey(result.RemediationKey ?? result.Definition.RemediationKey);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        session.WarningCount = await context.Set<OnboardingCheckEntity>()
            .CountAsync(item => item.SessionId == sessionId && item.Status == nameof(OnboardingCheckStatus.Warning), cancellationToken)
            .ConfigureAwait(false);
        session.ActorId = actorId;
        session.CorrelationId = correlationId;
        session.UpdatedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        session.Revision++;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Map(session);
    }

    /// <inheritdoc />
    public async ValueTask<OnboardingSession> TransitionAsync(
        OnboardingTransitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateActorAndCorrelation(request.ActorId, request.CorrelationId);
        if (!OnboardingSteps.IsAllowed(request.CurrentStep)
            || request.AcknowledgedWarningCount < 0)
        {
            throw new ArgumentException("The onboarding transition is invalid.", nameof(request));
        }

        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: false);
        _ = await context.Database.UseTransactionAsync(transaction, cancellationToken).ConfigureAwait(false);
        OnboardingSessionEntity session = await RequireMutableSessionAsync(
            context,
            request.SessionId,
            request.ExpectedRevision,
            cancellationToken).ConfigureAwait(false);
        ValidateTransition(session, request);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        session.Status = request.TargetStatus.ToString();
        session.CurrentStep = request.CurrentStep;
        session.ActorId = request.ActorId;
        session.CorrelationId = request.CorrelationId;
        session.UpdatedAtUtc = nowUtc;
        session.AcknowledgedWarningCount = request.AcknowledgedWarningCount;
        session.CompletedStepCount += request.IncrementCompletedStepCount ? 1 : 0;
        session.DeferredAtUtc = request.TargetStatus == OnboardingSessionStatus.Deferred ? nowUtc : null;
        session.CompletedAtUtc = request.TargetStatus == OnboardingSessionStatus.Completed ? nowUtc : null;
        session.CancelledAtUtc = request.TargetStatus == OnboardingSessionStatus.Cancelled ? nowUtc : null;
        session.ActiveSlot = request.TargetStatus == OnboardingSessionStatus.InProgress ? ActiveSlot : null;
        session.Revision++;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Map(session);
    }

    private static async ValueTask<OnboardingSessionEntity> RequireMutableSessionAsync(
        AssistantDbContext context,
        Guid sessionId,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        OnboardingSessionEntity? session = await context.Set<OnboardingSessionEntity>()
            .SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            throw new InvalidOperationException("The onboarding session was not found.");
        }

        if (expectedRevision <= 0 || session.Revision != expectedRevision)
        {
            throw new OnboardingConcurrencyException();
        }

        return session;
    }

    private static void ValidateTransition(OnboardingSessionEntity session, OnboardingTransitionRequest request)
    {
        OnboardingSessionStatus current = Enum.Parse<OnboardingSessionStatus>(session.Status);
        bool allowed = current switch
        {
            OnboardingSessionStatus.InProgress when request.TargetStatus == OnboardingSessionStatus.Deferred => true,
            OnboardingSessionStatus.Deferred when request.TargetStatus == OnboardingSessionStatus.InProgress => true,
            OnboardingSessionStatus.InProgress when request.TargetStatus == OnboardingSessionStatus.InProgress
                && session.CurrentStep == OnboardingSteps.Readiness
                && request.CurrentStep == OnboardingSteps.AgentSelectionPending => true,
            OnboardingSessionStatus.InProgress when request.TargetStatus is OnboardingSessionStatus.Completed or OnboardingSessionStatus.Cancelled => true,
            _ => false,
        };
        if (!allowed)
        {
            throw new InvalidOperationException("The onboarding lifecycle transition is not allowed.");
        }
    }

    private static void ValidateResult(OnboardingReadinessCheckResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Definition.Key)
            || result.Definition.Key.Length > 128
            || string.IsNullOrWhiteSpace(result.ReasonCode)
            || result.ReasonCode.Length > 128
            || string.IsNullOrWhiteSpace(result.Message)
            || result.Message.Length > 1_000
            || result.Details.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            throw new ArgumentException("A readiness result is invalid.", nameof(result));
        }
    }

    private static void ValidateChoice(SaveOnboardingAgentChoice choice)
    {
        if (choice.SelectionStatus is not OnboardingAgentSelectionStatus.Selected and not OnboardingAgentSelectionStatus.Deferred
            || string.IsNullOrWhiteSpace(choice.AdapterId)
            || choice.AdapterId.Length > 128
            || string.IsNullOrWhiteSpace(choice.CurrentStepKey)
            || choice.CurrentStepKey.Length > 128
            || string.IsNullOrWhiteSpace(choice.ReasonCode)
            || choice.ReasonCode.Length > 128
            || choice.StartingConfigurationHash is { Length: not 64 })
        {
            throw new ArgumentException("An onboarding agent choice is invalid.", nameof(choice));
        }
    }

    private static void ValidateProgressRequest(UpdateOnboardingAgentProgressRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AdapterId)
            || request.AdapterId.Length > 128
            || string.IsNullOrWhiteSpace(request.CurrentStepKey)
            || request.CurrentStepKey.Length > 128
            || string.IsNullOrWhiteSpace(request.ReasonCode)
            || request.ReasonCode.Length > 128
            || request.SavedConfigurationHash is { Length: not 64 })
        {
            throw new ArgumentException("The onboarding agent progress update is invalid.", nameof(request));
        }
    }

    private static string? ValidateRemediationKey(string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Length is < 1 or > 128
            || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-')))
        {
            throw new ArgumentException("The readiness remediation key is invalid.", nameof(value));
        }

        return value;
    }

    private static void ValidateActorAndCorrelation(string actorId, Guid correlationId)
    {
        if (string.IsNullOrWhiteSpace(actorId) || actorId.Length > 128 || correlationId == Guid.Empty)
        {
            throw new ArgumentException("A bounded actor and correlation identifier are required.");
        }
    }

    private static OnboardingSession Map(OnboardingSessionEntity entity) => new(
        entity.Id,
        entity.SchemaVersion,
        Enum.Parse<OnboardingSessionKind>(entity.Kind),
        Enum.Parse<OnboardingSessionStatus>(entity.Status),
        entity.CurrentStep,
        entity.CreatedAtUtc,
        entity.StartedAtUtc,
        entity.UpdatedAtUtc,
        entity.DeferredAtUtc,
        entity.CompletedAtUtc,
        entity.CancelledAtUtc,
        entity.ActorId,
        entity.CorrelationId,
        entity.Revision,
        entity.WarningCount,
        entity.AcknowledgedWarningCount,
        entity.CompletedStepCount);

    private static OnboardingCheckRecord Map(OnboardingCheckEntity entity) => new(
        entity.Id,
        entity.SessionId,
        Enum.Parse<OnboardingCheckScope>(entity.Scope),
        entity.CheckKey,
        entity.AgentId is null ? null : AgentId.Parse(entity.AgentId),
        Enum.Parse<OnboardingCheckStatus>(entity.Status),
        entity.ReasonCode,
        entity.Title,
        entity.Message,
        entity.ObservedAtUtc,
        entity.ExpiresAtUtc,
        entity.DetailsSchemaVersion,
        entity.DetailsJson,
        entity.RemediationKey);

    private static OnboardingAgentSelection Map(OnboardingAgentSelectionEntity entity) => new(
        entity.SessionId,
        AgentId.Parse(entity.AgentId),
        Enum.Parse<OnboardingAgentSelectionStatus>(entity.SelectionStatus),
        Enum.Parse<OnboardingAgentProgressStatus>(entity.ProgressStatus),
        entity.AdapterId,
        entity.CurrentStepKey,
        entity.ReviewedManifestVersion,
        entity.ReviewedConfigurationSchemaVersion,
        entity.StartingConfigurationRevisionId,
        entity.StartingConfigurationHash,
        entity.SavedConfigurationRevisionId,
        entity.SavedConfigurationHash,
        entity.LastReasonCode,
        entity.CreatedAtUtc,
        entity.UpdatedAtUtc,
        entity.CompletedAtUtc,
        entity.Revision);
}
