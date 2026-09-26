using FounderScout.Application;
using FounderScout.Domain;
using FounderScout.Infrastructure.Browser;
using FounderScout.Infrastructure.Files;
using FounderScout.Infrastructure.Persistence;
using Moq;

namespace FounderScout.Tests;

internal sealed class FounderScoutBrowserTests
{
    private static readonly string[] ExpectedDiagnosticPaths =
    [
        "browser-diagnostics/sanitized-page.html",
        "browser-diagnostics/browser-error.json",
        "browser-diagnostics/locator-report.json",
        "browser-diagnostics/console-summary.json",
    ];

    [Test]
    public async Task PlaywrightAdapterUsesDedicatedProfileExtractsFixtureAndSanitizesDiagnostics()
    {
        await using StartupSchoolFixtureServer server = StartupSchoolFixtureServer.Start();
        string root = NewRoot();
        string artifacts = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(artifacts);
        try
        {
            StartupSchoolSourceOptions options = server.CreateOptions();
            options = options with
            {
                ProfileLinkLocators = [new("css", "[data-testid='missing-profile-link']"), .. options.ProfileLinkLocators],
                DisplayNameLocators = [new("css", "[data-testid='missing-profile-name']"), .. options.DisplayNameLocators],
                StoreRawHtml = true,
            };
            BrowserRuntimeStatus runtime = await new PlaywrightBrowserRuntimeFactory().DiagnoseAsync(null);
            var manager = new PlaywrightBrowserSessionManager(root);
            string profile = BrowserProfilePath.Resolve(root, "fixture-account");
            BrowserSessionOpenResult opened = await manager.OpenAsync(new(
                "fixture-account",
                profile,
                Headless: true,
                BrowserChannel: null,
                NavigationTimeoutSeconds: 10));
            Assert.That(runtime.IsAvailable, Is.True, runtime.Message);
            Assert.That(opened.IsSuccess, Is.True, opened.StopSignal.Message);
            await using IBrowserSession browser = opened.Session!;
            var detector = new PlaywrightBrowserChallengeDetector();
            var source = new StartupSchoolSourceAdapter(detector);

            BrowserStopSignal entry = await source.OpenEntryAsync(browser, options);
            ProfileDiscoveryBatch batch = await source.GetNextBatchAsync(browser, options, null, 0);
            ProfileCaptureExtractionResult extracted = await new PlaywrightProfileCaptureExtractor(detector).ExtractAsync(
                browser,
                options,
                "fixture-account",
                "fixture-segment",
                batch.Links[0]);
            BrowserSessionOpenResult competing = await manager.OpenAsync(new(
                "fixture-account",
                profile,
                Headless: true,
                BrowserChannel: null,
                NavigationTimeoutSeconds: 10));
            IReadOnlyList<BrowserDiagnosticArtifact> diagnostics = await new PlaywrightBrowserDiagnosticCapture().CaptureAsync(
                browser,
                artifacts,
                new(BrowserStopKind.ParserFailure, "fixture.diagnostic", "Synthetic diagnostic."),
                14);
            string sanitized = await File.ReadAllTextAsync(Path.Combine(artifacts, "browser-diagnostics", "sanitized-page.html"));
            string browserError = await File.ReadAllTextAsync(Path.Combine(artifacts, "browser-diagnostics", "browser-error.json"));

            Assert.Multiple(() =>
            {
                Assert.That(entry.Kind, Is.EqualTo(BrowserStopKind.None));
                Assert.That(batch.Links, Has.Count.EqualTo(25));
                Assert.That(extracted.StopSignal.Kind, Is.EqualTo(BrowserStopKind.None));
                Assert.That(extracted.Capture, Is.Not.Null);
                Assert.That(extracted.Capture!.DisplayName, Does.StartWith("Synthetic Founder fixture-"));
                Assert.That(extracted.Capture.StructuredFields.GetProperty("sections").EnumerateObject().Count(), Is.EqualTo(2));
                Assert.That(extracted.Capture.RawText, Does.Not.Contain("portrait"));
                Assert.That(extracted.Capture.RawHtml, Is.Not.Null);
                Assert.That(extracted.Capture.RawHtml, Does.Not.Contain("fixture-sensitive-value"));
                Assert.That(extracted.Capture.RawHtml, Does.Not.Contain("fixture-secret"));
                Assert.That(extracted.Capture.RawHtml, Does.Not.Contain("<img"));
                Assert.That(extracted.Capture.ProfileUrl, Does.Not.Contain("session="));
                Assert.That(competing.StopSignal.Kind, Is.EqualTo(BrowserStopKind.ProfileLocked));
                Assert.That(diagnostics.Select(item => item.RelativePath), Is.EquivalentTo(ExpectedDiagnosticPaths));
                Assert.That(sanitized, Does.Not.Contain("fixture-sensitive-value"));
                Assert.That(sanitized, Does.Not.Contain("fixture-secret"));
                Assert.That(sanitized, Does.Not.Contain("<img"));
                Assert.That(sanitized, Does.Not.Contain("private-photo"));
                Assert.That(browserError, Does.Not.Contain("fixture-secret"));
                Assert.That(diagnostics, Has.All.Matches<BrowserDiagnosticArtifact>(item =>
                    item.RelativePath.StartsWith("browser-diagnostics/", StringComparison.Ordinal)
                    && item.FileSize > 0
                    && item.DeleteAfterUtc > DateTimeOffset.UtcNow.AddDays(13)));
                Assert.That(diagnostics.Single(item => item.RelativePath.EndsWith("sanitized-page.html", StringComparison.Ordinal)).FileSize,
                    Is.LessThanOrEqualTo(2 * 1024 * 1024));
            });
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Test]
    public async Task DetectorMapsAuthenticationAndChallengeFixturesWithoutFailover()
    {
        await using StartupSchoolFixtureServer server = StartupSchoolFixtureServer.Start();
        string root = NewRoot();
        try
        {
            var manager = new PlaywrightBrowserSessionManager(root);
            var detector = new PlaywrightBrowserChallengeDetector();
            async ValueTask<BrowserStopSignal> OpenAsync(string account, string url)
            {
                StartupSchoolSourceOptions options = server.CreateOptions(url);
                BrowserSessionOpenResult opened = await manager.OpenAsync(new(
                    account,
                    BrowserProfilePath.Resolve(root, account),
                    Headless: true,
                    BrowserChannel: null,
                    NavigationTimeoutSeconds: 10));
                Assert.That(opened.IsSuccess, Is.True);
                await using IBrowserSession browser = opened.Session!;
                return await new StartupSchoolSourceAdapter(detector).OpenEntryAsync(browser, options);
            }

            BrowserStopSignal login = await OpenAsync("login-account", server.LoginUrl);
            BrowserStopSignal challenge = await OpenAsync("challenge-account", server.ChallengeUrl);

            Assert.Multiple(() =>
            {
                Assert.That(login.Kind, Is.EqualTo(BrowserStopKind.ReauthenticationRequired));
                Assert.That(challenge.Kind, Is.EqualTo(BrowserStopKind.ChallengeDetected));
            });
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Test]
    public async Task SourceAdapterStopsWhenDiscoveredLinkLeavesAllowedHost()
    {
        await using StartupSchoolFixtureServer server = StartupSchoolFixtureServer.Start();
        string root = NewRoot();
        try
        {
            StartupSchoolSourceOptions options = server.CreateOptions(server.UnexpectedHostUrl);
            BrowserSessionOpenResult opened = await new PlaywrightBrowserSessionManager(root).OpenAsync(new(
                "unexpected-host-account",
                BrowserProfilePath.Resolve(root, "unexpected-host-account"),
                Headless: true,
                BrowserChannel: null,
                NavigationTimeoutSeconds: 10));
            Assert.That(opened.IsSuccess, Is.True);
            await using IBrowserSession browser = opened.Session!;
            var source = new StartupSchoolSourceAdapter(new PlaywrightBrowserChallengeDetector());

            Assert.That((await source.OpenEntryAsync(browser, options)).Kind, Is.EqualTo(BrowserStopKind.None));
            ProfileDiscoveryBatch batch = await source.GetNextBatchAsync(browser, options, null, 0);

            Assert.Multiple(() =>
            {
                Assert.That(batch.StopSignal.Kind, Is.EqualTo(BrowserStopKind.UnexpectedHost));
                Assert.That(batch.StopSignal.ReasonCode, Is.EqualTo("browser.source.unexpectedProfileHost"));
                Assert.That(batch.Links, Is.Empty);
            });
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestCase("load-more")]
    [TestCase("infinite-scroll")]
    public async Task SourceAdapterSupportsBoundedIncrementalDiscoveryModes(string mode)
    {
        await using StartupSchoolFixtureServer server = StartupSchoolFixtureServer.Start();
        string root = NewRoot();
        try
        {
            string entry = mode == "load-more" ? server.LoadMoreUrl : server.InfiniteUrl;
            StartupSchoolSourceOptions options = server.CreateOptions(entry) with { DiscoveryMode = mode };
            BrowserSessionOpenResult opened = await new PlaywrightBrowserSessionManager(root).OpenAsync(new(
                $"{mode.Replace("-scroll", string.Empty, StringComparison.Ordinal)}-account",
                BrowserProfilePath.Resolve(root, $"{mode.Replace("-scroll", string.Empty, StringComparison.Ordinal)}-account"),
                Headless: true,
                BrowserChannel: null,
                NavigationTimeoutSeconds: 10));
            Assert.That(opened.IsSuccess, Is.True);
            await using IBrowserSession browser = opened.Session!;
            var source = new StartupSchoolSourceAdapter(new PlaywrightBrowserChallengeDetector());

            Assert.That((await source.OpenEntryAsync(browser, options)).Kind, Is.EqualTo(BrowserStopKind.None));
            ProfileDiscoveryBatch first = await source.GetNextBatchAsync(browser, options, null, 0);
            ProfileDiscoveryBatch second = await source.GetNextBatchAsync(browser, options, first.Continuation, first.PageOrScrollMarker);
            ProfileDiscoveryBatch third = await source.GetNextBatchAsync(browser, options, second.Continuation, second.PageOrScrollMarker);

            Assert.Multiple(() =>
            {
                Assert.That(first.Links, Has.Count.EqualTo(1));
                Assert.That(first.Exhausted, Is.False);
                Assert.That(second.Links, Has.Count.EqualTo(2));
                Assert.That(third.Exhausted, Is.True);
            });
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Test]
    public async Task DiscoveryServiceCommitsProfilesAndCheckpointsBeforeReturning()
    {
        await using StartupSchoolFixtureServer server = StartupSchoolFixtureServer.Start();
        await using TemporaryFounderScoutDatabase temporary = await TemporaryFounderScoutDatabase.CreateAsync();
        FounderScoutRepository repository = temporary.CreateRepository();
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        var accountRecord = new BrowserAccount(
            "fixture-account",
            "Fixture account",
            "browser/fixture-account",
            true,
            BrowserSessionStatus.Healthy,
            ["default"],
            nowUtc,
            null,
            null,
            null,
            null,
            null,
            nowUtc,
            nowUtc,
            1);
        BrowserAccount account = await ((IBrowserAccountRepository)repository).UpsertAsync(accountRecord);
        var segmentRecord = new DiscoverySegment(
            "default",
            "Default fixture",
            true,
            100,
            "{\"version\":\"1.0\"}",
            account.Id,
            null,
            0,
            0,
            0,
            0,
            0,
            null,
            nowUtc,
            nowUtc,
            1);
        DiscoverySegment segment = await ((IDiscoverySegmentRepository)repository).UpsertAsync(segmentRecord);
        StartupSchoolSourceOptions options = server.CreateOptions();
        var detector = new PlaywrightBrowserChallengeDetector();
        var service = new FounderScoutDiscoveryService(
            new PlaywrightBrowserSessionManager(temporary.Database.DataDirectory),
            new StartupSchoolSourceAdapter(detector),
            new PlaywrightProfileCaptureExtractor(detector),
            detector,
            new PlaywrightBrowserDiagnosticCapture(),
            new FounderScoutCaptureCommitService(
                new FounderScoutRawArtifactStore(temporary.Database.DataDirectory, temporary.Database.SnapshotsDirectory),
                repository,
                repository),
            repository,
            repository,
            repository,
            repository,
            TimeProvider.System);

        FounderScoutDiscoveryResult result = await service.DiscoverAsync(new(
            account,
            segment,
            new(true, 1, 2, 10, 60, 600, 600, 8, true, true, true, true, false),
            options,
            BrowserProfilePath.Resolve(temporary.Database.DataDirectory, account.Id),
            Path.Combine(temporary.Root, "run-artifacts"),
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D"),
            30,
            14));
        FounderScoutDomainCounts counts = await repository.GetCountsAsync();
        DiscoveryCheckpoint? checkpoint = await repository.GetLatestAsync(account.Id, segment.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result.Completion, Is.EqualTo(FounderScoutDiscoveryCompletion.Capped));
            Assert.That(result.NewSnapshots, Is.EqualTo(2));
            Assert.That(counts.Candidates, Is.EqualTo(2));
            Assert.That(counts.Snapshots, Is.EqualTo(2));
            Assert.That(checkpoint, Is.Not.Null);
        });
    }

    [Test]
    public void DiagnosticSanitizerRemovesUrlSecretsAndResidualSensitiveValues()
    {
        string url = BrowserDiagnosticSanitizer.SanitizeUrl("https://example.test/profile/1?token=secret#fragment");
        string html = BrowserDiagnosticSanitizer.SanitizeHtml("<meta content=\"csrf=meta-secret\"><a href=\"/profile?session=link-secret\" data-access-token=\"data-secret\">csrf=visible-secret</a><form action=\"/submit?token=action-secret\"><input value=\"input-secret\"></form><div>access_token=abc</div>");

        Assert.Multiple(() =>
        {
            Assert.That(url, Is.EqualTo("https://example.test/profile/1"));
            Assert.That(html, Does.Not.Contain("meta-secret"));
            Assert.That(html, Does.Not.Contain("link-secret"));
            Assert.That(html, Does.Not.Contain("data-secret"));
            Assert.That(html, Does.Not.Contain("visible-secret"));
            Assert.That(html, Does.Not.Contain("action-secret"));
            Assert.That(html, Does.Not.Contain("input-secret"));
            Assert.That(html, Does.Not.Contain("abc"));
            Assert.That(html, Does.Contain("[redacted]"));
        });
    }

    [Test]
    public void RecommendedScheduleUsesCompletionDelayAndTimeoutMargin()
    {
        FounderScoutDiscoverySettings settings = new(true, 1, 20, 40, 60, 600, 600, 8, true, true, true, true, false);
        HomeBusinessAssistant.Application.Scheduling.FixedDelayScheduleDefinition definition =
            FounderScoutDiscoverySchedulePolicy.CreateDefinition(settings);

        Assert.Multiple(() =>
        {
            Assert.That(definition.Delay, Is.EqualTo(TimeSpan.FromMinutes(10)));
            Assert.That(definition.StartImmediately, Is.True);
            Assert.That(FounderScoutDiscoverySchedulePolicy.GetTimeoutSeconds(settings), Is.EqualTo(720));
        });
    }

    [TestCase(BrowserStopKind.AccessDenied, BrowserSessionStatus.AccessDenied)]
    [TestCase(BrowserStopKind.Throttled, BrowserSessionStatus.Throttled)]
    [TestCase(BrowserStopKind.ChallengeDetected, BrowserSessionStatus.ChallengeDetected)]
    [TestCase(BrowserStopKind.UnexpectedHost, BrowserSessionStatus.ParserFailure)]
    [TestCase(BrowserStopKind.ParserFailure, BrowserSessionStatus.ParserFailure)]
    [TestCase(BrowserStopKind.NavigationFailure, BrowserSessionStatus.Unknown)]
    public async Task DiscoveryServiceMapsEverySourceStopWithoutOpeningAnotherAccount(
        BrowserStopKind stopKind,
        BrowserSessionStatus expectedStatus)
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        BrowserAccount account = Account(nowUtc);
        DiscoverySegment segment = Segment(nowUtc);
        var session = new FakeBrowserSession();
        var sessions = new Mock<IBrowserSessionManager>();
        sessions.Setup(item => item.OpenAsync(It.IsAny<BrowserSessionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowserSessionOpenResult(session, BrowserStopSignal.None));
        var source = new Mock<IProfileDiscoverySource>();
        BrowserStopSignal stop = new(stopKind, $"fixture.{stopKind}", "Synthetic stop.", stopKind == BrowserStopKind.NavigationFailure);
        source.Setup(item => item.OpenEntryAsync(session, It.IsAny<StartupSchoolSourceOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stop);
        var diagnostics = new Mock<IBrowserDiagnosticCapture>();
        diagnostics.Setup(item => item.CaptureAsync(session, It.IsAny<string>(), stop, 14, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var accounts = new Mock<IBrowserAccountRepository>();
        accounts.Setup(item => item.GetAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        accounts.Setup(item => item.TransitionHealthAsync(account.Id, expectedStatus, stop.ReasonCode, stop.Message, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account with { SessionStatus = expectedStatus });
        var checkpoints = new Mock<IDiscoveryCheckpointRepository>();
        var history = new Mock<IDiscoveryHistoryReader>();
        history.Setup(item => item.CountDistinctCandidatesCapturedSinceAsync(account.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        FounderScoutDiscoveryService service = CreatePolicyService(
            sessions.Object,
            source.Object,
            diagnostics.Object,
            checkpoints.Object,
            history.Object,
            accounts.Object);
        FounderScoutDiscoveryResult result = await service.DiscoverAsync(Request(account, segment));

        Assert.Multiple(() =>
        {
            Assert.That(result.Completion, Is.EqualTo(FounderScoutDiscoveryCompletion.Stopped));
            Assert.That(result.StopSignal.Kind, Is.EqualTo(stopKind));
            Assert.That(session.Disposed, Is.True);
        });
        accounts.Verify(item => item.TransitionHealthAsync(account.Id, expectedStatus, stop.ReasonCode, stop.Message, null, It.IsAny<CancellationToken>()), Times.Once);
        sessions.Verify(item => item.OpenAsync(It.IsAny<BrowserSessionRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task DailyCapAndDisabledStateReturnBeforeOpeningBrowser()
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        BrowserAccount account = Account(nowUtc);
        DiscoverySegment segment = Segment(nowUtc);
        var sessions = new Mock<IBrowserSessionManager>();
        var history = new Mock<IDiscoveryHistoryReader>();
        history.Setup(item => item.CountDistinctCandidatesCapturedSinceAsync(account.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(60);
        FounderScoutDiscoveryService service = CreatePolicyService(
            sessions.Object,
            Mock.Of<IProfileDiscoverySource>(),
            Mock.Of<IBrowserDiagnosticCapture>(),
            Mock.Of<IDiscoveryCheckpointRepository>(),
            history.Object,
            Mock.Of<IBrowserAccountRepository>());

        FounderScoutDiscoveryResult capped = await service.DiscoverAsync(Request(account, segment));
        FounderScoutDiscoveryRequest disabledRequest = Request(account, segment) with
        {
            Limits = Request(account, segment).Limits with { Enabled = false },
        };
        FounderScoutDiscoveryResult disabled = await service.DiscoverAsync(disabledRequest);

        Assert.Multiple(() =>
        {
            Assert.That(capped.Completion, Is.EqualTo(FounderScoutDiscoveryCompletion.Capped));
            Assert.That(capped.CompletionReasonCode, Is.EqualTo("discovery.dailyLimit"));
            Assert.That(disabled.Completion, Is.EqualTo(FounderScoutDiscoveryCompletion.NoWork));
            Assert.That(disabled.CompletionReasonCode, Is.EqualTo("discovery.disabled"));
        });
        sessions.VerifyNoOtherCalls();
    }

    [Test]
    public async Task RuntimeLimitStopsBeforeRequestingAnotherSourceBatch()
    {
        DateTimeOffset nowUtc = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        BrowserAccount account = Account(nowUtc);
        DiscoverySegment segment = Segment(nowUtc);
        var session = new FakeBrowserSession();
        var sessions = new Mock<IBrowserSessionManager>();
        sessions.Setup(item => item.OpenAsync(It.IsAny<BrowserSessionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowserSessionOpenResult(session, BrowserStopSignal.None));
        var source = new Mock<IProfileDiscoverySource>();
        source.Setup(item => item.OpenEntryAsync(session, It.IsAny<StartupSchoolSourceOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BrowserStopSignal.None);
        var history = new Mock<IDiscoveryHistoryReader>();
        history.Setup(item => item.CountDistinctCandidatesCapturedSinceAsync(account.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var accounts = new Mock<IBrowserAccountRepository>();
        accounts.Setup(item => item.GetAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        var segments = new Mock<IDiscoverySegmentRepository>();
        segments.Setup(item => item.GetAsync(segment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(segment);
        FounderScoutDiscoveryService service = CreatePolicyService(
            sessions.Object,
            source.Object,
            Mock.Of<IBrowserDiagnosticCapture>(),
            Mock.Of<IDiscoveryCheckpointRepository>(),
            history.Object,
            accounts.Object,
            segments.Object,
            new IncrementingTimeProvider(nowUtc, TimeSpan.FromSeconds(2)));
        FounderScoutDiscoveryRequest request = Request(account, segment) with
        {
            Limits = Request(account, segment).Limits with { MaxRuntimeSeconds = 1 },
        };

        FounderScoutDiscoveryResult result = await service.DiscoverAsync(request);

        Assert.Multiple(() =>
        {
            Assert.That(result.Completion, Is.EqualTo(FounderScoutDiscoveryCompletion.Capped));
            Assert.That(result.CompletionReasonCode, Is.EqualTo("discovery.runtimeLimit"));
            Assert.That(session.Disposed, Is.True);
        });
        source.Verify(item => item.GetNextBatchAsync(
            It.IsAny<IBrowserSession>(),
            It.IsAny<StartupSchoolSourceOptions>(),
            It.IsAny<string?>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task IncompatibleCheckpointRestartsFromSafeSourceBeginning()
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        BrowserAccount account = Account(nowUtc);
        DiscoverySegment segment = Segment(nowUtc);
        var session = new FakeBrowserSession();
        var sessions = new Mock<IBrowserSessionManager>();
        sessions.Setup(item => item.OpenAsync(It.IsAny<BrowserSessionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowserSessionOpenResult(session, BrowserStopSignal.None));
        var source = new Mock<IProfileDiscoverySource>();
        source.Setup(item => item.OpenEntryAsync(session, It.IsAny<StartupSchoolSourceOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BrowserStopSignal.None);
        source.Setup(item => item.GetNextBatchAsync(session, It.IsAny<StartupSchoolSourceOptions>(), null, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProfileDiscoveryBatch([], null, 1, true, BrowserStopSignal.None));
        var checkpoints = new Mock<IDiscoveryCheckpointRepository>();
        checkpoints.Setup(item => item.GetLatestAsync(account.Id, segment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new DiscoveryCheckpoint(
            Guid.NewGuid(),
            account.Id,
            segment.Id,
            Guid.NewGuid(),
            "unsafe-last-key",
            "{\"adapterVersion\":\"obsolete-adapter\",\"continuation\":\"https://outside.example/next\",\"pageOrScrollMarker\":99,\"seenProfileKeys\":[\"unsafe\"],\"linkSetHash\":\"hash\",\"viewed\":1,\"created\":1,\"known\":0}",
            nowUtc,
            "profile-committed",
            "1.0"));
        var history = new Mock<IDiscoveryHistoryReader>();
        history.Setup(item => item.CountDistinctCandidatesCapturedSinceAsync(account.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var accounts = new Mock<IBrowserAccountRepository>();
        accounts.Setup(item => item.GetAsync(account.Id, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        var segments = new Mock<IDiscoverySegmentRepository>();
        segments.Setup(item => item.GetAsync(segment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(segment);
        FounderScoutDiscoveryService service = CreatePolicyService(
            sessions.Object,
            source.Object,
            Mock.Of<IBrowserDiagnosticCapture>(),
            checkpoints.Object,
            history.Object,
            accounts.Object,
            segments.Object);

        FounderScoutDiscoveryResult result = await service.DiscoverAsync(Request(account, segment));

        Assert.That(result.Completion, Is.EqualTo(FounderScoutDiscoveryCompletion.NoWork));
        source.Verify(item => item.GetNextBatchAsync(session, It.IsAny<StartupSchoolSourceOptions>(), null, 0, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void CancellationClosesTheAcquiredBrowserSession()
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        BrowserAccount account = Account(nowUtc);
        DiscoverySegment segment = Segment(nowUtc);
        var session = new FakeBrowserSession();
        var sessions = new Mock<IBrowserSessionManager>();
        sessions.Setup(item => item.OpenAsync(It.IsAny<BrowserSessionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowserSessionOpenResult(session, BrowserStopSignal.None));
        using var cancellation = new CancellationTokenSource();
        var history = new Mock<IDiscoveryHistoryReader>();
        history.Setup(item => item.CountDistinctCandidatesCapturedSinceAsync(account.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        FounderScoutDiscoveryService service = CreatePolicyService(
            sessions.Object,
            new CancellingSource(cancellation),
            Mock.Of<IBrowserDiagnosticCapture>(),
            Mock.Of<IDiscoveryCheckpointRepository>(),
            history.Object,
            Mock.Of<IBrowserAccountRepository>());

        Assert.That(async () => await service.DiscoverAsync(Request(account, segment), cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(session.Disposed, Is.True);
    }

    private static FounderScoutDiscoveryService CreatePolicyService(
        IBrowserSessionManager sessions,
        IProfileDiscoverySource source,
        IBrowserDiagnosticCapture diagnostics,
        IDiscoveryCheckpointRepository checkpoints,
        IDiscoveryHistoryReader history,
        IBrowserAccountRepository accounts,
        IDiscoverySegmentRepository? segments = null,
        TimeProvider? timeProvider = null)
    {
        var candidates = new Mock<ICandidateRepository>();
        var snapshots = new Mock<IProfileSnapshotRepository>();
        return new(
            sessions,
            source,
            Mock.Of<IProfileCaptureExtractor>(),
            Mock.Of<IBrowserChallengeDetector>(),
            diagnostics,
            new FounderScoutCaptureCommitService(Mock.Of<IFounderScoutRawArtifactStore>(), candidates.Object, snapshots.Object),
            checkpoints,
            history,
            accounts,
            segments ?? Mock.Of<IDiscoverySegmentRepository>(),
            timeProvider ?? TimeProvider.System);
    }

    private static FounderScoutDiscoveryRequest Request(BrowserAccount account, DiscoverySegment segment) => new(
        account,
        segment,
        new(true, 1, 20, 40, 60, 600, 600, 8, true, true, true, true, false),
        StartupSchoolSourceOptions.Default,
        Path.Combine(Path.GetTempPath(), "hba-policy-profile"),
        Path.Combine(Path.GetTempPath(), "hba-policy-artifacts"),
        Guid.NewGuid(),
        Guid.NewGuid().ToString("D"),
        30,
        14);

    private static BrowserAccount Account(DateTimeOffset nowUtc) => new(
        "fixture-account",
        "Fixture account",
        "browser/fixture-account",
        true,
        BrowserSessionStatus.Healthy,
        ["default"],
        nowUtc,
        null,
        null,
        null,
        null,
        null,
        nowUtc,
        nowUtc,
        1);

    private static DiscoverySegment Segment(DateTimeOffset nowUtc) => new(
        "default",
        "Default fixture",
        true,
        100,
        "{\"version\":\"1.0\"}",
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
        1);

    private sealed class FakeBrowserSession : IBrowserSession
    {
        public string CurrentUrl => "https://example.invalid/list";

        public bool IsClosed => false;

        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancellingSource(CancellationTokenSource cancellation) : IProfileDiscoverySource
    {
        public ValueTask<BrowserStopSignal> OpenEntryAsync(
            IBrowserSession session,
            StartupSchoolSourceOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return ValueTask.FromCanceled<BrowserStopSignal>(cancellation.Token);
        }

        public ValueTask<BrowserStopSignal> WaitForAuthenticationAsync(
            IBrowserSession session,
            StartupSchoolSourceOptions options,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ProfileDiscoveryBatch> GetNextBatchAsync(
            IBrowserSession session,
            StartupSchoolSourceOptions options,
            string? continuation,
            int pageOrScrollMarker,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class IncrementingTimeProvider(DateTimeOffset utcNow, TimeSpan increment) : TimeProvider
    {
        private DateTimeOffset current = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            DateTimeOffset result = current;
            current = current.Add(increment);
            return result;
        }
    }

    private static string NewRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-founder-browser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        string full = Path.GetFullPath(root);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(full).StartsWith("hba-founder-browser-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to delete an unexpected browser-test directory.");
        }

        if (Directory.Exists(full))
        {
            Directory.Delete(full, recursive: true);
        }
    }
}
