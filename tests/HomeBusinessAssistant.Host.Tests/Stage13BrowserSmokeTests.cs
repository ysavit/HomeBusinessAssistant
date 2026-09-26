using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FounderScout.Application;
using FounderScout.Domain;
using FounderScout.Infrastructure.Persistence;
using HomeBusinessAssistant.Host.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Playwright;

namespace HomeBusinessAssistant.Host.Tests;

internal sealed class Stage13BrowserSmokeTests
{
    private static readonly string[] Stage16DesktopPaths =
    [
        "/", "/Agents", "/Schedules", "/Runs", "/Audit", "/WakeRemote", "/FounderScout",
        "/FounderScout/Candidates", "/FounderScout/Queue", "/FounderScout/Operations", "/Settings", "/About",
    ];

    private static readonly string[] Stage16NarrowPaths =
    [
        "/", "/FounderScout/Candidates", "/FounderScout/Queue", "/FounderScout/Operations", "/Settings",
    ];

    [Test]
    [Category("BrowserSmoke")]
    public async Task ServeSyntheticFounderScoutWorkspaceForInteractiveBrowserQa()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("HBA_STAGE13_BROWSER_SMOKE"), "1", StringComparison.Ordinal))
        {
            Assert.Ignore("Set HBA_STAGE13_BROWSER_SMOKE=1 to run the interactive Stage 13 browser fixture.");
        }

        string repositoryRoot = FindRepositoryRoot();
        string root = Path.Combine(Path.GetTempPath(), "hba-stage13-browser-smoke");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        string data = Path.Combine(root, "data");
        string agents = Path.Combine(root, "agents");
        Directory.CreateDirectory(agents);
        int port = 5193;
        string url = $"http://127.0.0.1:{port}";
        var settings = CreateSettings(repositoryRoot, data, agents, url);
        var logger = HostLogFactory.Create(data);
        try
        {
            await using HostRuntime runtime = await HostRuntime.CreateAsync(settings, logger);
            await SeedAsync(runtime.WebComposition.Management!.FounderScout!);
            if (runtime.WebComposition.Management.Operations is not null)
            {
                _ = await runtime.WebComposition.Management.Operations.DetectAsync();
                _ = await runtime.WebComposition.Management.Operations.GenerateDailySummaryAsync(
                    DateOnly.FromDateTime(DateTime.Today),
                    TimeZoneInfo.Local.Id);
            }
            HostWebComposition webOnly = runtime.WebComposition with { HostedServices = [] };
            string hostContentRoot = Path.Combine(repositoryRoot, "src", "HomeBusinessAssistant.Host");
            await using WebApplication application = HostApplication.Build(
                ["--urls", url, "--contentRoot", hostContentRoot],
                webOnly);
            await application.StartAsync();
            await File.WriteAllTextAsync(Path.Combine(root, "ready.txt"), url);
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(10);
            while (!File.Exists(Path.Combine(root, "stop.txt")) && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(250);
            }
            Assert.That(File.Exists(Path.Combine(root, "stop.txt")), Is.True, "Browser smoke fixture timed out before stop.txt was created.");
            await application.StopAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Category("Stage16RenderedUi")]
    public async Task Stage16DesktopAndNarrowSyntheticWorkspaceHasAccessibleSafeLayout()
    {
        string repositoryRoot = FindRepositoryRoot();
        string root = Path.Combine(Path.GetTempPath(), $"hba-stage16 rendered Ω {Guid.NewGuid():N}");
        string data = Path.Combine(root, "data");
        string agents = Path.Combine(root, "agents");
        Directory.CreateDirectory(agents);
        int port = GetAvailablePort();
        string url = $"http://127.0.0.1:{port}";
        var settings = CreateSettings(repositoryRoot, data, agents, url);
        var logger = HostLogFactory.Create(data);
        try
        {
            await using HostRuntime runtime = await HostRuntime.CreateAsync(settings, logger);
            await SeedAsync(runtime.WebComposition.Management!.FounderScout!);
            if (runtime.WebComposition.Management.Operations is not null)
            {
                _ = await runtime.WebComposition.Management.Operations.DetectAsync();
                _ = await runtime.WebComposition.Management.Operations.GenerateDailySummaryAsync(
                    DateOnly.FromDateTime(DateTime.Today),
                    TimeZoneInfo.Local.Id);
            }

            HostWebComposition webOnly = runtime.WebComposition with { HostedServices = [] };
            string hostContentRoot = Path.Combine(repositoryRoot, "src", "HomeBusinessAssistant.Host");
            await using WebApplication application = HostApplication.Build(
                ["--urls", url, "--contentRoot", hostContentRoot],
                webOnly);
            await application.StartAsync();

            using IPlaywright playwright = await Playwright.CreateAsync();
            await using IBrowser browser = await playwright.Chromium.LaunchAsync(new()
            {
                Headless = true,
                Args = ["--auth-server-allowlist=127.0.0.1", "--auth-negotiate-delegate-allowlist=127.0.0.1"],
            });
            await using IBrowserContext context = await browser.NewContextAsync(new()
            {
                ViewportSize = new() { Width = 1_440, Height = 900 },
            });
            IPage page = await context.NewPageAsync();
            var consoleErrors = new List<string>();
            page.Console += (_, message) =>
            {
                if (message.Type == "error") consoleErrors.Add(message.Text);
            };
            page.PageError += (_, error) => consoleErrors.Add(error);

            foreach (string path in Stage16DesktopPaths)
            {
                await AssertRenderedPageAsync(page, url, path);
            }

            await AssertRenderedPageAsync(page, url, "/FounderScout/Candidates");
            string? candidatePath = await page.Locator("a[href*='/FounderScout/Candidates/']").First.GetAttributeAsync("href");
            if (!string.IsNullOrWhiteSpace(candidatePath))
            {
                await AssertRenderedPageAsync(page, url, candidatePath);
            }

            await page.SetViewportSizeAsync(390, 844);
            foreach (string path in Stage16NarrowPaths)
            {
                await AssertRenderedPageAsync(page, url, path);
            }

            string body = await page.Locator("body").InnerTextAsync();
            Assert.Multiple(() =>
            {
                Assert.That(body, Does.Contain("Local-only control plane"));
                Assert.That(body, Does.Not.Contain("stage-08-secret-must-not-render"));
                Assert.That(body, Does.Not.Contain("browser/primary-account"));
                Assert.That(consoleErrors, Is.Empty);
            });

            string? screenshotRoot = Environment.GetEnvironmentVariable("HBA_STAGE16_UI_SCREENSHOTS");
            if (!string.IsNullOrWhiteSpace(screenshotRoot))
            {
                Directory.CreateDirectory(screenshotRoot);
                await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotRoot, "narrow-settings.png"), FullPage = true });
                await page.SetViewportSizeAsync(1_440, 900);
                await page.GotoAsync(url + "/FounderScout/Candidates", new() { WaitUntil = WaitUntilState.NetworkIdle });
                await page.ScreenshotAsync(new() { Path = Path.Combine(screenshotRoot, "desktop-candidates.png"), FullPage = true });
            }

            await application.StopAsync();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AssertRenderedPageAsync(IPage page, string origin, string path)
    {
        IResponse? response = await page.GotoAsync(origin + path, new() { WaitUntil = WaitUntilState.NetworkIdle });
        Assert.That(response?.Status, Is.EqualTo(200), path);
        Assert.That(await page.Locator("main").CountAsync(), Is.EqualTo(1), path);
        Assert.That(await page.Locator("a.skip-link").CountAsync(), Is.EqualTo(1), path);
        bool fitsViewport = await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1");
        Assert.That(fitsViewport, Is.True, $"Horizontal overflow at {path}");
        await page.Keyboard.PressAsync("Tab");
        bool focusVisible = await page.EvaluateAsync<bool>("() => document.activeElement !== document.body && document.activeElement?.getBoundingClientRect().width > 0");
        Assert.That(focusVisible, Is.True, $"Keyboard focus was not visible at {path}");
    }

    private static int GetAvailablePort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task SeedAsync(FounderScoutManagementComposition composition)
    {
        FounderScoutDatabase database = await FounderScoutDatabaseInitializer.InitializeAsync(new(composition.DataDirectory), TimeProvider.System);
        var repository = new FounderScoutRepository(database.ContextFactory, TimeProvider.System);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BrowserAccount account = await repository.UpsertAsync(new BrowserAccount(
            "primary-account", "Primary Startup School", "browser/primary-account", true, BrowserSessionStatus.Healthy,
            ["technical-founders"], now.AddDays(-12), now.AddHours(-2), null, null, null, null, now.AddMonths(-2), now, 1));
        _ = await repository.UpsertAsync(new DiscoverySegment(
            "technical-founders", "Technical founders with customer evidence", true, 100,
            "{\"commitment\":\"full-time\",\"technical\":true}", account.Id, now.AddHours(-2), 84, 11, 29, 0, 0, null, now.AddMonths(-2), now, 1));

        var candidates = new List<Candidate>();
        string[] names = ["Maya Chen", "Jon Bell", "Priya Shah", "Lucas Martin", "Sofia Reyes", "Amir Patel", "Elena Kovacs", "Theo Brooks"];
        for (var index = 0; index < names.Length; index++)
        {
            candidates.Add(await SeedCandidateAsync(repository, composition.DataDirectory, account.Id, names[index], index, now));
        }

        var results = (FounderScoutResultsService)composition.Commands;
        ManualInvitationWindow window = await results.CreateInvitationWindowAsync(now.AddDays(-1), now.AddDays(29), 15, 15, "September founder conversations");
        _ = await results.AddToQueueAsync(new(window.Id, candidates[0].Id, InvitationQueueKind.Primary, false, "browser-fixture"), .55m, 120);
        _ = await results.AddToQueueAsync(new(window.Id, candidates[1].Id, InvitationQueueKind.Primary, false, "browser-fixture"), .55m, 120);
        _ = await results.AddToQueueAsync(new(window.Id, candidates[2].Id, InvitationQueueKind.Reserve, false, "browser-fixture"), .55m, 120);
    }

    private static async Task<Candidate> SeedCandidateAsync(FounderScoutRepository repository, string root, string accountId, string displayName, int index, DateTimeOffset now)
    {
        string key = $"browser-candidate-{index + 1}";
        ResolveCandidateResult resolved = await repository.ResolveAsync(new(
            Guid.NewGuid(), key, $"https://example.invalid/founders/{key}", displayName, now.AddDays(-index),
            [new(CandidateIdentityAliasType.SourceKey, FounderScoutIdentityNormalizer.Hash("source-key", key), accountId, 1m, true)], Guid.NewGuid(), "browser-fixture"));
        string relativePath = $"snapshots/browser/{key}.json";
        string fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "{\"fixture\":true}");
        AddProfileSnapshotResult snapshot = await repository.AddIfNewAsync(new(
            Guid.NewGuid(), resolved.Candidate.Id, accountId, "technical-founders", key, resolved.Candidate.CanonicalSourceUrl,
            new string((char)('a' + index % 6), 64), "startup-school-profile-1.0", "startup-school-1.0", now.AddDays(-index), relativePath,
            now.AddDays(45), null, .9m, .9m, ProfileSnapshotStatus.AnalysisCompleted, Guid.NewGuid(), "browser-fixture"));
        Guid evaluationId = Guid.NewGuid();
        decimal quality = 91m - index * 3m;
        decimal fit = 88m - index * 2m;
        decimal priority = 94m - index * 4m;
        var response = new ModelEvaluationResponse(
            FounderEvaluationRequest.CurrentEvaluatorSchemaVersion,
            $"{displayName} has shipped technical products, spoken with customers, and is looking for a committed operating partner.",
            [], [],
            [new("scope", ["The profile names two customer segments."], "Validate initial wedge and founder workload.", .8m)],
            ["Shipped product independently", "Direct customer conversations", "Full-time founder intent"],
            [], ["Revenue evidence is not yet stated"],
            ["Which customer segment has the strongest pull?", "What must a cofounder own in the first six months?", "How is founder commitment funded?"],
            "High-quality technical profile with strong local fit; validate scope and commercial evidence on a first call.",
            new($"Hi {displayName} — your product-building and customer discovery work stood out. I’d enjoy comparing notes on the problem and what you want a cofounder to own.",
                $"Hi {displayName}, I was impressed by the combination of hands-on product work and direct customer conversations in your profile. I’m building from a complementary operating background and would enjoy a short conversation about the customer wedge, technical workload, and the kind of partnership you want to build.",
                ["product-building", "customer conversations"], ["operating background"], "customer wedge and cofounder ownership", .86m));
        string structured = JsonSerializer.Serialize(new { response });
        var evaluation = new Evaluation(
            evaluationId, resolved.Candidate.Id, snapshot.Snapshot.Id, "founder-scorecard-1.0", "DiagnosticFake", "fixture-model", "fixture-1", "founder-evaluator-1.0",
            new string('e', 64), quality, fit, .86m, 78m - index, 87m, 4m + index, 83m - index, priority,
            index < 3 ? "StrongConnect" : index < 6 ? "Connect" : "Monitor", structured, null, EvaluationStatus.Pending, null, 0, null, now, now, 1);
        _ = await repository.AddAsync(new(evaluation,
            [
                new(Guid.NewGuid(), evaluationId, "execution", 18m - index / 2m, 20m, "[\"Shipped product independently\"]", "Strong hands-on execution evidence.", now),
                new(Guid.NewGuid(), evaluationId, "customerEvidence", 15m - index / 2m, 20m, "[\"Direct customer conversations\"]", "Customer discovery is explicit.", now),
                new(Guid.NewGuid(), evaluationId, "founderCommitment", 17m, 20m, "[\"Full-time founder intent\"]", "Commitment appears aligned.", now),
            ],
            [new(Guid.NewGuid(), evaluationId, "scope", 4m + index, "[\"Two customer segments\"]", "Validate the initial wedge.", now)]));
        _ = await repository.TransitionAsync(evaluationId, EvaluationStatus.Claimed, "evaluation.claimed");
        _ = await repository.TransitionAsync(evaluationId, EvaluationStatus.Completed, "evaluation.completed");
        _ = await repository.TransitionAsync(new(resolved.Candidate.Id, CandidateStatus.PendingAnalysis, "fixture.pending", "browser-fixture", null, "{}", null, evaluationId, Guid.NewGuid(), "browser-fixture"));
        FounderScoutAnalysisClaim claim = await repository.ClaimPendingAnalysisAsync($"browser-worker-{index}", TimeSpan.FromMinutes(2)) ?? throw new InvalidOperationException("Expected fixture analysis claim.");
        _ = await repository.FinalizeAnalysisClaimAsync(new(resolved.Candidate.Id, claim.WorkerId, CandidateStatus.Shortlisted, evaluationId, quality, fit, .86m, 78m - index, 4m + index, priority, "analysis.completed", Guid.NewGuid(), "browser-fixture"));
        InvitationDraft draft = await repository.CreateAndSupersedePreviousAsync(new(
            Guid.NewGuid(), evaluationId, resolved.Candidate.Id, response.Invitation.ShortDraft, response.Invitation.DetailedDraft,
            JsonSerializer.Serialize(new { response.Invitation.CandidateFactsUsed, response.Invitation.PersonaStrengthsUsed }), response.Invitation.Confidence, InvitationDraftStatus.Draft, "[]", new string('d', 64), now, null, null, false, 1));
        _ = await repository.TransitionAsync(draft.Id, InvitationDraftStatus.Valid, "draft.valid", "browser-fixture");
        return await ((ICandidateRepository)repository).GetAsync(resolved.Candidate.Id) ?? throw new InvalidOperationException("Expected fixture candidate.");
    }

    private static HostBootstrapSettings CreateSettings(string repositoryRoot, string data, string agents, string url)
    {
        string runnerDirectory = Path.Combine(repositoryRoot, "src", "HomeBusinessAssistant.Runner", "bin", "Release", "net10.0-windows");
        return new(repositoryRoot, data, agents, Path.Combine(repositoryRoot, "manifests"), Path.Combine(runnerDirectory, "HomeBusinessAssistant.Runner.exe"), runnerDirectory, "assistant.db", url, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(15));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HomeBusinessAssistant.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
