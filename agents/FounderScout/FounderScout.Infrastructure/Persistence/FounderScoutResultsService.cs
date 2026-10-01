using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FounderScout.Application;
using FounderScout.Domain;
using FounderScout.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FounderScout.Infrastructure.Persistence;

/// <summary>Bounded Stage 13 result queries and explicit local review mutations.</summary>
public sealed class FounderScoutResultsService(
    FounderScoutDbContextFactory contextFactory,
    string dataDirectory,
    TimeProvider timeProvider) : IFounderScoutResultsQuery, IFounderScoutResultsCommands, IFounderScoutReportLeaseRepository
{
    private static readonly string[] NonProfileSourceKeys = ["founders-you-may-know", "saved-profiles"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };
    private readonly FounderScoutDbContextFactory contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    private readonly string dataDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
    private readonly TimeProvider timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    public async ValueTask<FounderScoutAnalysisQueueResult> QueueUnanalyzedForAnalysisAsync(
        int maximumCandidates,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (maximumCandidates is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(maximumCandidates));
        ValidateActor(actor);

        string pending = CandidateStatus.PendingAnalysis.ToString();
        string analyzing = CandidateStatus.Analyzing.ToString();
        string parsedSnapshot = ProfileSnapshotStatus.Parsed.ToString();
        string failedSnapshot = ProfileSnapshotStatus.Failed.ToString();
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        IQueryable<CandidateEntity> active = ActiveCandidates(context);
        int alreadyPending = await active.CountAsync(
            item => item.Status == pending || item.Status == analyzing,
            cancellationToken).ConfigureAwait(false);
        IQueryable<CandidateEntity> eligible = active.Where(item =>
            item.CurrentSnapshotId.HasValue
            && !item.LatestEvaluationId.HasValue
            && item.Status != pending
            && item.Status != analyzing
            && context.Set<ScreeningDecisionEntity>().Any(screening =>
                screening.CandidateId == item.Id
                && screening.SnapshotId == item.CurrentSnapshotId.Value));
        int eligibleCount = await eligible.CountAsync(cancellationToken).ConfigureAwait(false);
        List<CandidateEntity> candidates = await eligible
            .OrderByDescending(item => item.LastSeenAtUtc)
            .ThenBy(item => item.CreatedAtUtc)
            .ThenBy(item => item.Id)
            .Take(maximumCandidates)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (CandidateEntity candidate in candidates)
        {
            context.Attach(candidate);
            string previousStatus = candidate.Status;
            candidate.Status = pending;
            candidate.AnalysisWorkerId = null;
            candidate.AnalysisClaimedAtUtc = null;
            candidate.AnalysisClaimExpiresAtUtc = null;
            candidate.LastAnalysisErrorCode = null;
            candidate.UpdatedAtUtc = nowUtc;
            candidate.Version++;

            ProfileSnapshotEntity? snapshot = await context.Set<ProfileSnapshotEntity>()
                .SingleOrDefaultAsync(item => item.Id == candidate.CurrentSnapshotId, cancellationToken)
                .ConfigureAwait(false);
            if (snapshot is not null && snapshot.Status is not null
                && (snapshot.Status == parsedSnapshot || snapshot.Status == failedSnapshot))
            {
                snapshot.Status = ProfileSnapshotStatus.PendingAnalysis.ToString();
            }

            context.Add(NewAction(
                candidate.Id,
                CandidateActionType.StateTransitioned,
                nowUtc,
                actor,
                "The user explicitly queued a stored candidate for AI evaluation.",
                new { from = previousStatus, to = pending, reasonCode = "analysis.user-requested" },
                null,
                null,
                Guid.NewGuid().ToString("D")));
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(candidates.Count, alreadyPending, eligibleCount);
    }

    /// <inheritdoc />
    public async ValueTask<bool> QueueCandidateForAnalysisAsync(Guid candidateId, string actor, CancellationToken cancellationToken = default)
    {
        if (candidateId == Guid.Empty) throw new ArgumentException("A candidate is required.", nameof(candidateId));
        ValidateActor(actor);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        CandidateEntity? candidate = await ActiveCandidates(context)
            .SingleOrDefaultAsync(item => item.Id == candidateId, cancellationToken).ConfigureAwait(false);
        if (candidate is null || !candidate.CurrentSnapshotId.HasValue
            || candidate.Status == CandidateStatus.Analyzing.ToString()
            || !await context.Set<ScreeningDecisionEntity>().AnyAsync(item => item.CandidateId == candidateId
                && item.SnapshotId == candidate.CurrentSnapshotId.Value, cancellationToken).ConfigureAwait(false))
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        context.Attach(candidate);
        string previousStatus = candidate.Status;
        candidate.Status = CandidateStatus.PendingAnalysis.ToString();
        candidate.AnalysisWorkerId = null;
        candidate.AnalysisClaimedAtUtc = null;
        candidate.AnalysisClaimExpiresAtUtc = null;
        candidate.LastAnalysisErrorCode = null;
        candidate.UpdatedAtUtc = nowUtc;
        candidate.Version++;
        context.Add(NewAction(candidate.Id, CandidateActionType.StateTransitioned, nowUtc, actor,
            "The user explicitly requested a fresh AI evaluation for this candidate.",
            new { from = previousStatus, to = candidate.Status, reasonCode = "analysis.candidate-requested" },
            null, candidate.LatestEvaluationId, Guid.NewGuid().ToString("D")));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async ValueTask<FounderScoutDashboard> GetDashboardAsync(
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset boundary = sinceUtc.ToUniversalTime();
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<CandidateEntity> active = ActiveCandidates(context);
        int candidates = await active.CountAsync(cancellationToken).ConfigureAwait(false);
        int changed = await active.CountAsync(item => item.FirstSeenAtUtc >= boundary || item.UpdatedAtUtc >= boundary, cancellationToken).ConfigureAwait(false);
        int pendingScreening = await context.Set<ProfileSnapshotEntity>().AsNoTracking()
            .CountAsync(item => item.Status == ProfileSnapshotStatus.Captured.ToString(), cancellationToken).ConfigureAwait(false);
        int pendingAnalysis = await active.CountAsync(item => item.Status == CandidateStatus.PendingAnalysis.ToString(), cancellationToken).ConfigureAwait(false);
        int analyzed = await active.CountAsync(item => item.LatestEvaluationId.HasValue, cancellationToken).ConfigureAwait(false);
        Dictionary<string, int> recommendations = await context.Set<EvaluationEntity>().AsNoTracking()
            .Where(item => item.Id == context.Set<CandidateEntity>().Where(candidate => candidate.Id == item.CandidateId && !candidate.MergedIntoCandidateId.HasValue).Select(candidate => candidate.LatestEvaluationId).FirstOrDefault())
            .GroupBy(item => item.Recommendation)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Count, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
        int readyDrafts = await context.Set<InvitationDraftEntity>().AsNoTracking().CountAsync(
            item => !item.Superseded && (item.Status == InvitationDraftStatus.Valid.ToString() || item.Status == InvitationDraftStatus.Reviewed.ToString()),
            cancellationToken).ConfigureAwait(false);
        int reviewDrafts = await context.Set<InvitationDraftEntity>().AsNoTracking().CountAsync(
            item => !item.Superseded && (item.Status == InvitationDraftStatus.NeedsReview.ToString() || item.Status == InvitationDraftStatus.Invalid.ToString()),
            cancellationToken).ConfigureAwait(false);
        ManualInvitationWindowEntity? window = await CurrentWindowAsync(context, nowUtc, cancellationToken).ConfigureAwait(false);
        int primary = 0;
        int reserve = 0;
        if (window is not null)
        {
            primary = await context.Set<InvitationQueueEntryEntity>().AsNoTracking().CountAsync(
                item => item.WindowId == window.Id && item.RemovedAtUtc == null && item.QueueKind == InvitationQueueKind.Primary.ToString(), cancellationToken).ConfigureAwait(false);
            reserve = await context.Set<InvitationQueueEntryEntity>().AsNoTracking().CountAsync(
                item => item.WindowId == window.Id && item.RemovedAtUtc == null && item.QueueKind == InvitationQueueKind.Reserve.ToString(), cancellationToken).ConfigureAwait(false);
        }
        int manuallySent = await active.CountAsync(item => item.Status == CandidateStatus.ManuallySent.ToString(), cancellationToken).ConfigureAwait(false);
        int accepted = await active.CountAsync(item => item.Status == CandidateStatus.Accepted.ToString(), cancellationToken).ConfigureAwait(false);
        int calls = await active.CountAsync(item => item.Status == CandidateStatus.CallScheduled.ToString(), cancellationToken).ConfigureAwait(false);
        List<BrowserAccountEntity> accountEntities = await context.Set<BrowserAccountEntity>().AsNoTracking().OrderBy(item => item.DisplayName).ThenBy(item => item.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        List<DiscoverySegmentEntity> segmentEntities = await context.Set<DiscoverySegmentEntity>().AsNoTracking().OrderByDescending(item => item.Priority).ThenBy(item => item.Name).ThenBy(item => item.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        int accountAttention = accountEntities.Count(item => item.Enabled && item.SessionStatus != BrowserSessionStatus.Healthy.ToString());
        int segmentAttention = segmentEntities.Count(item => !item.Enabled || item.PausedUntilUtc > nowUtc || item.ConsecutiveLowYieldRuns >= 3);
        int manualReview = await active.CountAsync(item => item.Status == CandidateStatus.ManualReview.ToString(), cancellationToken).ConfigureAwait(false);
        List<ReportExportEntity> reports = await context.Set<ReportExportEntity>().AsNoTracking().OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id).Take(8).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new(
            candidates,
            changed,
            pendingScreening,
            pendingAnalysis,
            analyzed,
            recommendations,
            readyDrafts,
            reviewDrafts,
            primary,
            reserve,
            manuallySent,
            accepted,
            calls,
            accountAttention + segmentAttention + manualReview + reviewDrafts,
            accountEntities.Select(FounderScoutPersistenceMapper.Map).ToArray(),
            segmentEntities.Select(FounderScoutPersistenceMapper.Map).ToArray(),
            reports.Select(FounderScoutPersistenceMapper.Map).ToArray());
    }

    /// <inheritdoc />
    public async ValueTask<FounderScoutCandidateResultPage> QueryCandidatesAsync(
        FounderScoutCandidateQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateQuery(query);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ManualInvitationWindowEntity? window = await CurrentWindowAsync(context, nowUtc, cancellationToken).ConfigureAwait(false);
        IQueryable<CandidateEntity> source = ActiveCandidates(context);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string search = query.Search.Trim();
            source = source.Where(item => item.DisplayName.Contains(search)
                || (item.NormalizedLocation != null && item.NormalizedLocation.Contains(search))
                || (item.LastActivityText != null && item.LastActivityText.Contains(search)));
        }
        if (query.Statuses is { Count: > 0 })
        {
            string[] statuses = query.Statuses.Select(item => item.ToString()).Distinct(StringComparer.Ordinal).ToArray();
            source = source.Where(item => statuses.Contains(item.Status));
        }
        if (query.Recommendations is { Count: > 0 })
        {
            string[] recommendations = query.Recommendations.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToArray();
            source = source.Where(item => item.LatestEvaluationId.HasValue
                && context.Set<EvaluationEntity>().Any(evaluation => evaluation.Id == item.LatestEvaluationId && recommendations.Contains(evaluation.Recommendation)));
        }
        if (query.MinimumScore.HasValue) source = source.Where(item => item.InvitationPriority >= query.MinimumScore.Value);
        if (query.MaximumScore.HasValue) source = source.Where(item => item.InvitationPriority <= query.MaximumScore.Value);
        if (query.MinimumConfidence.HasValue) source = source.Where(item => item.Confidence >= query.MinimumConfidence.Value);
        if (query.TechnicalStatus.HasValue) source = source.Where(item => item.TechnicalStatus == query.TechnicalStatus.Value.ToString());
        if (query.CommitmentStatus.HasValue) source = source.Where(item => item.CommitmentStatus == query.CommitmentStatus.Value.ToString());
        if (query.IdeaStatus.HasValue) source = source.Where(item => item.IdeaCommitmentStatus == query.IdeaStatus.Value.ToString());
        if (query.ChangedFromUtc.HasValue) source = source.Where(item => item.UpdatedAtUtc >= query.ChangedFromUtc.Value.ToUniversalTime());
        if (query.ChangedToUtc.HasValue) source = source.Where(item => item.UpdatedAtUtc <= query.ChangedToUtc.Value.ToUniversalTime());
        if (query.ActiveSinceUtc.HasValue) source = source.Where(item => item.LastActivityAtUtc >= query.ActiveSinceUtc.Value.ToUniversalTime());
        if (!string.IsNullOrWhiteSpace(query.AccountId)) source = source.Where(item => item.CurrentSnapshotId.HasValue && context.Set<ProfileSnapshotEntity>().Any(snapshot => snapshot.Id == item.CurrentSnapshotId && snapshot.SourceAccountId == query.AccountId));
        if (!string.IsNullOrWhiteSpace(query.SegmentId)) source = source.Where(item => item.CurrentSnapshotId.HasValue && context.Set<ProfileSnapshotEntity>().Any(snapshot => snapshot.Id == item.CurrentSnapshotId && snapshot.SourceSegmentId == query.SegmentId));
        if (!string.IsNullOrWhiteSpace(query.RiskKey)) source = source.Where(item => item.LatestEvaluationId.HasValue && context.Set<EvaluationRiskEntity>().Any(risk => risk.EvaluationId == item.LatestEvaluationId && risk.Key == query.RiskKey));
        if (query.NeedsManualReview.HasValue)
        {
            string manual = CandidateStatus.ManualReview.ToString();
            string needsReview = EvaluationStatus.NeedsReview.ToString();
            source = query.NeedsManualReview.Value
                ? source.Where(item => item.Status == manual || (item.LatestEvaluationId.HasValue && context.Set<EvaluationEntity>().Any(evaluation => evaluation.Id == item.LatestEvaluationId && evaluation.Status == needsReview)))
                : source.Where(item => item.Status != manual && (!item.LatestEvaluationId.HasValue || !context.Set<EvaluationEntity>().Any(evaluation => evaluation.Id == item.LatestEvaluationId && evaluation.Status == needsReview)));
        }
        if (query.HasTractionEvidence.HasValue)
        {
            bool wanted = query.HasTractionEvidence.Value;
            source = wanted
                ? source.Where(item => item.LatestEvaluationId.HasValue
                    && context.Set<EvaluationCategoryEntity>().Any(category => category.EvaluationId == item.LatestEvaluationId
                        && category.Key == "tractionAndValidation" && category.Score > 0))
                : source.Where(item => !item.LatestEvaluationId.HasValue
                    || !context.Set<EvaluationCategoryEntity>().Any(category => category.EvaluationId == item.LatestEvaluationId
                        && category.Key == "tractionAndValidation" && category.Score > 0));
        }
        if (query.QueueKind.HasValue)
        {
            if (window is null)
            {
                source = source.Where(_ => false);
            }
            else
            {
                string queueKind = query.QueueKind.Value.ToString();
                source = source.Where(item => context.Set<InvitationQueueEntryEntity>().Any(entry => entry.WindowId == window.Id && entry.CandidateId == item.Id && entry.RemovedAtUtc == null && entry.QueueKind == queueKind));
            }
        }

        int total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        source = ApplySort(source, query.Sort);
        List<CandidateEntity> candidates = await source.Skip(query.Offset).Take(query.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        Guid[] ids = candidates.Select(item => item.Id).ToArray();
        Guid[] snapshotIds = candidates.Where(item => item.CurrentSnapshotId.HasValue).Select(item => item.CurrentSnapshotId!.Value).ToArray();
        Guid[] evaluationIds = candidates.Where(item => item.LatestEvaluationId.HasValue).Select(item => item.LatestEvaluationId!.Value).ToArray();
        Dictionary<Guid, ProfileSnapshotEntity> snapshots = await context.Set<ProfileSnapshotEntity>().AsNoTracking().Where(item => snapshotIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken).ConfigureAwait(false);
        Dictionary<Guid, EvaluationEntity> evaluations = await context.Set<EvaluationEntity>().AsNoTracking().Where(item => evaluationIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken).ConfigureAwait(false);
        HashSet<Guid> changedCandidates = (await context.Set<ProfileSnapshotEntity>().AsNoTracking().Where(item => ids.Contains(item.CandidateId)).GroupBy(item => item.CandidateId).Where(group => group.Count() > 1).Select(group => group.Key).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();
        HashSet<Guid> tractionEvaluations = (await context.Set<EvaluationCategoryEntity>().AsNoTracking().Where(item => evaluationIds.Contains(item.EvaluationId) && item.Key == "tractionAndValidation" && item.Score > 0).Select(item => item.EvaluationId).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();
        Dictionary<Guid, InvitationQueueEntryEntity> queueEntries = window is null
            ? []
            : await context.Set<InvitationQueueEntryEntity>().AsNoTracking().Where(item => item.WindowId == window.Id && ids.Contains(item.CandidateId) && item.RemovedAtUtc == null).ToDictionaryAsync(item => item.CandidateId, cancellationToken).ConfigureAwait(false);
        FounderScoutCandidateListItem[] items = candidates.Select((candidate, index) => CreateListItem(
            candidate,
            query.Offset + index + 1,
            candidate.CurrentSnapshotId.HasValue ? snapshots.GetValueOrDefault(candidate.CurrentSnapshotId.Value) : null,
            candidate.LatestEvaluationId.HasValue ? evaluations.GetValueOrDefault(candidate.LatestEvaluationId.Value) : null,
            queueEntries.GetValueOrDefault(candidate.Id),
            changedCandidates.Contains(candidate.Id),
            candidate.LatestEvaluationId.HasValue && tractionEvaluations.Contains(candidate.LatestEvaluationId.Value))).ToArray();
        return new(items, query.Offset, query.PageSize, total);
    }

    /// <inheritdoc />
    public async ValueTask<FounderScoutCandidateDetail?> GetCandidateAsync(Guid candidateId, CancellationToken cancellationToken = default)
    {
        ValidateGuid(candidateId, nameof(candidateId));
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        CandidateEntity? candidate = await context.Set<CandidateEntity>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == candidateId && !item.MergedIntoCandidateId.HasValue, cancellationToken).ConfigureAwait(false);
        if (candidate is null) return null;
        List<ProfileSnapshotEntity> snapshotEntities = await context.Set<ProfileSnapshotEntity>().AsNoTracking().Where(item => item.CandidateId == candidateId).OrderByDescending(item => item.CapturedAtUtc).ThenByDescending(item => item.Id).Take(100).ToListAsync(cancellationToken).ConfigureAwait(false);
        List<ScreeningDecisionEntity> screenings = await context.Set<ScreeningDecisionEntity>().AsNoTracking().Where(item => item.CandidateId == candidateId).OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id).Take(100).ToListAsync(cancellationToken).ConfigureAwait(false);
        List<EvaluationEntity> evaluationEntities = await context.Set<EvaluationEntity>().AsNoTracking().Where(item => item.CandidateId == candidateId).OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id).Take(100).ToListAsync(cancellationToken).ConfigureAwait(false);
        Guid[] evaluationIds = evaluationEntities.Select(item => item.Id).ToArray();
        List<EvaluationCategoryEntity> categories = await context.Set<EvaluationCategoryEntity>().AsNoTracking().Where(item => evaluationIds.Contains(item.EvaluationId)).OrderBy(item => item.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
        List<EvaluationRiskEntity> risks = await context.Set<EvaluationRiskEntity>().AsNoTracking().Where(item => evaluationIds.Contains(item.EvaluationId)).OrderByDescending(item => item.Penalty).ThenBy(item => item.Key).ToListAsync(cancellationToken).ConfigureAwait(false);
        List<InvitationDraftEntity> draftEntities = await context.Set<InvitationDraftEntity>().AsNoTracking().Where(item => item.CandidateId == candidateId).OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id).Take(100).ToListAsync(cancellationToken).ConfigureAwait(false);
        Guid[] draftIds = draftEntities.Select(item => item.Id).ToArray();
        List<InvitationDraftRevisionEntity> revisionEntities = await context.Set<InvitationDraftRevisionEntity>().AsNoTracking().Where(item => draftIds.Contains(item.SourceDraftId)).OrderByDescending(item => item.EditedAtUtc).ThenByDescending(item => item.Id).Take(100).ToListAsync(cancellationToken).ConfigureAwait(false);
        List<CandidateActionEntity> actions = await context.Set<CandidateActionEntity>().AsNoTracking().Where(item => item.CandidateId == candidateId).OrderByDescending(item => item.OccurredAtUtc).ThenByDescending(item => item.Id).Take(200).ToListAsync(cancellationToken).ConfigureAwait(false);
        List<CandidateIdentityConflictEntity> conflicts = await context.Set<CandidateIdentityConflictEntity>().AsNoTracking().Where(item => item.CandidateId == candidateId || item.ConflictingCandidateId == candidateId).OrderByDescending(item => item.CreatedAtUtc).Take(50).ToListAsync(cancellationToken).ConfigureAwait(false);
        ManualInvitationWindowEntity? window = await CurrentWindowAsync(context, nowUtc, cancellationToken).ConfigureAwait(false);
        InvitationQueueEntryEntity? entry = window is null ? null : await context.Set<InvitationQueueEntryEntity>().AsNoTracking().SingleOrDefaultAsync(item => item.WindowId == window.Id && item.CandidateId == candidateId && item.RemovedAtUtc == null, cancellationToken).ConfigureAwait(false);

        Dictionary<Guid, IReadOnlyList<FounderProfileChange>> changes = actions
            .Where(item => item.ActionType == CandidateActionType.ProfileChanged.ToString())
            .Select(item => ParseChanges(item.DataJson))
            .Where(item => item.SnapshotId.HasValue)
            .GroupBy(item => item.SnapshotId!.Value)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<FounderProfileChange>)group.SelectMany(item => item.Changes).Distinct().ToArray());
        FounderScoutSnapshotHistoryItem[] snapshots = snapshotEntities.Select(item => new FounderScoutSnapshotHistoryItem(
            FounderScoutPersistenceMapper.Map(item),
            changes.GetValueOrDefault(item.Id) ?? [],
            item.RawArtifactDeletedAtUtc is null && !string.IsNullOrWhiteSpace(item.RawArtifactRelativePath))).ToArray();
        FounderScoutEvaluationHistoryItem[] evaluations = evaluationEntities.Select(item => CreateEvaluationHistory(
            item,
            categories.Where(category => category.EvaluationId == item.Id).ToArray(),
            risks.Where(risk => risk.EvaluationId == item.Id).ToArray())).ToArray();
        ProfileSnapshotEntity? currentSnapshot = candidate.CurrentSnapshotId.HasValue
            ? snapshotEntities.SingleOrDefault(item => item.Id == candidate.CurrentSnapshotId.Value)
            : snapshotEntities.FirstOrDefault();
        NormalizedFounderProfile? profile = DeserializeProfile(currentSnapshot?.NormalizedProfileJson);
        string summary = evaluations.FirstOrDefault()?.Evaluation.StructuredEvaluationJson is string structured
            ? ParseEvaluationResponse(structured)?.ProfileSummary ?? BuildProfileSummary(profile)
            : BuildProfileSummary(profile);
        return new(
            FounderScoutPersistenceMapper.Map(candidate),
            profile,
            summary,
            screenings.FirstOrDefault() is { } screening ? FounderScoutPersistenceMapper.Map(screening) : null,
            snapshots,
            evaluations,
            draftEntities.Select(FounderScoutPersistenceMapper.Map).ToArray(),
            revisionEntities.Select(FounderScoutPersistenceMapper.Map).ToArray(),
            entry is null ? null : FounderScoutPersistenceMapper.Map(entry),
            window is null ? null : FounderScoutPersistenceMapper.Map(window),
            actions.Select(FounderScoutPersistenceMapper.Map).ToArray(),
            conflicts.Select(FounderScoutPersistenceMapper.Map).ToArray());
    }

    /// <inheritdoc />
    public async ValueTask<FounderScoutInvitationQueueView> GetInvitationQueueAsync(
        DateTimeOffset atUtc,
        int defaultPrimarySize,
        int defaultReserveSize,
        decimal minimumConfidence,
        CancellationToken cancellationToken = default)
    {
        if (defaultPrimarySize is < 1 or > 500 || defaultReserveSize is < 0 or > 500 || minimumConfidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(defaultPrimarySize));
        DateTimeOffset utc = atUtc.ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ManualInvitationWindowEntity? windowEntity = await CurrentWindowAsync(context, utc, cancellationToken).ConfigureAwait(false);
        ManualInvitationWindow window = windowEntity is null
            ? new(Guid.Empty, utc.Date, utc.Date.AddDays(30), defaultPrimarySize, defaultReserveSize, 0, null, utc, utc, 0)
            : FounderScoutPersistenceMapper.Map(windowEntity);
        List<FounderScoutInvitationQueueItem> primary = [];
        List<FounderScoutInvitationQueueItem> reserve = [];
        if (windowEntity is not null)
        {
            List<InvitationQueueEntryEntity> entries = await context.Set<InvitationQueueEntryEntity>().AsNoTracking().Where(item => item.WindowId == windowEntity.Id && item.RemovedAtUtc == null).OrderBy(item => item.QueueKind).ThenBy(item => item.Position).ThenBy(item => item.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            Guid[] candidateIds = entries.Select(item => item.CandidateId).ToArray();
            Dictionary<Guid, CandidateEntity> candidates = await context.Set<CandidateEntity>().AsNoTracking().Where(item => candidateIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken).ConfigureAwait(false);
            List<InvitationDraftEntity> drafts = await context.Set<InvitationDraftEntity>().AsNoTracking().Where(item => candidateIds.Contains(item.CandidateId) && !item.Superseded).OrderByDescending(item => item.CreatedAtUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
            Guid[] draftIds = drafts.Select(item => item.Id).ToArray();
            List<InvitationDraftRevisionEntity> revisions = await context.Set<InvitationDraftRevisionEntity>().AsNoTracking().Where(item => draftIds.Contains(item.SourceDraftId) && item.IsActive).ToListAsync(cancellationToken).ConfigureAwait(false);
            HashSet<Guid> conflicts = (await context.Set<CandidateIdentityConflictEntity>().AsNoTracking().Where(item => !item.Resolved && (candidateIds.Contains(item.CandidateId) || (item.ConflictingCandidateId.HasValue && candidateIds.Contains(item.ConflictingCandidateId.Value)))).Select(item => item.CandidateId).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();
            foreach (InvitationQueueEntryEntity entry in entries)
            {
                CandidateEntity candidate = candidates[entry.CandidateId];
                InvitationDraftEntity? draft = drafts.FirstOrDefault(item => item.CandidateId == candidate.Id);
                string[] reasons = EligibilityReasons(candidate, draft, conflicts.Contains(candidate.Id), minimumConfidence, 120, utc, entry.ManuallyIncluded);
                var row = new FounderScoutInvitationQueueItem(
                    FounderScoutPersistenceMapper.Map(entry),
                    CreateListItem(candidate, entry.Position, null, null, entry, profileChanged: false, hasTraction: false),
                    candidate.CanonicalSourceUrl,
                    draft is null ? null : FounderScoutPersistenceMapper.Map(draft),
                    draft is null ? null : revisions.Where(item => item.SourceDraftId == draft.Id).Select(FounderScoutPersistenceMapper.Map).FirstOrDefault(),
                    reasons.Length == 0,
                    reasons);
                (entry.QueueKind == InvitationQueueKind.Primary.ToString() ? primary : reserve).Add(row);
            }
        }

        FounderScoutCandidateResultPage suggestions = await QueryCandidatesAsync(new(
            Search: null,
            Recommendations: null,
            Statuses: [CandidateStatus.Shortlisted],
            MinimumScore: null,
            MaximumScore: null,
            MinimumConfidence: minimumConfidence,
            TechnicalStatus: null,
            CommitmentStatus: null,
            IdeaStatus: null,
            HasTractionEvidence: null,
            RiskKey: null,
            ChangedFromUtc: null,
            ChangedToUtc: null,
            ActiveSinceUtc: null,
            QueueKind: null,
            AccountId: null,
            SegmentId: null,
            NeedsManualReview: false,
            FounderScoutCandidateSort.PriorityDescending,
            0,
            Math.Min(100, defaultPrimarySize + defaultReserveSize)), cancellationToken).ConfigureAwait(false);
        HashSet<Guid> queued = primary.Concat(reserve).Select(item => item.Candidate.CandidateId).ToHashSet();
        return new(window, primary, reserve, suggestions.Items.Where(item => !queued.Contains(item.CandidateId)).ToArray());
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<BrowserAccount>> GetBrowserAccountsAsync(CancellationToken cancellationToken = default)
    {
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return (await context.Set<BrowserAccountEntity>().AsNoTracking().OrderBy(item => item.DisplayName).ThenBy(item => item.Id).ToListAsync(cancellationToken).ConfigureAwait(false)).Select(FounderScoutPersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DiscoverySegment>> GetDiscoverySegmentsAsync(CancellationToken cancellationToken = default)
    {
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return (await context.Set<DiscoverySegmentEntity>().AsNoTracking().OrderByDescending(item => item.Priority).ThenBy(item => item.Name).ThenBy(item => item.Id).ToListAsync(cancellationToken).ConfigureAwait(false)).Select(FounderScoutPersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ReportExport>> GetReportsAsync(int maximumResults, CancellationToken cancellationToken = default)
    {
        if (maximumResults is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(maximumResults));
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return (await context.Set<ReportExportEntity>().AsNoTracking().OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id).Take(maximumResults).ToListAsync(cancellationToken).ConfigureAwait(false)).Select(FounderScoutPersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<FounderScoutReportModel> BuildReportModelAsync(
        int top,
        DateTimeOffset atUtc,
        decimal minimumConfidence,
        CancellationToken cancellationToken = default)
    {
        if (top is < 1 or > 1_000) throw new ArgumentOutOfRangeException(nameof(top));
        FounderScoutCandidateResultPage page = await QueryCandidatesAsync(new(
            Search: null,
            Recommendations: null,
            Statuses: null,
            MinimumScore: null,
            MaximumScore: null,
            MinimumConfidence: null,
            TechnicalStatus: null,
            CommitmentStatus: null,
            IdeaStatus: null,
            HasTractionEvidence: null,
            RiskKey: null,
            ChangedFromUtc: null,
            ChangedToUtc: null,
            ActiveSinceUtc: null,
            QueueKind: null,
            AccountId: null,
            SegmentId: null,
            NeedsManualReview: false,
            Sort: FounderScoutCandidateSort.PriorityDescending,
            Offset: 0,
            PageSize: top), cancellationToken).ConfigureAwait(false);
        var rows = new List<FounderScoutReportCandidate>(page.Items.Count);
        var positiveDistribution = new Dictionary<string, int>(StringComparer.Ordinal);
        var riskDistribution = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (FounderScoutCandidateListItem item in page.Items)
        {
            FounderScoutCandidateDetail? detail = await GetCandidateAsync(item.CandidateId, cancellationToken).ConfigureAwait(false);
            if (detail is null) continue;
            FounderScoutEvaluationHistoryItem? evaluation = detail.Evaluations.Count == 0 ? null : detail.Evaluations[0];
            InvitationDraft? draft = detail.Drafts.FirstOrDefault(current => !current.Superseded);
            InvitationDraftRevision? revision = draft is null ? null : detail.DraftRevisions.FirstOrDefault(current => current.SourceDraftId == draft.Id && current.IsActive);
            foreach (string signal in evaluation?.PositiveSignals ?? []) positiveDistribution[signal] = positiveDistribution.GetValueOrDefault(signal) + 1;
            foreach (EvaluationRisk risk in evaluation?.Risks ?? []) riskDistribution[risk.Key] = riskDistribution.GetValueOrDefault(risk.Key) + 1;
            rows.Add(new(
                item,
                detail.Summary,
                evaluation?.Categories ?? [],
                evaluation?.Risks ?? [],
                evaluation?.PositiveSignals ?? [],
                evaluation?.MissingEvidence ?? [],
                evaluation?.PriorityQuestions ?? [],
                revision?.ShortDraft ?? draft?.ShortDraft,
                revision?.DetailedDraft ?? draft?.DetailedDraft,
                detail.Candidate.CanonicalSourceUrl,
                detail.QueueEntry?.QueueKind,
                detail.QueueEntry?.Position,
                evaluation is null ? "unscored" : $"{evaluation.Evaluation.ScorecardVersion}/{evaluation.Evaluation.PromptVersion}/{evaluation.Evaluation.EvaluatorModel}"));
        }
        FounderScoutCandidateResultPage attention = await QueryCandidatesAsync(new(
            Search: null,
            Recommendations: null,
            Statuses: null,
            MinimumScore: null,
            MaximumScore: null,
            MinimumConfidence: null,
            TechnicalStatus: null,
            CommitmentStatus: null,
            IdeaStatus: null,
            HasTractionEvidence: null,
            RiskKey: null,
            ChangedFromUtc: null,
            ChangedToUtc: null,
            ActiveSinceUtc: null,
            QueueKind: null,
            AccountId: null,
            SegmentId: null,
            NeedsManualReview: true,
            Sort: FounderScoutCandidateSort.PriorityDescending,
            Offset: 0,
            PageSize: Math.Min(100, top)), cancellationToken).ConfigureAwait(false);
        IReadOnlyList<BrowserAccount> accounts = await GetBrowserAccountsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<DiscoverySegment> segments = await GetDiscoverySegmentsAsync(cancellationToken).ConfigureAwait(false);
        FounderScoutInvitationQueueView queue = await GetInvitationQueueAsync(atUtc, 15, 15, minimumConfidence, cancellationToken).ConfigureAwait(false);
        string scorecards = string.Join(", ", rows.Select(item => item.EvaluationVersion.Split('/')[0]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        string filterHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"stage13-report-v1|top={top}|order=priority-confidence-activity-name-id|{string.Join('|', rows.Select(item => item.Candidate.CandidateId))}")));
        DateTimeOffset generatedUtc = atUtc.ToUniversalTime();
        return new(
            "1.0",
            generatedUtc,
            generatedUtc.ToLocalTime(),
            filterHash,
            scorecards,
            rows.Count(item => item.Candidate.Status is CandidateStatus.MessageReviewed
                or CandidateStatus.ManuallySent
                or CandidateStatus.Accepted
                or CandidateStatus.Declined
                or CandidateStatus.NoResponse
                or CandidateStatus.CallScheduled
                or CandidateStatus.CallCompleted
                or CandidateStatus.SecondCall
                or CandidateStatus.PassedAfterCall
                or CandidateStatus.TrialProject
                or CandidateStatus.Selected),
            rows,
            attention.Items,
            positiveDistribution,
            riskDistribution,
            accounts,
            segments,
            queue.Window.Id == Guid.Empty ? null : queue);
    }

    /// <inheritdoc />
    public async ValueTask<InvitationDraftRevision> SaveDraftRevisionAsync(
        SaveInvitationDraftRevisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateActor(request.Actor);
        if (request.ShortDraft.Length > 5_000 || request.DetailedDraft.Length > 20_000 || request.MaximumCharacters is < 100 or > 5_000 || request.SimilarityThreshold is < 0 or > 1) throw new ArgumentException("The edited invitation draft is invalid.", nameof(request));
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        InvitationDraftEntity source = await context.Set<InvitationDraftEntity>().SingleOrDefaultAsync(item => item.Id == request.SourceDraftId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The generated invitation draft does not exist.");
        List<InvitationDraftEntity> recent = await context.Set<InvitationDraftEntity>().AsNoTracking().Where(item => !item.Superseded && item.CandidateId != source.CandidateId).OrderByDescending(item => item.CreatedAtUtc).Take(100).ToListAsync(cancellationToken).ConfigureAwait(false);
        ManualDraftValidation validation = ValidateManualDraft(request, recent);
        await context.Set<InvitationDraftRevisionEntity>().Where(item => item.SourceDraftId == source.Id && item.IsActive).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsActive, false), cancellationToken).ConfigureAwait(false);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        var entity = new InvitationDraftRevisionEntity
        {
            Id = Guid.NewGuid(),
            SourceDraftId = source.Id,
            CandidateId = source.CandidateId,
            ShortDraft = request.ShortDraft.Trim(),
            DetailedDraft = request.DetailedDraft.Trim(),
            EditedBy = request.Actor,
            EditedAtUtc = nowUtc,
            ValidationPassed = validation.Errors.Count == 0,
            ValidationErrorsJson = JsonSerializer.Serialize(validation.Errors, JsonOptions),
            MaximumObservedSimilarity = validation.Similarity,
            SimilarityFingerprint = validation.Fingerprint,
            IsActive = true,
        };
        context.Add(entity);
        context.Add(NewAction(source.CandidateId, CandidateActionType.InvitationDraftRevisionCreated, nowUtc, request.Actor, "A human created an immutable invitation draft revision.", new { sourceDraftId = source.Id, revisionId = entity.Id, validationPassed = entity.ValidationPassed, similarityAdvisory = validation.Similarity }, source.Id, source.EvaluationId, entity.Id.ToString("D")));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return FounderScoutPersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<InvitationDraft> MarkDraftReviewedAsync(Guid draftId, string actor, CancellationToken cancellationToken = default)
    {
        ValidateGuid(draftId, nameof(draftId));
        ValidateActor(actor);
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        InvitationDraftEntity draft = await context.Set<InvitationDraftEntity>().SingleOrDefaultAsync(item => item.Id == draftId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The invitation draft does not exist.");
        InvitationDraftStatus current = Enum.Parse<InvitationDraftStatus>(draft.Status);
        FounderScoutTransitionResult<InvitationDraftStatus> allowed = InvitationLifecycle.Validate(current, InvitationDraftStatus.Reviewed, "invitation.reviewed.local");
        if (!allowed.IsAllowed) throw new InvalidOperationException(allowed.Error!.Code);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        draft.Status = InvitationDraftStatus.Reviewed.ToString();
        draft.ReviewedAtUtc = nowUtc;
        draft.ReviewedBy = actor;
        draft.Version++;
        CandidateEntity candidate = await context.Set<CandidateEntity>().SingleAsync(item => item.Id == draft.CandidateId, cancellationToken).ConfigureAwait(false);
        if (candidate.Status == CandidateStatus.QueuedForInvite.ToString())
        {
            candidate.Status = CandidateStatus.MessageReviewed.ToString();
            candidate.UpdatedAtUtc = nowUtc;
            candidate.Version++;
        }
        context.Add(NewAction(draft.CandidateId, CandidateActionType.InvitationDraftReviewed, nowUtc, actor, "The invitation draft was reviewed for manual use.", new { draftId }, draft.Id, draft.EvaluationId, draft.Id.ToString("D")));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return FounderScoutPersistenceMapper.Map(draft);
    }

    /// <inheritdoc />
    public async ValueTask<ManualInvitationWindow> CreateInvitationWindowAsync(DateTimeOffset startAtUtc, DateTimeOffset endAtUtc, int primarySize, int reserveSize, string? notes, CancellationToken cancellationToken = default)
    {
        if (endAtUtc <= startAtUtc || primarySize is < 1 or > 500 || reserveSize is < 0 or > 500 || (notes?.Length ?? 0) > 2_000) throw new ArgumentException("The invitation window is invalid.");
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        var entity = new ManualInvitationWindowEntity
        {
            Id = Guid.NewGuid(),
            StartAtUtc = startAtUtc.ToUniversalTime(),
            EndAtUtc = endAtUtc.ToUniversalTime(),
            PrimaryQueueSize = primarySize,
            ReserveQueueSize = reserveSize,
            SentCount = 0,
            Notes = notes?.Trim(),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            Version = 1,
        };
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.Add(entity);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FounderScoutPersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<InvitationQueueEntry> AddToQueueAsync(AddInvitationQueueEntryRequest request, decimal minimumConfidence, int staleAfterDays, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateActor(request.Actor);
        if (minimumConfidence is < 0 or > 1 || staleAfterDays is < 1 or > 3_650) throw new ArgumentOutOfRangeException(nameof(minimumConfidence));
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        ManualInvitationWindowEntity window = await context.Set<ManualInvitationWindowEntity>().SingleOrDefaultAsync(item => item.Id == request.WindowId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The invitation window does not exist.");
        if (nowUtc < window.StartAtUtc || nowUtc > window.EndAtUtc) throw new InvalidOperationException("invitation.window.inactive");
        CandidateEntity candidate = await context.Set<CandidateEntity>().SingleOrDefaultAsync(item => item.Id == request.CandidateId && !item.MergedIntoCandidateId.HasValue, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The candidate does not exist.");
        InvitationDraftEntity? draft = await context.Set<InvitationDraftEntity>().AsNoTracking().Where(item => item.CandidateId == candidate.Id && !item.Superseded).OrderByDescending(item => item.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        bool hasConflict = await context.Set<CandidateIdentityConflictEntity>().AsNoTracking().AnyAsync(item => !item.Resolved && (item.CandidateId == candidate.Id || item.ConflictingCandidateId == candidate.Id), cancellationToken).ConfigureAwait(false);
        string[] reasons = EligibilityReasons(candidate, draft, hasConflict, minimumConfidence, staleAfterDays, nowUtc, request.ManuallyIncluded);
        if (reasons.Length > 0) throw new InvalidOperationException(reasons[0]);
        if (await context.Set<InvitationQueueEntryEntity>().AnyAsync(item => item.WindowId == window.Id && item.CandidateId == candidate.Id && item.RemovedAtUtc == null, cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("invitation.queue.duplicate");
        int capacity = request.QueueKind == InvitationQueueKind.Primary ? window.PrimaryQueueSize : window.ReserveQueueSize;
        int count = await context.Set<InvitationQueueEntryEntity>().CountAsync(item => item.WindowId == window.Id && item.QueueKind == request.QueueKind.ToString() && item.RemovedAtUtc == null, cancellationToken).ConfigureAwait(false);
        if (count >= capacity) throw new InvalidOperationException("invitation.queue.capacity");
        int position = (await context.Set<InvitationQueueEntryEntity>().Where(item => item.WindowId == window.Id && item.QueueKind == request.QueueKind.ToString() && item.RemovedAtUtc == null).MaxAsync(item => (int?)item.Position, cancellationToken).ConfigureAwait(false) ?? 0) + 1;
        var entry = new InvitationQueueEntryEntity
        {
            Id = Guid.NewGuid(),
            WindowId = window.Id,
            CandidateId = candidate.Id,
            QueueKind = request.QueueKind.ToString(),
            Position = position,
            ManuallyIncluded = request.ManuallyIncluded,
            AddedBy = request.Actor,
            AddedAtUtc = nowUtc,
            Version = 1,
        };
        context.Add(entry);
        if (candidate.Status == CandidateStatus.Monitor.ToString() && request.ManuallyIncluded) candidate.Status = CandidateStatus.Shortlisted.ToString();
        if (candidate.Status == CandidateStatus.Shortlisted.ToString()) candidate.Status = CandidateStatus.QueuedForInvite.ToString();
        candidate.UpdatedAtUtc = nowUtc;
        candidate.Version++;
        context.Add(NewAction(candidate.Id, CandidateActionType.InvitationQueueAdded, nowUtc, request.Actor, "The candidate was added to the local manual invitation queue.", new { entryId = entry.Id, windowId = window.Id, request.QueueKind, position, request.ManuallyIncluded }, draft?.Id, candidate.LatestEvaluationId, entry.Id.ToString("D")));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return FounderScoutPersistenceMapper.Map(entry);
    }

    /// <inheritdoc />
    public async ValueTask RemoveFromQueueAsync(Guid entryId, string actor, string reasonCode, CancellationToken cancellationToken = default)
    {
        ValidateGuid(entryId, nameof(entryId)); ValidateActor(actor); ValidateReason(reasonCode);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        InvitationQueueEntryEntity entry = await context.Set<InvitationQueueEntryEntity>().SingleOrDefaultAsync(item => item.Id == entryId && item.RemovedAtUtc == null, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The active queue entry does not exist.");
        entry.RemovedAtUtc = nowUtc; entry.RemovalReasonCode = reasonCode; entry.Version++;
        CandidateEntity candidate = await context.Set<CandidateEntity>().SingleAsync(item => item.Id == entry.CandidateId, cancellationToken).ConfigureAwait(false);
        if (candidate.Status == CandidateStatus.QueuedForInvite.ToString()) { candidate.Status = CandidateStatus.Shortlisted.ToString(); candidate.UpdatedAtUtc = nowUtc; candidate.Version++; }
        context.Add(NewAction(candidate.Id, CandidateActionType.InvitationQueueRemoved, nowUtc, actor, "The candidate was removed from the local invitation queue.", new { entryId, reasonCode }, null, candidate.LatestEvaluationId, entryId.ToString("D")));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask ReorderQueueAsync(ReorderInvitationQueueRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ValidateActor(request.Actor);
        if (request.EntryIds.Count > 500 || request.EntryIds.Distinct().Count() != request.EntryIds.Count) throw new ArgumentException("The queue order is invalid.", nameof(request));
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        List<InvitationQueueEntryEntity> entries = await context.Set<InvitationQueueEntryEntity>().Where(item => item.WindowId == request.WindowId && item.QueueKind == request.QueueKind.ToString() && item.RemovedAtUtc == null).OrderBy(item => item.Position).ThenBy(item => item.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (!entries.Select(item => item.Id).Order().SequenceEqual(request.EntryIds.Order())) throw new InvalidOperationException("invitation.queue.order.incomplete");
        Dictionary<Guid, InvitationQueueEntryEntity> byId = entries.ToDictionary(item => item.Id);
        for (var index = 0; index < request.EntryIds.Count; index++) { InvitationQueueEntryEntity entry = byId[request.EntryIds[index]]; entry.Position = index + 1; entry.Version++; }
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        foreach (Guid candidateId in entries.Select(item => item.CandidateId)) context.Add(NewAction(candidateId, CandidateActionType.InvitationQueueReordered, nowUtc, request.Actor, "The invitation queue order changed.", new { request.WindowId, request.QueueKind }, null, null, request.WindowId.ToString("D")));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<Candidate> RecordOutcomeAsync(RecordFounderScoutOutcomeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ValidateActor(request.Actor); ValidateReason(request.ReasonCode);
        if ((request.Notes?.Length ?? 0) > 2_000 || string.IsNullOrWhiteSpace(request.CorrelationId) || request.CorrelationId.Length > 128) throw new ArgumentException("The manual outcome is invalid.", nameof(request));
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        CandidateEntity candidate = await context.Set<CandidateEntity>().SingleOrDefaultAsync(item => item.Id == request.CandidateId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The candidate does not exist.");
        CandidateStatus current = Enum.Parse<CandidateStatus>(candidate.Status);
        if (request.Outcome == CandidateStatus.ManuallySent && current == CandidateStatus.QueuedForInvite)
        {
            throw new InvalidOperationException("invitation.review.requiredBeforeManualSend");
        }
        FounderScoutTransitionResult<CandidateStatus> transition = CandidateLifecycle.Validate(current, request.Outcome, request.ReasonCode);
        if (!transition.IsAllowed) throw new InvalidOperationException(transition.Error!.Code);
        DateTimeOffset occurred = request.OccurredAtUtc == default ? timeProvider.GetUtcNow().ToUniversalTime() : request.OccurredAtUtc.ToUniversalTime();
        candidate.Status = request.Outcome.ToString(); candidate.UpdatedAtUtc = occurred; candidate.Version++;
        if (request.Outcome == CandidateStatus.ManuallySent && request.DraftId.HasValue)
        {
            InvitationDraftEntity draft = await context.Set<InvitationDraftEntity>().SingleAsync(item => item.Id == request.DraftId.Value && item.CandidateId == candidate.Id, cancellationToken).ConfigureAwait(false);
            FounderScoutTransitionResult<InvitationDraftStatus> draftTransition = InvitationLifecycle.Validate(Enum.Parse<InvitationDraftStatus>(draft.Status), InvitationDraftStatus.ManuallySent, request.ReasonCode);
            if (!draftTransition.IsAllowed) throw new InvalidOperationException(draftTransition.Error!.Code);
            draft.Status = InvitationDraftStatus.ManuallySent.ToString(); draft.ReviewedAtUtc = occurred; draft.ReviewedBy = request.Actor; draft.Version++;
            if (request.WindowId.HasValue && request.WindowVersion.HasValue)
            {
                int changed = await context.Set<ManualInvitationWindowEntity>().Where(item => item.Id == request.WindowId.Value && item.Version == request.WindowVersion.Value && item.SentCount < item.PrimaryQueueSize).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.SentCount, item => item.SentCount + 1).SetProperty(item => item.UpdatedAtUtc, occurred).SetProperty(item => item.Version, item => item.Version + 1), cancellationToken).ConfigureAwait(false);
                if (changed != 1) throw new InvalidOperationException("invitation.window.concurrentOrFull");
            }
        }
        CandidateActionType actionType = request.Outcome == CandidateStatus.ManuallySent ? CandidateActionType.InvitationMarkedSent : CandidateActionType.OutcomeRecorded;
        context.Add(new CandidateActionEntity { Id = Guid.NewGuid(), CandidateId = candidate.Id, ActionType = actionType.ToString(), OccurredAtUtc = occurred, Actor = request.Actor, Notes = request.Notes?.Trim(), DataJson = JsonSerializer.Serialize(new { outcome = request.Outcome.ToString(), request.ReasonCode }, JsonOptions), RelatedInvitationId = request.DraftId, RelatedEvaluationId = candidate.LatestEvaluationId, CorrelationId = request.CorrelationId });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return FounderScoutPersistenceMapper.Map(candidate);
    }

    /// <inheritdoc />
    public async ValueTask<BrowserAccount> SetBrowserAccountEnabledAsync(string accountId, bool enabled, CancellationToken cancellationToken = default)
    {
        ValidateStableId(accountId, nameof(accountId));
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        BrowserAccountEntity account = await context.Set<BrowserAccountEntity>().SingleOrDefaultAsync(item => item.Id == accountId, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("The browser account does not exist.");
        account.Enabled = enabled; account.SessionStatus = enabled ? BrowserSessionStatus.Unknown.ToString() : BrowserSessionStatus.Disabled.ToString(); account.UpdatedAtUtc = timeProvider.GetUtcNow().ToUniversalTime(); account.Version++;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FounderScoutPersistenceMapper.Map(account);
    }

    /// <inheritdoc />
    public async ValueTask<BrowserAccount> ClearBrowserAccountAttentionAsync(string accountId, string actor, CancellationToken cancellationToken = default)
    {
        ValidateStableId(accountId, nameof(accountId)); ValidateActor(actor);
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        BrowserAccountEntity account = await context.Set<BrowserAccountEntity>().SingleOrDefaultAsync(item => item.Id == accountId, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("The browser account does not exist.");
        if (!account.Enabled) throw new InvalidOperationException("browserAccount.disabled");
        account.SessionStatus = BrowserSessionStatus.Unknown.ToString(); account.LastErrorReasonCode = null; account.LastErrorMessage = null; account.SuspendedUntilUtc = null; account.UpdatedAtUtc = timeProvider.GetUtcNow().ToUniversalTime(); account.Version++;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FounderScoutPersistenceMapper.Map(account);
    }

    /// <inheritdoc />
    public async ValueTask<DiscoverySegment> SetDiscoverySegmentPausedAsync(string segmentId, bool paused, CancellationToken cancellationToken = default)
    {
        ValidateStableId(segmentId, nameof(segmentId));
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        DiscoverySegmentEntity segment = await context.Set<DiscoverySegmentEntity>().SingleOrDefaultAsync(item => item.Id == segmentId, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("The discovery segment does not exist.");
        segment.PausedUntilUtc = paused ? DateTimeOffset.MaxValue : null; segment.UpdatedAtUtc = timeProvider.GetUtcNow().ToUniversalTime(); segment.Version++;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FounderScoutPersistenceMapper.Map(segment);
    }

    /// <inheritdoc />
    public async ValueTask<FounderScoutRetentionResult> ApplyRawRetentionAsync(DateTimeOffset nowUtc, Guid? candidateId, bool includeCurrentSnapshot, string actor, CancellationToken cancellationToken = default)
    {
        ValidateActor(actor);
        DateTimeOffset utc = nowUtc.ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<ProfileSnapshotEntity> source = context.Set<ProfileSnapshotEntity>().Where(item => item.RawArtifactDeletedAtUtc == null && item.DeleteAfterUtc <= utc);
        if (candidateId.HasValue) source = source.Where(item => item.CandidateId == candidateId.Value);
        List<ProfileSnapshotEntity> snapshots = await source.OrderBy(item => item.DeleteAfterUtc).ThenBy(item => item.Id).Take(1_000).ToListAsync(cancellationToken).ConfigureAwait(false);
        int deleted = 0, missing = 0, skipped = 0, failed = 0;
        var failureCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (ProfileSnapshotEntity snapshot in snapshots)
        {
            CandidateEntity candidate = await context.Set<CandidateEntity>().SingleAsync(item => item.Id == snapshot.CandidateId, cancellationToken).ConfigureAwait(false);
            bool unsafeState = snapshot.ProcessingWorkerId is not null
                || candidate.AnalysisWorkerId is not null
                || snapshot.Status is nameof(ProfileSnapshotStatus.Captured) or nameof(ProfileSnapshotStatus.Failed)
                || snapshot.ErrorCode is not null
                || (!includeCurrentSnapshot && candidate.CurrentSnapshotId == snapshot.Id);
            if (unsafeState) { skipped++; continue; }
            try
            {
                string relative = snapshot.RawArtifactRelativePath;
                if (string.IsNullOrWhiteSpace(relative)) { missing++; snapshot.RawArtifactDeletedAtUtc = utc; snapshot.RawArtifactDeletionReasonCode = "retention.raw.alreadyMissing"; continue; }
                string path = FounderScoutPathPolicy.CombineContained(dataDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path)) { FounderScoutPathPolicy.RejectReparsePoint(path); File.Delete(path); deleted++; } else { missing++; }
                snapshot.RawArtifactRelativePath = string.Empty;
                snapshot.RawArtifactDeletedAtUtc = utc;
                snapshot.RawArtifactDeletionReasonCode = candidateId.HasValue ? "retention.raw.manualCandidate" : "retention.raw.expired";
                context.Add(NewAction(snapshot.CandidateId, CandidateActionType.RawProfileDeleted, utc, actor, "Raw profile evidence was deleted while derived history was retained.", new { snapshotId = snapshot.Id, snapshot.RawArtifactDeletionReasonCode }, null, candidate.LatestEvaluationId, snapshot.Id.ToString("D")));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                failed++; failureCodes.Add(exception is InvalidOperationException ? "retention.path.invalid" : "retention.file.failure");
            }
        }
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new(snapshots.Count, deleted, missing, skipped, failed, failureCodes.Order(StringComparer.Ordinal).ToArray());
    }

    /// <inheritdoc />
    public async ValueTask<FounderScoutReportLease?> TryAcquireAsync(string ownerId, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        ValidateActor(ownerId);
        if (duration < TimeSpan.FromSeconds(10) || duration > TimeSpan.FromHours(1)) throw new ArgumentOutOfRangeException(nameof(duration));
        const string resource = "reports";
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        DateTimeOffset expires = nowUtc.Add(duration);
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        FounderScoutLeaseEntity? lease = await context.Set<FounderScoutLeaseEntity>().SingleOrDefaultAsync(item => item.Resource == resource, cancellationToken).ConfigureAwait(false);
        if (lease is not null && lease.ExpiresAtUtc > nowUtc && lease.OwnerId != ownerId) return null;
        if (lease is null)
        {
            lease = new FounderScoutLeaseEntity { Resource = resource, OwnerId = ownerId, FencingToken = 1, AcquiredAtUtc = nowUtc, ExpiresAtUtc = expires };
            context.Add(lease);
        }
        else
        {
            lease.OwnerId = ownerId; lease.FencingToken++; lease.AcquiredAtUtc = nowUtc; lease.ExpiresAtUtc = expires;
        }
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(resource, ownerId, lease.FencingToken, expires);
    }

    /// <inheritdoc />
    public async ValueTask ReleaseAsync(FounderScoutReportLease lease, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        _ = await context.Set<FounderScoutLeaseEntity>()
            .Where(item => item.Resource == lease.Resource && item.OwnerId == lease.OwnerId && item.FencingToken == lease.FencingToken)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.OwnerId, "released")
                .SetProperty(item => item.ExpiresAtUtc, DateTimeOffset.UnixEpoch), cancellationToken).ConfigureAwait(false);
    }

    private static IQueryable<CandidateEntity> ApplySort(IQueryable<CandidateEntity> source, FounderScoutCandidateSort sort) => sort switch
    {
        FounderScoutCandidateSort.FounderQualityDescending => source.OrderByDescending(item => item.FounderQualityScore).ThenByDescending(item => item.InvitationPriority).ThenBy(item => item.DisplayName).ThenBy(item => item.Id),
        FounderScoutCandidateSort.FitDescending => source.OrderByDescending(item => item.OurFitScore).ThenByDescending(item => item.InvitationPriority).ThenBy(item => item.DisplayName).ThenBy(item => item.Id),
        FounderScoutCandidateSort.ConfidenceDescending => source.OrderByDescending(item => item.Confidence).ThenByDescending(item => item.InvitationPriority).ThenBy(item => item.DisplayName).ThenBy(item => item.Id),
        FounderScoutCandidateSort.ActivityDescending => source.OrderByDescending(item => item.ActivityScore).ThenByDescending(item => item.InvitationPriority).ThenBy(item => item.DisplayName).ThenBy(item => item.Id),
        FounderScoutCandidateSort.LastCapturedDescending => source.OrderByDescending(item => item.LastSeenAtUtc).ThenByDescending(item => item.InvitationPriority).ThenBy(item => item.DisplayName).ThenBy(item => item.Id),
        FounderScoutCandidateSort.NameAscending => source.OrderBy(item => item.DisplayName).ThenByDescending(item => item.InvitationPriority).ThenBy(item => item.Id),
        _ => source.OrderByDescending(item => item.InvitationPriority).ThenByDescending(item => item.Confidence).ThenByDescending(item => item.ActivityScore).ThenBy(item => item.DisplayName).ThenBy(item => item.Id),
    };

    private static IQueryable<CandidateEntity> ActiveCandidates(FounderScoutDbContext context) =>
        context.Set<CandidateEntity>().AsNoTracking().Where(item =>
            !item.MergedIntoCandidateId.HasValue
            && !NonProfileSourceKeys.Contains(item.CurrentSourceProfileKey));

    private static FounderScoutCandidateListItem CreateListItem(CandidateEntity candidate, int rank, ProfileSnapshotEntity? snapshot, EvaluationEntity? evaluation, InvitationQueueEntryEntity? queue, bool profileChanged, bool hasTraction) => new(
        rank,
        candidate.Id,
        candidate.DisplayName,
        Enum.Parse<CandidateStatus>(candidate.Status),
        evaluation?.Recommendation,
        candidate.FounderQualityScore,
        candidate.OurFitScore,
        candidate.Confidence,
        candidate.ActivityScore,
        candidate.RiskPenalty,
        candidate.InvitationPriority,
        candidate.LastSeenAtUtc,
        candidate.LastActivityAtUtc,
        snapshot?.SourceAccountId,
        snapshot?.SourceSegmentId,
        queue is null ? null : Enum.Parse<InvitationQueueKind>(queue.QueueKind),
        queue?.Position,
        profileChanged,
        candidate.Status == CandidateStatus.ManualReview.ToString() || evaluation?.Status == EvaluationStatus.NeedsReview.ToString(),
        hasTraction);

    private static FounderScoutEvaluationHistoryItem CreateEvaluationHistory(EvaluationEntity entity, IReadOnlyList<EvaluationCategoryEntity> categories, IReadOnlyList<EvaluationRiskEntity> risks)
    {
        ModelEvaluationResponse? response = ParseEvaluationResponse(entity.StructuredEvaluationJson);
        return new(
            FounderScoutPersistenceMapper.Map(entity),
            categories.Select(FounderScoutPersistenceMapper.Map).ToArray(),
            risks.Select(FounderScoutPersistenceMapper.Map).ToArray(),
            response?.PositiveSignals ?? [],
            response?.MissingEvidence ?? [],
            response?.PriorityQuestions ?? [],
            response?.RedFlags ?? [],
            response?.RecommendationRationale ?? string.Empty);
    }

    private static ModelEvaluationResponse? ParseEvaluationResponse(string structured)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(structured);
            return document.RootElement.TryGetProperty("response", out JsonElement response)
                ? response.Deserialize<ModelEvaluationResponse>(StrictJson)
                : null;
        }
        catch (JsonException) { return null; }
    }

    private static NormalizedFounderProfile? DeserializeProfile(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<NormalizedFounderProfile>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static string BuildProfileSummary(NormalizedFounderProfile? profile)
    {
        if (profile is null) return "No normalized profile summary is available.";
        return new[] { profile.Introduction, profile.StartupDescription, profile.ProblemDescription, profile.CareerBackground }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "The normalized profile contains limited narrative evidence.";
    }

    private static (Guid? SnapshotId, IReadOnlyList<FounderProfileChange> Changes) ParseChanges(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            Guid? snapshotId = document.RootElement.TryGetProperty("snapshotId", out JsonElement id) && id.TryGetGuid(out Guid parsed) ? parsed : null;
            FounderProfileChange[] changes = document.RootElement.TryGetProperty("changes", out JsonElement value) ? value.Deserialize<FounderProfileChange[]>(JsonOptions) ?? [] : [];
            return (snapshotId, changes);
        }
        catch (JsonException) { return (null, []); }
    }

    private static string[] EligibilityReasons(CandidateEntity candidate, InvitationDraftEntity? draft, bool hasConflict, decimal minimumConfidence, int staleAfterDays, DateTimeOffset atUtc, bool manuallyIncluded)
    {
        var reasons = new List<string>();
        if (candidate.LatestEvaluationId is null) reasons.Add("invitation.eligibility.evaluationRequired");
        if (!manuallyIncluded && candidate.Confidence.GetValueOrDefault() < minimumConfidence) reasons.Add("invitation.eligibility.confidence");
        if (draft is null || (!manuallyIncluded && draft.Status is not (nameof(InvitationDraftStatus.Valid) or nameof(InvitationDraftStatus.Reviewed)))) reasons.Add("invitation.eligibility.draft");
        if (candidate.Status is nameof(CandidateStatus.ManuallySent) or nameof(CandidateStatus.Declined) or nameof(CandidateStatus.Passed) or nameof(CandidateStatus.FilteredOut) or nameof(CandidateStatus.Selected)) reasons.Add("invitation.eligibility.lifecycle");
        if (hasConflict) reasons.Add("invitation.eligibility.identityConflict");
        if (!manuallyIncluded && candidate.LastSeenAtUtc < atUtc.AddDays(-staleAfterDays)) reasons.Add("invitation.eligibility.stale");
        if (candidate.Status is not (nameof(CandidateStatus.Shortlisted) or nameof(CandidateStatus.Monitor) or nameof(CandidateStatus.QueuedForInvite) or nameof(CandidateStatus.MessageReviewed))) reasons.Add("invitation.eligibility.status");
        return reasons.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static ManualDraftValidation ValidateManualDraft(SaveInvitationDraftRevisionRequest request, List<InvitationDraftEntity> recent)
    {
        var errors = new HashSet<string>(StringComparer.Ordinal);
        string combined = $"{request.ShortDraft}\n{request.DetailedDraft}";
        if (string.IsNullOrWhiteSpace(request.ShortDraft) || request.ShortDraft.Length > Math.Min(350, request.MaximumCharacters)) errors.Add("invitation.short.length");
        if (string.IsNullOrWhiteSpace(request.DetailedDraft) || request.DetailedDraft.Length > request.MaximumCharacters) errors.Add("invitation.detailed.length");
        string normalized = combined.ToLowerInvariant();
        string[] forbidden = ["automated analysis", "algorithm ranked", "scorecard", "scraping", "scraped", "i will join", "i will invest", "work for free", "age", "gender", "race", "ethnicity", "religion", "marital", "disability", "health condition", "sexual orientation", "photo", "appearance"];
        if (forbidden.Any(normalized.Contains)) errors.Add("invitation.manual.forbiddenContent");
        string fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeForSimilarity(combined))));
        decimal similarity = recent.Count == 0 ? 0 : recent.Max(item => Jaccard(combined, $"{item.ShortDraft}\n{item.DetailedDraft}"));
        return new(errors.Order(StringComparer.Ordinal).ToArray(), Math.Round(similarity, 4, MidpointRounding.AwayFromZero), fingerprint);
    }

    private static string NormalizeForSimilarity(string value) => string.Join(' ', Tokenize(value).Order(StringComparer.Ordinal));
    private static HashSet<string> Tokenize(string value) => value.ToLowerInvariant().Split([' ', '\r', '\n', '\t', '.', ',', ';', ':', '!', '?', '-', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(item => item.Length > 2).ToHashSet(StringComparer.Ordinal);
    private static decimal Jaccard(string left, string right) { HashSet<string> a = Tokenize(left); HashSet<string> b = Tokenize(right); int union = a.Union(b).Count(); return union == 0 ? 1 : (decimal)a.Count(b.Contains) / union; }

    private static CandidateActionEntity NewAction(Guid candidateId, CandidateActionType type, DateTimeOffset occurredAtUtc, string actor, string notes, object data, Guid? invitationId, Guid? evaluationId, string correlationId) => new()
    {
        Id = Guid.NewGuid(),
        CandidateId = candidateId,
        ActionType = type.ToString(),
        OccurredAtUtc = occurredAtUtc,
        Actor = actor,
        Notes = notes,
        DataJson = JsonSerializer.Serialize(data, JsonOptions),
        RelatedInvitationId = invitationId,
        RelatedEvaluationId = evaluationId,
        CorrelationId = correlationId,
    };

    private static ValueTask<ManualInvitationWindowEntity?> CurrentWindowAsync(FounderScoutDbContext context, DateTimeOffset atUtc, CancellationToken cancellationToken) =>
        new(context.Set<ManualInvitationWindowEntity>().AsNoTracking().Where(item => item.StartAtUtc <= atUtc && item.EndAtUtc >= atUtc).OrderByDescending(item => item.StartAtUtc).ThenByDescending(item => item.Id).FirstOrDefaultAsync(cancellationToken));

    private static void ValidateQuery(FounderScoutCandidateQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Offset < 0 || query.PageSize is < 1 or > 500 || (query.Search?.Length ?? 0) > 200 || query.MinimumScore is < 0 or > 100 || query.MaximumScore is < 0 or > 100 || query.MinimumConfidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(query));
    }
    private static void ValidateGuid(Guid value, string name) { if (value == Guid.Empty) throw new ArgumentException("A non-empty identifier is required.", name); }
    private static void ValidateActor(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl)) throw new ArgumentException("A bounded actor is required."); }
    private static void ValidateReason(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl)) throw new ArgumentException("A bounded reason code is required."); }
    private static void ValidateStableId(string value, string name) { if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || value.Any(item => !(char.IsLetterOrDigit(item) || item is '-' or '_' or '.'))) throw new ArgumentException("A stable identifier is required.", name); }
    private sealed record ManualDraftValidation(IReadOnlyList<string> Errors, decimal Similarity, string Fingerprint);
}
