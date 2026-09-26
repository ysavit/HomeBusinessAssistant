using System.Diagnostics;
using System.Text.Json;
using FounderScout.Application;
using FounderScout.Domain;
using FounderScout.Infrastructure.Files;
using FounderScout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FounderScout.Tests;

internal sealed class FounderScoutResultsTests
{
    private static readonly string[] AllReportNames = ["top-candidates.html", "top-candidates.md", "candidates.csv", "candidates.json", "discovery-summary.md", "manual-invitation-queue.md"];

    [Test]
    public async Task TenThousandCandidateQueryRemainsPagedStableAndBounded()
    {
        await using TemporaryFounderScoutDatabase fixture = await TemporaryFounderScoutDatabase.CreateAsync();
        await SeedCandidatesAsync(fixture.Database.ContextFactory, 10_000);
        var service = new FounderScoutResultsService(fixture.Database.ContextFactory, fixture.Root, TimeProvider.System);
        var timer = Stopwatch.StartNew();

        FounderScoutCandidateResultPage page = await service.QueryCandidatesAsync(FounderScoutUiQuery(offset: 5_000, pageSize: 50));

        timer.Stop();
        Assert.Multiple(() =>
        {
            Assert.That(page.TotalCount, Is.EqualTo(10_000));
            Assert.That(page.Items, Has.Count.EqualTo(50));
            Assert.That(page.Items.Select(item => item.Rank), Is.Ordered.Ascending);
            Assert.That(page.Items.Select(item => item.CandidateId), Is.Unique);
            Assert.That(page.Items[0].Rank, Is.EqualTo(5_001));
            Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        });
    }

    [Test]
    public async Task AllReportsShareCanonicalOrderAndEncodeHostileContent()
    {
        await using TemporaryFounderScoutDatabase fixture = await TemporaryFounderScoutDatabase.CreateAsync();
        await SeedCandidatesAsync(fixture.Database.ContextFactory, 3, hostileFirst: true);
        var service = new FounderScoutResultsService(fixture.Database.ContextFactory, fixture.Root, TimeProvider.System);
        FounderScoutReportModel model = await service.BuildReportModelAsync(30, DateTimeOffset.UtcNow, .5m);
        string runnerDirectory = Path.Combine(fixture.Root, "runner-artifacts");
        var store = new FounderScoutReportStore(fixture.Root, fixture.Database.ReportsDirectory, runnerDirectory);

        IReadOnlyList<FounderScoutReportFile> files = await store.WriteAsync(Guid.NewGuid(), "all", model);
        string html = await File.ReadAllTextAsync(Path.Combine(runnerDirectory, "top-candidates.html"));
        string markdown = await File.ReadAllTextAsync(Path.Combine(runnerDirectory, "top-candidates.md"));
        string csv = await File.ReadAllTextAsync(Path.Combine(runnerDirectory, "candidates.csv"));
        string json = await File.ReadAllTextAsync(Path.Combine(runnerDirectory, "candidates.json"));
        using JsonDocument document = JsonDocument.Parse(json);
        string[] jsonOrder = document.RootElement.GetProperty("candidates").EnumerateArray().Select(item => item.GetProperty("displayName").GetString()!).ToArray();
        string[] jsonIds = document.RootElement.GetProperty("candidates").EnumerateArray().Select(item => item.GetProperty("candidateId").GetGuid().ToString("D")).ToArray();
        string[] csvIds = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(line => ParseCsvTextField(line, 1)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(files.Select(file => file.RunnerRelativePath), Is.EquivalentTo(AllReportNames));
            Assert.That(files.All(file => file.FileHash.Length == 64 && file.FileSize > 0), Is.True);
            Assert.That(jsonOrder, Is.EqualTo(model.Candidates.Select(item => item.Candidate.DisplayName)));
            Assert.That(csvIds, Is.EqualTo(jsonIds));
            Assert.That(html, Does.Not.Contain("<script>alert('stage13')</script>"));
            Assert.That(html, Does.Contain("&lt;script&gt;alert(&#39;stage13&#39;)&lt;/script&gt;"));
            Assert.That(markdown, Does.Contain("&lt;script&gt;alert('stage13')&lt;/script&gt;"));
            Assert.That(csv, Does.Contain("\"'=SUM(1,1)\""));
            Assert.That(document.RootElement.GetProperty("schemaVersion").GetString(), Is.EqualTo("1.0"));
            Assert.That(json, Does.Not.Contain("rawArtifactRelativePath"));
            Assert.That(json, Does.Not.Contain("structuredEvaluationJson"));
        });
    }

    [Test]
    public async Task ReportLeaseHasOneOwnerAndUsesFencingTokens()
    {
        await using TemporaryFounderScoutDatabase fixture = await TemporaryFounderScoutDatabase.CreateAsync();
        var service = new FounderScoutResultsService(fixture.Database.ContextFactory, fixture.Root, TimeProvider.System);

        FounderScoutReportLease? first = await service.TryAcquireAsync("owner-one", TimeSpan.FromMinutes(1));
        FounderScoutReportLease? conflict = await service.TryAcquireAsync("owner-two", TimeSpan.FromMinutes(1));
        await service.ReleaseAsync(first!);
        FounderScoutReportLease? second = await service.TryAcquireAsync("owner-two", TimeSpan.FromMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(conflict, Is.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(second!.FencingToken, Is.GreaterThan(first!.FencingToken));
        });
    }

    [Test]
    public async Task DraftQueueOutcomeAndRawRetentionRemainExplicitAndAudited()
    {
        await using TemporaryFounderScoutDatabase fixture = await TemporaryFounderScoutDatabase.CreateAsync();
        FounderScoutRepository repository = fixture.CreateRepository();
        (Candidate first, InvitationDraft firstDraft, ProfileSnapshot firstSnapshot) = await SeedReviewCandidateAsync(fixture, repository, "review-one", "Review One", 91m);
        (Candidate second, _, _) = await SeedReviewCandidateAsync(fixture, repository, "review-two", "Review Two", 82m);
        (Candidate third, _, _) = await SeedReviewCandidateAsync(fixture, repository, "review-three", "Review Three", 73m);
        var service = new FounderScoutResultsService(fixture.Database.ContextFactory, fixture.Root, TimeProvider.System);
        ManualInvitationWindow window = await service.CreateInvitationWindowAsync(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(2), 2, 1, "Explicit test window");

        InvitationDraftRevision revision = await service.SaveDraftRevisionAsync(new(firstDraft.Id, "Edited short draft", "Edited detailed draft with grounded facts.", "test-user", 2_000, .9m));
        InvitationQueueEntry firstEntry = await service.AddToQueueAsync(new(window.Id, first.Id, InvitationQueueKind.Primary, false, "test-user"), .5m, 120);
        InvitationQueueEntry secondEntry = await service.AddToQueueAsync(new(window.Id, second.Id, InvitationQueueKind.Primary, false, "test-user"), .5m, 120);
        InvalidOperationException duplicate = Assert.ThrowsAsync<InvalidOperationException>(async () => await service.AddToQueueAsync(new(window.Id, first.Id, InvitationQueueKind.Primary, false, "test-user"), .5m, 120))!;
        InvalidOperationException capacity = Assert.ThrowsAsync<InvalidOperationException>(async () => await service.AddToQueueAsync(new(window.Id, third.Id, InvitationQueueKind.Primary, false, "test-user"), .5m, 120))!;
        await service.ReorderQueueAsync(new(window.Id, InvitationQueueKind.Primary, [secondEntry.Id, firstEntry.Id], "test-user"));
        _ = await service.MarkDraftReviewedAsync(firstDraft.Id, "test-user");
        FounderScoutCandidateDetail queued = await service.GetCandidateAsync(first.Id) ?? throw new InvalidOperationException("Expected queued candidate detail.");
        Candidate sent = await service.RecordOutcomeAsync(new(first.Id, CandidateStatus.ManuallySent, "test.manual-sent", "Sent outside the application.", firstDraft.Id, window.Id, queued.InvitationWindow?.Version, DateTimeOffset.UtcNow, "test-user", Guid.NewGuid().ToString("D")));
        Candidate accepted = await service.RecordOutcomeAsync(new(first.Id, CandidateStatus.Accepted, "test.accepted", null, firstDraft.Id, window.Id, null, DateTimeOffset.UtcNow, "test-user", Guid.NewGuid().ToString("D")));
        FounderScoutRetentionResult retention = await service.ApplyRawRetentionAsync(DateTimeOffset.UtcNow, first.Id, includeCurrentSnapshot: true, "test-user");
        FounderScoutCandidateDetail after = await service.GetCandidateAsync(first.Id) ?? throw new InvalidOperationException("Expected retained derived detail.");

        Assert.Multiple(() =>
        {
            Assert.That(revision.ValidationPassed, Is.True);
            Assert.That(after.Drafts.Single(draft => draft.Id == firstDraft.Id).ShortDraft, Is.EqualTo("Short review-one"));
            Assert.That(after.DraftRevisions.Any(item => item.Id == revision.Id && item.IsActive), Is.True);
            Assert.That(duplicate.Message, Is.EqualTo("invitation.queue.duplicate"));
            Assert.That(capacity.Message, Is.EqualTo("invitation.queue.capacity"));
            Assert.That(sent.Status, Is.EqualTo(CandidateStatus.ManuallySent));
            Assert.That(accepted.Status, Is.EqualTo(CandidateStatus.Accepted));
            Assert.That(retention.Deleted, Is.EqualTo(1));
            Assert.That(after.Snapshots.Single(item => item.Snapshot.Id == firstSnapshot.Id).RawArtifactAvailable, Is.False);
            Assert.That(after.Evaluations, Is.Not.Empty);
            Assert.That(after.Timeline.Any(action => action.ActionType == CandidateActionType.RawProfileDeleted), Is.True);
            Assert.That(after.Timeline.Any(action => action.ActionType == CandidateActionType.InvitationMarkedSent), Is.True);
        });
    }

    private static FounderScoutCandidateQuery FounderScoutUiQuery(int offset, int pageSize) => new(
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
        NeedsManualReview: null,
        Sort: FounderScoutCandidateSort.PriorityDescending,
        offset,
        pageSize);

    private static async Task SeedCandidatesAsync(FounderScoutDbContextFactory factory, int count, bool hostileFirst = false)
    {
        string firstName = hostileFirst ? "<script>alert('stage13')</script>" : "Candidate 00001";
        string secondName = hostileFirst ? "=SUM(1,1)" : "Candidate 00002";
        await using FounderScoutDbContext context = await factory.CreateDbContextAsync();
        string sql = $"""
            WITH RECURSIVE sequence(value) AS (
                SELECT 1
                UNION ALL
                SELECT value + 1 FROM sequence WHERE value < {count}
            )
            INSERT INTO Candidates (
                Id, CurrentSourceProfileKey, CanonicalSourceUrl, DisplayName, NormalizedLocation,
                TechnicalStatus, CommitmentStatus, IdeaCommitmentStatus, Status,
                FirstSeenAtUtc, LastSeenAtUtc, LastActivityText, LastActivityAtUtc,
                CurrentSnapshotId, LatestEvaluationId, FounderQualityScore, OurFitScore,
                Confidence, ActivityScore, RiskPenalty, InvitationPriority,
                AnalysisWorkerId, AnalysisClaimedAtUtc, AnalysisClaimExpiresAtUtc,
                AnalysisAttemptCount, LastAnalysisErrorCode, CreatedAtUtc, UpdatedAtUtc, Version,
                MergedIntoCandidateId)
            SELECT
                printf('%08x-0000-4000-8000-%012x', value, value),
                'candidate-' || printf('%05d', value),
                'https://example.invalid/profile/' || value,
                CASE value WHEN 1 THEN {SqlLiteral(firstName)} WHEN 2 THEN {SqlLiteral(secondName)} ELSE 'Candidate ' || printf('%05d', value) END,
                NULL, 'Unknown', 'Unknown', 'Unknown', 'Analyzed',
                '2026-08-31 12:00:00+00:00', '2026-08-31 12:00:00+00:00', NULL, NULL,
                NULL, NULL, CAST(value % 100 AS TEXT), CAST((value * 3) % 100 AS TEXT),
                '0.8', CAST((value * 7) % 100 AS TEXT), '0', CAST(value % 100 AS TEXT),
                NULL, NULL, NULL, 0, NULL,
                '2026-08-31 12:00:00+00:00', '2026-08-31 12:00:00+00:00', 1, NULL
            FROM sequence;
            """;
        _ = await context.Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task<(Candidate Candidate, InvitationDraft Draft, ProfileSnapshot Snapshot)> SeedReviewCandidateAsync(
        TemporaryFounderScoutDatabase fixture,
        FounderScoutRepository repository,
        string sourceKey,
        string displayName,
        decimal priority)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string normalized = FounderScoutIdentityNormalizer.NormalizeSourceKey(sourceKey);
        ResolveCandidateResult resolved = await repository.ResolveAsync(new(
            Guid.NewGuid(), normalized, $"https://example.invalid/profile/{normalized}", displayName, now,
            [new(CandidateIdentityAliasType.SourceKey, FounderScoutIdentityNormalizer.Hash("source-key", normalized), "fixture-account", 1m, true)],
            Guid.NewGuid(), "results-test"));
        string relativePath = $"snapshots/test/{normalized}.capture.json";
        string fullPath = Path.Combine(fixture.Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "{\"safe\":true}");
        AddProfileSnapshotResult added = await repository.AddIfNewAsync(new(
            Guid.NewGuid(), resolved.Candidate.Id, "fixture-account", "fixture-segment", normalized,
            resolved.Candidate.CanonicalSourceUrl, new string('a', 64), "parser-1", "adapter-1", now,
            relativePath, now.AddMinutes(-1), null, 1m, 1m, ProfileSnapshotStatus.AnalysisCompleted,
            Guid.NewGuid(), "results-test"));
        Guid evaluationId = Guid.NewGuid();
        var evaluation = new Evaluation(
            evaluationId, resolved.Candidate.Id, added.Snapshot.Id, "scorecard-1", "fixture", "model-1", "deployment-1", "prompt-1", new string('e', 64),
            80m, 75m, .9m, 70m, 78m, 3m, 75m, priority, "StrongConnect", "{}", null, EvaluationStatus.Pending, null, 0, null, now, now, 1);
        _ = await repository.AddAsync(new(evaluation,
            [new(Guid.NewGuid(), evaluationId, "execution", 16m, 20m, "{}", "Grounded execution evidence.", now)],
            [new(Guid.NewGuid(), evaluationId, "scope", 3m, "{}", "Bounded scope risk.", now)]));
        _ = await repository.TransitionAsync(evaluationId, EvaluationStatus.Claimed, "evaluation.claimed");
        _ = await repository.TransitionAsync(evaluationId, EvaluationStatus.Completed, "evaluation.completed");
        _ = await repository.TransitionAsync(new(resolved.Candidate.Id, CandidateStatus.PendingAnalysis, "test.pending", "test-user", null, "{}", null, evaluationId, Guid.NewGuid(), "results-test"));
        FounderScoutAnalysisClaim claim = await repository.ClaimPendingAnalysisAsync($"worker-{sourceKey}", TimeSpan.FromMinutes(2)) ?? throw new InvalidOperationException("Expected analysis claim.");
        _ = await repository.FinalizeAnalysisClaimAsync(new(resolved.Candidate.Id, claim.WorkerId, CandidateStatus.Shortlisted, evaluationId, 80m, 75m, .9m, 70m, 3m, priority, "analysis.completed", Guid.NewGuid(), "results-test"));
        InvitationDraft draft = await repository.CreateAndSupersedePreviousAsync(new(
            Guid.NewGuid(), evaluationId, resolved.Candidate.Id, $"Short {sourceKey}", $"Detailed {sourceKey}", "{}", .9m,
            InvitationDraftStatus.Draft, "[]", new string('d', 64), now, null, null, false, 1));
        draft = await repository.TransitionAsync(draft.Id, InvitationDraftStatus.Valid, "draft.valid", "test-user");
        Candidate candidate = await ((ICandidateRepository)repository).GetAsync(resolved.Candidate.Id) ?? throw new InvalidOperationException("Expected review candidate.");
        return (candidate, draft, added.Snapshot);
    }

    private static string SqlLiteral(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static string ParseCsvTextField(string line, int fieldIndex)
    {
        var fields = new List<string>();
        var builder = new System.Text.StringBuilder();
        bool quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            char current = line[index];
            if (current == '"' && quoted && index + 1 < line.Length && line[index + 1] == '"') { _ = builder.Append('"'); index++; }
            else if (current == '"') quoted = !quoted;
            else if (current == ',' && !quoted) { fields.Add(builder.ToString()); builder.Clear(); }
            else _ = builder.Append(current);
        }
        fields.Add(builder.ToString());
        return fields[fieldIndex];
    }
}
