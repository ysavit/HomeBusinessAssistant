using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using FounderScout.Application;
using FounderScout.Domain;
using FounderScout.Infrastructure.Persistence;
using HomeBusinessAssistant.AgentSdk.Execution;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Operations;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Operations;
using HomeBusinessAssistant.Infrastructure.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;
using HomeBusinessAssistant.Runner;
using Microsoft.EntityFrameworkCore;

namespace FounderScout.Tests;

internal sealed class FounderScoutRunnerIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] RelevantChangeRoles = ["operations", "domain"];
    private static readonly string[] RelevantChangeTraction = ["Completed 5 customer interviews.", "Started two design partnerships."];
    private static readonly string[] RelevantChangeIndustries = ["local commerce", "operations"];
    private static readonly string[] DeepEvaluationRoles = ["operations", "sales", "domain"];
    private static readonly string[] DeepEvaluationIndustries = ["local commerce", "operations"];
    private static readonly string[] Stage16Traction = ["Completed 50 customer interviews."];

    [Test]
    public async Task RequiredFixtureSmokeRunsThroughRunnerAndCorrelatesSeparateDatabases()
    {
        await using FounderScoutRunnerFixture fixture = await FounderScoutRunnerFixture.CreateAsync();

        RunnerExecution imported = await fixture.ExecuteAsync("import", JsonSerializer.Serialize(new { inputPath = fixture.FixturePath }, JsonOptions));
        RunnerExecution screened = await fixture.ExecuteAsync("analyze", JsonSerializer.Serialize(new { phase = "screen", max = 20 }, JsonOptions));
        RunnerExecution repeatedScreen = await fixture.ExecuteAsync("analyze", JsonSerializer.Serialize(new { phase = "screen", max = 20 }, JsonOptions));
        FounderScoutDomainCounts beforeChange = await fixture.CreateFounderRepository().GetCountsAsync();
        string changedFixturePath = await fixture.WriteRelevantChangeFixtureAsync();
        RunnerExecution changedImport = await fixture.ExecuteAsync("import", JsonSerializer.Serialize(new { inputPath = changedFixturePath }, JsonOptions));
        RunnerExecution changedScreen = await fixture.ExecuteAsync("analyze", JsonSerializer.Serialize(new { phase = "screen", max = 20 }, JsonOptions));
        RunnerExecution diagnosed = await fixture.ExecuteAsync("diagnose", "{}");
        RunnerExecution reported = await fixture.ExecuteAsync("report", "{}");
        FounderScoutRepository repository = fixture.CreateFounderRepository();
        FounderScoutDomainCounts counts = await repository.GetCountsAsync();
        CandidatePage candidates = await repository.QueryRankedAsync(new(null, null, 0, 100));
        CandidateAction[] actions = (await Task.WhenAll(
            candidates.Items.Select(candidate => repository.ListAsync(candidate.Id, 100).AsTask())))
            .SelectMany(candidateActions => candidateActions)
            .ToArray();
        int importedEventCount = await fixture.GetCentralEventCountAsync(imported.Run.Id);
        int reportArtifactCount = await fixture.GetCentralArtifactCountAsync(reported.Run.Id);
        int aliases = await fixture.GetFounderCountAsync("CandidateIdentityAliases");
        int conflicts = await fixture.GetFounderCountAsync("CandidateIdentityConflicts");
        int reasonMetrics = await fixture.GetCentralMetricCountAsync(screened.Run.Id, "processing.reason");
        int processingCheckpoints = await fixture.GetCentralEventTypeCountAsync(screened.Run.Id, "checkpoint");
        bool protectedMarkerPersisted = await fixture.FounderColumnContainsAsync("ScreeningDecisions", "EvaluatorInputJson", "PROTECTED_AGE_MARKER");

        Assert.Multiple(() =>
        {
            Assert.That(imported.ExitCode, Is.Zero, imported.Diagnostic);
            Assert.That(screened.ExitCode, Is.Zero, screened.Diagnostic);
            Assert.That(repeatedScreen.ExitCode, Is.Zero, repeatedScreen.Diagnostic);
            Assert.That(changedImport.ExitCode, Is.Zero, changedImport.Diagnostic);
            Assert.That(changedScreen.ExitCode, Is.Zero, changedScreen.Diagnostic);
            Assert.That(diagnosed.ExitCode, Is.Zero, diagnosed.Diagnostic);
            Assert.That(reported.ExitCode, Is.Zero, reported.Diagnostic);
            Assert.That(new[] { imported, screened, repeatedScreen, changedImport, changedScreen, diagnosed, reported },
                Has.All.Matches<RunnerExecution>(result => result.Run.Status == AgentRunStatus.Completed));
            Assert.That(imported.Run.SummaryJson, Does.Contain("\"capturesRead\":20"));
            Assert.That(imported.Run.SummaryJson, Does.Contain("\"snapshotsCreated\":19"));
            Assert.That(imported.Run.SummaryJson, Does.Contain("\"duplicateSnapshots\":1"));
            Assert.That(screened.Run.SummaryJson, Does.Contain("\"claimed\":19"));
            Assert.That(screened.Run.SummaryJson, Does.Contain("\"completed\":19"));
            Assert.That(screened.Run.SummaryJson, Does.Contain("\"noAiCalls\":true"));
            Assert.That(repeatedScreen.Run.SummaryJson, Does.Contain("\"result\":\"NoWork\""));
            Assert.That(changedImport.Run.SummaryJson, Does.Contain("\"snapshotsCreated\":1"));
            Assert.That(changedScreen.Run.SummaryJson, Does.Contain("\"claimed\":1"));
            Assert.That(changedScreen.Run.SummaryJson, Does.Contain("\"changed\":1"));
            Assert.That(diagnosed.Run.SummaryJson, Does.Contain("\"result\":\"Healthy\""));
            Assert.That(reported.Run.SummaryJson, Does.Contain("\"reportType\":\"all\""));
            Assert.That(reported.Run.SummaryJson, Does.Contain("\"schemaVersion\":\"1.0\""));
            Assert.That(reported.Run.SummaryJson, Does.Contain("\"candidates\":18"));
            Assert.That(beforeChange.Candidates, Is.EqualTo(18));
            Assert.That(beforeChange.Snapshots, Is.EqualTo(19));
            Assert.That(beforeChange.ScreeningDecisions, Is.EqualTo(19));
            Assert.That(counts.Candidates, Is.EqualTo(18));
            Assert.That(counts.Snapshots, Is.EqualTo(20));
            Assert.That(counts.ScreeningDecisions, Is.EqualTo(20));
            Assert.That(aliases, Is.EqualTo(54));
            Assert.That(conflicts, Is.Zero);
            Assert.That(actions.Count, Is.EqualTo(139));
            Assert.That(reasonMetrics, Is.GreaterThan(0));
            Assert.That(processingCheckpoints, Is.GreaterThan(0));
            Assert.That(protectedMarkerPersisted, Is.False);
            Assert.That(actions.Any(action => action.RelatedRunId == imported.Run.Id.Value), Is.True);
            Assert.That(importedEventCount, Is.GreaterThan(0));
            Assert.That(reportArtifactCount, Is.EqualTo(6));
            Assert.That(File.Exists(Path.Combine(fixture.FounderDataDirectory, "founders.db")), Is.True);
            Assert.That(File.Exists(Path.Combine(fixture.DataDirectory, "assistant.db")), Is.True);
        });
    }

    [Test]
    public async Task RequiredDeepEvaluationSmokeRunsTwentySyntheticCandidatesThroughRunner()
    {
        await using FounderScoutRunnerFixture fixture = await FounderScoutRunnerFixture.CreateAsync(enableDeepEvaluation: true);

        RunnerExecution imported = await fixture.ExecuteAsync("import", JsonSerializer.Serialize(new { inputPath = fixture.FixturePath }, JsonOptions));
        RunnerExecution screened = await fixture.ExecuteAsync("analyze", JsonSerializer.Serialize(new { phase = "screen", max = 20 }, JsonOptions));
        RunnerExecution analyzed = await fixture.ExecuteAsync("analyze", JsonSerializer.Serialize(new { phase = "deep", max = 20 }, JsonOptions));
        FounderScoutRepository repository = fixture.CreateFounderRepository();
        CandidatePage firstPage = await repository.QueryRankedAsync(new(null, null, 0, 100));
        int firstEvaluationCount = await fixture.GetFounderCountAsync("Evaluations");
        int firstDraftCount = await fixture.GetFounderCountAsync("InvitationDrafts");
        IReadOnlyList<InvitationDraft> firstDrafts = await repository.ListRecentActiveAsync(100);

        Candidate cacheCandidate = firstPage.Items.First(candidate => candidate.LatestEvaluationId.HasValue);
        _ = await repository.TransitionAsync(new(
            cacheCandidate.Id,
            CandidateStatus.PendingAnalysis,
            "test.cache.requeue",
            "stage12-smoke",
            "Explicitly requeue one unchanged candidate to exercise the durable cache.",
            "{}",
            null,
            cacheCandidate.LatestEvaluationId,
            analyzed.Run.Id.Value,
            "stage12-cache"));
        RunnerExecution cached = await fixture.ExecuteAsync("analyze", JsonSerializer.Serialize(new { phase = "deep", max = 1 }, JsonOptions));
        int cachedEvaluationCount = await fixture.GetFounderCountAsync("Evaluations");

        await fixture.UpdatePersonaRevisionAsync();
        RunnerExecution reevaluated = await fixture.ExecuteAsync("analyze", JsonSerializer.Serialize(new { phase = "deep", max = 20 }, JsonOptions));
        CandidatePage finalPage = await repository.QueryRankedAsync(new(null, null, 0, 100));
        int finalEvaluationCount = await fixture.GetFounderCountAsync("Evaluations");
        IReadOnlyList<InvitationDraft> finalDrafts = await repository.ListRecentActiveAsync(100);
        decimal? providerRequests = await fixture.GetCentralMetricAsync(analyzed.Run.Id, "analysis.providerRequests");
        decimal? providerRetries = await fixture.GetCentralMetricAsync(analyzed.Run.Id, "analysis.providerRetries");
        decimal? cacheHits = await fixture.GetCentralMetricAsync(cached.Run.Id, "analysis.cacheHit");
        decimal? reevaluationQueued = await fixture.GetCentralMetricAsync(reevaluated.Run.Id, "analysis.reevaluationQueued");

        Assert.Multiple(() =>
        {
            Assert.That(imported.ExitCode, Is.Zero, imported.Diagnostic);
            Assert.That(screened.ExitCode, Is.Zero, screened.Diagnostic);
            Assert.That(analyzed.ExitCode, Is.Zero, analyzed.Diagnostic);
            Assert.That(cached.ExitCode, Is.Zero, cached.Diagnostic);
            Assert.That(reevaluated.ExitCode, Is.Zero, reevaluated.Diagnostic);
            Assert.That(screened.Run.SummaryJson, Does.Contain("\"claimed\":20"));
            Assert.That(analyzed.Run.SummaryJson, Does.Contain("\"claimed\":20"));
            Assert.That(analyzed.Run.SummaryJson, Does.Contain("\"completed\":20"));
            Assert.That(analyzed.Run.SummaryJson, Does.Contain("\"invitationsSent\":0"));
            Assert.That(firstEvaluationCount, Is.EqualTo(20));
            Assert.That(firstDraftCount, Is.EqualTo(20));
            Assert.That(firstPage.Items, Has.Count.EqualTo(20));
            Assert.That(firstPage.Items.Select(candidate => candidate.Status), Does.Contain(CandidateStatus.Shortlisted));
            Assert.That(firstPage.Items.Select(candidate => candidate.Status), Does.Contain(CandidateStatus.Monitor));
            Assert.That(firstPage.Items.Select(candidate => candidate.Status), Does.Contain(CandidateStatus.Passed));
            Assert.That(firstPage.Items.Select(candidate => candidate.Status), Does.Contain(CandidateStatus.ManualReview));
            Assert.That(firstPage.Items.Count(candidate => candidate.FounderQualityScore.HasValue), Is.EqualTo(19));
            Assert.That(firstPage.Items.Where(candidate => candidate.FounderQualityScore.HasValue), Has.All.Matches<Candidate>(candidate => candidate.FounderQualityScore is >= 0 and <= 100));
            Assert.That(firstPage.Items.Where(candidate => candidate.OurFitScore.HasValue), Has.All.Matches<Candidate>(candidate => candidate.OurFitScore is >= 0 and <= 100));
            Assert.That(firstPage.Items.Where(candidate => candidate.InvitationPriority.HasValue), Has.All.Matches<Candidate>(candidate => candidate.InvitationPriority is >= 0 and <= 100));
            Assert.That(providerRequests, Is.EqualTo(24m));
            Assert.That(providerRetries, Is.EqualTo(4m));
            Assert.That(cacheHits, Is.EqualTo(1m));
            Assert.That(cachedEvaluationCount, Is.EqualTo(firstEvaluationCount));
            Assert.That(reevaluationQueued, Is.EqualTo(20m));
            Assert.That(finalEvaluationCount, Is.EqualTo(40));
            Assert.That(finalPage.Items, Has.Count.EqualTo(20));
            Assert.That(firstDrafts.Concat(finalDrafts), Has.All.Matches<InvitationDraft>(draft => !draft.ShortDraft.Contains("PROTECTED_", StringComparison.Ordinal) && !draft.DetailedDraft.Contains("PROTECTED_", StringComparison.Ordinal)));
            Assert.That(finalDrafts.Where(draft => draft.Status == InvitationDraftStatus.Valid), Has.All.Matches<InvitationDraft>(draft => !draft.ShortDraft.Contains("automation", StringComparison.OrdinalIgnoreCase) && !draft.DetailedDraft.Contains("scraping", StringComparison.OrdinalIgnoreCase)));
        });
    }

    [Test]
    [Category("Stage16E2E")]
    public async Task Stage16FortyProfileWorkflowRunsThroughRealRunnerAndAgentProcesses()
    {
        await using StartupSchoolFixtureServer server = StartupSchoolFixtureServer.Start(profileCount: 45);
        await using FounderScoutRunnerFixture fixture = await FounderScoutRunnerFixture.CreateAsync(
            server.CreateOptions(),
            enableDeepEvaluation: true,
            discoveryLimit: 40,
            useUnicodeRoot: true);

        RunnerExecution discovered = await fixture.ExecuteAsRunnerProcessAsync(
            "discover",
            JsonSerializer.Serialize(new { accountId = "fixture-account", segmentId = "default" }, JsonOptions));
        RunnerExecution repeated = await fixture.ExecuteAsRunnerProcessAsync(
            "discover",
            JsonSerializer.Serialize(new { accountId = "fixture-account", segmentId = "repeat" }, JsonOptions));
        string changedPath = await fixture.WriteStage16ChangedFixtureAsync(server.BaseUrl);
        RunnerExecution changed = await fixture.ExecuteAsRunnerProcessAsync(
            "import",
            JsonSerializer.Serialize(new { inputPath = changedPath }, JsonOptions));
        RunnerExecution screened = await fixture.ExecuteAsRunnerProcessAsync(
            "analyze",
            JsonSerializer.Serialize(new { phase = "screen", max = 50 }, JsonOptions));
        RunnerExecution analyzedFirst = await fixture.ExecuteAsRunnerProcessAsync(
            "analyze",
            JsonSerializer.Serialize(new { phase = "deep", max = 20 }, JsonOptions));
        RunnerExecution analyzedSecond = await fixture.ExecuteAsRunnerProcessAsync(
            "analyze",
            JsonSerializer.Serialize(new { phase = "deep", max = 20 }, JsonOptions));
        RunnerExecution reported = await fixture.ExecuteAsRunnerProcessAsync("report", "{}");

        FounderScoutRepository repository = fixture.CreateFounderRepository();
        FounderScoutDomainCounts counts = await repository.GetCountsAsync();
        CandidatePage ranked = await repository.QueryRankedAsync(new(null, null, 0, 100));
        IReadOnlyList<InvitationDraft> drafts = await repository.ListRecentActiveAsync(100);
        var results = new FounderScoutResultsService(
            fixture.FounderDatabase.ContextFactory,
            fixture.FounderDataDirectory,
            TimeProvider.System);
        ManualInvitationWindow window = await results.CreateInvitationWindowAsync(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1),
            2,
            2,
            "Stage 16 synthetic manual review window");
        Candidate selected = ranked.Items.First(item => drafts.Any(draft =>
            draft.CandidateId == item.Id && draft.Status == InvitationDraftStatus.Valid));
        InvitationDraft selectedDraft = drafts.First(draft =>
            draft.CandidateId == selected.Id && draft.Status == InvitationDraftStatus.Valid);
        _ = await results.AddToQueueAsync(new(
            window.Id,
            selected.Id,
            InvitationQueueKind.Primary,
            false,
            "stage16-e2e"), 0.1m, 3_650);
        _ = await results.MarkDraftReviewedAsync(selectedDraft.Id, "stage16-e2e");
        FounderScoutCandidateDetail queued = await results.GetCandidateAsync(selected.Id)
            ?? throw new InvalidOperationException("The Stage 16 candidate detail was not available.");
        Candidate sent = await results.RecordOutcomeAsync(new(
            selected.Id,
            CandidateStatus.ManuallySent,
            "stage16.manual-sent",
            "Recorded after an explicit synthetic user action.",
            selectedDraft.Id,
            window.Id,
            queued.InvitationWindow?.Version,
            DateTimeOffset.UtcNow,
            "stage16-e2e",
            Guid.NewGuid().ToString("D")));
        Candidate accepted = await results.RecordOutcomeAsync(new(
            selected.Id,
            CandidateStatus.Accepted,
            "stage16.accepted",
            "Synthetic outcome after the manual-send record.",
            selectedDraft.Id,
            window.Id,
            null,
            DateTimeOffset.UtcNow,
            "stage16-e2e",
            Guid.NewGuid().ToString("D")));

        var operations = new OperationalService(
            fixture.CentralDatabase.ContextFactory,
            fixture.DataDirectory,
            fixture.CentralDatabase.DatabasePath,
            TimeProvider.System,
            OperationalPolicy.Default with
            {
                LowDiskThresholdBytes = 0,
                QuietHoursStartLocal = new(23, 0),
                QuietHoursEndLocal = new(23, 1),
            },
            new Stage16SignalSource(ranked.Items.Count));
        AttentionScanResult scan = await operations.DetectAsync();
        DailySummary summary = await operations.GenerateDailySummaryAsync(
            DateOnly.FromDateTime(DateTime.UtcNow),
            "UTC");
        IReadOnlyList<LocalNotification> notifications = await operations.GetNotificationsAsync();

        int evaluationCount = await fixture.GetFounderCountAsync("Evaluations");
        int draftCount = await fixture.GetFounderCountAsync("InvitationDrafts");
        int reportArtifacts = await fixture.GetCentralArtifactCountAsync(reported.Run.Id);
        bool hashesValid = await fixture.AllStage16HashesAreSha256Async();

        Assert.Multiple(() =>
        {
            Assert.That(fixture.RootPath, Does.Contain(" "));
            Assert.That(fixture.RootPath, Does.Contain("Ω"));
            Assert.That(new[] { discovered, repeated, changed, screened, analyzedFirst, analyzedSecond, reported },
                Has.All.Matches<RunnerExecution>(item => item.ExitCode == 0 && item.Run.Status == AgentRunStatus.Completed));
            Assert.That(discovered.Run.SummaryJson, Does.Contain("\"newCandidates\":40"));
            Assert.That(repeated.Run.SummaryJson, Does.Contain("\"knownUnchangedProfiles\":8"));
            Assert.That(changed.Run.SummaryJson, Does.Contain("\"snapshotsCreated\":1"));
            Assert.That(screened.Run.SummaryJson, Does.Contain("\"noAiCalls\":true"));
            Assert.That(analyzedFirst.Run.SummaryJson, Does.Contain("\"invitationsSent\":0"));
            Assert.That(analyzedSecond.Run.SummaryJson, Does.Contain("\"invitationsSent\":0"));
            Assert.That(counts.Candidates, Is.EqualTo(40));
            Assert.That(counts.Snapshots, Is.EqualTo(41));
            Assert.That(evaluationCount, Is.EqualTo(40));
            Assert.That(draftCount, Is.EqualTo(40));
            Assert.That(reportArtifacts, Is.EqualTo(6));
            Assert.That(sent.Status, Is.EqualTo(CandidateStatus.ManuallySent));
            Assert.That(accepted.Status, Is.EqualTo(CandidateStatus.Accepted));
            Assert.That(scan.NotificationsQueued, Is.GreaterThanOrEqualTo(1));
            Assert.That(notifications, Is.Not.Empty);
            Assert.That(summary.SourceHash, Has.Length.EqualTo(64));
            Assert.That(hashesValid, Is.True);
        });
    }

    [Test]
    public async Task BrowserDiscoverySmokeRunsTwentyProfilesResumesKnownProfilesAndReleasesProfileLease()
    {
        await using StartupSchoolFixtureServer server = StartupSchoolFixtureServer.Start();
        await using FounderScoutRunnerFixture fixture = await FounderScoutRunnerFixture.CreateAsync(server.CreateOptions());

        RunnerExecution discovered = await fixture.ExecuteAsync("discover", JsonSerializer.Serialize(new { accountId = "fixture-account", segmentId = "default" }, JsonOptions));
        RunnerExecution repeated = await fixture.ExecuteAsync("discover", JsonSerializer.Serialize(new { accountId = "fixture-account", segmentId = "repeat" }, JsonOptions));
        RunnerExecution screened = await fixture.ExecuteAsync("analyze", JsonSerializer.Serialize(new { phase = "screen", max = 20 }, JsonOptions));
        FounderScoutRepository repository = fixture.CreateFounderRepository();
        FounderScoutDomainCounts counts = await repository.GetCountsAsync();
        CandidatePage candidatePage = await repository.QueryRankedAsync(new(null, null, 0, 100));
        ScreeningDecision[] decisions = (await Task.WhenAll(candidatePage.Items.Select(async candidate => await repository.GetLatestAsync(candidate.Id))))
            .Where(decision => decision is not null)
            .Cast<ScreeningDecision>()
            .ToArray();
        BrowserAccount? account = await ((IBrowserAccountRepository)repository).GetAsync("fixture-account");
        DiscoveryCheckpoint? firstCheckpoint = await repository.GetLatestAsync("fixture-account", "default");
        DiscoveryCheckpoint? repeatCheckpoint = await repository.GetLatestAsync("fixture-account", "repeat");
        decimal? viewedMetric = await fixture.GetCentralMetricAsync(discovered.Run.Id, "discovery.profiles.viewed");
        decimal? knownMetric = await fixture.GetCentralMetricAsync(repeated.Run.Id, "discovery.profiles.known_unchanged");
        string lockPath = Path.Combine(fixture.FounderDataDirectory, "browser", "fixture-account", ".hba-browser-profile.lock");
        await using var releasedLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        Assert.Multiple(() =>
        {
            Assert.That(discovered.ExitCode, Is.Zero, discovered.Diagnostic);
            Assert.That(repeated.ExitCode, Is.Zero, repeated.Diagnostic);
            Assert.That(screened.ExitCode, Is.Zero, screened.Diagnostic);
            Assert.That(discovered.Run.Status, Is.EqualTo(AgentRunStatus.Completed));
            Assert.That(repeated.Run.Status, Is.EqualTo(AgentRunStatus.Completed));
            Assert.That(discovered.Run.SummaryJson, Does.Contain("\"completionReasonCode\":\"discovery.newProfileLimit\""));
            Assert.That(repeated.Run.SummaryJson, Does.Contain("\"completionReasonCode\":\"discovery.consecutiveKnownLimit\""));
            Assert.That(counts.Candidates, Is.EqualTo(20));
            Assert.That(counts.Snapshots, Is.EqualTo(20));
            Assert.That(counts.ScreeningDecisions, Is.EqualTo(20));
            Assert.That(screened.Run.SummaryJson, Does.Contain("\"noAiCalls\":true"));
            Assert.That(decisions, Has.Length.EqualTo(20));
            Assert.That(decisions, Has.All.Matches<ScreeningDecision>(decision => !decision.EvaluatorInputJson.Contains("PROTECTED_AGE_MARKER", StringComparison.Ordinal)));
            Assert.That(account!.SessionStatus, Is.EqualTo(BrowserSessionStatus.Healthy));
            Assert.That(account.LastSuccessfulRunAtUtc, Is.Not.Null);
            Assert.That(firstCheckpoint, Is.Not.Null);
            Assert.That(repeatCheckpoint, Is.Not.Null);
            Assert.That(firstCheckpoint!.StateJson, Does.Contain("linkSetHash"));
            Assert.That(firstCheckpoint.StateJson, Does.Not.Contain("cookie").IgnoreCase);
            Assert.That(firstCheckpoint.StateJson, Does.Not.Contain("token").IgnoreCase);
            Assert.That(viewedMetric, Is.EqualTo(20m));
            Assert.That(knownMetric, Is.EqualTo(8m));
            Assert.That(releasedLock.CanWrite, Is.True);
        });
    }

    [TestCase("login", AgentExitCode.AuthenticationRequired, BrowserSessionStatus.ReauthenticationRequired, "browser.authentication.required")]
    [TestCase("challenge", AgentExitCode.ChallengeDetected, BrowserSessionStatus.ChallengeDetected, "browser.challenge.detected")]
    public async Task BrowserEnforcementFixturesStopOneAccountWithoutFailover(
        string fixture,
        int expectedExit,
        BrowserSessionStatus expectedStatus,
        string expectedReason)
    {
        await using StartupSchoolFixtureServer server = StartupSchoolFixtureServer.Start();
        string entry = fixture == "login" ? server.LoginUrl : server.ChallengeUrl;
        await using FounderScoutRunnerFixture runner = await FounderScoutRunnerFixture.CreateAsync(server.CreateOptions(entry));

        RunnerExecution execution = await runner.ExecuteAsync("discover", JsonSerializer.Serialize(new { accountId = "fixture-account", segmentId = "default" }, JsonOptions));
        BrowserAccount? account = await ((IBrowserAccountRepository)runner.CreateFounderRepository()).GetAsync("fixture-account");
        int artifacts = await runner.GetCentralArtifactCountAsync(execution.Run.Id);

        Assert.Multiple(() =>
        {
            Assert.That(execution.ExitCode, Is.EqualTo(AgentExitCode.TransientFailure), execution.Diagnostic);
            Assert.That(execution.Run.ExitCode, Is.EqualTo(expectedExit));
            Assert.That(execution.Run.Status, Is.EqualTo(AgentRunStatus.Failed));
            Assert.That(execution.Run.SummaryJson, Does.Contain(expectedReason));
            Assert.That(execution.Run.SummaryJson, Does.Contain("\"accountFailover\":false"));
            Assert.That(account!.SessionStatus, Is.EqualTo(expectedStatus));
            Assert.That(account.LastErrorReasonCode, Is.EqualTo(expectedReason));
            Assert.That(artifacts, Is.EqualTo(4));
        });
    }

    private sealed class FounderScoutRunnerFixture : IAsyncDisposable
    {
        private readonly string root;
        private readonly AssistantDatabase centralDatabase;
        private readonly FounderScoutDatabase founderDatabase;
        private readonly AgentRunRepository runs;
        private AgentConfigurationRecord configuration;

        private FounderScoutRunnerFixture(
            string root,
            string dataDirectory,
            string agentDirectory,
            string manifestDirectory,
            string founderDataDirectory,
            string fixturePath,
            AssistantDatabase centralDatabase,
            FounderScoutDatabase founderDatabase,
            AgentConfigurationRecord configuration)
        {
            this.root = root;
            DataDirectory = dataDirectory;
            AgentDirectory = agentDirectory;
            ManifestDirectory = manifestDirectory;
            FounderDataDirectory = founderDataDirectory;
            FixturePath = fixturePath;
            this.centralDatabase = centralDatabase;
            this.founderDatabase = founderDatabase;
            this.configuration = configuration;
            runs = new(centralDatabase.ContextFactory);
        }

        public string DataDirectory { get; }
        public string AgentDirectory { get; }
        public string ManifestDirectory { get; }
        public string FounderDataDirectory { get; }
        public string FixturePath { get; }
        public string RootPath => root;
        public AssistantDatabase CentralDatabase => centralDatabase;
        public FounderScoutDatabase FounderDatabase => founderDatabase;

        public static async ValueTask<FounderScoutRunnerFixture> CreateAsync(
            StartupSchoolSourceOptions? sourceOptions = null,
            bool enableDeepEvaluation = false,
            int discoveryLimit = 20,
            bool useUnicodeRoot = false)
        {
            string rootName = useUnicodeRoot
                ? $"hba-founder-runner-tests-Stage 16 Ω-{Guid.NewGuid():N}"
                : $"hba-founder-runner-tests-{Guid.NewGuid():N}";
            string root = Path.Combine(Path.GetTempPath(), rootName);
            string data = Path.Combine(root, "data");
            string agents = Path.Combine(root, "agents");
            string manifests = Path.Combine(root, "manifests");
            string founderAgentDirectory = Path.Combine(agents, "founder-scout");
            string founderDataDirectory = Path.Combine(data, "agents", "founder-scout");
            string repositoryRoot = FindRepositoryRoot();
            Directory.CreateDirectory(founderAgentDirectory);
            Directory.CreateDirectory(manifests);
            foreach (string path in Directory.GetFiles(Path.Combine(repositoryRoot, "manifests"), "*.json"))
            {
                File.Copy(path, Path.Combine(manifests, Path.GetFileName(path)));
            }

            CopyDirectory(
                Path.Combine(repositoryRoot, "agents", "FounderScout", "FounderScout.Agent", "bin", "Release", "net10.0-windows"),
                founderAgentDirectory);
            AssistantDatabase centralDatabase = await AssistantDatabase.InitializeAsync(
                new(data, ManifestDirectory: manifests),
                TimeProvider.System);
            var configurations = new AgentConfigurationService(
                centralDatabase.ContextFactory,
                new CompositeAgentConfigurationValidator([
                    new BasicAgentConfigurationValidator(),
                    new FounderScoutConfigurationValidator(),
                ]),
                TimeProvider.System);
            FounderScoutConfiguration model = new FounderScoutDefaults().GetDefault().Configuration
                .Deserialize<FounderScoutConfiguration>(JsonOptions)
                ?? throw new InvalidOperationException("Founder Scout defaults did not deserialize.");
            FounderScoutConfiguration configuredModel = sourceOptions is null
                ? model with { DataDirectory = founderDataDirectory }
                : model with
                {
                    DataDirectory = founderDataDirectory,
                    StartupSchool = sourceOptions,
                    Discovery = model.Discovery with
                    {
                        MaxNewProfilesPerRun = discoveryLimit,
                        MaxViewedProfilesPerRun = Math.Max(discoveryLimit + 20, 40),
                        MaxNewProfilesPerDay = Math.Max(discoveryLimit + 20, 60),
                        MaxRuntimeSeconds = 600,
                        StopAfterConsecutiveKnownProfiles = 8,
                    },
                };
            if (enableDeepEvaluation)
            {
                configuredModel = configuredModel with
                {
                    Analysis = configuredModel.Analysis with
                    {
                        BatchSize = 20,
                        MaximumConcurrency = 2,
                        MaximumRetries = 3,
                    },
                    Processing = (configuredModel.Processing ?? FounderScoutProcessingSettings.Default) with
                    {
                        DeepAnalysisThreshold = 0,
                        MonitorThreshold = 0,
                        PreferNonTechnical = false,
                        HardFilterUnpaidImplementationLabor = false,
                        FilterIdenticalTechnicalPreference = false,
                    },
                    Invitation = configuredModel.Invitation with { SimilarityThreshold = 1m },
                    Ai = new(
                        "DiagnosticFake",
                        "diagnostic://fake",
                        "deterministic-evaluator-v1",
                        "secret://founder-scout/diagnostic-fake",
                        30,
                        MaxOutputTokens: 6_000),
                };
            }
            SaveConfigurationResult saved = await configurations.SaveAsync(new(
                FounderScoutDefaults.AgentId,
                FounderScoutConfiguration.CurrentSchemaVersion,
                JsonSerializer.SerializeToElement(configuredModel, JsonOptions),
                "founder-scout-runner-test",
                "Create the Founder Scout Runner smoke configuration.",
                Guid.NewGuid()));
            AgentConfigurationRecord configuration = await configurations.GetCurrentAsync(FounderScoutDefaults.AgentId)
                ?? throw new InvalidOperationException("Founder Scout configuration was not persisted.");
            if (saved.Revision.Id != configuration.CurrentRevisionId)
            {
                throw new InvalidOperationException("Founder Scout current configuration revision is inconsistent.");
            }

            FounderScoutDatabase founderDatabase = await FounderScoutDatabaseInitializer.InitializeAsync(
                new FounderScoutDatabaseSettings(founderDataDirectory),
                TimeProvider.System);
            if (sourceOptions is not null)
            {
                DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
                var repository = new FounderScoutRepository(founderDatabase.ContextFactory, TimeProvider.System);
                await repository.UpsertAsync(new BrowserAccount(
                    "fixture-account",
                    "Synthetic fixture account",
                    "browser/fixture-account",
                    Enabled: true,
                    BrowserSessionStatus.Healthy,
                    ["default", "repeat"],
                    nowUtc,
                    null,
                    null,
                    null,
                    null,
                    null,
                    nowUtc,
                    nowUtc,
                    1));
                foreach (string segmentId in new[] { "default", "repeat" })
                {
                    await repository.UpsertAsync(new DiscoverySegment(
                        segmentId,
                        $"Synthetic {segmentId} segment",
                        Enabled: true,
                        Priority: segmentId == "default" ? 100 : 50,
                        JsonSerializer.Serialize(new { version = "1.0", sourceOptions.EntryUrl }, JsonOptions),
                        "fixture-account",
                        null,
                        0,
                        0,
                        0,
                        0,
                        0,
                        null,
                        nowUtc,
                        nowUtc,
                        1));
                }
            }
            string fixturePath = Path.Combine(founderDatabase.ImportsDirectory, "runner-smoke.json");
            await File.WriteAllTextAsync(fixturePath, enableDeepEvaluation ? CreateDeepEvaluationFixtureJson() : CreateFixtureJson());
            return new(
                root,
                data,
                agents,
                manifests,
                founderDataDirectory,
                fixturePath,
                centralDatabase,
                founderDatabase,
                configuration);
        }

        public FounderScoutRepository CreateFounderRepository() =>
            new(founderDatabase.ContextFactory, TimeProvider.System);

        public async ValueTask<RunnerExecution> ExecuteAsync(string command, string argumentsJson)
        {
            OccurrenceId occurrenceId = OccurrenceId.New();
            var occurrences = new OccurrenceRepository(centralDatabase.ContextFactory, TimeProvider.System);
            _ = await occurrences.CreateIfAbsentAsync(new(
                occurrenceId,
                ScheduleId: null,
                FounderScoutDefaults.AgentId,
                command,
                argumentsJson,
                configuration.CurrentRevisionId,
                DateTimeOffset.UtcNow,
                TriggerType.CommandLine,
                AttemptNumber: 0,
                ParentOccurrenceId: null,
                OccurrenceStatus.Ready,
                RequiresWake: false,
                KeepSystemAwake: false,
                KeepDisplayOn: false));
            using StringWriter output = new();
            using StringWriter error = new();
            int exitCode = await RunnerCommand.ExecuteAsync([
                "execute",
                "--occurrence-id", occurrenceId.ToString(),
                "--data-directory", DataDirectory,
                "--agent-directory", AgentDirectory,
                "--manifest-directory", ManifestDirectory,
            ], output, error);
            AgentRunRecord run = await runs.GetForOccurrenceAsync(occurrenceId)
                ?? throw new InvalidOperationException($"Founder Scout Runner execution did not create a central run. output={output}; error={error}");
            string eventDiagnostics = await GetCentralEventDiagnosticsAsync(run.Id);
            return new(exitCode, run, $"{output}{error}{Environment.NewLine}errorType={run.ErrorType}; errorMessage={run.ErrorMessage}; summary={run.SummaryJson}; events={eventDiagnostics}");
        }

        public async ValueTask<RunnerExecution> ExecuteAsRunnerProcessAsync(string command, string argumentsJson)
        {
            OccurrenceId occurrenceId = OccurrenceId.New();
            var occurrences = new OccurrenceRepository(centralDatabase.ContextFactory, TimeProvider.System);
            _ = await occurrences.CreateIfAbsentAsync(new(
                occurrenceId,
                ScheduleId: null,
                FounderScoutDefaults.AgentId,
                command,
                argumentsJson,
                configuration.CurrentRevisionId,
                DateTimeOffset.UtcNow,
                TriggerType.CommandLine,
                AttemptNumber: 0,
                ParentOccurrenceId: null,
                OccurrenceStatus.Ready,
                RequiresWake: false,
                KeepSystemAwake: false,
                KeepDisplayOn: false));
            string runnerPath = Path.Combine(
                FindRepositoryRoot(),
                "src",
                "HomeBusinessAssistant.Runner",
                "bin",
                "Release",
                "net10.0-windows",
                "HomeBusinessAssistant.Runner.exe");
            var startInfo = new ProcessStartInfo
            {
                FileName = runnerPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("execute");
            startInfo.ArgumentList.Add("--occurrence-id");
            startInfo.ArgumentList.Add(occurrenceId.ToString());
            startInfo.ArgumentList.Add("--data-directory");
            startInfo.ArgumentList.Add(DataDirectory);
            startInfo.ArgumentList.Add("--agent-directory");
            startInfo.ArgumentList.Add(AgentDirectory);
            startInfo.ArgumentList.Add("--manifest-directory");
            startInfo.ArgumentList.Add(ManifestDirectory);

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The Stage 16 Runner process could not be started.");
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw new TimeoutException("The Stage 16 Runner process exceeded its three-minute bound.");
            }

            AgentRunRecord run = await runs.GetForOccurrenceAsync(occurrenceId)
                ?? throw new InvalidOperationException("The Stage 16 Runner process did not create a central run.");
            string eventDiagnostics = await GetCentralEventDiagnosticsAsync(run.Id);
            return new(
                process.ExitCode,
                run,
                $"{await output}{await error}{Environment.NewLine}errorType={run.ErrorType}; errorMessage={run.ErrorMessage}; summary={run.SummaryJson}; events={eventDiagnostics}");
        }

        public ValueTask<int> GetCentralEventCountAsync(AgentRunId runId) =>
            GetCentralCountAsync("AgentRunEvents", runId);

        public ValueTask<int> GetCentralArtifactCountAsync(AgentRunId runId) =>
            GetCentralCountAsync("RunArtifacts", runId);

        public async ValueTask<int> GetCentralMetricCountAsync(AgentRunId runId, string name)
        {
            await using AssistantDbContext context = await centralDatabase.ContextFactory.CreateDbContextAsync();
            await context.Database.OpenConnectionAsync();
            await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM AgentRunMetrics WHERE RunId = $id AND Name = $name";
            AddParameter(command, "$id", runId.Value);
            AddParameter(command, "$name", name);
            return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }

        public async ValueTask<int> GetCentralEventTypeCountAsync(AgentRunId runId, string eventType)
        {
            await using AssistantDbContext context = await centralDatabase.ContextFactory.CreateDbContextAsync();
            await context.Database.OpenConnectionAsync();
            await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM AgentRunEvents WHERE RunId = $id AND EventType = $eventType";
            AddParameter(command, "$id", runId.Value);
            AddParameter(command, "$eventType", eventType);
            return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }

        public async ValueTask<int> GetFounderCountAsync(string table)
        {
            if (table is not ("CandidateIdentityAliases" or "CandidateIdentityConflicts" or "Candidates" or "ProfileSnapshots" or "ScreeningDecisions" or "Evaluations" or "InvitationDrafts"))
            {
                throw new ArgumentOutOfRangeException(nameof(table));
            }
            await using FounderScoutDbContext context = await founderDatabase.ContextFactory.CreateDbContextAsync();
            await context.Database.OpenConnectionAsync();
            await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table}";
            return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }

        public async ValueTask UpdatePersonaRevisionAsync()
        {
            FounderScoutConfiguration current = JsonSerializer.Deserialize<FounderScoutConfiguration>(
                configuration.CurrentRevision.CanonicalConfigurationJson,
                JsonOptions) ?? throw new InvalidOperationException("The current Founder Scout configuration could not be read.");
            FounderScoutConfiguration changed = current with
            {
                Persona = current.Persona with
                {
                    Reference = "founder-persona/stage12-smoke-v2",
                    Strengths = [.. current.Persona.Strengths, "Local automation operations"],
                },
            };
            var configurations = new AgentConfigurationService(
                centralDatabase.ContextFactory,
                new CompositeAgentConfigurationValidator([
                    new BasicAgentConfigurationValidator(),
                    new FounderScoutConfigurationValidator(),
                ]),
                TimeProvider.System);
            _ = await configurations.SaveAsync(new(
                FounderScoutDefaults.AgentId,
                FounderScoutConfiguration.CurrentSchemaVersion,
                JsonSerializer.SerializeToElement(changed, JsonOptions),
                "founder-scout-runner-test",
                "Change the synthetic founder persona to verify cache invalidation.",
                Guid.NewGuid(),
                configuration.CurrentRevision.RevisionNumber,
                configuration.CurrentRevision.ConfigurationHash));
            configuration = await configurations.GetCurrentAsync(FounderScoutDefaults.AgentId)
                ?? throw new InvalidOperationException("The changed Founder Scout configuration was not promoted.");
        }

        public async ValueTask<bool> FounderColumnContainsAsync(string table, string column, string marker)
        {
            if (table != "ScreeningDecisions" || column != "EvaluatorInputJson")
            {
                throw new ArgumentOutOfRangeException(nameof(table));
            }
            await using FounderScoutDbContext context = await founderDatabase.ContextFactory.CreateDbContextAsync();
            await context.Database.OpenConnectionAsync();
            await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT EXISTS(SELECT 1 FROM {table} WHERE instr({column}, $marker) > 0)";
            AddParameter(command, "$marker", marker);
            return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) == 1;
        }

        public async ValueTask<string> WriteRelevantChangeFixtureAsync()
        {
            string path = Path.Combine(founderDatabase.ImportsDirectory, "runner-smoke-relevant-change.json");
            await File.WriteAllTextAsync(path, CreateRelevantChangeFixtureJson());
            return path;
        }

        public async ValueTask<string> WriteStage16ChangedFixtureAsync(string sourceBaseUrl)
        {
            string path = Path.Combine(founderDatabase.ImportsDirectory, "stage16-changed-profile.json");
            DateTimeOffset capturedAtUtc = DateTimeOffset.UtcNow;
            string json = JsonSerializer.Serialize(new[]
            {
                new
                {
                    captureSchemaVersion = "1.0",
                    source = "fixture",
                    sourceAccountId = "fixture-account",
                    sourceSegmentId = "default",
                    sourceProfileKey = "fixture-01",
                    profileUrl = $"{sourceBaseUrl}/profile/fixture-01",
                    capturedAtUtc,
                    displayName = "Synthetic Founder fixture-01",
                    rawText = "Background: Built the changed synthetic fixture company and completed 50 customer interviews. Age: PROTECTED_AGE_MARKER. Looking for: A technical co-founder.",
                    structuredFields = new
                    {
                        location = "Chicago, IL",
                        technical = "non-technical",
                        commitment = "full-time",
                        roles = DeepEvaluationRoles,
                        introduction = "Builds practical tools for local retailers.",
                        background = "Built the changed synthetic fixture company.",
                        problem = "Local retailers lose time coordinating suppliers.",
                        customer = "Independent retailers",
                        solution = "A shared supplier coordination workflow.",
                        traction = Stage16Traction,
                        cofounderRole = "Technical co-founder and CTO",
                        cofounderCommitment = "full-time",
                        equity = "Equal founder-level partnership.",
                        industries = DeepEvaluationIndustries,
                    },
                    sourceAdapterVersion = "fixture-1.0",
                },
            }, JsonOptions);
            await File.WriteAllTextAsync(path, json);
            return path;
        }

        public async ValueTask<bool> AllStage16HashesAreSha256Async()
        {
            await using FounderScoutDbContext founder = await founderDatabase.ContextFactory.CreateDbContextAsync();
            await founder.Database.OpenConnectionAsync();
            await using DbCommand founderCommand = founder.Database.GetDbConnection().CreateCommand();
            founderCommand.CommandText = "SELECT COUNT(*) = SUM(CASE WHEN length(ContentHash) = 64 AND length(RawContentHash) = 64 AND (EvaluatorInputHash IS NULL OR length(EvaluatorInputHash) = 64) THEN 1 ELSE 0 END) FROM ProfileSnapshots";
            bool founderHashes = Convert.ToInt32(await founderCommand.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) == 1;

            await using AssistantDbContext central = await centralDatabase.ContextFactory.CreateDbContextAsync();
            await central.Database.OpenConnectionAsync();
            await using DbCommand centralCommand = central.Database.GetDbConnection().CreateCommand();
            centralCommand.CommandText = "SELECT COUNT(*) = SUM(CASE WHEN length(ConfigurationHash) = 64 AND length(ExecutableHash) = 64 THEN 1 ELSE 0 END) FROM AgentRuns WHERE AgentId = 'founder-scout'";
            bool centralHashes = Convert.ToInt32(await centralCommand.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) == 1;
            return founderHashes && centralHashes;
        }

        public async ValueTask<decimal?> GetCentralMetricAsync(AgentRunId runId, string name)
        {
            await using AssistantDbContext context = await centralDatabase.ContextFactory.CreateDbContextAsync();
            await context.Database.OpenConnectionAsync();
            await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT NumericValue FROM AgentRunMetrics WHERE RunId = $id AND Name = $name ORDER BY Id DESC LIMIT 1";
            DbParameter id = command.CreateParameter();
            id.ParameterName = "$id";
            id.Value = runId.Value;
            command.Parameters.Add(id);
            DbParameter metricName = command.CreateParameter();
            metricName.ParameterName = "$name";
            metricName.Value = name;
            command.Parameters.Add(metricName);
            object? value = await command.ExecuteScalarAsync();
            return value is null or DBNull ? null : Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        public ValueTask DisposeAsync()
        {
            string fullRoot = Path.GetFullPath(root);
            if (!fullRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullRoot).StartsWith("hba-founder-runner-tests-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to delete an unexpected Founder Scout Runner test directory.");
            }

            if (Directory.Exists(fullRoot))
            {
                Directory.Delete(fullRoot, recursive: true);
            }

            return ValueTask.CompletedTask;
        }

        private async ValueTask<int> GetCentralCountAsync(string table, AgentRunId runId)
        {
            await using AssistantDbContext context = await centralDatabase.ContextFactory.CreateDbContextAsync();
            await context.Database.OpenConnectionAsync();
            await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE RunId = $id";
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "$id";
            parameter.Value = runId.Value;
            command.Parameters.Add(parameter);
            object? count = await command.ExecuteScalarAsync();
            return Convert.ToInt32(count, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        private async ValueTask<string> GetCentralEventDiagnosticsAsync(AgentRunId runId)
        {
            await using AssistantDbContext context = await centralDatabase.ContextFactory.CreateDbContextAsync();
            await context.Database.OpenConnectionAsync();
            await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT EventType || ':' || Message || ':' || DataJson FROM AgentRunEvents WHERE RunId = $id ORDER BY Sequence";
            DbParameter id = command.CreateParameter();
            id.ParameterName = "$id";
            id.Value = runId.Value;
            command.Parameters.Add(id);
            var values = new List<string>();
            await using DbDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                values.Add(reader.GetString(0));
            }

            return string.Join(" | ", values);
        }

        private static void CopyDirectory(string source, string destination)
        {
            foreach (string sourcePath in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(source, sourcePath);
                string destinationPath = Path.Combine(destination, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)
                    ?? throw new InvalidOperationException("Copied file has no destination directory."));
                File.Copy(sourcePath, destinationPath);
            }
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, "HomeBusinessAssistant.sln")))
            {
                current = current.Parent;
            }

            return current?.FullName
                ?? throw new InvalidOperationException("The repository root could not be located.");
        }

        private static string CreateFixtureJson()
        {
            DateTimeOffset capturedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            object Capture(int index, string customer) => new
            {
                captureSchemaVersion = "1.0",
                source = "fixture",
                sourceAccountId = "fixture-account",
                sourceSegmentId = "fixture-segment",
                sourceProfileKey = $"candidate-{index:000}",
                profileUrl = $"https://example.invalid/profile/candidate-{index:000}",
                capturedAtUtc,
                displayName = $"Candidate {index:00}",
                rawText = $"About: Candidate {index:00} has led customer operations and local distribution for several years.\nAge: PROTECTED_AGE_MARKER\nProblem: Small businesses lose time coordinating suppliers.\nCustomer: {customer}",
                structuredFields = new
                {
                    location = index % 2 == 0 ? "Chicago, IL" : "Austin, TX",
                    technical = index % 5 == 0 ? "technical" : "non-technical",
                    commitment = index % 4 == 0 ? "part-time with a transition plan" : "full-time",
                    roles = new[] { index % 3 == 0 ? "sales" : "operations", "domain" },
                    introduction = $"Candidate {index:00} builds practical tools for local operators.",
                    background = $"Candidate {index:00} has led customer operations and local distribution for several years.",
                    problem = "Small businesses lose time coordinating suppliers.",
                    customer,
                    solution = "A workflow that connects local operators with verified suppliers.",
                    traction = index % 6 == 0 ? Array.Empty<string>() : new[] { $"Completed {index + 4} customer interviews." },
                    cofounderRole = "Technical co-founder and CTO",
                    cofounderCommitment = "full-time",
                    equity = "equal founder-level partnership",
                    industries = new[] { "local commerce", "operations" },
                },
                sourceAdapterVersion = "fixture-1.0",
            };
            object first = Capture(1, "Independent neighborhood retailers");
            var captures = Enumerable.Range(1, 18)
                .Select(index => Capture(index, $"Independent retailer segment {index:00}"))
                .ToList();
            captures[0] = first;
            captures.Add(first);
            captures.Add(Capture(1, "Independent neighborhood retailers and service businesses"));
            return JsonSerializer.Serialize(captures, JsonOptions);
        }

        private static string CreateDeepEvaluationFixtureJson()
        {
            string[] keys =
            [
                "strong-01",
                "strong-02",
                "invalid-schema-strong-03",
                "evidence-failure-strong-04",
                "draft-failure-strong-05",
                "exploratory-06",
                "exploratory-07",
                "transient-exploratory-08",
                "exploratory-09",
                "monitor-10",
                "monitor-11",
                "monitor-12",
                "monitor-13",
                "pass-14",
                "pass-15",
                "pass-16",
                "pass-17",
                "manual-18",
                "manual-19",
                "manual-20",
            ];
            DateTimeOffset capturedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
            object[] captures = keys.Select((key, index) => new
            {
                captureSchemaVersion = "1.0",
                source = "fixture",
                sourceAccountId = "fixture-account",
                sourceSegmentId = "fixture-segment",
                sourceProfileKey = key,
                profileUrl = $"https://example.invalid/profile/{key}",
                capturedAtUtc = capturedAtUtc.AddSeconds(index),
                displayName = $"Synthetic Founder {index + 1:00}",
                rawText = $"About: Synthetic Founder {index + 1:00} leads customer operations for local retailers.\nAge: PROTECTED_AGE_MARKER\nProblem: Local retailers lose time coordinating suppliers.\nCustomer: Independent retailer segment {index + 1:00}",
                structuredFields = new
                {
                    location = "Remote US Central",
                    technical = "non-technical",
                    commitment = "full-time",
                    roles = DeepEvaluationRoles,
                    introduction = $"Synthetic Founder {index + 1:00} builds practical tools for local retailers.",
                    background = $"Synthetic Founder {index + 1:00} leads customer operations for local retailers.",
                    problem = "Local retailers lose time coordinating suppliers.",
                    customer = $"Independent retailer segment {index + 1:00}",
                    solution = "A shared supplier coordination workflow.",
                    traction = new[] { $"Completed {index + 10} customer interviews." },
                    cofounderRole = "Technical co-founder and CTO",
                    cofounderCommitment = "full-time",
                    equity = "Equal founder-level partnership.",
                    industries = DeepEvaluationIndustries,
                },
                sourceAdapterVersion = "fixture-1.0",
            }).ToArray();
            return JsonSerializer.Serialize(captures, JsonOptions);
        }

        private static string CreateRelevantChangeFixtureJson()
        {
            DateTimeOffset capturedAtUtc = DateTimeOffset.UtcNow;
            return JsonSerializer.Serialize(new[]
            {
                new
                {
                    captureSchemaVersion = "1.0",
                    source = "fixture",
                    sourceAccountId = "fixture-account",
                    sourceSegmentId = "fixture-segment",
                    sourceProfileKey = "candidate-001",
                    profileUrl = "https://example.invalid/profile/candidate-001",
                    capturedAtUtc,
                    displayName = "Candidate 01",
                    rawText = "About: Candidate 01 has led customer operations and local distribution for several years.\nAge: PROTECTED_AGE_MARKER\nProblem: Small businesses lose time coordinating suppliers.\nCustomer: Regional retailers with multiple locations",
                    structuredFields = new
                    {
                        location = "Austin, TX",
                        technical = "non-technical",
                        commitment = "full-time",
                        roles = RelevantChangeRoles,
                        introduction = "Candidate 01 builds practical tools for local operators.",
                        background = "Candidate 01 has led customer operations and local distribution for several years.",
                        problem = "Small businesses lose time coordinating suppliers.",
                        customer = "Regional retailers with multiple locations",
                        solution = "A workflow that connects local operators with verified suppliers.",
                        traction = RelevantChangeTraction,
                        cofounderRole = "Technical co-founder and CTO",
                        cofounderCommitment = "full-time",
                        equity = "equal founder-level partnership",
                        industries = RelevantChangeIndustries,
                    },
                    sourceAdapterVersion = "fixture-1.0",
                },
            }, JsonOptions);
        }
    }

    private sealed record RunnerExecution(int ExitCode, AgentRunRecord Run, string Diagnostic);

    private sealed class Stage16SignalSource(int candidateCount) : IOperationalSignalSource
    {
        public ValueTask<IReadOnlyList<OperationalSignal>> ReadSignalsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<OperationalSignal> signals =
            [
                new(
                    "founder-opportunity",
                    AttentionSeverity.Info,
                    "Synthetic Founder Scout review is ready",
                    $"{candidateCount} synthetic candidates completed the release workflow.",
                    "founder-scout",
                    null,
                    null,
                    "stage16:founder-workflow",
                    "/FounderScout/Candidates",
                    new { candidateCount }),
            ];
            return ValueTask.FromResult(signals);
        }
    }
}
