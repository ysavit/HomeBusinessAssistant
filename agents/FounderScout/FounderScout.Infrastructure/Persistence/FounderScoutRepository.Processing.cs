using System.Text.Json;
using FounderScout.Application;
using FounderScout.Domain;
using FounderScout.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FounderScout.Infrastructure.Persistence;

public sealed partial class FounderScoutRepository
{
    /// <inheritdoc />
    public async ValueTask<FounderScoutProcessingClaim?> ClaimPendingProcessingAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > 128 || leaseDuration < TimeSpan.FromSeconds(10) || leaseDuration > TimeSpan.FromHours(24))
            throw new ArgumentException("The processing worker or lease duration is invalid.", nameof(workerId));
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        DateTimeOffset expiresAtUtc = nowUtc.Add(leaseDuration);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            Guid? snapshotId = await context.Set<ProfileSnapshotEntity>().AsNoTracking()
                .Where(item => item.Status == ProfileSnapshotStatus.Captured.ToString()
                    && (!item.ProcessingClaimExpiresAtUtc.HasValue || item.ProcessingClaimExpiresAtUtc <= nowUtc)
                    && !context.Set<ProfileSnapshotEntity>().Any(other => other.CandidateId == item.CandidateId
                        && other.Id != item.Id
                        && other.ProcessingClaimExpiresAtUtc > nowUtc))
                .OrderBy(item => item.CapturedAtUtc)
                .ThenBy(item => item.Id)
                .Select(item => (Guid?)item.Id)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (!snapshotId.HasValue)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return null;
            }
            int changed = await context.Set<ProfileSnapshotEntity>()
                .Where(item => item.Id == snapshotId.Value && item.Status == ProfileSnapshotStatus.Captured.ToString()
                    && (!item.ProcessingClaimExpiresAtUtc.HasValue || item.ProcessingClaimExpiresAtUtc <= nowUtc))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.ProcessingWorkerId, workerId)
                    .SetProperty(item => item.ProcessingClaimedAtUtc, nowUtc)
                    .SetProperty(item => item.ProcessingClaimExpiresAtUtc, expiresAtUtc)
                    .SetProperty(item => item.ProcessingAttemptCount, item => item.ProcessingAttemptCount + 1)
                    .SetProperty(item => item.ErrorCode, (string?)null), cancellationToken).ConfigureAwait(false);
            if (changed == 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            ProfileSnapshotEntity snapshot = await context.Set<ProfileSnapshotEntity>().AsNoTracking().SingleAsync(item => item.Id == snapshotId.Value, cancellationToken).ConfigureAwait(false);
            CandidateEntity candidate = await context.Set<CandidateEntity>().AsNoTracking().SingleAsync(item => item.Id == snapshot.CandidateId, cancellationToken).ConfigureAwait(false);
            ProfileSnapshotEntity? current = candidate.CurrentSnapshotId.HasValue
                ? await context.Set<ProfileSnapshotEntity>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == candidate.CurrentSnapshotId.Value, cancellationToken).ConfigureAwait(false)
                : null;
            return new(FounderScoutPersistenceMapper.Map(candidate), FounderScoutPersistenceMapper.Map(snapshot), current?.NormalizedProfileJson, current?.NormalizedProfileHash, current?.EvaluatorInputHash, workerId, nowUtc, expiresAtUtc);
        }
        return null;
    }

    /// <inheritdoc />
    public async ValueTask<CompleteFounderProfileProcessingResult> CompleteProcessingAsync(
        CompleteFounderProfileProcessingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateProcessingRequest(request);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        ProfileSnapshotEntity snapshot = await context.Set<ProfileSnapshotEntity>()
            .SingleOrDefaultAsync(item => item.Id == request.SnapshotId && item.CandidateId == request.CandidateId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The claimed Founder Scout snapshot does not exist.");
        if (snapshot.Status != ProfileSnapshotStatus.Captured.ToString() || snapshot.ProcessingWorkerId != request.WorkerId || snapshot.ProcessingClaimExpiresAtUtc < nowUtc)
            throw new InvalidOperationException("founderScout.processing.claimNotOwned");

        CandidateEntity candidate = await context.Set<CandidateEntity>().SingleAsync(item => item.Id == request.CandidateId, cancellationToken).ConfigureAwait(false);
        var conflictCount = 0;
        Guid resolvedCandidateId = candidate.Id;
        Guid[] strongOwners = await FindStrongOwnersAsync(context, request.DerivedIdentities, candidate.Id, cancellationToken).ConfigureAwait(false);
        if (strongOwners.Length == 1)
        {
            resolvedCandidateId = await MergeCandidateGraphAsync(context, candidate.Id, strongOwners[0], "founder-scout", "identity.strongFingerprint.autoMerge", request.RelatedRunId, request.CorrelationId, nowUtc, cancellationToken).ConfigureAwait(false);
            candidate = await context.Set<CandidateEntity>().SingleAsync(item => item.Id == resolvedCandidateId, cancellationToken).ConfigureAwait(false);
            snapshot.CandidateId = resolvedCandidateId;
        }
        else if (strongOwners.Length > 1)
        {
            conflictCount += await AddIdentityConflictsAsync(context, candidate.Id, strongOwners, request.DerivedIdentities.Where(item => item.IsStrong), "identity.multipleStrongOwners", nowUtc, cancellationToken).ConfigureAwait(false);
        }

        foreach (CandidateIdentityInput identity in request.DerivedIdentities)
        {
            List<Guid> owners = await context.Set<CandidateIdentityAliasEntity>().AsNoTracking()
                .Where(item => item.IsActive && item.NormalizedValueHash == identity.NormalizedValueHash && item.CandidateId != resolvedCandidateId)
                .Select(item => item.CandidateId).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
            if (owners.Count > 0 && (!identity.IsStrong || strongOwners.Length > 1))
            {
                conflictCount += await AddIdentityConflictsAsync(context, resolvedCandidateId, owners, [identity], identity.IsStrong ? "identity.strongAmbiguous" : "identity.weakAmbiguous", nowUtc, cancellationToken).ConfigureAwait(false);
                continue;
            }
            bool exists = await context.Set<CandidateIdentityAliasEntity>().AnyAsync(item => item.CandidateId == resolvedCandidateId && item.AliasType == identity.AliasType.ToString() && item.NormalizedValueHash == identity.NormalizedValueHash, cancellationToken).ConfigureAwait(false);
            if (!exists) context.Add(NewAlias(resolvedCandidateId, identity, nowUtc));
        }

        ProfileSnapshotEntity? current = candidate.CurrentSnapshotId.HasValue
            ? await context.Set<ProfileSnapshotEntity>().SingleOrDefaultAsync(item => item.Id == candidate.CurrentSnapshotId.Value, cancellationToken).ConfigureAwait(false)
            : null;
        bool normalizedChanged = current?.NormalizedProfileHash != request.NormalizedProfileHash || current?.EvaluatorInputHash != request.EvaluatorInputHash;
        snapshot.ParserVersion = request.Profile.ParserVersion;
        snapshot.NormalizedProfileJson = request.NormalizedProfileJson;
        snapshot.NormalizedProfileHash = request.NormalizedProfileHash;
        snapshot.EvaluatorInputHash = request.EvaluatorInputHash;
        snapshot.EvidenceJson = request.EvidenceJson;
        snapshot.RedactionJson = request.RedactionJson;
        snapshot.ExtractionCompleteness = request.Profile.Completeness;
        snapshot.ExtractionConfidence = conflictCount > 0 ? Math.Min(request.Profile.Completeness, 0.7m) : request.Profile.Completeness;
        snapshot.ProcessingWorkerId = null;
        snapshot.ProcessingClaimedAtUtc = null;
        snapshot.ProcessingClaimExpiresAtUtc = null;
        snapshot.ErrorCode = null;
        bool screeningCreated = false;
        CandidateStatus target;
        if (!normalizedChanged)
        {
            snapshot.Status = ProfileSnapshotStatus.Superseded.ToString();
            target = await RestoreCandidateStatusAsync(context, candidate, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            ScreeningOutcome outcome = conflictCount > 0 ? ScreeningOutcome.ManualReview : request.Screening.Outcome;
            target = MapCandidateStatus(outcome);
            snapshot.Status = outcome == ScreeningOutcome.DeepAnalyze ? ProfileSnapshotStatus.PendingAnalysis.ToString() : ProfileSnapshotStatus.Parsed.ToString();
            candidate.CurrentSnapshotId = snapshot.Id;
            candidate.DisplayName = request.Profile.DisplayName;
            candidate.CurrentSourceProfileKey = request.Profile.SourceProfileKey;
            candidate.CanonicalSourceUrl = request.Profile.CanonicalUrl ?? candidate.CanonicalSourceUrl;
            candidate.NormalizedLocation = request.Profile.Location;
            candidate.TechnicalStatus = request.Profile.TechnicalStatus.ToString();
            candidate.CommitmentStatus = request.Profile.CommitmentStatus.ToString();
            candidate.IdeaCommitmentStatus = request.Profile.IdeaCommitmentStatus.ToString();
            candidate.LastActivityText = request.Profile.LastActivityText;
            candidate.LastActivityAtUtc = request.Profile.LastActivityAtUtc;
            var decision = new ScreeningDecisionEntity
            {
                Id = Guid.NewGuid(),
                CandidateId = resolvedCandidateId,
                SnapshotId = snapshot.Id,
                RulesetVersion = request.Screening.RulesetVersion,
                Outcome = outcome.ToString(),
                Score = request.Screening.Score,
                ReasonCodesJson = JsonSerializer.Serialize(conflictCount > 0 ? request.Screening.ReasonCodes.Append("screen.review.identityConflict") : request.Screening.ReasonCodes, JsonOptions),
                EvidenceJson = JsonSerializer.Serialize(request.Screening.Evidence, JsonOptions),
                MissingEvidenceJson = JsonSerializer.Serialize(request.Screening.MissingEvidence, JsonOptions),
                EvaluatorInputJson = request.EvaluatorInputJson,
                EvaluatorInputHash = request.EvaluatorInputHash,
                IsManualOverride = false,
                CreatedAtUtc = nowUtc,
            };
            context.Add(decision);
            screeningCreated = true;
            context.Add(NewAction(resolvedCandidateId, CandidateActionType.ProfileChanged, nowUtc, request.WorkerId, "Relevant normalized profile fields changed.", JsonSerializer.Serialize(new { snapshotId = snapshot.Id, changes = request.Changes.Take(32) }, JsonOptions), null, null, request.RelatedRunId, request.CorrelationId));
            context.Add(NewAction(resolvedCandidateId, CandidateActionType.ScreeningCompleted, nowUtc, request.WorkerId, "A deterministic fast-screen decision was recorded.", JsonSerializer.Serialize(new { snapshotId = snapshot.Id, outcome = outcome.ToString(), request.Screening.RulesetVersion }, JsonOptions), null, null, request.RelatedRunId, request.CorrelationId));
        }
        candidate.Status = target.ToString();
        candidate.LastSeenAtUtc = snapshot.CapturedAtUtc > candidate.LastSeenAtUtc ? snapshot.CapturedAtUtc : candidate.LastSeenAtUtc;
        candidate.UpdatedAtUtc = nowUtc;
        candidate.Version++;
        context.Add(NewAction(resolvedCandidateId, CandidateActionType.ProfileNormalized, nowUtc, request.WorkerId, normalizedChanged ? "A captured profile was normalized." : "A layout-only or irrelevant profile change was normalized without requeueing.", JsonSerializer.Serialize(new { snapshotId = snapshot.Id, normalizedChanged }, JsonOptions), null, null, request.RelatedRunId, request.CorrelationId));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(resolvedCandidateId, snapshot.Id, normalizedChanged, screeningCreated, target, conflictCount);
    }

    /// <inheritdoc />
    public async ValueTask<bool> FailProcessingAsync(FailFounderProfileProcessingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ProfileSnapshotEntity? snapshot = await context.Set<ProfileSnapshotEntity>().SingleOrDefaultAsync(item => item.Id == request.SnapshotId && item.Status == ProfileSnapshotStatus.Captured.ToString() && item.ProcessingWorkerId == request.WorkerId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null) return false;
        snapshot.Status = request.Retry ? ProfileSnapshotStatus.Captured.ToString() : ProfileSnapshotStatus.Failed.ToString();
        snapshot.ErrorCode = request.ReasonCode.Length <= 128 ? request.ReasonCode : request.ReasonCode[..128];
        snapshot.ProcessingWorkerId = null;
        snapshot.ProcessingClaimedAtUtc = null;
        snapshot.ProcessingClaimExpiresAtUtc = null;
        if (!request.Retry)
        {
            CandidateEntity candidate = await context.Set<CandidateEntity>().SingleAsync(item => item.Id == snapshot.CandidateId, cancellationToken).ConfigureAwait(false);
            if (candidate.CurrentSnapshotId.HasValue && candidate.CurrentSnapshotId != snapshot.Id)
            {
                candidate.Status = (await RestoreCandidateStatusAsync(context, candidate, cancellationToken).ConfigureAwait(false)).ToString();
                candidate.UpdatedAtUtc = nowUtc;
                candidate.Version++;
            }
        }
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async ValueTask MarkParserFailureAsync(string accountId, string segmentId, string reasonCode, CancellationToken cancellationToken = default)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        BrowserAccountEntity? account = await context.Set<BrowserAccountEntity>().SingleOrDefaultAsync(item => item.Id == accountId, cancellationToken).ConfigureAwait(false);
        if (account is not null)
        {
            account.SessionStatus = BrowserSessionStatus.ParserFailure.ToString();
            account.LastErrorReasonCode = reasonCode;
            account.LastErrorMessage = "Repeated deterministic parser-health failures stopped processing.";
            account.LastFailedRunAtUtc = nowUtc;
            account.UpdatedAtUtc = nowUtc;
            account.Version++;
        }
        DiscoverySegmentEntity? segment = await context.Set<DiscoverySegmentEntity>().SingleOrDefaultAsync(item => item.Id == segmentId, cancellationToken).ConfigureAwait(false);
        if (segment is not null)
        {
            segment.PausedUntilUtc = DateTimeOffset.MaxValue;
            segment.ErrorCount++;
            segment.UpdatedAtUtc = nowUtc;
            segment.Version++;
        }
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<ScreeningDecision> OverrideScreeningAsync(ScreeningOverrideRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        ScreeningDecisionEntity previous = await context.Set<ScreeningDecisionEntity>().AsNoTracking().Where(item => item.CandidateId == request.CandidateId && item.SnapshotId == request.SnapshotId).OrderByDescending(item => item.CreatedAtUtc).FirstAsync(cancellationToken).ConfigureAwait(false);
        CandidateEntity candidate = await context.Set<CandidateEntity>().SingleAsync(item => item.Id == request.CandidateId, cancellationToken).ConfigureAwait(false);
        var decision = new ScreeningDecisionEntity
        {
            Id = Guid.NewGuid(),
            CandidateId = request.CandidateId,
            SnapshotId = request.SnapshotId,
            RulesetVersion = $"{previous.RulesetVersion}-override-{nowUtc.UtcTicks}",
            Outcome = request.Outcome.ToString(),
            Score = previous.Score,
            ReasonCodesJson = JsonSerializer.Serialize(new[] { request.ReasonCode }, JsonOptions),
            EvidenceJson = previous.EvidenceJson,
            MissingEvidenceJson = previous.MissingEvidenceJson,
            EvaluatorInputJson = previous.EvaluatorInputJson,
            EvaluatorInputHash = previous.EvaluatorInputHash,
            IsManualOverride = true,
            CreatedAtUtc = nowUtc,
        };
        context.Add(decision);
        candidate.Status = MapCandidateStatus(request.Outcome).ToString();
        candidate.UpdatedAtUtc = nowUtc;
        candidate.Version++;
        context.Add(NewAction(candidate.Id, CandidateActionType.ScreeningOverridden, nowUtc, request.Actor, "A user overrode a reversible screening decision.", JsonSerializer.Serialize(new { request.SnapshotId, outcome = request.Outcome.ToString(), request.ReasonCode }, JsonOptions), null, null, request.RelatedRunId, request.CorrelationId));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return FounderScoutPersistenceMapper.Map(decision);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CandidateIdentityConflictRecord>> ListOpenAsync(int maximumResults, CancellationToken cancellationToken = default)
    {
        ValidateMaximum(maximumResults);
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<CandidateIdentityConflictEntity> entities = await context.Set<CandidateIdentityConflictEntity>().AsNoTracking().Where(item => !item.Resolved).OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).Take(maximumResults).ToListAsync(cancellationToken).ConfigureAwait(false);
        return entities.Select(FounderScoutPersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<Guid> ManualMergeAsync(Guid sourceCandidateId, Guid targetCandidateId, string actor, string reasonCode, Guid? relatedRunId, string correlationId, CancellationToken cancellationToken = default)
    {
        ValidateGuid(sourceCandidateId, nameof(sourceCandidateId)); ValidateGuid(targetCandidateId, nameof(targetCandidateId));
        if (sourceCandidateId == targetCandidateId) throw new ArgumentException("Manual merge candidates must differ.", nameof(sourceCandidateId));
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        Guid result = await MergeCandidateGraphAsync(context, sourceCandidateId, targetCandidateId, actor, reasonCode, relatedRunId, correlationId, nowUtc, cancellationToken).ConfigureAwait(false);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static async ValueTask<Guid[]> FindStrongOwnersAsync(FounderScoutDbContext context, IReadOnlyList<CandidateIdentityInput> identities, Guid candidateId, CancellationToken cancellationToken)
    {
        string[] hashes = identities.Where(item => item.IsStrong).Select(item => item.NormalizedValueHash).Distinct(StringComparer.Ordinal).ToArray();
        if (hashes.Length == 0) return [];
        return await context.Set<CandidateIdentityAliasEntity>().AsNoTracking().Where(item => item.IsStrong && item.IsActive && hashes.Contains(item.NormalizedValueHash) && item.CandidateId != candidateId).Select(item => item.CandidateId).Distinct().ToArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<int> AddIdentityConflictsAsync(FounderScoutDbContext context, Guid candidateId, IEnumerable<Guid> owners, IEnumerable<CandidateIdentityInput> identities, string reasonCode, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (CandidateIdentityInput identity in identities)
            foreach (Guid owner in owners)
            {
                bool exists = await context.Set<CandidateIdentityConflictEntity>().AnyAsync(item => !item.Resolved && item.CandidateId == candidateId && item.ConflictingCandidateId == owner && item.NormalizedValueHash == identity.NormalizedValueHash, cancellationToken).ConfigureAwait(false);
                if (exists) continue;
                context.Add(new CandidateIdentityConflictEntity { Id = Guid.NewGuid(), CandidateId = candidateId, ConflictingCandidateId = owner, AliasType = identity.AliasType.ToString(), NormalizedValueHash = identity.NormalizedValueHash, ReasonCode = reasonCode, Confidence = identity.Confidence, CreatedAtUtc = nowUtc });
                context.Add(NewAction(candidateId, CandidateActionType.IdentityConflictRecorded, nowUtc, "founder-scout", "An ambiguous identity signal requires manual review.", JsonSerializer.Serialize(new { conflictingCandidateId = owner, aliasType = identity.AliasType.ToString(), reasonCode }, JsonOptions), null, null, null, candidateId.ToString("D")));
                count++;
            }
        return count;
    }

    private static async ValueTask<Guid> MergeCandidateGraphAsync(FounderScoutDbContext context, Guid sourceId, Guid targetId, string actor, string reasonCode, Guid? relatedRunId, string correlationId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        if (sourceId == targetId) return targetId;
        CandidateEntity source = await context.Set<CandidateEntity>().SingleAsync(item => item.Id == sourceId, cancellationToken).ConfigureAwait(false);
        CandidateEntity target = await context.Set<CandidateEntity>().SingleAsync(item => item.Id == targetId, cancellationToken).ConfigureAwait(false);
        List<CandidateIdentityAliasEntity> sourceAliases = await context.Set<CandidateIdentityAliasEntity>().Where(item => item.CandidateId == sourceId).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (CandidateIdentityAliasEntity alias in sourceAliases)
        {
            bool duplicate = await context.Set<CandidateIdentityAliasEntity>().AnyAsync(item => item.CandidateId == targetId && item.AliasType == alias.AliasType && item.NormalizedValueHash == alias.NormalizedValueHash, cancellationToken).ConfigureAwait(false);
            if (duplicate) context.Remove(alias); else alias.CandidateId = targetId;
        }
        foreach (ProfileSnapshotEntity item in await context.Set<ProfileSnapshotEntity>().Where(item => item.CandidateId == sourceId).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            bool duplicate = await context.Set<ProfileSnapshotEntity>().AnyAsync(
                candidate => candidate.CandidateId == targetId
                    && candidate.SourceProfileKey == item.SourceProfileKey
                    && candidate.ContentHash == item.ContentHash,
                cancellationToken).ConfigureAwait(false);
            if (!duplicate)
            {
                item.CandidateId = targetId;
            }
        }
        foreach (EvaluationEntity item in await context.Set<EvaluationEntity>().Where(item => item.CandidateId == sourceId).ToListAsync(cancellationToken).ConfigureAwait(false)) item.CandidateId = targetId;
        foreach (InvitationDraftEntity item in await context.Set<InvitationDraftEntity>().Where(item => item.CandidateId == sourceId).ToListAsync(cancellationToken).ConfigureAwait(false)) item.CandidateId = targetId;
        target.FirstSeenAtUtc = source.FirstSeenAtUtc < target.FirstSeenAtUtc ? source.FirstSeenAtUtc : target.FirstSeenAtUtc;
        target.LastSeenAtUtc = source.LastSeenAtUtc > target.LastSeenAtUtc ? source.LastSeenAtUtc : target.LastSeenAtUtc;
        target.UpdatedAtUtc = nowUtc; target.Version++;
        source.MergedIntoCandidateId = targetId;
        source.Status = CandidateStatus.Passed.ToString();
        source.CurrentSnapshotId = null;
        source.LatestEvaluationId = null;
        source.UpdatedAtUtc = nowUtc;
        source.Version++;
        context.Add(NewAction(targetId, CandidateActionType.ManualMerge, nowUtc, actor, "Candidate identity aggregates were merged without discarding dependent records.", JsonSerializer.Serialize(new { sourceCandidateId = sourceId, targetCandidateId = targetId, reasonCode }, JsonOptions), null, null, relatedRunId, correlationId));
        return targetId;
    }

    private static async ValueTask<CandidateStatus> RestoreCandidateStatusAsync(FounderScoutDbContext context, CandidateEntity candidate, CancellationToken cancellationToken)
    {
        if (candidate.LatestEvaluationId.HasValue) return CandidateStatus.Analyzed;
        ScreeningDecisionEntity? decision = await context.Set<ScreeningDecisionEntity>().AsNoTracking().Where(item => item.CandidateId == candidate.Id).OrderByDescending(item => item.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return decision is null ? CandidateStatus.Parsed : MapCandidateStatus(ParseEnum<ScreeningOutcome>(decision.Outcome));
    }

    private static CandidateStatus MapCandidateStatus(ScreeningOutcome outcome) => outcome switch
    {
        ScreeningOutcome.DeepAnalyze or ScreeningOutcome.Passed => CandidateStatus.PendingAnalysis,
        ScreeningOutcome.Monitor => CandidateStatus.Monitor,
        ScreeningOutcome.FilteredOut => CandidateStatus.FilteredOut,
        ScreeningOutcome.ManualReview or ScreeningOutcome.NeedsReview => CandidateStatus.ManualReview,
        _ => CandidateStatus.Parsed,
    };

    private static void ValidateProcessingRequest(CompleteFounderProfileProcessingRequest request)
    {
        ValidateGuid(request.CandidateId, nameof(request)); ValidateGuid(request.SnapshotId, nameof(request));
        if (string.IsNullOrWhiteSpace(request.WorkerId) || request.WorkerId.Length > 128 || request.NormalizedProfileJson.Length > 262_144 || request.EvaluatorInputJson.Length > 262_144 || request.EvidenceJson.Length > 262_144 || request.RedactionJson.Length > 32_768 || request.NormalizedProfileHash.Length != 64 || request.EvaluatorInputHash.Length != 64 || request.RelatedRunId == Guid.Empty || string.IsNullOrWhiteSpace(request.CorrelationId))
            throw new ArgumentException("The completed Founder Scout processing output is invalid.", nameof(request));
    }
}
