using System.Net;
using System.Net.Sockets;
using HomeBusinessAssistant.Host.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Playwright;

namespace HomeBusinessAssistant.Host.Tests;

internal sealed class Stage18OnboardingBrowserSmokeTests
{
    [Test]
    [Category("BrowserSmoke")]
    public async Task ServeFreshOnboardingForInteractiveBrowserQa()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("HBA_STAGE18_BROWSER_SMOKE"), "1", StringComparison.Ordinal))
        {
            Assert.Ignore("Set HBA_STAGE18_BROWSER_SMOKE=1 to run the interactive Stage 18 browser fixture.");
        }

        string root = Path.Combine(Path.GetTempPath(), "hba-stage18-browser-smoke");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(Path.Combine(root, "agents"));
        const string url = "http://127.0.0.1:5194";
        try
        {
            await using HostRuntime runtime = await CreateRuntimeAsync(root, url);
            HostWebComposition webOnly = runtime.WebComposition with { HostedServices = [] };
            await using WebApplication application = HostApplication.Build(
                ["--urls", url, "--contentRoot", Path.Combine(FindRepositoryRoot(), "src", "HomeBusinessAssistant.Host")],
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
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    [Category("Stage18RenderedUi")]
    public async Task FreshOnboardingRendersAndDefersResumesAtDesktopAndNarrowWidths()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-stage18-rendered-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "agents"));
        int port = GetAvailablePort();
        string url = $"http://127.0.0.1:{port}";
        try
        {
            await using HostRuntime runtime = await CreateRuntimeAsync(root, url);
            HostWebComposition webOnly = runtime.WebComposition with { HostedServices = [] };
            await using WebApplication application = HostApplication.Build(
                ["--urls", url, "--contentRoot", Path.Combine(FindRepositoryRoot(), "src", "HomeBusinessAssistant.Host")],
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
                if (message.Type == "error")
                {
                    consoleErrors.Add(message.Text);
                }
            };
            page.PageError += (_, error) => consoleErrors.Add(error);

            IResponse? entry = await page.GotoAsync(url + "/", new() { WaitUntil = WaitUntilState.NetworkIdle });
            Assert.That(entry?.Status, Is.EqualTo(200));
            Assert.That(page.Url, Does.Contain("/Onboarding"));
            Assert.That(await page.Locator(".onboarding-steps li").CountAsync(), Is.EqualTo(5));
            Assert.That(await page.Locator(".readiness-card").CountAsync(), Is.EqualTo(7));
            await AssertFitsViewportAsync(page);

            await page.GetByRole(AriaRole.Button, new() { Name = "Set up later" }).ClickAsync();
            await page.WaitForURLAsync(url + "/");
            Assert.That(await page.GetByText("Setup is saved for later.").CountAsync(), Is.EqualTo(1));
            string? resumeHref = await page.GetByRole(AriaRole.Link, new() { Name = "Resume setup" }).GetAttributeAsync("href");
            await page.GetByRole(AriaRole.Button, new() { Name = "Dismiss for this session" }).ClickAsync();
            await page.WaitForURLAsync(url + "/");
            Assert.That(await page.GetByText("Setup is saved for later.").CountAsync(), Is.Zero);
            Assert.That(resumeHref, Is.Not.Null.And.Not.Empty);
            await page.GotoAsync(url + resumeHref, new() { WaitUntil = WaitUntilState.NetworkIdle });
            await page.GetByRole(AriaRole.Button, new() { Name = "Resume setup" }).ClickAsync();
            await page.WaitForURLAsync(value => value.Contains("/Onboarding", StringComparison.Ordinal));

            await page.SetViewportSizeAsync(390, 844);
            await AssertFitsViewportAsync(page);
            Assert.That(await page.Locator("main").CountAsync(), Is.EqualTo(1));
            Assert.That(await page.Locator("a.skip-link").CountAsync(), Is.EqualTo(1));
            string body = await page.Locator("body").InnerTextAsync();
            Assert.Multiple(() =>
            {
                Assert.That(body, Does.Contain("Make sure this workstation is ready"));
                Assert.That(body, Does.Contain("Protected API-key storage"));
                Assert.That(body, Does.Not.Contain(root));
                Assert.That(consoleErrors, Is.Empty);
            });

            await application.StopAsync();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async ValueTask<HostRuntime> CreateRuntimeAsync(string root, string url)
    {
        string repository = FindRepositoryRoot();
        string runner = Path.Combine(repository, "src", "HomeBusinessAssistant.Runner", "bin", "Release", "net10.0-windows", "HomeBusinessAssistant.Runner.exe");
        var settings = new HostBootstrapSettings(
            repository,
            Path.Combine(root, "data"),
            Path.Combine(root, "agents"),
            Path.Combine(repository, "manifests"),
            runner,
            Path.GetDirectoryName(runner)!,
            "assistant.db",
            url,
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(1));
        return await HostRuntime.CreateAsync(settings, HostLogFactory.Create(settings.DataDirectory));
    }

    private static async Task AssertFitsViewportAsync(IPage page)
    {
        bool fits = await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1");
        Assert.That(fits, Is.True, "The onboarding document overflows the viewport.");
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
