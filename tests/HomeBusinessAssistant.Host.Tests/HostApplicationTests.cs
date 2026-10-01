using System.Net;
using System.Net.Sockets;
using System.Reflection;
using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Host.Dashboard;
using HomeBusinessAssistant.Host.Health;
using Microsoft.AspNetCore.Builder;

namespace HomeBusinessAssistant.Host.Tests;

internal sealed class HostApplicationTests
{
    [Test]
    public void DirectHostBuildOutputIncludesStaticAssets()
    {
        string configuration = typeof(HostApplication).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
            ?? throw new InvalidOperationException("The Host build configuration is unavailable.");
        string outputRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "HomeBusinessAssistant.Host",
            "bin",
            configuration,
            "net10.0-windows",
            "wwwroot");

        Assert.Multiple(() =>
        {
            Assert.That(Path.Combine(outputRoot, "css", "site.css"), Does.Exist);
            Assert.That(Path.Combine(outputRoot, "js", "site.js"), Does.Exist);
        });
    }

    [Test]
    public async Task HealthEndpointsAndDashboardAreAvailableOnlyOnIpv4Loopback()
    {
        int port = GetAvailablePort();
        string url = $"http://127.0.0.1:{port}";
        await using WebApplication application = HostApplication.Build(["--urls", url]);

        await application.StartAsync();

        using HttpClient client = CreateAuthenticatedClient(url);
        using HttpClient anonymous = new() { BaseAddress = new Uri(url) };
        using HttpResponseMessage live = await client.GetAsync("/health/live");
        using HttpResponseMessage ready = await client.GetAsync("/health/ready");
        using HttpResponseMessage dashboard = await client.GetAsync("/");
        using HttpResponseMessage anonymousHealth = await anonymous.GetAsync("/health/live");
        using HttpResponseMessage anonymousDashboard = await anonymous.GetAsync("/");
        string dashboardBody = await dashboard.Content.ReadAsStringAsync();

        await application.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(live.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(ready.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(dashboard.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(anonymousHealth.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(anonymousDashboard.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(dashboardBody, Does.Contain("No installed agent manifests were found"));
            Assert.That(dashboardBody, Does.Contain("href=\"/Agents\""));
            Assert.That(dashboardBody, Does.Contain("href=\"/WakeRemote\""));
            Assert.That(dashboardBody, Does.Not.Contain("href=\"/health/ready\""));
        });
    }

    [Test]
    public async Task ReadyHealthTransitionsAndStateChangingHandlerRequiresAntiforgery()
    {
        int port = GetAvailablePort();
        string url = $"http://127.0.0.1:{port}";
        var readiness = new MutableReadinessEvaluator();
        var health = new HostHealthState(TimeProvider.System);
        var composition = new HostWebComposition(
            health,
            readiness,
            new FakeDashboardService(),
            new FakeGlobalControlService(),
            [],
            LoggerProvider: null);
        await using WebApplication application = HostApplication.Build(
            ["--urls", url, "--contentRoot", Path.Combine(FindRepositoryRoot(), "src", "HomeBusinessAssistant.Host")],
            composition);
        await application.StartAsync();
        using HttpClient client = CreateAuthenticatedClient(url);

        using HttpResponseMessage notReady = await client.GetAsync("/health/ready");
        readiness.IsReady = true;
        using HttpResponseMessage ready = await client.GetAsync("/health/ready");
        using HttpResponseMessage postWithoutToken = await client.PostAsync("/?handler=PauseAll", content: null);
        using HttpResponseMessage dashboard = await client.GetAsync("/");
        string body = await dashboard.Content.ReadAsStringAsync();

        await application.StopAsync();
        Assert.Multiple(() =>
        {
            Assert.That(notReady.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(ready.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(postWithoutToken.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body, Does.Contain("runner.path-missing"));
            Assert.That(body, Does.Contain("No agents are running"));
        });
    }

    [Test]
    public void UrlPolicyRejectsNonLoopbackBinding()
    {
        Assert.That(
            () => LoopbackUrlPolicy.Validate("http://0.0.0.0:5180"),
            Throws.ArgumentException.With.Message.Contains("127.0.0.1"));
    }

    [Test]
    public async Task HostAndUnsafeOriginAreBoundToTheConfiguredLoopbackOrigin()
    {
        int port = GetAvailablePort();
        string url = $"http://127.0.0.1:{port}";
        await using WebApplication application = HostApplication.Build(["--urls", url]);
        await application.StartAsync();
        using HttpClient client = CreateAuthenticatedClient(url);

        using var badHostRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        badHostRequest.Headers.Host = $"localhost:{port}";
        using HttpResponseMessage badHost = await client.SendAsync(badHostRequest);
        using var badOriginRequest = new HttpRequestMessage(HttpMethod.Post, "/?handler=PauseAll");
        badOriginRequest.Headers.Add("Origin", "https://attacker.invalid");
        using HttpResponseMessage badOrigin = await client.SendAsync(badOriginRequest);

        await application.StopAsync();
        Assert.Multiple(() =>
        {
            Assert.That(badHost.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(badOrigin.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        });
    }

    [Test]
    public async Task SimpleModeSkipsOnboardingEntryWhileOnboardingHealthAndAboutRemainReachable()
    {
        int port = GetAvailablePort();
        string url = $"http://127.0.0.1:{port}";
        var onboarding = new FakeOnboardingService();
        var composition = new HostWebComposition(
            new HostHealthState(TimeProvider.System),
            new MutableReadinessEvaluator { IsReady = true },
            new FakeDashboardService(),
            new FakeGlobalControlService(),
            [],
            LoggerProvider: null,
            new HostManagementComposition(Onboarding: onboarding));
        await using WebApplication application = HostApplication.Build(
            ["--urls", url, "--contentRoot", Path.Combine(FindRepositoryRoot(), "src", "HomeBusinessAssistant.Host")],
            composition);
        await application.StartAsync();
        using var handler = new HttpClientHandler { UseDefaultCredentials = true, AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(url) };
        using var anonymousHandler = new HttpClientHandler { UseDefaultCredentials = false, AllowAutoRedirect = false };
        using var anonymous = new HttpClient(anonymousHandler) { BaseAddress = new Uri(url) };

        using HttpResponseMessage dashboard = await client.GetAsync("/");
        using HttpResponseMessage onboardingPage = await client.GetAsync($"/Onboarding?sessionId={onboarding.Session.Id:D}");
        using HttpResponseMessage about = await client.GetAsync("/About");
        using HttpResponseMessage health = await client.GetAsync("/health/live");
        using HttpResponseMessage staticAsset = await client.GetAsync("/css/site.css");
        using HttpResponseMessage anonymousOnboarding = await anonymous.GetAsync("/Onboarding");
        using HttpResponseMessage missingAntiforgery = await client.PostAsync(
            $"/Onboarding?handler=RunChecks&sessionId={onboarding.Session.Id:D}",
            content: null);
        string onboardingBody = await onboardingPage.Content.ReadAsStringAsync();

        onboarding.Deferred = true;
        using HttpResponseMessage deferredDashboard = await client.GetAsync("/");
        string deferredBody = await deferredDashboard.Content.ReadAsStringAsync();
        await application.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(dashboard.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(dashboard.Headers.Location, Is.Null);
            Assert.That(onboardingPage.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(onboardingBody, Does.Contain("Make sure this workstation is ready"));
            Assert.That(onboardingBody, Does.Not.Contain("<script>stage18-hostile</script>"));
            Assert.That(onboardingBody, Does.Contain("&lt;script&gt;stage18-hostile&lt;/script&gt;"));
            Assert.That(onboardingBody, Does.Not.Contain("stage18-secret-value"));
            Assert.That(onboardingBody, Does.Contain("Task Scheduler could not be queried"));
            Assert.That(onboardingBody, Does.Contain("Automatic wake is not supported"));
            Assert.That(about.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(staticAsset.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(anonymousOnboarding.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(missingAntiforgery.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(deferredDashboard.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(deferredBody, Does.Not.Contain("Setup is saved for later"));
            Assert.That(deferredBody, Does.Not.Contain("Dismiss for this session"));
        });
    }

    private static HttpClient CreateAuthenticatedClient(string url) => new(
        new HttpClientHandler { UseDefaultCredentials = true })
    {
        BaseAddress = new Uri(url),
    };

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

    private sealed class MutableReadinessEvaluator : IHostReadinessEvaluator
    {
        public bool IsReady { get; set; }

        public ValueTask<HostReadinessResult> EvaluateAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HostReadinessResult(IsReady, IsReady ? [] : ["runner.path-missing"]));
    }

    private sealed class FakeDashboardService : IHostDashboardService
    {
        public ValueTask<HostDashboardSnapshot> GetAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new HostDashboardSnapshot(
                IsReady: false,
                ReadinessReasonCodes: ["runner.path-missing"],
                new(
                    [new(
                        AgentId.Parse("wake-remote"),
                        "Wake & Remote test",
                        "A persisted test agent.",
                        Enabled: true,
                        SupportsManualRun: true,
                        HasConfiguration: true)],
                    [],
                    [],
                    [],
                    LatestSummary: null),
                new(false, []),
                new(false, false, null, null, null),
                []));
    }

    private sealed class FakeGlobalControlService : IGlobalScheduleControlService
    {
        public ValueTask<GlobalSchedulePauseState> GetStateAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new GlobalSchedulePauseState(false, []));

        public ValueTask<GlobalSchedulePauseState> PauseAllAsync(
            string actorId,
            Guid correlationId,
            CancellationToken cancellationToken = default) => GetStateAsync(cancellationToken);

        public ValueTask<GlobalSchedulePauseState> ResumeAllAsync(
            string actorId,
            Guid correlationId,
            CancellationToken cancellationToken = default) => GetStateAsync(cancellationToken);
    }

    private sealed class FakeOnboardingService : IOnboardingService
    {
        private readonly OnboardingReadinessCheckDefinition definition = new(
            "test.hostile",
            OnboardingCheckScope.Platform,
            "Host identity",
            "A safe description.",
            Required: true,
            RequiresExplicitAction: false,
            TimeSpan.FromSeconds(1));
        private readonly OnboardingReadinessCheckDefinition taskSchedulerDefinition = new(
            "windows.task-scheduler",
            OnboardingCheckScope.Windows,
            "Windows Task Scheduler",
            "Read-only Task Scheduler readiness.",
            Required: true,
            RequiresExplicitAction: false,
            TimeSpan.FromSeconds(1));
        private readonly OnboardingReadinessCheckDefinition powerDefinition = new(
            "windows.power-and-wake",
            OnboardingCheckScope.Windows,
            "Power and wake capability",
            "Read-only power readiness.",
            Required: false,
            RequiresExplicitAction: false,
            TimeSpan.FromSeconds(1));

        public FakeOnboardingService()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Session = new(
                Guid.NewGuid(),
                "1.0",
                OnboardingSessionKind.FirstRun,
                OnboardingSessionStatus.InProgress,
                OnboardingSteps.Readiness,
                now,
                now,
                now,
                DeferredAtUtc: null,
                CompletedAtUtc: null,
                CancelledAtUtc: null,
                "local-web",
                Guid.NewGuid(),
                Revision: 1,
                WarningCount: 0,
                AcknowledgedWarningCount: 0,
                CompletedStepCount: 0);
        }

        public OnboardingSession Session { get; private set; }
        public bool Deferred { get; set; }

        public ValueTask<OnboardingEntryDecision> GetEntryDecisionAsync(string actorId, CancellationToken cancellationToken = default)
        {
            OnboardingSession session = Deferred
                ? Session with { Status = OnboardingSessionStatus.Deferred, DeferredAtUtc = DateTimeOffset.UtcNow }
                : Session;
            return ValueTask.FromResult(new OnboardingEntryDecision(
                session,
                RedirectToOnboarding: !Deferred,
                ShowResumeReminder: Deferred,
                ShowReviewInvitation: false,
                Deferred ? "onboarding.deferred" : "onboarding.first-run-required"));
        }

        public ValueTask<OnboardingOverview> GetOverviewAsync(Guid? sessionId, string actorId, CancellationToken cancellationToken = default)
        {
            var check = new OnboardingCheckView(
                definition,
                OnboardingCheckStatus.Blocked,
                "test.blocked",
                "<script>stage18-hostile</script>",
                DateTimeOffset.UtcNow,
                ExpiresAtUtc: null,
                "1.0",
                "{\"secret\":\"stage18-secret-value\"}",
                "host-security-help");
            var taskScheduler = new OnboardingCheckView(
                taskSchedulerDefinition,
                OnboardingCheckStatus.Warning,
                "task-scheduler.query-unavailable",
                "Task Scheduler could not be queried. Manual Host startup remains available.",
                DateTimeOffset.UtcNow,
                ExpiresAtUtc: null,
                "1.0",
                "{}",
                "task-scheduler-help");
            var power = new OnboardingCheckView(
                powerDefinition,
                OnboardingCheckStatus.Warning,
                "power-wake.unsupported",
                "Automatic wake is not supported by the reported power configuration.",
                DateTimeOffset.UtcNow,
                ExpiresAtUtc: null,
                "1.0",
                "{}",
                "power-wake-help");
            return ValueTask.FromResult(new OnboardingOverview(Session, [check, taskScheduler, power], false, 1, 2, 0));
        }

        public ValueTask<OnboardingOverview> RunChecksAsync(Guid sessionId, long expectedRevision, string actorId, CancellationToken cancellationToken = default) =>
            GetOverviewAsync(sessionId, actorId, cancellationToken);

        public ValueTask<OnboardingOverview> RunExplicitProbesAsync(Guid sessionId, long expectedRevision, string actorId, CancellationToken cancellationToken = default) =>
            GetOverviewAsync(sessionId, actorId, cancellationToken);

        public ValueTask<OnboardingSession> StartReadinessReviewAsync(string actorId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Session);

        public ValueTask<OnboardingSession> DeferAsync(Guid sessionId, long expectedRevision, string actorId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Session);

        public ValueTask<OnboardingSession> ResumeAsync(Guid sessionId, long expectedRevision, string actorId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Session);

        public ValueTask<OnboardingSession> CancelAsync(Guid sessionId, long expectedRevision, string actorId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Session);

        public ValueTask<OnboardingSession> ContinueAsync(Guid sessionId, long expectedRevision, bool acknowledgeWarnings, string actorId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Session);
    }
}
