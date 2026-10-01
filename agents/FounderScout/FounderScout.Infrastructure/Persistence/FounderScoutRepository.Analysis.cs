using System.Text.Json;
using FounderScout.Application;
using FounderScout.Domain;
using FounderScout.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FounderScout.Infrastructure.Persistence;

public sealed partial class FounderScoutRepository
{
    /// <inheritdoc />
    public async ValueTask AppendAsync(
        ScreeningDecision decision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ValidateGuid(decision.Id, nameof(decision));
        ValidateGuid(decision.CandidateId, nameof(decision));
        ValidateGuid(decision.SnapshotId, nameof(decision));
        ValidateJsonObject(decision.EvidenceJson, 262_144, nameof(decision));
        ValidateJsonObject(decision.EvaluatorInputJson, 262_144, nameof(decision));
        using JsonDocument reasonCodes = JsonDocument.Parse(decision.ReasonCodesJson, new JsonDocumentOptions { MaxDepth = 16 });
        using JsonDocument missingEvidence = JsonDocument.Parse(decision.MissingEvidenceJson, new JsonDocumentOptions { MaxDepth = 16 });
        if (reasonCodes.RootElement.ValueKind != JsonValueKind.Array
            || missingEvidence.RootElement.ValueKind != JsonValueKind.Array
            || string.IsNullOrWhiteSpace(decision.RulesetVersion)
            || decision.RulesetVersion.Length > 128
            || decision.EvaluatorInputHash.Length != 64
            || decision.Score is < 0 or > 100)
        {
            throw new ArgumentException("The screening decision is invalid.", nameof(decision));
        }

        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.Add(FounderScoutPersistenceMapper.Map(decision));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ScreeningDecision?> GetLatestAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(candidateId, nameof(candidateId));
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ScreeningDecisionEntity? entity = await context.Set<ScreeningDecisionEntity>().AsNoTracking()
            .Where(item => item.CandidateId == candidateId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return entity is null ? null : FounderScoutPersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<EvaluationAggregate> AddAsync(
        EvaluationAggregate aggregate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ValidateEvaluation(aggregate);
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        context.Add(FounderScoutPersistenceMapper.Map(aggregate.Evaluation));
        context.AddRange(aggregate.Categories.Select(FounderScoutPersistenceMapper.Map));
        context.AddRange(aggregate.Risks.Select(FounderScoutPersistenceMapper.Map));
        CandidateEntity candidate = await context.Set<CandidateEntity>()
            .SingleAsync(item => item.Id == aggregate.Evaluation.CandidateId, cancellationToken).ConfigureAwait(false);
        if (aggregate.Evaluation.Status == EvaluationStatus.Completed)
        {
            candidate.LatestEvaluationId = aggregate.Evaluation.Id;
            candidate.FounderQualityScore = aggregate.Evaluation.FounderQualityScore;
            candidate.OurFitScore = aggregate.Evaluation.OurFitScore;
            candidate.Confidence = aggregate.Evaluation.Confidence;
            candidate.ActivityScore = aggregate.Evaluation.ActivityScore;
            candidate.RiskPenalty = aggregate.Evaluation.RiskPenalty;
            candidate.InvitationPriority = aggregate.Evaluation.InvitationPriority;
            candidate.UpdatedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
            candidate.Version++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return aggregate;
    }

    /// <inheritdoc />
    async ValueTask<EvaluationAggregate?> IEvaluationRepository.GetAsync(
        Guid evaluationId,
        CancellationToken cancellationToken)
    {
        ValidateGuid(evaluationId, nameof(evaluationId));
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        EvaluationEntity? evaluation = await context.Set<EvaluationEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == evaluationId, cancellationToken).ConfigureAwait(false);
        if (evaluation is null)
        {
            return null;
        }

        List<EvaluationCategoryEntity> categories = await context.Set<EvaluationCategoryEntity>().AsNoTracking()
            .Where(item => item.EvaluationId == evaluationId)
            .OrderBy(item => item.Key)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        List<EvaluationRiskEntity> risks = await context.Set<EvaluationRiskEntity>().AsNoTracking()
            .Where(item => item.EvaluationId == evaluationId)
            .OrderBy(item => item.Key)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new(
            FounderScoutPersistenceMapper.Map(evaluation),
            categories.Select(FounderScoutPersistenceMapper.Map).ToArray(),
            risks.Select(FounderScoutPersistenceMapper.Map).ToArray());
    }

    /// <inheritdoc />
    public async ValueTask<EvaluationAggregate?> GetCompletedByInputHashAsync(
        Guid candidateId,
        string inputHash,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(candidateId, nameof(candidateId));
        if (inputHash.Length != 64 || !inputHash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("A SHA-256 evaluation input hash is required.", nameof(inputHash));
        }

        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        Guid? evaluationId = await context.Set<EvaluationEntity>().AsNoTracking()
            .Where(item => item.CandidateId == candidateId
                && item.InputHash == inputHash
                && item.Status == EvaluationStatus.Completed.ToString())
            .Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return evaluationId.HasValue
            ? await ((IEvaluationRepository)this).GetAsync(evaluationId.Value, cancellationToken).ConfigureAwait(false)
            : null;
    }

    /// <inheritdoc />
    public async ValueTask<Evaluation> TransitionAsync(
        Guid evaluationId,
        EvaluationStatus requested,
        string reasonCode,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(evaluationId, nameof(evaluationId));
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        EvaluationEntity entity = await context.Set<EvaluationEntity>()
            .SingleOrDefaultAsync(item => item.Id == evaluationId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The Founder Scout evaluation does not exist.");
        EvaluationStatus current = ParseEnum<EvaluationStatus>(entity.Status);
        FounderScoutTransitionResult<EvaluationStatus> transition = EvaluationLifecycle.Validate(current, requested, reasonCode);
        if (!transition.IsAllowed)
        {
            throw new InvalidOperationException(transition.Error!.Code);
        }

        entity.Status = requested.ToString();
        entity.UpdatedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        entity.Version++;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FounderScoutPersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<InvitationDraft> CreateAndSupersedePreviousAsync(
        InvitationDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ValidateInvitationDraft(draft);
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        _ = await context.Set<InvitationDraftEntity>()
            .Where(item => item.CandidateId == draft.CandidateId && !item.Superseded)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Superseded, true)
                .SetProperty(item => item.Status, InvitationDraftStatus.Superseded.ToString())
                .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken)
            .ConfigureAwait(false);
        InvitationDraftEntity entity = FounderScoutPersistenceMapper.Map(draft with
        {
            CreatedAtUtc = draft.CreatedAtUtc == default ? nowUtc : draft.CreatedAtUtc.ToUniversalTime(),
            Superseded = false,
            Version = 1,
        });
        context.Add(entity);
        context.Add(NewAction(
            draft.CandidateId,
            CandidateActionType.InvitationDraftCreated,
            nowUtc,
            "founder-scout",
            "An invitation draft was created for manual review only.",
            JsonSerializer.Serialize(new { draftId = draft.Id }, JsonOptions),
            draft.Id,
            draft.EvaluationId,
            null,
            draft.Id.ToString("D")));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return FounderScoutPersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<InvitationDraft?> GetForEvaluationAsync(
        Guid evaluationId,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(evaluationId, nameof(evaluationId));
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        InvitationDraftEntity? entity = await context.Set<InvitationDraftEntity>().AsNoTracking()
            .Where(item => item.EvaluationId == evaluationId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return entity is null ? null : FounderScoutPersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<InvitationDraft>> ListRecentActiveAsync(
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        ValidateMaximum(maximumResults);
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<InvitationDraftEntity> entities = await context.Set<InvitationDraftEntity>().AsNoTracking()
            .Where(item => !item.Superseded)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Take(maximumResults)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return entities.Select(FounderScoutPersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<InvitationDraft> TransitionAsync(
        Guid draftId,
        InvitationDraftStatus requested,
        string reasonCode,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(draftId, nameof(draftId));
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 128)
        {
            throw new ArgumentException("A bounded invitation actor is required.", nameof(actor));
        }

        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        InvitationDraftEntity entity = await context.Set<InvitationDraftEntity>()
            .SingleOrDefaultAsync(item => item.Id == draftId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The Founder Scout invitation draft does not exist.");
        InvitationDraftStatus current = ParseEnum<InvitationDraftStatus>(entity.Status);
        FounderScoutTransitionResult<InvitationDraftStatus> transition = InvitationLifecycle.Validate(current, requested, reasonCode);
        if (!transition.IsAllowed)
        {
            throw new InvalidOperationException(transition.Error!.Code);
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        entity.Status = requested.ToString();
        entity.Superseded = requested == InvitationDraftStatus.Superseded;
        entity.Version++;
        if (requested is InvitationDraftStatus.Reviewed or InvitationDraftStatus.ManuallySent)
        {
            entity.ReviewedAtUtc = nowUtc;
            entity.ReviewedBy = actor;
        }

        CandidateActionType actionType = requested == InvitationDraftStatus.ManuallySent
            ? CandidateActionType.InvitationMarkedSent
            : CandidateActionType.InvitationDraftReviewed;
        context.Add(NewAction(
            entity.CandidateId,
            actionType,
            nowUtc,
            actor,
            requested == InvitationDraftStatus.ManuallySent
                ? "A human recorded that the invitation was sent."
                : "The invitation draft review state changed.",
            JsonSerializer.Serialize(new { draftId, status = requested.ToString(), reasonCode }, JsonOptions),
            draftId,
            entity.EvaluationId,
            null,
            draftId.ToString("D")));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return FounderScoutPersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<ManualInvitationWindow> SaveWindowAsync(
        ManualInvitationWindow window,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        ValidateInvitationWindow(window);
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ManualInvitationWindowEntity? entity = await context.Set<ManualInvitationWindowEntity>()
            .SingleOrDefaultAsync(item => item.Id == window.Id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            entity = FounderScoutPersistenceMapper.Map(window);
            context.Add(entity);
        }
        else
        {
            entity.StartAtUtc = window.StartAtUtc.ToUniversalTime();
            entity.EndAtUtc = window.EndAtUtc.ToUniversalTime();
            entity.PrimaryQueueSize = window.PrimaryQueueSize;
            entity.ReserveQueueSize = window.ReserveQueueSize;
            entity.SentCount = window.SentCount;
            entity.Notes = Bounded(window.Notes, 2_000);
            entity.UpdatedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
            entity.Version++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FounderScoutPersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<ManualInvitationWindow?> GetCurrentWindowAsync(
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset utc = atUtc.ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ManualInvitationWindowEntity? entity = await context.Set<ManualInvitationWindowEntity>().AsNoTracking()
            .Where(item => item.StartAtUtc <= utc && item.EndAtUtc >= utc)
            .OrderByDescending(item => item.StartAtUtc)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return entity is null ? null : FounderScoutPersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryIncrementSentCountAsync(
        Guid windowId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(windowId, nameof(windowId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedVersion);

        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        int changed = await context.Set<ManualInvitationWindowEntity>()
            .Where(item => item.Id == windowId
                && item.Version == expectedVersion
                && item.SentCount < item.PrimaryQueueSize)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.SentCount, item => item.SentCount + 1)
                .SetProperty(item => item.UpdatedAtUtc, nowUtc)
                .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken)
            .ConfigureAwait(false);
        return changed == 1;
    }

    /// <inheritdoc />
    public async ValueTask<InvitationQueuePage> QueryAsync(
        int primarySize,
        int reserveSize,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken = default)
    {
        if (primarySize is < 1 or > 500 || reserveSize is < 0 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(primarySize));
        }

        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<CandidateEntity> entities = await context.Set<CandidateEntity>().AsNoTracking()
            .Where(item => !item.MergedIntoCandidateId.HasValue)
            .Where(item => item.Status == CandidateStatus.QueuedForInvite.ToString())
            .OrderByDescending(item => item.InvitationPriority)
            .ThenByDescending(item => item.Confidence)
            .ThenBy(item => item.Id)
            .Take(primarySize + reserveSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset utc = atUtc.ToUniversalTime();
        ManualInvitationWindowEntity? window = await context.Set<ManualInvitationWindowEntity>().AsNoTracking()
            .Where(item => item.StartAtUtc <= utc && item.EndAtUtc >= utc)
            .OrderByDescending(item => item.StartAtUtc)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        Candidate[] mapped = entities.Select(FounderScoutPersistenceMapper.Map).ToArray();
        return new(
            mapped.Take(primarySize).ToArray(),
            mapped.Skip(primarySize).Take(reserveSize).ToArray(),
            window is null ? null : FounderScoutPersistenceMapper.Map(window));
    }

    /// <inheritdoc />
    public async ValueTask AppendAsync(ReportExport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ValidateReport(report);
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.Add(FounderScoutPersistenceMapper.Map(report));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ReportExport>> ListRecentAsync(
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        ValidateMaximum(maximumResults);
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<ReportExportEntity> entities = await context.Set<ReportExportEntity>().AsNoTracking()
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Take(maximumResults)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return entities.Select(FounderScoutPersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<FounderScoutAnalysisClaim?> ClaimPendingAnalysisAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
        => await ClaimAnalysisAsync(null, workerId, leaseDuration, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask<FounderScoutAnalysisClaim?> ClaimCandidateAnalysisAsync(
        Guid candidateId, string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
        => await ClaimAnalysisAsync(candidateId, workerId, leaseDuration, cancellationToken).ConfigureAwait(false);

    private async ValueTask<FounderScoutAnalysisClaim?> ClaimAnalysisAsync(
        Guid? requestedCandidateId, string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        if (requestedCandidateId == Guid.Empty || string.IsNullOrWhiteSpace(workerId) || workerId.Length > 128
            || leaseDuration < TimeSpan.FromSeconds(10) || leaseDuration > TimeSpan.FromHours(24))
        {
            throw new ArgumentException("The analysis worker or lease duration is invalid.", nameof(workerId));
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        DateTimeOffset expiresAtUtc = nowUtc.Add(leaseDuration);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
                await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            Guid? candidateId = await context.Set<CandidateEntity>().AsNoTracking()
                .Where(item => (!requestedCandidateId.HasValue || item.Id == requestedCandidateId.Value)
                    && !item.MergedIntoCandidateId.HasValue && (item.Status == CandidateStatus.PendingAnalysis.ToString()
                        || (item.Status == CandidateStatus.Analyzing.ToString()
                            && item.AnalysisClaimExpiresAtUtc <= nowUtc))
                    && (!item.AnalysisClaimExpiresAtUtc.HasValue || item.AnalysisClaimExpiresAtUtc <= nowUtc))
                .OrderByDescending(item => item.InvitationPriority)
                .ThenBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.Id)
                .Select(item => (Guid?)item.Id)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (!candidateId.HasValue)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }

            int changed = await context.Set<CandidateEntity>()
                .Where(item => item.Id == candidateId.Value && !item.MergedIntoCandidateId.HasValue
                    && (item.Status == CandidateStatus.PendingAnalysis.ToString()
                        || (item.Status == CandidateStatus.Analyzing.ToString()
                            && item.AnalysisClaimExpiresAtUtc <= nowUtc))
                    && (!item.AnalysisClaimExpiresAtUtc.HasValue || item.AnalysisClaimExpiresAtUtc <= nowUtc))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, CandidateStatus.Analyzing.ToString())
                    .SetProperty(item => item.AnalysisWorkerId, workerId)
                    .SetProperty(item => item.AnalysisClaimedAtUtc, nowUtc)
                    .SetProperty(item => item.AnalysisClaimExpiresAtUtc, expiresAtUtc)
                    .SetProperty(item => item.AnalysisAttemptCount, item => item.AnalysisAttemptCount + 1)
                    .SetProperty(item => item.LastAnalysisErrorCode, (string?)null)
                    .SetProperty(item => item.UpdatedAtUtc, nowUtc)
                    .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken)
                .ConfigureAwait(false);
            if (changed == 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            context.Add(NewAction(
                candidateId.Value,
                CandidateActionType.AnalysisClaimed,
                nowUtc,
                workerId,
                "A worker claimed pending analysis.",
                JsonSerializer.Serialize(new { workerId, expiresAtUtc }, JsonOptions),
                null,
                null,
                null,
                workerId));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            CandidateEntity entity = await context.Set<CandidateEntity>().AsNoTracking()
                .SingleAsync(item => item.Id == candidateId.Value, cancellationToken).ConfigureAwait(false);
            return new(FounderScoutPersistenceMapper.Map(entity), workerId, nowUtc, expiresAtUtc);
        }

        return null;
    }

    /// <inheritdoc />
    public async ValueTask<bool> ReleaseAnalysisClaimAsync(
        ReleaseAnalysisClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateGuid(request.CandidateId, nameof(request));
        ValidateWorkerContext(request.WorkerId, request.ReasonCode, request.CorrelationId);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        CandidateStatus target = request.Retry ? CandidateStatus.PendingAnalysis : request.FailureStatus;
        FounderScoutTransitionResult<CandidateStatus> transition = CandidateLifecycle.Validate(
            CandidateStatus.Analyzing,
            target,
            request.ReasonCode);
        if (!transition.IsAllowed)
        {
            throw new ArgumentException(transition.Error!.Code, nameof(request));
        }
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        int changed = await context.Set<CandidateEntity>()
            .Where(item => item.Id == request.CandidateId
                && item.Status == CandidateStatus.Analyzing.ToString()
                && item.AnalysisWorkerId == request.WorkerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, target.ToString())
                .SetProperty(item => item.AnalysisWorkerId, (string?)null)
                .SetProperty(item => item.AnalysisClaimedAtUtc, (DateTimeOffset?)null)
                .SetProperty(item => item.AnalysisClaimExpiresAtUtc, (DateTimeOffset?)null)
                .SetProperty(item => item.LastAnalysisErrorCode, request.ReasonCode)
                .SetProperty(item => item.UpdatedAtUtc, nowUtc)
                .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken)
            .ConfigureAwait(false);
        if (changed == 1)
        {
            context.Add(NewAction(
                request.CandidateId,
                CandidateActionType.AnalysisReleased,
                nowUtc,
                request.WorkerId,
                request.Retry ? "Analysis was released for a bounded retry." : "Analysis was released without retry.",
                JsonSerializer.Serialize(new { target = target.ToString(), request.ReasonCode }, JsonOptions),
                null,
                null,
                request.RelatedRunId,
                request.CorrelationId));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        return false;
    }

    /// <inheritdoc />
    public async ValueTask<bool> FinalizeAnalysisClaimAsync(
        FinalizeAnalysisClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateGuid(request.CandidateId, nameof(request));
        ValidateWorkerContext(request.WorkerId, request.ReasonCode, request.CorrelationId);
        FounderScoutTransitionResult<CandidateStatus> transition = CandidateLifecycle.Validate(
            CandidateStatus.Analyzing,
            request.FinalStatus,
            request.ReasonCode);
        if (!transition.IsAllowed)
        {
            throw new ArgumentException(transition.Error!.Code, nameof(request));
        }

        ValidateScore(request.FounderQualityScore, false, nameof(request));
        ValidateScore(request.OurFitScore, false, nameof(request));
        ValidateScore(request.Confidence, true, nameof(request));
        ValidateScore(request.ActivityScore, false, nameof(request));
        ValidateScore(request.RiskPenalty, false, nameof(request));
        ValidateScore(request.InvitationPriority, false, nameof(request));
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        int changed = await context.Set<CandidateEntity>()
            .Where(item => item.Id == request.CandidateId
                && item.Status == CandidateStatus.Analyzing.ToString()
                && item.AnalysisWorkerId == request.WorkerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, request.FinalStatus.ToString())
                .SetProperty(item => item.LatestEvaluationId, request.EvaluationId)
                .SetProperty(item => item.FounderQualityScore, request.FounderQualityScore)
                .SetProperty(item => item.OurFitScore, request.OurFitScore)
                .SetProperty(item => item.Confidence, request.Confidence)
                .SetProperty(item => item.ActivityScore, request.ActivityScore)
                .SetProperty(item => item.RiskPenalty, request.RiskPenalty)
                .SetProperty(item => item.InvitationPriority, request.InvitationPriority)
                .SetProperty(item => item.AnalysisWorkerId, (string?)null)
                .SetProperty(item => item.AnalysisClaimedAtUtc, (DateTimeOffset?)null)
                .SetProperty(item => item.AnalysisClaimExpiresAtUtc, (DateTimeOffset?)null)
                .SetProperty(item => item.LastAnalysisErrorCode, (string?)null)
                .SetProperty(item => item.UpdatedAtUtc, nowUtc)
                .SetProperty(item => item.Version, item => item.Version + 1), cancellationToken)
            .ConfigureAwait(false);
        if (changed == 1)
        {
            if (request.EvaluationId.HasValue)
            {
                Guid? snapshotId = await context.Set<CandidateEntity>().AsNoTracking()
                    .Where(item => item.Id == request.CandidateId)
                    .Select(item => item.CurrentSnapshotId)
                    .SingleAsync(cancellationToken).ConfigureAwait(false);
                if (snapshotId.HasValue)
                {
                    _ = await context.Set<ProfileSnapshotEntity>()
                        .Where(item => item.Id == snapshotId.Value && item.Status == ProfileSnapshotStatus.PendingAnalysis.ToString())
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(item => item.Status, ProfileSnapshotStatus.AnalysisCompleted.ToString())
                            .SetProperty(item => item.ErrorCode, (string?)null), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            context.Add(NewAction(
                request.CandidateId,
                CandidateActionType.AnalysisCompleted,
                nowUtc,
                request.WorkerId,
                "Analysis completed and its separately stored dimensions were applied.",
                JsonSerializer.Serialize(new { status = request.FinalStatus.ToString(), request.ReasonCode }, JsonOptions),
                null,
                request.EvaluationId,
                request.RelatedRunId,
                request.CorrelationId));
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        return false;
    }

    private static void ValidateEvaluation(EvaluationAggregate aggregate)
    {
        Evaluation evaluation = aggregate.Evaluation;
        ValidateGuid(evaluation.Id, nameof(aggregate));
        ValidateGuid(evaluation.CandidateId, nameof(aggregate));
        ValidateGuid(evaluation.SnapshotId, nameof(aggregate));
        ValidateScore(evaluation.FounderQualityScore, false, nameof(aggregate));
        ValidateScore(evaluation.OurFitScore, false, nameof(aggregate));
        ValidateScore(evaluation.Confidence, true, nameof(aggregate));
        ValidateScore(evaluation.ActivityScore, false, nameof(aggregate));
        ValidateScore(evaluation.RiskPenalty, false, nameof(aggregate));
        ValidateScore(evaluation.InvitationPriority, false, nameof(aggregate));
        ValidateJsonObject(evaluation.StructuredEvaluationJson, 1_048_576, nameof(aggregate));
        if (evaluation.InputHash.Length != 64 || !evaluation.InputHash.All(Uri.IsHexDigit)
            || aggregate.Categories.Count > 100 || aggregate.Risks.Count > 100
            || aggregate.Categories.Any(item => item.EvaluationId != evaluation.Id || item.Score < 0 || item.Maximum <= 0 || item.Score > item.Maximum)
            || aggregate.Risks.Any(item => item.EvaluationId != evaluation.Id || item.Penalty is < 0 or > 100))
        {
            throw new ArgumentException("The evaluation aggregate is invalid.", nameof(aggregate));
        }
    }

    private static void ValidateInvitationDraft(InvitationDraft draft)
    {
        ValidateGuid(draft.Id, nameof(draft));
        ValidateGuid(draft.CandidateId, nameof(draft));
        ValidateGuid(draft.EvaluationId, nameof(draft));
        ValidateJsonObject(draft.FactsUsedJson, 65_536, nameof(draft));
        using JsonDocument errors = JsonDocument.Parse(draft.ValidationErrorsJson, new JsonDocumentOptions { MaxDepth = 32 });
        if (errors.RootElement.ValueKind != JsonValueKind.Array
            || draft.ShortDraft.Length > 5_000
            || draft.DetailedDraft.Length > 20_000
            || draft.Confidence is < 0 or > 1
            || draft.SimilarityFingerprint.Length != 64
            || !draft.SimilarityFingerprint.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("The invitation draft is invalid.", nameof(draft));
        }
    }

    private static void ValidateInvitationWindow(ManualInvitationWindow window)
    {
        ValidateGuid(window.Id, nameof(window));
        if (window.EndAtUtc <= window.StartAtUtc
            || window.PrimaryQueueSize is < 1 or > 500
            || window.ReserveQueueSize is < 0 or > 500
            || window.SentCount < 0
            || window.SentCount > window.PrimaryQueueSize
            || window.Notes?.Length > 2_000)
        {
            throw new ArgumentException("The manual invitation window is invalid.", nameof(window));
        }
    }

    private static void ValidateReport(ReportExport report)
    {
        ValidateGuid(report.Id, nameof(report));
        if (string.IsNullOrWhiteSpace(report.ReportType) || report.ReportType.Length > 128
            || string.IsNullOrWhiteSpace(report.Format) || report.Format.Length > 32
            || report.FilterSortHash.Length != 64 || !report.FilterSortHash.All(Uri.IsHexDigit)
            || report.FileHash.Length != 64 || !report.FileHash.All(Uri.IsHexDigit)
            || string.IsNullOrWhiteSpace(report.RelativePath) || Path.IsPathFullyQualified(report.RelativePath)
            || report.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "." or "..")
            || report.RowCount < 0 || report.FileSize < 0 || report.DeleteAfterUtc <= report.CreatedAtUtc)
        {
            throw new ArgumentException("The report metadata is invalid.", nameof(report));
        }
    }

    private static void ValidateWorkerContext(string workerId, string reasonCode, string correlationId)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > 128
            || string.IsNullOrWhiteSpace(reasonCode) || reasonCode.Length > 128
            || string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 128)
        {
            throw new ArgumentException("The analysis worker context is invalid.", nameof(workerId));
        }
    }

    private static void ValidateScore(decimal? value, bool confidence, string parameterName)
    {
        if (value.HasValue && (value.Value < 0 || value.Value > (confidence ? 1 : 100)))
        {
            throw new ArgumentOutOfRangeException(parameterName, "A persisted score is outside its supported range.");
        }
    }
}
