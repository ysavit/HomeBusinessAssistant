using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Management;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Secrets;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Host.Logging;
using HomeBusinessAssistant.Infrastructure.Configuration;
using HomeBusinessAssistant.Infrastructure.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Builder;

namespace HomeBusinessAssistant.Host.Tests;

internal sealed class ManagementUiIntegrationTests
{
    [Test]
    public async Task RealPersistedManagementPagesRenderAndDiagnosticsExcludeSecretMaterial()
    {
        string repositoryRoot = FindRepositoryRoot();
        string root = Path.Combine(Path.GetTempPath(), $"hba-management-ui-{Guid.NewGuid():N}");
        string data = Path.Combine(root, "data");
        string agents = Path.Combine(root, "agents");
        Directory.CreateDirectory(agents);
        int port = GetAvailablePort();
        string url = $"http://127.0.0.1:{port}";
        var settings = CreateSettings(repositoryRoot, data, agents, url);
        var logger = HostLogFactory.Create(data);
        try
        {
            await RegisterSampleAgentAsync(repositoryRoot, data, agents);
            await using HostRuntime runtime = await HostRuntime.CreateAsync(settings, logger);
            await runtime.InitializeAsync();
            Assert.That(runtime.WebComposition.Management?.Secrets, Is.Not.Null);
            await runtime.WebComposition.Management!.Secrets!.SetAsync(
                SecretReference.Parse("secret://founder-scout/azure-openai-key"),
                "stage-08-secret-must-not-render");
            await runtime.WebComposition.Management.Secrets.SetAsync(
                SecretReference.Parse("secret://sample-business-agent/notification-token"),
                "stage-17-generic-secret-must-not-render");
            AgentRunId hostileRunId = AgentRunId.New();
            HostManagementComposition management = runtime.WebComposition.Management with
            {
                Queries = new HostileRunQueryService(
                    runtime.WebComposition.Management.Queries!,
                    CreateHostileRun(hostileRunId)),
            };
            HostWebComposition webOnly = runtime.WebComposition with { HostedServices = [], Management = management };
            await using WebApplication application = HostApplication.Build(["--urls", url], webOnly);
            await application.StartAsync();
            using var client = new HttpClient(new HttpClientHandler { UseDefaultCredentials = true })
            {
                BaseAddress = new Uri(url),
            };

            string[] pages =
            [
                "/",
                "/Agents",
                "/Agents/wake-remote",
                "/Agents/wake-remote/configuration",
                "/Agents/founder-scout/configuration",
                "/Agents/sample-business-agent/configuration",
                "/Schedules",
                "/Schedules/Edit",
                "/Runs",
                "/Audit",
                "/FounderScout",
                "/FounderScout/Candidates",
                "/FounderScout/Settings",
                "/FounderScout/Queue",
                "/FounderScout/Operations",
                "/Settings",
            ];
            foreach (string path in pages)
            {
                using HttpResponseMessage response = await client.GetAsync(path);
                string body = await response.Content.ReadAsStringAsync();
                Assert.Multiple(() =>
                {
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), path);
                    Assert.That(body, Does.Contain("Skip to content"), path);
                    Assert.That(body, Does.Contain("Local-only control plane"), path);
                    Assert.That(body, Does.Not.Contain("stage-08-secret-must-not-render"), path);
                    Assert.That(body, Does.Not.Contain("stage-17-generic-secret-must-not-render"), path);
                });
            }

            string genericConfiguration = await client.GetStringAsync("/Agents/sample-business-agent/configuration");
            string founderScoutAgent = await client.GetStringAsync("/Agents/founder-scout");
            Assert.Multiple(() =>
            {
                Assert.That(genericConfiguration, Does.Contain("Sample Business Agent configuration"));
                Assert.That(genericConfiguration, Does.Contain("Maximum files"));
                Assert.That(genericConfiguration, Does.Contain("Included extensions"));
                Assert.That(genericConfiguration, Does.Contain("Protected secrets"));
                Assert.That(genericConfiguration, Does.Not.Contain("<script>"));
                Assert.That(founderScoutAgent, Does.Contain("pill positive\">Present"));
            });

            using HttpResponseMessage missingAntiforgery = await client.PostAsync(
                "/Agents/wake-remote/configuration?handler=Save",
                new StringContent(string.Empty));
            Assert.That(missingAntiforgery.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

            using HttpResponseMessage founderScoutMissingAntiforgery = await client.PostAsync(
                "/FounderScout/Operations?handler=Retention",
                new StringContent(string.Empty));
            Assert.That(founderScoutMissingAntiforgery.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

            string founderScoutOverview = await client.GetStringAsync("/FounderScout");
            string founderScoutCandidates = await client.GetStringAsync("/FounderScout/Candidates?recommendation=StrongConnect&minimumScore=100&needsManualReview=true");
            string founderScoutSettings = await client.GetStringAsync("/Agents/founder-scout/configuration");
            string founderScoutOperations = await client.GetStringAsync("/FounderScout/Operations");
            Assert.Multiple(() =>
            {
                Assert.That(founderScoutOverview, Does.Contain("Start Founder Scout"));
                Assert.That(founderScoutOverview, Does.Contain("Delay between profiles (seconds)"));
                Assert.That(founderScoutOverview, Does.Contain("AI is not used by Start"));
                Assert.That(founderScoutOverview, Does.Contain("saves each result locally before any optional processing"));
                Assert.That(founderScoutOverview, Does.Contain("enter your password only in the Startup School browser window"));
                Assert.That(founderScoutOverview, Does.Not.Contain("type=\"password\""));
                Assert.That(founderScoutOverview, Does.Not.Contain("Manual queue"));
                Assert.That(founderScoutOverview, Does.Not.Contain("Operations &amp; reports"));
                Assert.That(founderScoutOverview, Does.Contain(">Dashboard<"));
                Assert.That(founderScoutOverview, Does.Contain(">Founder Scout<"));
                Assert.That(founderScoutOverview, Does.Contain("aria-label=\"Founder Scout sections\""));
                Assert.That(founderScoutOverview, Does.Contain(">Overview<"));
                Assert.That(founderScoutOverview, Does.Contain(">Candidates<"));
                Assert.That(founderScoutOverview, Does.Contain(">Settings<"));
                Assert.That(founderScoutOverview, Does.Not.Contain(">Send<"));
                Assert.That(founderScoutOverview, Does.Not.Contain("browser-profiles"));
                Assert.That(founderScoutCandidates, Does.Contain("saved candidates"));
                Assert.That(founderScoutCandidates, Does.Contain("Analyze candidates now"));
                Assert.That(founderScoutCandidates, Does.Contain("Analyze saved candidates"));
                Assert.That(founderScoutCandidates, Does.Not.Contain("Minimum score"));
                Assert.That(founderScoutCandidates, Does.Not.Contain("Needs attention only"));
                Assert.That(founderScoutSettings, Does.Contain("AI settings"));
                Assert.That(founderScoutSettings, Does.Not.Contain("Analyze candidates now"));
                Assert.That(founderScoutSettings, Does.Contain("Analysis enabled"));
                Assert.That(founderScoutSettings, Does.Contain("OpenAI model"));
                Assert.That(founderScoutSettings, Does.Contain("Founder context and evaluation instructions"));
                Assert.That(founderScoutSettings, Does.Contain("type=\"password\""));
                Assert.That(founderScoutSettings, Does.Not.Contain("stage-08-secret-must-not-render"));
                Assert.That(founderScoutOperations, Does.Contain("startup-school-primary"));
                Assert.That(founderScoutOperations, Does.Contain("startup-school-default"));
            });

            using HttpResponseMessage founderScoutSettingsMissingAntiforgery = await client.PostAsync(
                "/FounderScout/Settings?handler=Save",
                new StringContent(string.Empty));
            Assert.That(founderScoutSettingsMissingAntiforgery.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

            using HttpResponseMessage hostileOutput = await client.GetAsync($"/Runs/{hostileRunId.Value:D}");
            string hostileBody = await hostileOutput.Content.ReadAsStringAsync();
            Assert.Multiple(() =>
            {
                Assert.That(hostileOutput.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(hostileBody, Does.Not.Contain("<script>alert('stage-08-xss')</script>"));
                Assert.That(hostileBody, Does.Contain("&lt;script&gt;alert(&#x27;stage-08-xss&#x27;)&lt;/script&gt;"));
                Assert.That(hostileOutput.Headers.GetValues("Content-Security-Policy").Single(), Does.Contain("default-src 'self'"));
            });

            using HttpResponseMessage export = await client.GetAsync("/Settings?handler=Diagnostics");
            byte[] bytes = await export.Content.ReadAsByteArrayAsync();
            using var stream = new MemoryStream(bytes);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            string contents = string.Join('\n', await Task.WhenAll(archive.Entries.Select(ReadEntryAsync)));
            Assert.Multiple(() =>
            {
                Assert.That(export.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(export.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/zip"));
                Assert.That(archive.GetEntry("EXCLUSIONS.txt"), Is.Not.Null);
                Assert.That(contents, Does.Not.Contain("stage-08-secret-must-not-render"));
                Assert.That(contents, Does.Not.Contain("canonicalConfigurationJson"));
                Assert.That(contents, Does.Contain("browser profiles"));
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

    private static async Task<string> ReadEntryAsync(ZipArchiveEntry entry)
    {
        await using Stream stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static async Task RegisterSampleAgentAsync(string repositoryRoot, string data, string agents)
    {
        string founderPackage = Path.Combine(agents, "founder-scout");
        Directory.CreateDirectory(founderPackage);
        File.Copy(Path.Combine(repositoryRoot, "manifests", "founder-scout.agent-manifest.json"), Path.Combine(founderPackage, "manifest.json"));
        File.Copy(Path.Combine(repositoryRoot, "manifests", "founder-scout.configuration.schema.json"), Path.Combine(founderPackage, "configuration.schema.json"));
        File.Copy(typeof(AgentRegistryScanner).Assembly.Location, Path.Combine(founderPackage, "FounderScout.exe"));

        string package = Path.Combine(agents, "sample-business-agent");
        Directory.CreateDirectory(package);
        File.Copy(Path.Combine(repositoryRoot, "agents", "SampleBusinessAgent", "manifest.json"), Path.Combine(package, "manifest.json"));
        File.Copy(Path.Combine(repositoryRoot, "agents", "SampleBusinessAgent", "configuration.schema.json"), Path.Combine(package, "configuration.schema.json"));
        File.Copy(typeof(AgentRegistryScanner).Assembly.Location, Path.Combine(package, "SampleBusinessAgent.exe"));
        string manifests = Path.Combine(repositoryRoot, "manifests");
        AssistantDatabase database = await AssistantDatabase.InitializeAsync(new(data, ManifestDirectory: manifests), TimeProvider.System);
        var definitions = new AgentDefinitionRepository(database.ContextFactory, TimeProvider.System);
        var catalog = new FileAgentConfigurationSchemaCatalog(agents);
        var configurations = new AgentConfigurationService(
            database.ContextFactory,
            new CompositeAgentConfigurationValidator([
                new BasicAgentConfigurationValidator(),
                new GenericAgentConfigurationValidator(catalog, new HashSet<AgentId>()),
            ]),
            TimeProvider.System);
        AgentRegistryScanResult result = await new AgentRegistryScanner(
            agents,
            definitions,
            catalog,
            configurations,
            new AuditWriter(database.ContextFactory, TimeProvider.System),
            TimeProvider.System).ScanAsync();
        Assert.That(result.InvalidCount, Is.Zero);
    }

    private static ManagementRunDetail CreateHostileRun(AgentRunId runId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        OccurrenceId occurrenceId = OccurrenceId.New();
        AgentId agentId = AgentId.Parse("wake-remote");
        Guid revisionId = Guid.NewGuid();
        const string hostile = "</span><script>alert('stage-08-xss')</script>";
        var occurrence = new ScheduleOccurrenceRecord(
            occurrenceId,
            ScheduleId: null,
            agentId,
            "diagnose",
            "{}",
            revisionId,
            now,
            TriggerType.ManualUi,
            OccurrenceStatus.Completed,
            RequiresWake: false,
            KeepSystemAwake: false,
            KeepDisplayOn: false,
            AttemptNumber: 0,
            ParentOccurrenceId: null,
            ClaimedBy: null,
            ClaimedAtUtc: null,
            ClaimExpiresAtUtc: null,
            StartedAtUtc: now,
            CompletedAtUtc: now,
            CancellationRequestedAtUtc: null,
            CancellationReason: null,
            TerminalReasonCode: null,
            TerminalMessage: null,
            runId,
            now,
            now);
        var run = new AgentRunRecord(
            runId,
            occurrenceId,
            agentId,
            revisionId,
            new string('a', 64),
            TriggerType.ManualUi,
            AgentRunStatus.Completed,
            "test-runner",
            "test-agent",
            "1.0",
            new string('b', 64),
            Environment.MachineName,
            ProcessId: null,
            ProcessStartedAtUtc: null,
            now,
            now,
            now,
            DurationMilliseconds: 1,
            ExitCode: 0,
            SummaryText: hostile,
            SummaryJson: $"{{\"message\":\"{hostile}\"}}",
            ErrorType: "HostileError",
            ErrorMessage: hostile,
            now,
            now,
            ConcurrencyToken: 1);
        return new(
            new(run, "Wake & Remote", "diagnose", now, 0, null, OccurrenceStatus.Completed),
            occurrence,
            ConfigurationRevision: null,
            [new(Guid.NewGuid(), runId, 1, now, "warning", "hostile.event", hostile, $"{{\"message\":\"{hostile}\"}}")],
            Metrics: [],
            Artifacts: [],
            AuditEvents: []);
    }

    private sealed class HostileRunQueryService(
        IManagementQueryService inner,
        ManagementRunDetail hostileRun) : IManagementQueryService
    {
        public ValueTask<IReadOnlyList<ManagementAgentItem>> GetAgentsAsync(CancellationToken cancellationToken = default) =>
            inner.GetAgentsAsync(cancellationToken);

        public ValueTask<ManagementAgentDetail?> GetAgentAsync(AgentId agentId, CancellationToken cancellationToken = default) =>
            inner.GetAgentAsync(agentId, cancellationToken);

        public ValueTask<IReadOnlyList<ManagementScheduleItem>> GetSchedulesAsync(CancellationToken cancellationToken = default) =>
            inner.GetSchedulesAsync(cancellationToken);

        public ValueTask<ManagementScheduleDetail?> GetScheduleAsync(Guid scheduleId, CancellationToken cancellationToken = default) =>
            inner.GetScheduleAsync(scheduleId, cancellationToken);

        public ValueTask<ManagementPage<ManagementRunItem>> GetRunsAsync(ManagementRunQuery query, CancellationToken cancellationToken = default) =>
            inner.GetRunsAsync(query, cancellationToken);

        public ValueTask<ManagementRunDetail?> GetRunAsync(AgentRunId runId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ManagementRunDetail?>(runId == hostileRun.Run.Run.Id ? hostileRun : null);

        public ValueTask<ManagementPage<AuditEventRecord>> GetAuditAsync(ManagementAuditQuery query, CancellationToken cancellationToken = default) =>
            inner.GetAuditAsync(query, cancellationToken);

        public ValueTask<AuditEventRecord?> GetAuditEventAsync(Guid auditId, CancellationToken cancellationToken = default) =>
            inner.GetAuditEventAsync(auditId, cancellationToken);

        public ValueTask<ManagementDatabaseStatus> GetDatabaseStatusAsync(CancellationToken cancellationToken = default) =>
            inner.GetDatabaseStatusAsync(cancellationToken);
    }

    private static HostBootstrapSettings CreateSettings(string repositoryRoot, string data, string agents, string url)
    {
        string runnerDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "HomeBusinessAssistant.Runner",
            "bin",
            "Release",
            "net10.0-windows");
        return new(
            repositoryRoot,
            data,
            agents,
            Path.Combine(repositoryRoot, "manifests"),
            Path.Combine(runnerDirectory, "HomeBusinessAssistant.Runner.exe"),
            runnerDirectory,
            "assistant.db",
            url,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(15));
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
