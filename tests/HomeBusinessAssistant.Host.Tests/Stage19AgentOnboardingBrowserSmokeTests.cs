using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FounderScout.Application;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Host.Logging;
using HomeBusinessAssistant.Infrastructure.Configuration;
using HomeBusinessAssistant.Infrastructure.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Moq;

namespace HomeBusinessAssistant.Host.Tests;

internal sealed class Stage19AgentOnboardingBrowserSmokeTests
{
    private static readonly AgentId SampleAgentId = AgentId.Parse("sample-business-agent");
    private static readonly string[] DefaultExtensions = [".txt", ".csv", ".json"];
    private static readonly string[] PrimaryNavigation = ["Dashboard", "Founder Scout"];
    private static readonly string[] FounderScoutNavigation = ["Overview", "Candidates", "Settings"];
    private static readonly string[] TextExtension = [".txt"];

    [Test]
    [Category("Stage20RenderedUi")]
    public async Task FounderScoutSimpleStartIsTheOnlyPrimaryWorkflowAndIsResponsive()
    {
        string repository = FindRepositoryRoot();
        string root = Path.Combine(Path.GetTempPath(), $"hba-stage20-rendered-{Guid.NewGuid():N}");
        string data = Path.Combine(root, "data");
        string agents = Path.Combine(root, "agents");
        Directory.CreateDirectory(agents);
        string url = $"http://127.0.0.1:{GetAvailablePort()}";
        _ = await PrepareFreshSelectionAsync(repository, data, agents);
        HostBootstrapSettings settings = CreateSettings(repository, data, agents, url);
        try
        {
            await using HostRuntime runtime = await HostRuntime.CreateAsync(settings, HostLogFactory.Create(data));
            await runtime.InitializeAsync();
            ManagementManualRunRequest? captured = null;
            var commands = new Mock<IManagementCommandService>(MockBehavior.Strict);
            commands.Setup(service => service.RunNowAsync(
                    It.IsAny<ManagementManualRunRequest>(),
                    It.IsAny<CancellationToken>()))
                .Callback<ManagementManualRunRequest, CancellationToken>((request, _) => captured = request)
                .ReturnsAsync(new OccurrenceDispatchResult(OccurrenceId.New(), true, false, true, 42, "runner.started"));
            HostManagementComposition management = runtime.WebComposition.Management! with { Commands = commands.Object };
            HostWebComposition webOnly = runtime.WebComposition with { HostedServices = [], Management = management };
            await using WebApplication application = HostApplication.Build(
                ["--urls", url, "--contentRoot", Path.Combine(repository, "src", "HomeBusinessAssistant.Host")],
                webOnly);
            await application.StartAsync();

            using IPlaywright playwright = await Playwright.CreateAsync();
            await using IBrowser browser = await playwright.Chromium.LaunchAsync(new()
            {
                Headless = true,
                Args = ["--auth-server-allowlist=127.0.0.1", "--auth-negotiate-delegate-allowlist=127.0.0.1"],
            });
            await using IBrowserContext context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1_440, Height = 1_000 } });
            IPage page = await context.NewPageAsync();
            var browserErrors = new List<string>();
            page.Console += (_, message) => { if (message.Type == "error") browserErrors.Add(message.Text); };
            page.PageError += (_, error) => browserErrors.Add(error);

            IResponse? response = await page.GotoAsync(
                $"{url}/FounderScout",
                new() { WaitUntil = WaitUntilState.NetworkIdle });
            string body = await page.Locator("body").InnerTextAsync();
            IReadOnlyList<string> primaryNavigation = await page.Locator("header.site-header nav a").AllTextContentsAsync();
            IReadOnlyList<string> founderScoutNavigation = await page.Locator("nav.tabs a").AllTextContentsAsync();
            string delay = await page.Locator("#DiscoveryDelaySeconds").InputValueAsync();
            int passwordInputs = await page.Locator("input[type=password]").CountAsync();
            int antiforgeryStatus = await page.EvaluateAsync<int>(
                "async () => (await fetch('/FounderScout?handler=Start', { method: 'POST' })).status");
            Assert.Multiple(() =>
            {
                Assert.That(response?.Status, Is.EqualTo(200));
                Assert.That(antiforgeryStatus, Is.EqualTo(400));
                Assert.That(primaryNavigation, Is.EqualTo(PrimaryNavigation));
                Assert.That(founderScoutNavigation, Is.EqualTo(FounderScoutNavigation));
                Assert.That(body, Does.Contain("Start Founder Scout"));
                Assert.That(body, Does.Contain("enter your password only in the Startup School browser window"));
                Assert.That(passwordInputs, Is.Zero);
                Assert.That(delay, Is.EqualTo("5"));
            });
            browserErrors.Clear(); // The deliberate tokenless POST reports its expected HTTP 400 to the console.
            await page.Locator("header.site-header nav").GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true }).ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            bool dashboardHeadingVisible = await page.GetByRole(AriaRole.Heading, new() { Name = "Good work starts with a quiet system." }).IsVisibleAsync();
            Assert.Multiple(() =>
            {
                Assert.That(new Uri(page.Url).AbsolutePath, Is.EqualTo("/"));
                Assert.That(dashboardHeadingVisible, Is.True);
            });
            await page.Locator("header.site-header nav").GetByRole(AriaRole.Link, new() { Name = "Founder Scout", Exact = true }).ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            Assert.That(await page.Locator("nav.tabs a").AllTextContentsAsync(), Is.EqualTo(FounderScoutNavigation));
            await page.Locator("#DiscoveryDelaySeconds").FillAsync("7");
            await page.GetByRole(AriaRole.Button, new() { Name = "Start Founder Scout" }).ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            using JsonDocument arguments = JsonDocument.Parse(captured?.ArgumentsJson ?? "{}");
            string statusText = await page.GetByRole(AriaRole.Status).InnerTextAsync();
            Assert.Multiple(() =>
            {
                Assert.That(captured, Is.Not.Null);
                Assert.That(captured!.CommandName, Is.EqualTo("start"));
                Assert.That(arguments.RootElement.GetProperty("accountId").GetString(), Is.EqualTo(FounderScoutSimpleMode.BrowserAccountId));
                Assert.That(arguments.RootElement.GetProperty("segmentId").GetString(), Is.EqualTo(FounderScoutSimpleMode.DiscoverySegmentId));
                Assert.That(arguments.RootElement.GetProperty("discoveryDelaySeconds").GetInt32(), Is.EqualTo(7));
                Assert.That(statusText, Does.Contain("discovery continues automatically"));
            });
            await AssertFitsViewportAsync(page);
            await page.ScreenshotAsync(new() { Path = Path.Combine(Path.GetTempPath(), "hba-founder-simple-start-desktop.png"), FullPage = true });

            IResponse? settingsResponse = await page.GotoAsync(
                $"{url}/FounderScout/Settings",
                new() { WaitUntil = WaitUntilState.NetworkIdle });
            string settingsBody = await page.Locator("body").InnerTextAsync();
            string modelName = await page.Locator("#Form_ModelName").InputValueAsync();
            string protectedKeyValue = await page.Locator("#openai-api-key").InputValueAsync();
            await page.GotoAsync($"{url}/FounderScout/Candidates", new() { WaitUntil = WaitUntilState.NetworkIdle });
            bool analyzeDisabled = await page.GetByRole(AriaRole.Button, new() { Name = "Complete AI setup first" }).IsDisabledAsync();
            await page.GotoAsync($"{url}/FounderScout/Settings", new() { WaitUntil = WaitUntilState.NetworkIdle });
            Assert.Multiple(() =>
            {
                Assert.That(settingsResponse?.Status, Is.EqualTo(200));
                Assert.That(settingsBody, Does.Contain("Founder context and evaluation instructions"));
                Assert.That(settingsBody, Does.Contain("The API cannot read ChatGPT Memory or old chats"));
                Assert.That(modelName, Is.EqualTo("gpt-5.4-mini"));
                Assert.That(protectedKeyValue, Is.Empty);
                Assert.That(analyzeDisabled, Is.True);
            });
            await page.Locator("#Form_AdditionalContext").FillAsync("Copied local founder context for the focused settings test.");
            await page.GetByRole(AriaRole.Button, new() { Name = "Save AI settings" }).ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            string flashText = await page.GetByRole(AriaRole.Status).InnerTextAsync();
            string savedContext = await page.Locator("#Form_AdditionalContext").InputValueAsync();
            string savedProtectedKeyValue = await page.Locator("#openai-api-key").InputValueAsync();
            Assert.Multiple(() =>
            {
                Assert.That(flashText, Does.Contain("AI settings were saved"));
                Assert.That(savedContext, Does.Contain("Copied local founder context"));
                Assert.That(savedProtectedKeyValue, Is.Empty);
            });

            const string testApiKey = "sk-test-never-render-this-value-1234567890";
            await page.Locator("#openai-api-key").FillAsync(testApiKey);
            await page.GetByRole(AriaRole.Button, new() { Name = "Protect API key" }).ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            string protectedSettingsBody = await page.Locator("body").InnerTextAsync();
            string reloadedProtectedKeyValue = await page.Locator("#openai-api-key").InputValueAsync();
            await page.GotoAsync($"{url}/FounderScout/Candidates", new() { WaitUntil = WaitUntilState.NetworkIdle });
            bool analyzeEnabled = await page.GetByRole(AriaRole.Button, new() { Name = "Analyze candidates now" }).IsEnabledAsync();
            Assert.Multiple(() =>
            {
                Assert.That(protectedSettingsBody, Does.Contain("API key was protected"));
                Assert.That(protectedSettingsBody, Does.Not.Contain(testApiKey));
                Assert.That(reloadedProtectedKeyValue, Is.Empty);
                Assert.That(analyzeEnabled, Is.True);
            });

            await page.SetViewportSizeAsync(390, 844);
            await AssertFitsViewportAsync(page);
            await page.ScreenshotAsync(new() { Path = Path.Combine(Path.GetTempPath(), "hba-founder-simple-start-narrow.png"), FullPage = true });
            Assert.That(browserErrors, Is.Empty);
            await application.StopAsync();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Category("BrowserSmoke")]
    public async Task ServeStage19AgentSelectionForInteractiveBrowserQa()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("HBA_STAGE19_BROWSER_SMOKE"), "1", StringComparison.Ordinal))
        {
            Assert.Ignore("Set HBA_STAGE19_BROWSER_SMOKE=1 to run the interactive Stage 19 browser fixture.");
        }

        string repository = FindRepositoryRoot();
        string root = Path.Combine(Path.GetTempPath(), "hba-stage19-browser-smoke");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        string data = Path.Combine(root, "data");
        string agents = Path.Combine(root, "agents");
        Directory.CreateDirectory(agents);
        const string url = "http://127.0.0.1:5195";
        try
        {
            Guid sessionId = await PrepareFreshSelectionAsync(repository, data, agents);
            HostBootstrapSettings settings = CreateSettings(repository, data, agents, url);
            await using HostRuntime runtime = await HostRuntime.CreateAsync(settings, HostLogFactory.Create(data));
            HostWebComposition webOnly = runtime.WebComposition with { HostedServices = [] };
            await using WebApplication application = HostApplication.Build(
                ["--urls", url, "--contentRoot", Path.Combine(repository, "src", "HomeBusinessAssistant.Host")],
                webOnly);
            await application.StartAsync();
            await File.WriteAllTextAsync(Path.Combine(root, "ready.txt"), $"{url}/Onboarding/Agents?sessionId={sessionId:D}");
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(10);
            while (!File.Exists(Path.Combine(root, "stop.txt")) && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(250);
            }

            Assert.That(File.Exists(Path.Combine(root, "stop.txt")), Is.True, "Browser fixture timed out before stop.txt was created.");
            await application.StopAsync();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Category("Stage19RenderedUi")]
    public async Task GenericAgentSelectionConfigurationSecretRestartAndRemovalAreSafeAtDesktopAndNarrowWidths()
    {
        string repository = FindRepositoryRoot();
        string root = Path.Combine(Path.GetTempPath(), $"hba-stage19-rendered-{Guid.NewGuid():N}");
        string data = Path.Combine(root, "data");
        string agents = Path.Combine(root, "agents");
        Directory.CreateDirectory(agents);
        int port = GetAvailablePort();
        string url = $"http://127.0.0.1:{port}";
        string protectedValue = $"stage19-secret-{Guid.NewGuid():N}";
        Guid sessionId = await PrepareFreshSelectionAsync(repository, data, agents);
        HostBootstrapSettings settings = CreateSettings(repository, data, agents, url);
        try
        {
            await using (HostRuntime runtime = await HostRuntime.CreateAsync(settings, HostLogFactory.Create(data)))
            {
                HostWebComposition webOnly = runtime.WebComposition with { HostedServices = [] };
                await using WebApplication application = HostApplication.Build(
                    ["--urls", url, "--contentRoot", Path.Combine(repository, "src", "HomeBusinessAssistant.Host")],
                    webOnly);
                await application.StartAsync();

                using (var client = new HttpClient(new HttpClientHandler { UseDefaultCredentials = true }) { BaseAddress = new(url) })
                {
                    using HttpResponseMessage missingAntiforgery = await client.PostAsync(
                        $"/Onboarding/Agents?handler=Save&sessionId={sessionId:D}",
                        new StringContent(string.Empty));
                    Assert.That(missingAntiforgery.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                }

                using IPlaywright playwright = await Playwright.CreateAsync();
                await using IBrowser browser = await playwright.Chromium.LaunchAsync(new()
                {
                    Headless = true,
                    Args = ["--auth-server-allowlist=127.0.0.1", "--auth-negotiate-delegate-allowlist=127.0.0.1"],
                });
                await using IBrowserContext context = await browser.NewContextAsync(new()
                {
                    ViewportSize = new() { Width = 1_440, Height = 1_000 },
                });
                IPage page = await context.NewPageAsync();
                var browserErrors = new List<string>();
                page.Console += (_, message) =>
                {
                    if (message.Type == "error") browserErrors.Add(message.Text);
                };
                page.PageError += (_, error) => browserErrors.Add(error);

                IResponse? response = await page.GotoAsync(
                    $"{url}/Onboarding/Agents?sessionId={sessionId:D}",
                    new() { WaitUntil = WaitUntilState.NetworkIdle });
                int initialCardCount = await page.Locator(".agent-choice-card").CountAsync();
                int initialSelectedCount = await page.Locator(".agent-choice-toggle input:checked").CountAsync();
                Assert.Multiple(() =>
                {
                    Assert.That(response?.Status, Is.EqualTo(200));
                    Assert.That(page.Url, Does.Contain("/Onboarding/Agents"));
                    Assert.That(initialCardCount, Is.EqualTo(3));
                    Assert.That(initialSelectedCount, Is.Zero,
                        "Installed agents must not be preselected.");
                });
                await AssertFitsViewportAsync(page);
                await page.ScreenshotAsync(new() { Path = Path.Combine(Path.GetTempPath(), "hba-stage19-qa-desktop.png"), FullPage = true });

                await page.Locator("#agent-sample-business-agent").CheckAsync();
                await page.GetByRole(AriaRole.Button, new() { Name = "Save choices" }).ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                int sampleSelectedCount = await page.Locator("#agent-sample-business-agent:checked").CountAsync();
                int founderSelectedCount = await page.Locator("#agent-founder-scout:checked").CountAsync();
                int wakeSelectedCount = await page.Locator("#agent-wake-remote:checked").CountAsync();
                Assert.Multiple(() =>
                {
                    Assert.That(sampleSelectedCount, Is.EqualTo(1));
                    Assert.That(founderSelectedCount, Is.Zero);
                    Assert.That(wakeSelectedCount, Is.Zero);
                });

                await page.GetByRole(AriaRole.Link, new() { Name = "Continue setup" }).ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                string encodedPage = await page.ContentAsync();
                string maximumFiles = await page.Locator("#generic-maximumFiles").InputValueAsync();
                string includedExtensions = await page.Locator("#generic-includedExtensions").InputValueAsync();
                Assert.Multiple(() =>
                {
                    Assert.That(encodedPage, Does.Not.Contain("<script>alert('stage19')</script>"));
                    Assert.That(encodedPage, Does.Contain("&lt;script&gt;alert('stage19')&lt;/script&gt;"));
                    Assert.That(maximumFiles, Is.EqualTo("100"));
                    Assert.That(
                        includedExtensions.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
                        Is.EqualTo(DefaultExtensions));
                });

                await page.Locator("#generic-maximumFiles").FillAsync("25");
                await page.GetByRole(AriaRole.Button, new() { Name = "Save and continue" }).ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await page.GetByRole(AriaRole.Link, new() { Name = "Continue setup" }).ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await page.Locator("input[name='secretValue']").FillAsync(protectedValue);
                await page.GetByRole(AriaRole.Button, new() { Name = "Set protected value" }).ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                await page.SetViewportSizeAsync(390, 844);
                await AssertFitsViewportAsync(page);
                string safeBody = await page.Locator("body").InnerTextAsync();
                string clearedSecretValue = await page.Locator("input[name='secretValue']").InputValueAsync();
                Assert.Multiple(() =>
                {
                    Assert.That(safeBody, Does.Contain("Configured"));
                    Assert.That(safeBody, Does.Not.Contain(protectedValue));
                    Assert.That(clearedSecretValue, Is.Empty);
                    Assert.That(browserErrors, Is.Empty);
                });
                await page.ScreenshotAsync(new() { Path = Path.Combine(Path.GetTempPath(), "hba-stage19-qa-narrow.png"), FullPage = true });

                await application.StopAsync();
            }

            await using (HostRuntime restarted = await HostRuntime.CreateAsync(settings, HostLogFactory.Create(data)))
            {
                IAgentOnboardingService onboarding = restarted.WebComposition.Management?.AgentOnboarding
                    ?? throw new InvalidOperationException("Agent onboarding composition is missing.");
                AgentSelectionOverview resumed = await onboarding.GetSelectionOverviewAsync(sessionId, "local-web");
                GenericAgentOnboardingEditor editor = await onboarding.GetGenericEditorAsync(sessionId, SampleAgentId, "local-web");
                AgentOnboardingCard sample = resumed.Agents.Single(item => item.Definition.Id == SampleAgentId);
                Assert.Multiple(() =>
                {
                    Assert.That(resumed.SelectedCount, Is.EqualTo(1));
                    Assert.That(resumed.DeferredCount, Is.EqualTo(2));
                    Assert.That(sample.Selection?.ProgressStatus, Is.EqualTo(OnboardingAgentProgressStatus.ReadyForValidation));
                    Assert.That(editor.Values["maximumFiles"], Is.EqualTo("25"));
                    Assert.That(editor.Secrets.Single().IsConfigured, Is.True);
                    Assert.That(sample.Definition.Enabled, Is.False);
                });

                AssistantDatabase persisted = await AssistantDatabase.InitializeAsync(
                    new(data, ManifestDirectory: Path.Combine(repository, "manifests")),
                    TimeProvider.System);
                var schedules = new ScheduleRepository(persisted.ContextFactory, TimeProvider.System);
                Assert.That(await schedules.GetByAgentAndNameAsync(SampleAgentId, "Existing sample schedule", null), Is.Null);
                AgentScheduleRecord existingSchedule = await schedules.SaveAsync(
                    CreateExistingSchedule(editor.Configuration!.CurrentRevisionId),
                    expectedConcurrencyToken: null);
                string configurationHash = editor.Configuration.CurrentRevision.ConfigurationHash;

                _ = await onboarding.StartReconfigureAgentAsync(SampleAgentId, "local-web");
                GenericAgentOnboardingEditor reentered = await onboarding.GetGenericEditorAsync(sessionId, SampleAgentId, "local-web");
                AgentScheduleRecord? preservedSchedule = await schedules.GetAsync(existingSchedule.Id);
                Assert.Multiple(() =>
                {
                    Assert.That(reentered.Configuration?.CurrentRevision.ConfigurationHash, Is.EqualTo(configurationHash));
                    Assert.That(preservedSchedule, Is.Not.Null);
                    Assert.That(preservedSchedule?.PinnedConfigurationRevisionId, Is.EqualTo(editor.Configuration.CurrentRevisionId));
                });

                OnboardingAgentSelection afterSecretDelete = await onboarding.DeleteGenericSecretAsync(
                    sessionId,
                    SampleAgentId,
                    reentered.Selection.Revision,
                    "notificationSecretReference",
                    "local-web");
                GenericAgentOnboardingEditor afterDelete = await onboarding.GetGenericEditorAsync(sessionId, SampleAgentId, "local-web");
                Assert.Multiple(() =>
                {
                    Assert.That(afterSecretDelete.ProgressStatus, Is.EqualTo(OnboardingAgentProgressStatus.NeedsAttention));
                    Assert.That(afterDelete.Secrets.Single().IsConfigured, Is.False);
                });

                Directory.Delete(Path.Combine(agents, SampleAgentId.Value), recursive: true);
                AgentSelectionOverview afterRemoval = await onboarding.GetSelectionOverviewAsync(sessionId, "local-web");
                AgentOnboardingCard removed = afterRemoval.Agents.Single(item => item.Definition.Id == SampleAgentId);
                Assert.Multiple(() =>
                {
                    Assert.That(removed.Selection?.SelectionStatus, Is.EqualTo(OnboardingAgentSelectionStatus.Removed));
                    Assert.That(removed.Selection?.SavedConfigurationHash, Is.Not.Null.And.Not.Empty);
                    Assert.That(afterRemoval.Agents.Count(item => item.Definition.Id != SampleAgentId), Is.EqualTo(2));
                });
            }

            AssistantDatabase database = await AssistantDatabase.InitializeAsync(
                new(data, ManifestDirectory: Path.Combine(repository, "manifests")),
                TimeProvider.System);
            await using AssistantDbContext db = await database.ContextFactory.CreateDbContextAsync();
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM AgentSchedules;";
            Assert.That(Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture),
                Is.EqualTo(1),
                "Re-entering setup must preserve the one pre-existing schedule without creating another.");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task NewlyScannedGenericAgentShowsNonBlockingDashboardBannerOnEstablishedInstallation()
    {
        string repository = FindRepositoryRoot();
        string root = Path.Combine(Path.GetTempPath(), $"hba-stage19-pending-{Guid.NewGuid():N}");
        string data = Path.Combine(root, "data");
        string agents = Path.Combine(root, "agents");
        Directory.CreateDirectory(agents);
        string url = $"http://127.0.0.1:{GetAvailablePort()}";
        try
        {
            AssistantDatabase database = await InitializeAndScanSampleAsync(repository, data, agents, hostileLabels: false);
            var catalog = new FileAgentConfigurationSchemaCatalog(agents);
            var configurations = CreateConfigurationService(database, catalog);
            JsonElement establishedConfiguration = JsonSerializer.SerializeToElement(new
            {
                schemaVersion = "1.0",
                sourceRelativePath = "established",
                maximumFiles = 42,
                includedExtensions = TextExtension,
                requireAtLeastOneFile = false,
                notificationSecretReference = "secret://sample-business-agent/notification-token",
            });
            _ = await configurations.SaveAsync(new(
                SampleAgentId,
                "1.0",
                establishedConfiguration,
                "local-web",
                "Establish the existing installation fixture.",
                Guid.NewGuid()));

            CreateGeneratedGenericPackage(repository, agents, "new-generic-agent");
            AgentRegistryScanResult scanned = await CreateScanner(database, agents, configurations, catalog).ScanAsync();
            Assert.That(scanned.Items.Single(item => item.AgentId == "new-generic-agent").Status,
                Is.EqualTo(AgentPackageScanStatus.Installed));

            HostBootstrapSettings settings = CreateSettings(repository, data, agents, url);
            await using HostRuntime runtime = await HostRuntime.CreateAsync(settings, HostLogFactory.Create(data));
            HostWebComposition webOnly = runtime.WebComposition with { HostedServices = [] };
            await using WebApplication application = HostApplication.Build(["--urls", url], webOnly);
            await application.StartAsync();
            using var client = new HttpClient(new HttpClientHandler
            {
                UseDefaultCredentials = true,
                AllowAutoRedirect = false,
            })
            {
                BaseAddress = new(url),
            };

            using HttpResponseMessage response = await client.GetAsync("/");
            string body = await response.Content.ReadAsStringAsync();
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(response.Headers.Location, Is.Null, "A newly scanned agent must not force navigation.");
                Assert.That(body, Does.Contain("New agent available — Set up"));
                Assert.That(body, Does.Contain("new-generic-agent"));
            });
            await application.StopAsync();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async ValueTask<Guid> PrepareFreshSelectionAsync(string repository, string data, string agents)
    {
        AssistantDatabase database = await InitializeAndScanSampleAsync(repository, data, agents, hostileLabels: true);
        CreateBuiltInPackage(repository, agents, "founder-scout", "FounderScout.exe");
        CreateBuiltInPackage(repository, agents, "wake-remote", "WakeRemote.exe");
        var repositoryAdapter = new OnboardingRepository(database.ContextFactory, TimeProvider.System);
        EnsureInitialOnboardingResult initialized = await repositoryAdapter.EnsureInitialAsync(false, "local-web", Guid.NewGuid());
        OnboardingSession selection = await repositoryAdapter.TransitionAsync(new(
            initialized.Session.Id,
            initialized.Session.Revision,
            OnboardingSessionStatus.InProgress,
            OnboardingSteps.AgentSelectionPending,
            "local-web",
            Guid.NewGuid(),
            AcknowledgedWarningCount: 0,
            IncrementCompletedStepCount: true));
        return selection.Id;
    }

    private static async ValueTask<AssistantDatabase> InitializeAndScanSampleAsync(
        string repository,
        string data,
        string agents,
        bool hostileLabels)
    {
        string package = Path.Combine(agents, SampleAgentId.Value);
        Directory.CreateDirectory(package);
        File.Copy(Path.Combine(repository, "agents", "SampleBusinessAgent", "manifest.json"), Path.Combine(package, "manifest.json"));
        string schema = await File.ReadAllTextAsync(Path.Combine(repository, "agents", "SampleBusinessAgent", "configuration.schema.json"));
        if (hostileLabels)
        {
            schema = schema.Replace(
                "\"title\": \"Maximum files\"",
                "\"title\": \"Maximum files <script>alert('stage19')</script>\"",
                StringComparison.Ordinal);
        }

        await File.WriteAllTextAsync(Path.Combine(package, "configuration.schema.json"), schema);
        File.Copy(typeof(AgentRegistryScanner).Assembly.Location, Path.Combine(package, "SampleBusinessAgent.exe"));
        AssistantDatabase database = await AssistantDatabase.InitializeAsync(
            new(data, ManifestDirectory: Path.Combine(repository, "manifests")),
            TimeProvider.System);
        var catalog = new FileAgentConfigurationSchemaCatalog(agents);
        var configurations = CreateConfigurationService(database, catalog);
        AgentRegistryScanResult result = await CreateScanner(database, agents, configurations, catalog).ScanAsync();
        Assert.That(result.InvalidCount, Is.Zero);
        return database;
    }

    private static AgentConfigurationService CreateConfigurationService(
        AssistantDatabase database,
        FileAgentConfigurationSchemaCatalog catalog) => new(
            database.ContextFactory,
            new CompositeAgentConfigurationValidator([
                new BasicAgentConfigurationValidator(),
                new GenericAgentConfigurationValidator(catalog, new HashSet<AgentId>()),
            ]),
            TimeProvider.System);

    private static AgentRegistryScanner CreateScanner(
        AssistantDatabase database,
        string agents,
        AgentConfigurationService configurations,
        FileAgentConfigurationSchemaCatalog catalog) => new(
            agents,
            new AgentDefinitionRepository(database.ContextFactory, TimeProvider.System),
            catalog,
            configurations,
            new AuditWriter(database.ContextFactory, TimeProvider.System),
            TimeProvider.System);

    private static void CreateBuiltInPackage(string repository, string agents, string id, string executable)
    {
        string package = Path.Combine(agents, id);
        Directory.CreateDirectory(package);
        File.Copy(Path.Combine(repository, "manifests", $"{id}.agent-manifest.json"), Path.Combine(package, "manifest.json"));
        File.Copy(Path.Combine(repository, "manifests", $"{id}.configuration.schema.json"), Path.Combine(package, "configuration.schema.json"));
        File.Copy(typeof(AgentRegistryScanner).Assembly.Location, Path.Combine(package, executable));
    }

    private static void CreateGeneratedGenericPackage(string repository, string agents, string id)
    {
        string package = Path.Combine(agents, id);
        Directory.CreateDirectory(package);
        string manifest = File.ReadAllText(Path.Combine(repository, "agents", "SampleBusinessAgent", "manifest.json"))
            .Replace("sample-business-agent", id, StringComparison.Ordinal)
            .Replace("Sample Business Agent", "New Generic Agent", StringComparison.Ordinal)
            .Replace("SampleBusinessAgent.exe", "NewGenericAgent.exe", StringComparison.Ordinal);
        string schema = File.ReadAllText(Path.Combine(repository, "agents", "SampleBusinessAgent", "configuration.schema.json"))
            .Replace("sample-business-agent", id, StringComparison.Ordinal)
            .Replace("Sample Business Agent", "New Generic Agent", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(package, "manifest.json"), manifest);
        File.WriteAllText(Path.Combine(package, "configuration.schema.json"), schema);
        File.Copy(typeof(AgentRegistryScanner).Assembly.Location, Path.Combine(package, "NewGenericAgent.exe"));
    }

    private static HostBootstrapSettings CreateSettings(
        string repository,
        string data,
        string agents,
        string url)
    {
        string runner = Path.Combine(repository, "src", "HomeBusinessAssistant.Runner", "bin", "Release", "net10.0-windows", "HomeBusinessAssistant.Runner.exe");
        return new(
            repository,
            data,
            agents,
            Path.Combine(repository, "manifests"),
            runner,
            Path.GetDirectoryName(runner)!,
            "assistant.db",
            url,
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(1));
    }

    private static AgentScheduleRecord CreateExistingSchedule(Guid configurationRevisionId)
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        var definition = new ManualScheduleDefinition();
        return new(
            Guid.NewGuid(),
            SampleAgentId,
            "Existing sample schedule",
            "run",
            "{}",
            definition.Kind,
            ScheduleDefinitionJson.Serialize(definition),
            TimeZoneInfo.Local.Id,
            MisfirePolicy.Skip,
            ConcurrencyPolicy.Forbid,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromSeconds(30),
            RetryPolicyJson.Serialize(RetryPolicyDefinition.None),
            WakePolicy.Never,
            configurationRevisionId,
            AllowDisabledAgent: true,
            IsEnabled: false,
            IsPaused: false,
            PausedUntilUtc: null,
            nowUtc,
            nowUtc,
            ConcurrencyToken: 0);
    }

    private static async Task AssertFitsViewportAsync(IPage page)
    {
        bool fits = await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1");
        Assert.That(fits, Is.True, "The rendered document overflows the viewport.");
    }

    private static int GetAvailablePort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HomeBusinessAssistant.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
