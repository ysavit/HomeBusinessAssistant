using System.Data.Common;
using FounderScout.Application;
using FounderScout.Domain;
using FounderScout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FounderScout.Tests;

internal sealed class FounderScoutPersistenceTests
{
    private static readonly DateTimeOffset BaselineUtc = new(2026, 8, 30, 20, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task FreshMigrationCreatesSeparateSchemaIndexesAndRequiredPragmas()
    {
        await using TemporaryFounderScoutDatabase fixture = await TemporaryFounderScoutDatabase.CreateAsync();
        await using FounderScoutDbContext context = await fixture.Database.ContextFactory.CreateDbContextAsync();
        string[] tables = await ReadNamesAsync(context, "table");
        string[] indexes = await ReadNamesAsync(context, "index");
        string[] candidateColumns = await ReadCandidateColumnsAsync(context);
        string[] migrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(Path.GetFileName(fixture.Database.DatabasePath), Is.EqualTo("founders.db"));
            Assert.That(tables, Does.Contain("Candidates"));
            Assert.That(tables, Does.Contain("ProfileSnapshots"));
            Assert.That(tables, Does.Contain("CandidateActions"));
            Assert.That(tables, Does.Contain("Evaluations"));
            Assert.That(tables, Does.Contain("InvitationDrafts"));
            Assert.That(tables, Does.Contain("CandidateIdentityConflicts"));
            Assert.That(indexes, Does.Contain("UX_ProfileSnapshots_Candidate_SourceKey_ContentHash"));
            Assert.That(indexes, Does.Contain("UX_CandidateIdentityAliases_StrongActiveHash"));
            Assert.That(fixture.Database.Pragmas.JournalMode, Is.EqualTo("wal").IgnoreCase);
            Assert.That(fixture.Database.Pragmas.ForeignKeysEnabled, Is.True);
            Assert.That(fixture.Database.Pragmas.BusyTimeoutMilliseconds, Is.EqualTo(5_000));
            Assert.That(fixture.Database.Pragmas.SynchronousMode, Is.EqualTo(1));
            Assert.That(migrations, Has.Length.EqualTo(3));
            Assert.That(migrations[^1], Does.EndWith("_AddFounderScoutResultsWorkflow"));
            Assert.That(tables, Does.Contain("InvitationDraftRevisions"));
            Assert.That(tables, Does.Contain("InvitationQueueEntries"));
            Assert.That(tables, Does.Contain("FounderScoutLeases"));
            Assert.That(candidateColumns, Does.Not.Contain("Age"));
            Assert.That(candidateColumns, Does.Not.Contain("Gender"));
            Assert.That(candidateColumns, Does.Not.Contain("Race"));
            Assert.That(candidateColumns, Does.Not.Contain("Religion"));
            Assert.That(candidateColumns, Does.Not.Contain("Photo"));
            Assert.That(candidateColumns, Does.Not.Contain("Health"));
        });
    }

    [Test]
    public async Task BrowserAccountSegmentAndCheckpointPersistWithoutSessionSecrets()
    {
        await using TemporaryFounderScoutDatabase fixture = await TemporaryFounderScoutDatabase.CreateAsync();
        FounderScoutRepository repository = fixture.CreateRepository();
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        BrowserAccount created = await repository.UpsertAsync(new BrowserAccount(
            "account-one",
            "Account One",
            "browser-profiles/account-one",
            Enabled: true,
            BrowserSessionStatus.Unknown,
            ["segment-one"],
            null,
            null,
            null,
            null,
            null,
            null,
            nowUtc,
            nowUtc,
            1));
        DiscoverySegment segment = await repository.UpsertAsync(new DiscoverySegment(
            "segment-one",
            "Operations founders",
            Enabled: true,
            Priority: 100,
            ConfigurationJson: "{\"filter\":\"operations\"}",
            AssignedAccountId: "account-one",
            LastRunAtUtc: null,
            ViewedCount: 0,
            NewCount: 0,
            DuplicateCount: 0,
            ErrorCount: 0,
            ConsecutiveLowYieldRuns: 0,
            PausedUntilUtc: null,
            nowUtc,
            nowUtc,
            Version: 1));
        BrowserAccount healthy = await repository.TransitionHealthAsync(
            created.Id,
            BrowserSessionStatus.Healthy,
            "authentication.completed",
            null,
            null);
        BrowserAccount disabled = await repository.TransitionHealthAsync(
            created.Id,
            BrowserSessionStatus.Disabled,
            "account.disabled",
            "Disabled by test operator.",
            null);
        var checkpoint = new DiscoveryCheckpoint(
            Guid.NewGuid(),
            created.Id,
            segment.Id,
            Guid.NewGuid(),
            "candidate-003",
            "{\"page\":2,\"lastSourceProfileKey\":\"candidate-003\"}",
            nowUtc,
            "Completed",
            "1.0");
        await repository.AppendAsync(checkpoint);
        DiscoveryCheckpoint? loaded = await repository.GetLatestAsync(created.Id, segment.Id);

        Assert.Multiple(() =>
        {
            Assert.That(healthy.SessionStatus, Is.EqualTo(BrowserSessionStatus.Healthy));
            Assert.That(healthy.LastAuthenticatedAtUtc, Is.Not.Null);
            Assert.That(disabled.Enabled, Is.False);
            Assert.That(loaded?.LastSourceProfileKey, Is.EqualTo("candidate-003"));
            Assert.That(async () => await repository.TransitionHealthAsync(
                created.Id,
                BrowserSessionStatus.Healthy,
                "invalid.reenable",
                null,
                null), Throws.TypeOf<InvalidOperationException>());
            Assert.That(async () => await repository.AppendAsync(checkpoint with
            {
                Id = Guid.NewGuid(),
                StateJson = "{\"cookies\":\"forbidden\"}",
            }), Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public async Task StrongAliasesResolveOneCandidateWeakAliasesRemainAmbiguousAndSnapshotsAreIdempotent()
    {
        await using TemporaryFounderScoutDatabase fixture = await TemporaryFounderScoutDatabase.CreateAsync();
        FounderScoutRepository repository = fixture.CreateRepository();
        ResolveCandidateResult first = await ResolveAsync(repository, "candidate-001", "Candidate One");
        ResolveCandidateResult repeated = await ResolveAsync(repository, "CANDIDATE-001", "Candidate One Updated");
        ResolveCandidateResult second = await ResolveAsync(repository, "candidate-002", "Candidate Two");
        string weakHash = FounderScoutIdentityNormalizer.Hash("fingerprint", "ambiguous-profile");
        AttachIdentityResult weakFirst = await repository.AttachAsync(first.Candidate.Id, new(
            CandidateIdentityAliasType.Fingerprint, weakHash, "fixture-account", 0.4m, IsStrong: false));
        AttachIdentityResult weakSecond = await repository.AttachAsync(second.Candidate.Id, new(
            CandidateIdentityAliasType.Fingerprint, weakHash, "fixture-account", 0.4m, IsStrong: false));
        string strongHash = FounderScoutIdentityNormalizer.Hash("canonical-url", "https://example.invalid/profile/shared");
        AttachIdentityResult strongFirst = await repository.AttachAsync(first.Candidate.Id, new(
            CandidateIdentityAliasType.CanonicalUrl, strongHash, "fixture-account", 1m, IsStrong: true));
        AttachIdentityResult strongConflict = await repository.AttachAsync(second.Candidate.Id, new(
            CandidateIdentityAliasType.CanonicalUrl, strongHash, "fixture-account", 1m, IsStrong: true));
        AddProfileSnapshotResult snapshot = await AddSnapshotAsync(repository, first.Candidate.Id, "candidate-001", 'a');
        AddProfileSnapshotResult duplicate = await AddSnapshotAsync(repository, first.Candidate.Id, "candidate-001", 'a');
        IReadOnlyList<CandidateAction> actions = await repository.ListAsync(first.Candidate.Id, 100);

        Assert.Multiple(() =>
        {
            Assert.That(first.Created, Is.True);
            Assert.That(repeated.Created, Is.False);
            Assert.That(repeated.Candidate.Id, Is.EqualTo(first.Candidate.Id));
            Assert.That(second.Candidate.Id, Is.Not.EqualTo(first.Candidate.Id));
            Assert.That(weakFirst.Created, Is.True);
            Assert.That(weakSecond.Created, Is.True);
            Assert.That(strongFirst.Created, Is.True);
            Assert.That(strongConflict.Conflict?.Code, Is.EqualTo("candidate.identity.strongAliasConflict"));
            Assert.That(snapshot.Created, Is.True);
            Assert.That(snapshot.Candidate.Status, Is.EqualTo(CandidateStatus.Captured));
            Assert.That(duplicate.Created, Is.False);
            Assert.That(duplicate.Snapshot.Id, Is.EqualTo(snapshot.Snapshot.Id));
            Assert.That(actions.Count(action => action.ActionType == CandidateActionType.SnapshotCaptured), Is.EqualTo(1));
            Assert.That(actions.Any(action => action.RelatedRunId.HasValue), Is.True);
        });
    }

    [Test]
    public async Task AnalysisClaimHasSingleOwnerExpiresAndRejectsStaleFinalizer()
    {
        await using TemporaryFounderScoutDatabase fixture = await TemporaryFounderScoutDatabase.CreateAsync();
        var clock = new AdjustableTimeProvider(BaselineUtc);
        FounderScoutRepository repository = fixture.CreateRepository(clock);
        ResolveCandidateResult candidate = await ResolveAsync(repository, "claim-candidate", "Claim Candidate");
        _ = await AddSnapshotAsync(repository, candidate.Candidate.Id, "claim-candidate", 'b');
        _ = await repository.TransitionAsync(new(candidate.Candidate.Id, CandidateStatus.PendingAnalysis, "test.queue", "test", null, "{}", null, null, Guid.NewGuid(), "claim-test"));

        FounderScoutAnalysisClaim? first = await repository.ClaimPendingAnalysisAsync("worker-one", TimeSpan.FromSeconds(10));
        FounderScoutAnalysisClaim? competing = await repository.ClaimPendingAnalysisAsync("worker-two", TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(11));
        FounderScoutAnalysisClaim? reclaimed = await repository.ClaimPendingAnalysisAsync("worker-two", TimeSpan.FromMinutes(1));
        bool staleFinalizer = await repository.FinalizeAnalysisClaimAsync(new(
            candidate.Candidate.Id,
            "worker-one",
            CandidateStatus.Analyzed,
            null,
            70m,
            60m,
            0.8m,
            50m,
            5m,
            70m,
            "analysis.completed",
            Guid.NewGuid(),
            "claim-test"));
        bool released = await repository.ReleaseAnalysisClaimAsync(new(
            candidate.Candidate.Id,
            "worker-two",
            "analysis.retry",
            Retry: true,
            Guid.NewGuid(),
            "claim-test"));

        Assert.Multiple(() =>
        {
            Assert.That(first?.WorkerId, Is.EqualTo("worker-one"));
            Assert.That(competing, Is.Null);
            Assert.That(reclaimed?.WorkerId, Is.EqualTo("worker-two"));
            Assert.That(staleFinalizer, Is.False);
            Assert.That(released, Is.True);
        });
    }

    [Test]
    public async Task ConcurrentAnalysisClaimsReturnExactlyOneOwner()
    {
        await using TemporaryFounderScoutDatabase fixture = await TemporaryFounderScoutDatabase.CreateAsync();
        FounderScoutRepository seedingRepository = fixture.CreateRepository();
        ResolveCandidateResult candidate = await ResolveAsync(seedingRepository, "concurrent-claim", "Concurrent Claim");
        _ = await AddSnapshotAsync(seedingRepository, candidate.Candidate.Id, "concurrent-claim", 'f');
        _ = await seedingRepository.TransitionAsync(new(candidate.Candidate.Id, CandidateStatus.PendingAnalysis, "test.queue", "test", null, "{}", null, null, Guid.NewGuid(), "claim-test"));
        FounderScoutRepository firstRepository = fixture.CreateRepository();
        FounderScoutRepository secondRepository = fixture.CreateRepository();

        FounderScoutAnalysisClaim?[] claims = await Task.WhenAll(
            firstRepository.ClaimPendingAnalysisAsync("worker-one", TimeSpan.FromMinutes(1)).AsTask(),
            secondRepository.ClaimPendingAnalysisAsync("worker-two", TimeSpan.FromMinutes(1)).AsTask());

        Assert.That(claims.Count(claim => claim is not null), Is.EqualTo(1));
    }

    [Test]
    public async Task EvaluationInvitationAndWindowTransitionsRequireExplicitReviewAndManualSend()
    {
        await using TemporaryFounderScoutDatabase fixture = await TemporaryFounderScoutDatabase.CreateAsync();
        FounderScoutRepository repository = fixture.CreateRepository();
        ResolveCandidateResult candidate = await ResolveAsync(repository, "evaluation-candidate", "Evaluation Candidate");
        AddProfileSnapshotResult snapshot = await AddSnapshotAsync(repository, candidate.Candidate.Id, "evaluation-candidate", 'c');
        Guid evaluationId = Guid.NewGuid();
        Evaluation evaluation = CreateEvaluation(evaluationId, candidate.Candidate.Id, snapshot.Snapshot.Id);
        EvaluationAggregate saved = await repository.AddAsync(new(
            evaluation,
            [new(Guid.NewGuid(), evaluationId, "execution", 10m, 20m, "{}", "Evidence pending Stage 12.", BaselineUtc)],
            [new(Guid.NewGuid(), evaluationId, "scope", 5m, "{}", "Risk pending Stage 12.", BaselineUtc)]));
        Evaluation claimed = await repository.TransitionAsync(evaluationId, EvaluationStatus.Claimed, "evaluation.claimed");
        Evaluation completed = await repository.TransitionAsync(evaluationId, EvaluationStatus.Completed, "evaluation.completed");
        string fingerprint = new('d', 64);
        Guid draftId = Guid.NewGuid();
        InvitationDraft draft = await repository.CreateAndSupersedePreviousAsync(new(
            draftId,
            evaluationId,
            candidate.Candidate.Id,
            "Short draft",
            "Detailed draft",
            "{}",
            0.8m,
            InvitationDraftStatus.Draft,
            "[]",
            fingerprint,
            BaselineUtc,
            null,
            null,
            Superseded: false,
            Version: 1));
        InvitationDraft valid = await repository.TransitionAsync(draftId, InvitationDraftStatus.Valid, "draft.valid", "user");
        InvitationDraft reviewed = await repository.TransitionAsync(draftId, InvitationDraftStatus.Reviewed, "draft.reviewed", "user");
        InvitationDraft sent = await repository.TransitionAsync(draftId, InvitationDraftStatus.ManuallySent, "draft.sent", "user");
        ManualInvitationWindow window = await repository.SaveWindowAsync(new(
            Guid.NewGuid(),
            BaselineUtc,
            BaselineUtc.AddDays(7),
            15,
            15,
            0,
            "User-managed window",
            BaselineUtc,
            BaselineUtc,
            1));
        bool incremented = await repository.TryIncrementSentCountAsync(window.Id, window.Version);
        bool staleIncrement = await repository.TryIncrementSentCountAsync(window.Id, window.Version);

        Assert.Multiple(() =>
        {
            Assert.That(saved.Categories, Has.Count.EqualTo(1));
            Assert.That(claimed.Status, Is.EqualTo(EvaluationStatus.Claimed));
            Assert.That(completed.Status, Is.EqualTo(EvaluationStatus.Completed));
            Assert.That(draft.Status, Is.EqualTo(InvitationDraftStatus.Draft));
            Assert.That(valid.Status, Is.EqualTo(InvitationDraftStatus.Valid));
            Assert.That(reviewed.Status, Is.EqualTo(InvitationDraftStatus.Reviewed));
            Assert.That(sent.Status, Is.EqualTo(InvitationDraftStatus.ManuallySent));
            Assert.That(sent.ReviewedBy, Is.EqualTo("user"));
            Assert.That(incremented, Is.True);
            Assert.That(staleIncrement, Is.False);
        });
    }

    [Test]
    public async Task RankedPagingAndInvitationQueueHaveStableShapeAtTenThousandCandidateScaleBoundary()
    {
        await using TemporaryFounderScoutDatabase fixture = await TemporaryFounderScoutDatabase.CreateAsync();
        FounderScoutRepository repository = fixture.CreateRepository();
        for (var index = 0; index < 12; index++)
        {
            ResolveCandidateResult candidate = await ResolveAsync(repository, $"rank-{index:00}", $"Rank {index:00}");
            _ = await AddSnapshotAsync(repository, candidate.Candidate.Id, $"rank-{index:00}", 'a');
            _ = await repository.TransitionAsync(new(candidate.Candidate.Id, CandidateStatus.PendingAnalysis, "test.queue", "test", null, "{}", null, null, Guid.NewGuid(), "ranking-test"));
            FounderScoutAnalysisClaim claim = await repository.ClaimPendingAnalysisAsync($"worker-{index}", TimeSpan.FromMinutes(1))
                ?? throw new InvalidOperationException("Expected a pending analysis claim.");
            _ = await repository.FinalizeAnalysisClaimAsync(new(
                claim.Candidate.Id,
                claim.WorkerId,
                CandidateStatus.Analyzed,
                null,
                50m + index,
                50m,
                0.8m,
                50m,
                0m,
                index,
                "analysis.completed",
                Guid.NewGuid(),
                "ranking-test"));
        }

        CandidatePage first = await repository.QueryRankedAsync(new([CandidateStatus.Analyzed], null, 0, 5));
        CandidatePage second = await repository.QueryRankedAsync(new([CandidateStatus.Analyzed], null, 5, 5));

        Assert.Multiple(() =>
        {
            Assert.That(first.TotalCount, Is.EqualTo(12));
            Assert.That(first.Items, Has.Count.EqualTo(5));
            Assert.That(second.Items, Has.Count.EqualTo(5));
            Assert.That(first.Items.Select(item => item.Id), Is.Not.EquivalentTo(second.Items.Select(item => item.Id)));
            Assert.That(first.Items[0].InvitationPriority, Is.EqualTo(11m));
        });
    }

    [Test]
    public async Task ReinitializationPreservesCompatibleExistingFounderData()
    {
        await using TemporaryFounderScoutDatabase fixture = await TemporaryFounderScoutDatabase.CreateAsync();
        FounderScoutRepository repository = fixture.CreateRepository();
        ResolveCandidateResult before = await ResolveAsync(repository, "upgrade-candidate", "Upgrade Candidate");

        FounderScoutDatabase reopened = await FounderScoutDatabaseInitializer.InitializeAsync(
            new FounderScoutDatabaseSettings(fixture.Root),
            TimeProvider.System);
        var afterRepository = new FounderScoutRepository(reopened.ContextFactory, TimeProvider.System);
        Candidate? after = await ((ICandidateRepository)afterRepository).GetAsync(before.Candidate.Id);

        Assert.That(after?.DisplayName, Is.EqualTo("Upgrade Candidate"));
    }

    private static async ValueTask<ResolveCandidateResult> ResolveAsync(
        FounderScoutRepository repository,
        string sourceKey,
        string displayName)
    {
        string normalized = FounderScoutIdentityNormalizer.NormalizeSourceKey(sourceKey);
        return await repository.ResolveAsync(new(
            Guid.NewGuid(),
            normalized,
            $"https://example.invalid/profile/{normalized}",
            displayName,
            BaselineUtc,
            [new(
                CandidateIdentityAliasType.SourceKey,
                FounderScoutIdentityNormalizer.Hash("source-key", normalized),
                "fixture-account",
                1m,
                IsStrong: true)],
            Guid.NewGuid(),
            "persistence-test"));
    }

    private static ValueTask<AddProfileSnapshotResult> AddSnapshotAsync(
        FounderScoutRepository repository,
        Guid candidateId,
        string sourceKey,
        char hashCharacter) => repository.AddIfNewAsync(new(
            Guid.NewGuid(),
            candidateId,
            "fixture-account",
            "fixture-segment",
            sourceKey,
            $"https://example.invalid/profile/{sourceKey}",
            new string(hashCharacter, 64),
            "fixture-envelope-1.0",
            "fixture-1.0",
            BaselineUtc,
            $"snapshots/test/{sourceKey}.capture.json",
            BaselineUtc.AddDays(30),
            null,
            1m,
            1m,
            ProfileSnapshotStatus.Captured,
            Guid.NewGuid(),
            "persistence-test"));

    private static Evaluation CreateEvaluation(Guid id, Guid candidateId, Guid snapshotId) => new(
        id,
        candidateId,
        snapshotId,
        "scorecard-1",
        "fixture",
        "fixture-model",
        "fixture-deployment",
        "prompt-1",
        new string('e', 64),
        50m,
        60m,
        0.8m,
        40m,
        55m,
        5m,
        50m,
        60m,
        "Monitor",
        "{}",
        null,
        EvaluationStatus.Pending,
        null,
        0,
        null,
        BaselineUtc,
        BaselineUtc,
        1);

    private static async ValueTask<string[]> ReadNamesAsync(FounderScoutDbContext context, string type)
    {
        await context.Database.OpenConnectionAsync();
        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type ORDER BY name";
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "$type";
        parameter.Value = type;
        command.Parameters.Add(parameter);
        var values = new List<string>();
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values.ToArray();
    }

    private static async ValueTask<string[]> ReadCandidateColumnsAsync(FounderScoutDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA table_info('Candidates')";
        var values = new List<string>();
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(1));
        }

        return values.ToArray();
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        private DateTimeOffset nowUtc = nowUtc;

        public override DateTimeOffset GetUtcNow() => nowUtc;

        public void Advance(TimeSpan duration) => nowUtc = nowUtc.Add(duration);
    }
}
