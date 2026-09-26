using System.Text.Json;
using FounderScout.Application;
using FounderScout.Domain;
using FounderScout.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FounderScout.Infrastructure.Persistence;

/// <summary>EF-backed focused Founder Scout repositories over the separate bounded-context database.</summary>
public sealed partial class FounderScoutRepository(
    FounderScoutDbContextFactory contextFactory,
    TimeProvider timeProvider) :
    IBrowserAccountRepository,
    IDiscoverySegmentRepository,
    IDiscoveryCheckpointRepository,
    ICandidateRepository,
    IProfileSnapshotRepository,
    ICandidateIdentityRepository,
    IScreeningRepository,
    IEvaluationRepository,
    IInvitationDraftRepository,
    ICandidateActionWriter,
    IInvitationQueueRepository,
    IReportExportRepository,
    IFounderScoutWorkQueue,
    IFounderScoutMetricsReader,
    IDiscoveryHistoryReader,
    IFounderScoutProcessingQueue,
    ICandidateIdentityConflictRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly FounderScoutDbContextFactory contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    private readonly TimeProvider timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    async ValueTask<BrowserAccount?> IBrowserAccountRepository.GetAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        ValidateStableId(accountId, nameof(accountId));
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        BrowserAccountEntity? entity = await context.Set<BrowserAccountEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == accountId, cancellationToken).ConfigureAwait(false);
        return entity is null ? null : FounderScoutPersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<BrowserAccount>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<BrowserAccountEntity> entities = await context.Set<BrowserAccountEntity>().AsNoTracking()
            .OrderBy(item => item.DisplayName)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return entities.Select(FounderScoutPersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<BrowserAccount> UpsertAsync(
        BrowserAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ValidateBrowserAccount(account);
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        BrowserAccountEntity? entity = await context.Set<BrowserAccountEntity>()
            .SingleOrDefaultAsync(item => item.Id == account.Id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            entity = FounderScoutPersistenceMapper.Map(account);
            context.Add(entity);
        }
        else
        {
            entity.DisplayName = account.DisplayName.Trim();
            entity.BrowserProfileRelativePath = ValidateRelativePath(account.BrowserProfileRelativePath);
            entity.Enabled = account.Enabled;
            entity.SessionStatus = account.SessionStatus.ToString();
            entity.AssignedSegmentIdsJson = JsonSerializer.Serialize(
                account.AssignedSegmentIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
                JsonOptions);
            entity.LastAuthenticatedAtUtc = Utc(account.LastAuthenticatedAtUtc);
            entity.LastSuccessfulRunAtUtc = Utc(account.LastSuccessfulRunAtUtc);
            entity.LastFailedRunAtUtc = Utc(account.LastFailedRunAtUtc);
            entity.LastErrorReasonCode = Bounded(account.LastErrorReasonCode, 128);
            entity.LastErrorMessage = Bounded(account.LastErrorMessage, 1_000);
            entity.SuspendedUntilUtc = Utc(account.SuspendedUntilUtc);
            entity.UpdatedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
            entity.Version++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FounderScoutPersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<BrowserAccount> TransitionHealthAsync(
        string accountId,
        BrowserSessionStatus requested,
        string reasonCode,
        string? boundedMessage,
        DateTimeOffset? suspendedUntilUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateStableId(accountId, nameof(accountId));
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        BrowserAccountEntity entity = await context.Set<BrowserAccountEntity>()
            .SingleOrDefaultAsync(item => item.Id == accountId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The Founder Scout browser account does not exist.");
        BrowserSessionStatus current = ParseEnum<BrowserSessionStatus>(entity.SessionStatus);
        FounderScoutTransitionResult<BrowserSessionStatus> transition = BrowserAccountLifecycle.Validate(current, requested, reasonCode);
        if (!transition.IsAllowed)
        {
            throw new InvalidOperationException(transition.Error!.Code);
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        entity.SessionStatus = requested.ToString();
        entity.Enabled = requested != BrowserSessionStatus.Disabled;
        entity.LastErrorReasonCode = requested == BrowserSessionStatus.Healthy ? null : Bounded(reasonCode, 128);
        entity.LastErrorMessage = requested == BrowserSessionStatus.Healthy ? null : Bounded(boundedMessage, 1_000);
        entity.SuspendedUntilUtc = Utc(suspendedUntilUtc);
        entity.UpdatedAtUtc = nowUtc;
        entity.Version++;
        if (requested == BrowserSessionStatus.Healthy)
        {
            entity.LastAuthenticatedAtUtc = nowUtc;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FounderScoutPersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    async ValueTask<DiscoverySegment?> IDiscoverySegmentRepository.GetAsync(
        string segmentId,
        CancellationToken cancellationToken)
    {
        ValidateStableId(segmentId, nameof(segmentId));
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        DiscoverySegmentEntity? entity = await context.Set<DiscoverySegmentEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == segmentId, cancellationToken).ConfigureAwait(false);
        return entity is null ? null : FounderScoutPersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DiscoverySegment>> ListEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<DiscoverySegmentEntity> entities = await context.Set<DiscoverySegmentEntity>().AsNoTracking()
            .Where(item => item.Enabled && (!item.PausedUntilUtc.HasValue || item.PausedUntilUtc <= nowUtc))
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return entities.Select(FounderScoutPersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<DiscoverySegment> UpsertAsync(
        DiscoverySegment segment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ValidateStableId(segment.Id, nameof(segment));
        ValidateJsonObject(segment.ConfigurationJson, 65_536, nameof(segment));
        if (string.IsNullOrWhiteSpace(segment.Name) || segment.Name.Length > 200 || segment.Priority is < 0 or > 10_000)
        {
            throw new ArgumentException("The discovery segment is invalid.", nameof(segment));
        }

        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        DiscoverySegmentEntity? entity = await context.Set<DiscoverySegmentEntity>()
            .SingleOrDefaultAsync(item => item.Id == segment.Id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            entity = FounderScoutPersistenceMapper.Map(segment);
            context.Add(entity);
        }
        else
        {
            entity.Name = segment.Name.Trim();
            entity.Enabled = segment.Enabled;
            entity.Priority = segment.Priority;
            entity.ConfigurationJson = segment.ConfigurationJson;
            entity.AssignedAccountId = Bounded(segment.AssignedAccountId, 64);
            entity.LastRunAtUtc = Utc(segment.LastRunAtUtc);
            entity.ViewedCount = segment.ViewedCount;
            entity.NewCount = segment.NewCount;
            entity.DuplicateCount = segment.DuplicateCount;
            entity.ErrorCount = segment.ErrorCount;
            entity.ConsecutiveLowYieldRuns = segment.ConsecutiveLowYieldRuns;
            entity.PausedUntilUtc = Utc(segment.PausedUntilUtc);
            entity.UpdatedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
            entity.Version++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return FounderScoutPersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask AppendAsync(
        DiscoveryCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ValidateStableId(checkpoint.AccountId, nameof(checkpoint));
        ValidateStableId(checkpoint.SegmentId, nameof(checkpoint));
        ValidateJsonObject(checkpoint.StateJson, 65_536, nameof(checkpoint));
        string lowered = checkpoint.StateJson.ToLowerInvariant();
        if (lowered.Contains("cookie", StringComparison.Ordinal)
            || lowered.Contains("password", StringComparison.Ordinal)
            || lowered.Contains("access_token", StringComparison.Ordinal)
            || lowered.Contains("authorization", StringComparison.Ordinal)
            || lowered.Contains("<html", StringComparison.Ordinal))
        {
            throw new ArgumentException("Checkpoint state contains forbidden session or whole-page content.", nameof(checkpoint));
        }

        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.Add(FounderScoutPersistenceMapper.Map(checkpoint));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<DiscoveryCheckpoint?> GetLatestAsync(
        string accountId,
        string segmentId,
        CancellationToken cancellationToken = default)
    {
        ValidateStableId(accountId, nameof(accountId));
        ValidateStableId(segmentId, nameof(segmentId));
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        DiscoveryCheckpointEntity? entity = await context.Set<DiscoveryCheckpointEntity>().AsNoTracking()
            .Where(item => item.AccountId == accountId && item.SegmentId == segmentId)
            .OrderByDescending(item => item.CapturedAtUtc)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return entity is null ? null : FounderScoutPersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    async ValueTask<Candidate?> ICandidateRepository.GetAsync(Guid candidateId, CancellationToken cancellationToken)
    {
        ValidateGuid(candidateId, nameof(candidateId));
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        CandidateEntity? entity = await context.Set<CandidateEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == candidateId, cancellationToken).ConfigureAwait(false);
        return entity is null ? null : FounderScoutPersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<ResolveCandidateResult> ResolveAsync(
        ResolveCandidateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateResolveRequest(request);
        DateTimeOffset seenAtUtc = request.SeenAtUtc.ToUniversalTime();
        string[] strongHashes = request.Identities.Where(item => item.IsStrong)
            .Select(item => item.NormalizedValueHash)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        List<Guid> candidateIds = await context.Set<CandidateIdentityAliasEntity>().AsNoTracking()
            .Where(item => item.IsStrong && item.IsActive && strongHashes.Contains(item.NormalizedValueHash))
            .Select(item => item.CandidateId)
            .Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (candidateIds.Count > 1)
        {
            throw new InvalidOperationException("candidate.identity.multipleStrongOwners");
        }

        CandidateEntity? entity = candidateIds.Count == 1
            ? await context.Set<CandidateEntity>().SingleAsync(item => item.Id == candidateIds[0], cancellationToken).ConfigureAwait(false)
            : null;
        bool created = entity is null;
        if (entity is null)
        {
            entity = new CandidateEntity
            {
                Id = request.ProposedCandidateId,
                CurrentSourceProfileKey = request.SourceProfileKey,
                CanonicalSourceUrl = request.CanonicalSourceUrl,
                DisplayName = request.DisplayName.Trim(),
                TechnicalStatus = TechnicalProfileStatus.Unknown.ToString(),
                CommitmentStatus = FounderCommitmentStatus.Unknown.ToString(),
                IdeaCommitmentStatus = IdeaCommitmentStatus.Unknown.ToString(),
                Status = CandidateStatus.Discovered.ToString(),
                FirstSeenAtUtc = seenAtUtc,
                LastSeenAtUtc = seenAtUtc,
                CreatedAtUtc = seenAtUtc,
                UpdatedAtUtc = seenAtUtc,
                Version = 1,
            };
            context.Add(entity);
            context.Add(NewAction(
                entity.Id,
                CandidateActionType.CandidateDiscovered,
                seenAtUtc,
                "founder-scout",
                "Candidate identity was first observed.",
                JsonSerializer.Serialize(new { request.SourceProfileKey }, JsonOptions),
                null,
                null,
                request.RelatedRunId,
                request.CorrelationId));
        }
        else
        {
            entity.CurrentSourceProfileKey = request.SourceProfileKey;
            entity.CanonicalSourceUrl ??= request.CanonicalSourceUrl;
            entity.DisplayName = request.DisplayName.Trim();
            entity.LastSeenAtUtc = seenAtUtc > entity.LastSeenAtUtc ? seenAtUtc : entity.LastSeenAtUtc;
            entity.UpdatedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
            entity.Version++;
        }

        var attached = new List<CandidateIdentityAlias>();
        foreach (CandidateIdentityInput identity in request.Identities)
        {
            CandidateIdentityAliasEntity? existing = await context.Set<CandidateIdentityAliasEntity>()
                .SingleOrDefaultAsync(item => item.CandidateId == entity.Id
                    && item.AliasType == identity.AliasType.ToString()
                    && item.NormalizedValueHash == identity.NormalizedValueHash, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                attached.Add(FounderScoutPersistenceMapper.Map(existing));
                continue;
            }

            if (identity.IsStrong)
            {
                CandidateIdentityAliasEntity? owner = await context.Set<CandidateIdentityAliasEntity>().AsNoTracking()
                    .SingleOrDefaultAsync(item => item.IsStrong
                        && item.IsActive
                        && item.NormalizedValueHash == identity.NormalizedValueHash, cancellationToken).ConfigureAwait(false);
                if (owner is not null && owner.CandidateId != entity.Id)
                {
                    throw new InvalidOperationException("candidate.identity.strongAliasConflict");
                }
            }

            CandidateIdentityAliasEntity alias = NewAlias(entity.Id, identity, seenAtUtc);
            context.Add(alias);
            attached.Add(FounderScoutPersistenceMapper.Map(alias));
        }

        if (attached.Count > 0)
        {
            context.Add(NewAction(
                entity.Id,
                CandidateActionType.IdentityAttached,
                seenAtUtc,
                "founder-scout",
                "Candidate identity aliases were resolved.",
                JsonSerializer.Serialize(new { aliasCount = attached.Count }, JsonOptions),
                null,
                null,
                request.RelatedRunId,
                request.CorrelationId));
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(FounderScoutPersistenceMapper.Map(entity), created, attached);
    }

    /// <inheritdoc />
    public async ValueTask<Candidate> TransitionAsync(
        CandidateTransitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateGuid(request.CandidateId, nameof(request));
        ValidateJsonObject(request.DataJson, 262_144, nameof(request));
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        CandidateEntity entity = await context.Set<CandidateEntity>()
            .SingleOrDefaultAsync(item => item.Id == request.CandidateId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The Founder Scout candidate does not exist.");
        CandidateStatus current = ParseEnum<CandidateStatus>(entity.Status);
        FounderScoutTransitionResult<CandidateStatus> transition = CandidateLifecycle.Validate(current, request.RequestedStatus, request.ReasonCode);
        if (!transition.IsAllowed)
        {
            throw new InvalidOperationException(transition.Error!.Code);
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        entity.Status = request.RequestedStatus.ToString();
        entity.UpdatedAtUtc = nowUtc;
        entity.Version++;
        context.Add(NewAction(
            entity.Id,
            CandidateActionType.StateTransitioned,
            nowUtc,
            request.Actor,
            request.Notes,
            request.DataJson,
            request.RelatedInvitationId,
            request.RelatedEvaluationId,
            request.RelatedRunId,
            request.CorrelationId));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return FounderScoutPersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<CandidatePage> QueryRankedAsync(
        CandidateRankingQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Offset < 0 || query.PageSize is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(query));
        }

        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        IQueryable<CandidateEntity> source = context.Set<CandidateEntity>().AsNoTracking().Where(item => !item.MergedIntoCandidateId.HasValue);
        if (query.Statuses is { Count: > 0 })
        {
            string[] statuses = query.Statuses.Select(item => item.ToString()).Distinct(StringComparer.Ordinal).ToArray();
            source = source.Where(item => statuses.Contains(item.Status));
        }

        if (query.MinimumPriority.HasValue)
        {
            source = source.Where(item => item.InvitationPriority >= query.MinimumPriority.Value);
        }

        int total = await source.CountAsync(cancellationToken).ConfigureAwait(false);
        List<CandidateEntity> entities = await source
            .OrderByDescending(item => item.InvitationPriority)
            .ThenByDescending(item => item.Confidence)
            .ThenBy(item => item.Id)
            .Skip(query.Offset)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new(entities.Select(FounderScoutPersistenceMapper.Map).ToArray(), query.Offset, query.PageSize, total);
    }

    /// <inheritdoc />
    public async ValueTask<AddProfileSnapshotResult> AddIfNewAsync(
        AddProfileSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateSnapshotRequest(request);
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        CandidateEntity candidate = await context.Set<CandidateEntity>()
            .SingleOrDefaultAsync(item => item.Id == request.CandidateId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The Founder Scout candidate does not exist.");
        ProfileSnapshotEntity? existing = await context.Set<ProfileSnapshotEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.CandidateId == request.CandidateId
                && item.SourceProfileKey == request.SourceProfileKey
                && item.ContentHash == request.ContentHash, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(FounderScoutPersistenceMapper.Map(existing), FounderScoutPersistenceMapper.Map(candidate), false);
        }

        ProfileSnapshotEntity snapshot = FounderScoutPersistenceMapper.Map(request);
        context.Add(snapshot);
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        candidate.CurrentSourceProfileKey = request.SourceProfileKey;
        candidate.CanonicalSourceUrl ??= request.CanonicalSourceUrl;
        candidate.LastSeenAtUtc = request.CapturedAtUtc.ToUniversalTime() > candidate.LastSeenAtUtc
            ? request.CapturedAtUtc.ToUniversalTime()
            : candidate.LastSeenAtUtc;
        candidate.Status = CandidateStatus.Captured.ToString();
        candidate.AnalysisWorkerId = null;
        candidate.AnalysisClaimedAtUtc = null;
        candidate.AnalysisClaimExpiresAtUtc = null;
        candidate.LastAnalysisErrorCode = null;
        candidate.UpdatedAtUtc = nowUtc;
        candidate.Version++;
        context.Add(NewAction(
            candidate.Id,
            CandidateActionType.SnapshotCaptured,
            request.CapturedAtUtc.ToUniversalTime(),
            "founder-scout",
            "A raw profile snapshot was committed before deterministic processing.",
            JsonSerializer.Serialize(new { snapshotId = snapshot.Id, request.ContentHash }, JsonOptions),
            null,
            null,
            request.RelatedRunId,
            request.CorrelationId));
        context.Add(NewAction(
            candidate.Id,
            CandidateActionType.StateTransitioned,
            nowUtc,
            "founder-scout",
            "The captured profile is pending deterministic processing.",
            JsonSerializer.Serialize(new { status = CandidateStatus.Captured.ToString() }, JsonOptions),
            null,
            null,
            request.RelatedRunId,
            request.CorrelationId));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(FounderScoutPersistenceMapper.Map(snapshot), FounderScoutPersistenceMapper.Map(candidate), true);
    }

    /// <inheritdoc />
    async ValueTask<ProfileSnapshot?> IProfileSnapshotRepository.GetAsync(
        Guid snapshotId,
        CancellationToken cancellationToken)
    {
        ValidateGuid(snapshotId, nameof(snapshotId));
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ProfileSnapshotEntity? entity = await context.Set<ProfileSnapshotEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == snapshotId, cancellationToken).ConfigureAwait(false);
        return entity is null ? null : FounderScoutPersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ProfileSnapshot>> ListForCandidateAsync(
        Guid candidateId,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(candidateId, nameof(candidateId));
        ValidateMaximum(maximumResults);
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<ProfileSnapshotEntity> entities = await context.Set<ProfileSnapshotEntity>().AsNoTracking()
            .Where(item => item.CandidateId == candidateId)
            .OrderByDescending(item => item.CapturedAtUtc)
            .ThenByDescending(item => item.Id)
            .Take(maximumResults)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return entities.Select(FounderScoutPersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<AttachIdentityResult> AttachAsync(
        Guid candidateId,
        CandidateIdentityInput identity,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(candidateId, nameof(candidateId));
        ValidateIdentity(identity);
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        _ = await context.Set<CandidateEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == candidateId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("The Founder Scout candidate does not exist.");
        CandidateIdentityAliasEntity? existing = await context.Set<CandidateIdentityAliasEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.CandidateId == candidateId
                && item.AliasType == identity.AliasType.ToString()
                && item.NormalizedValueHash == identity.NormalizedValueHash, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return new(FounderScoutPersistenceMapper.Map(existing), false, null);
        }

        if (identity.IsStrong)
        {
            CandidateIdentityAliasEntity? owner = await context.Set<CandidateIdentityAliasEntity>().AsNoTracking()
                .SingleOrDefaultAsync(item => item.IsStrong && item.IsActive
                    && item.NormalizedValueHash == identity.NormalizedValueHash, cancellationToken).ConfigureAwait(false);
            if (owner is not null && owner.CandidateId != candidateId)
            {
                return new(null, false, new(
                    "candidate.identity.strongAliasConflict",
                    identity.NormalizedValueHash,
                    owner.CandidateId,
                    candidateId));
            }
        }

        CandidateIdentityAliasEntity alias = NewAlias(
            candidateId,
            identity,
            timeProvider.GetUtcNow().ToUniversalTime());
        context.Add(alias);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new(FounderScoutPersistenceMapper.Map(alias), true, null);
        }
        catch (DbUpdateException) when (identity.IsStrong)
        {
            context.ChangeTracker.Clear();
            CandidateIdentityAliasEntity owner = await context.Set<CandidateIdentityAliasEntity>().AsNoTracking()
                .SingleAsync(item => item.IsStrong && item.IsActive
                    && item.NormalizedValueHash == identity.NormalizedValueHash, cancellationToken).ConfigureAwait(false);
            return owner.CandidateId == candidateId
                ? new(FounderScoutPersistenceMapper.Map(owner), false, null)
                : new(null, false, new("candidate.identity.strongAliasConflict", identity.NormalizedValueHash, owner.CandidateId, candidateId));
        }
    }

    /// <inheritdoc />
    public async ValueTask AppendAsync(CandidateAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ValidateAction(action);
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.Add(FounderScoutPersistenceMapper.Map(action));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CandidateAction>> ListAsync(
        Guid candidateId,
        int maximumResults,
        CancellationToken cancellationToken = default)
    {
        ValidateGuid(candidateId, nameof(candidateId));
        ValidateMaximum(maximumResults);
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<CandidateActionEntity> entities = await context.Set<CandidateActionEntity>().AsNoTracking()
            .Where(item => item.CandidateId == candidateId)
            .OrderByDescending(item => item.OccurredAtUtc)
            .ThenByDescending(item => item.Id)
            .Take(maximumResults)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return entities.Select(FounderScoutPersistenceMapper.Map).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<FounderScoutDomainCounts> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        List<(string Status, int Count)> statusCounts = await context.Set<CandidateEntity>().AsNoTracking().Where(item => !item.MergedIntoCandidateId.HasValue)
            .GroupBy(item => item.Status)
            .Select(group => new ValueTuple<string, int>(group.Key, group.Count()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<CandidateStatus, int> byStatus = statusCounts.ToDictionary(
            item => ParseEnum<CandidateStatus>(item.Status),
            item => item.Count);
        int browserAccounts = await context.Set<BrowserAccountEntity>().CountAsync(cancellationToken).ConfigureAwait(false);
        int enabledAccounts = await context.Set<BrowserAccountEntity>().CountAsync(item => item.Enabled, cancellationToken).ConfigureAwait(false);
        int segments = await context.Set<DiscoverySegmentEntity>().CountAsync(cancellationToken).ConfigureAwait(false);
        int candidates = await context.Set<CandidateEntity>().CountAsync(item => !item.MergedIntoCandidateId.HasValue, cancellationToken).ConfigureAwait(false);
        int snapshots = await context.Set<ProfileSnapshotEntity>().CountAsync(cancellationToken).ConfigureAwait(false);
        int screenings = await context.Set<ScreeningDecisionEntity>().CountAsync(cancellationToken).ConfigureAwait(false);
        int evaluations = await context.Set<EvaluationEntity>().CountAsync(cancellationToken).ConfigureAwait(false);
        int drafts = await context.Set<InvitationDraftEntity>().CountAsync(cancellationToken).ConfigureAwait(false);
        int actions = await context.Set<CandidateActionEntity>().CountAsync(cancellationToken).ConfigureAwait(false);
        int reports = await context.Set<ReportExportEntity>().CountAsync(cancellationToken).ConfigureAwait(false);
        return new(
            browserAccounts,
            enabledAccounts,
            segments,
            candidates,
            snapshots,
            screenings,
            evaluations,
            drafts,
            actions,
            byStatus.GetValueOrDefault(CandidateStatus.PendingAnalysis),
            byStatus.GetValueOrDefault(CandidateStatus.QueuedForInvite),
            reports,
            byStatus);
    }

    /// <inheritdoc />
    public async ValueTask<int> CountDistinctCandidatesCapturedSinceAsync(
        string accountId,
        DateTimeOffset fromUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateStableId(accountId, nameof(accountId));
        DateTimeOffset boundary = fromUtc.ToUniversalTime();
        await using FounderScoutDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.Set<ProfileSnapshotEntity>().AsNoTracking()
            .Where(item => item.SourceAccountId == accountId && item.CapturedAtUtc >= boundary)
            .Select(item => item.CandidateId)
            .Distinct()
            .CountAsync(cancellationToken).ConfigureAwait(false);
    }

    private static CandidateIdentityAliasEntity NewAlias(
        Guid candidateId,
        CandidateIdentityInput identity,
        DateTimeOffset createdAtUtc) => new()
        {
            Id = Guid.NewGuid(),
            CandidateId = candidateId,
            AliasType = identity.AliasType.ToString(),
            NormalizedValueHash = identity.NormalizedValueHash,
            SourceAccountId = identity.SourceAccountId,
            Confidence = identity.Confidence,
            IsStrong = identity.IsStrong,
            IsActive = true,
            CreatedAtUtc = createdAtUtc.ToUniversalTime(),
        };

    private static CandidateActionEntity NewAction(
        Guid candidateId,
        CandidateActionType actionType,
        DateTimeOffset occurredAtUtc,
        string actor,
        string? notes,
        string dataJson,
        Guid? relatedInvitationId,
        Guid? relatedEvaluationId,
        Guid? relatedRunId,
        string correlationId) => new()
        {
            Id = Guid.NewGuid(),
            CandidateId = candidateId,
            ActionType = actionType.ToString(),
            OccurredAtUtc = occurredAtUtc.ToUniversalTime(),
            Actor = Bounded(actor, 128) ?? "founder-scout",
            Notes = Bounded(notes, 2_000),
            DataJson = dataJson,
            RelatedInvitationId = relatedInvitationId,
            RelatedEvaluationId = relatedEvaluationId,
            RelatedRunId = relatedRunId,
            CorrelationId = Bounded(correlationId, 128) ?? throw new ArgumentException("A correlation ID is required."),
        };

    private static void ValidateResolveRequest(ResolveCandidateRequest request)
    {
        ValidateGuid(request.ProposedCandidateId, nameof(request));
        ValidateStableId(request.SourceProfileKey, nameof(request));
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Length > 200
            || request.Identities.Count is < 1 or > 8
            || !request.Identities.Any(item => item.IsStrong)
            || string.IsNullOrWhiteSpace(request.CorrelationId)
            || request.CorrelationId.Length > 128)
        {
            throw new ArgumentException("The candidate resolution request is invalid.", nameof(request));
        }

        foreach (CandidateIdentityInput identity in request.Identities)
        {
            ValidateIdentity(identity);
        }
    }

    private static void ValidateIdentity(CandidateIdentityInput identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.NormalizedValueHash.Length != 64
            || !identity.NormalizedValueHash.All(Uri.IsHexDigit)
            || identity.Confidence is < 0 or > 1
            || identity.SourceAccountId?.Length > 64
            || identity.IsStrong && identity.AliasType is not (CandidateIdentityAliasType.SourceKey
                or CandidateIdentityAliasType.CanonicalUrl
                or CandidateIdentityAliasType.TrustedSourceId
                or CandidateIdentityAliasType.Fingerprint
                or CandidateIdentityAliasType.ManualMerge))
        {
            throw new ArgumentException("The candidate identity alias is invalid.", nameof(identity));
        }
    }

    private static void ValidateSnapshotRequest(AddProfileSnapshotRequest request)
    {
        ValidateGuid(request.SnapshotId, nameof(request));
        ValidateGuid(request.CandidateId, nameof(request));
        ValidateStableId(request.SourceAccountId, nameof(request));
        ValidateStableId(request.SourceSegmentId, nameof(request));
        ValidateStableId(request.SourceProfileKey, nameof(request));
        if (request.ContentHash.Length != 64
            || !request.ContentHash.All(Uri.IsHexDigit)
            || string.IsNullOrWhiteSpace(request.RawArtifactRelativePath)
            || Path.IsPathFullyQualified(request.RawArtifactRelativePath)
            || request.RawArtifactRelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "." or "..")
            || request.ExtractionCompleteness is < 0 or > 1
            || request.ExtractionConfidence is < 0 or > 1
            || string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            throw new ArgumentException("The profile snapshot request is invalid.", nameof(request));
        }
    }

    private static void ValidateAction(CandidateAction action)
    {
        ValidateGuid(action.Id, nameof(action));
        ValidateGuid(action.CandidateId, nameof(action));
        ValidateJsonObject(action.DataJson, 262_144, nameof(action));
        if (string.IsNullOrWhiteSpace(action.Actor) || action.Actor.Length > 128
            || action.Notes?.Length > 2_000
            || string.IsNullOrWhiteSpace(action.CorrelationId)
            || action.CorrelationId.Length > 128)
        {
            throw new ArgumentException("The candidate action is invalid.", nameof(action));
        }
    }

    private static void ValidateBrowserAccount(BrowserAccount account)
    {
        ValidateStableId(account.Id, nameof(account));
        if (string.IsNullOrWhiteSpace(account.DisplayName) || account.DisplayName.Length > 200
            || account.AssignedSegmentIds.Count > 100
            || account.AssignedSegmentIds.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 64)
            || account.LastErrorReasonCode?.Length > 128
            || account.LastErrorMessage?.Length > 1_000)
        {
            throw new ArgumentException("The browser account is invalid.", nameof(account));
        }

        _ = ValidateRelativePath(account.BrowserProfileRelativePath);
    }

    private static string ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 512 || Path.IsPathFullyQualified(path)
            || path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "." or ".."))
        {
            throw new ArgumentException("A bounded relative browser profile path is required.", nameof(path));
        }

        return path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private static void ValidateJsonObject(string json, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > maximumLength)
        {
            throw new ArgumentException("A bounded JSON object is required.", parameterName);
        }

        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("A JSON object is required.", parameterName);
        }
    }

    private static void ValidateStableId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(char.IsControl))
        {
            throw new ArgumentException("A bounded stable identifier is required.", parameterName);
        }
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty identifier is required.", parameterName);
        }
    }

    private static void ValidateMaximum(int maximumResults)
    {
        if (maximumResults is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResults));
        }
    }

    private static string? Bounded(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maximum)];

    private static DateTimeOffset? Utc(DateTimeOffset? value) => value?.ToUniversalTime();

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum =>
        Enum.TryParse(value, ignoreCase: false, out TEnum parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException("Founder Scout persisted an unsupported enum value.");
}
