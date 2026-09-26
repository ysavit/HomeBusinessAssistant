using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Manifest;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Power;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Runner.Tests;

internal sealed class RunnerProcessIntegrationTests
{
    private static readonly int[] ExpectedCompetingExitCodes = [0, 3];

    [Test]
    public async Task NonzeroExitIsPersistedAsFailure()
    {
        await using RunnerFixture fixture = await RunnerFixture.CreateAsync("failure", exitCode: 40);

        CommandResult result = await fixture.RunAgentAsync("fail");
        AgentRunRecord run = await fixture.GetRunAsync(result.OccurrenceId);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(30));
            Assert.That(run.Status, Is.EqualTo(AgentRunStatus.Failed));
            Assert.That(run.ExitCode, Is.EqualTo(40));
        });
    }

    [Test]
    public async Task AllEventTypesAndMetricArePersisted()
    {
        await using RunnerFixture fixture = await RunnerFixture.CreateAsync("events");

        CommandResult result = await fixture.RunAgentAsync("emit-all-events");
        AgentRunRecord run = await fixture.GetRunAsync(result.OccurrenceId);
        (long metrics, _) = await fixture.QueryAsync(
            "SELECT COUNT(*), '' FROM AgentRunMetrics WHERE RunId = $id",
            run.Id.Value);
        (long events, _) = await fixture.QueryAsync(
            "SELECT COUNT(*), '' FROM AgentRunEvents WHERE RunId = $id AND Sequence > 0",
            run.Id.Value);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(metrics, Is.EqualTo(1));
            Assert.That(events, Is.EqualTo(8));
        });
    }

    [Test]
    public async Task CompetingExecutorsLaunchOccurrenceOnlyOnce()
    {
        await using RunnerFixture fixture = await RunnerFixture.CreateAsync("claim");
        OccurrenceId occurrenceId = await fixture.CreateOccurrenceAsync("delayed-heartbeats");

        CommandResult[] results = await Task.WhenAll(
            fixture.ExecuteOccurrenceAsync(occurrenceId),
            fixture.ExecuteOccurrenceAsync(occurrenceId));
        AgentRunRecord? run = await fixture.TryGetRunAsync(occurrenceId);

        Assert.Multiple(() =>
        {
            Assert.That(
                run,
                Is.Not.Null,
                string.Join(Environment.NewLine, results.Select(item => item.Diagnostic)));
            Assert.That(
                results.Select(item => item.ExitCode),
                Is.EquivalentTo(ExpectedCompetingExitCodes),
                string.Join(Environment.NewLine, results.Select(item => item.Diagnostic)));
            Assert.That(run!.Status, Is.EqualTo(AgentRunStatus.Completed));
        });
    }

    [Test]
    public async Task PersistedCancellationIsDistinctFromTimeoutAndReleasesProcess()
    {
        await using RunnerFixture fixture = await RunnerFixture.CreateAsync("cancel", timeoutSeconds: 10);
        OccurrenceId occurrenceId = await fixture.CreateOccurrenceAsync("ignore-cancellation");
        Task<CommandResult> execution = fixture.ExecuteOccurrenceAsync(occurrenceId);
        AgentRunRecord running = await fixture.WaitForRunningAsync(occurrenceId);

        await fixture.RequestCancellationAsync(occurrenceId);
        CommandResult result = await execution.WaitAsync(TimeSpan.FromSeconds(15));
        AgentRunRecord terminal = await fixture.GetRunAsync(occurrenceId);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(20));
            Assert.That(terminal.Status, Is.EqualTo(AgentRunStatus.Cancelled));
            Assert.That(terminal.ErrorType, Is.EqualTo("runner.cancelled"));
            Assert.That(running.ProcessId.HasValue && IsProcessRunning(running.ProcessId.Value), Is.False);
        });
    }

    [Test]
    public async Task RunnerHeartbeatAdvancesWithoutDependingOnAgentHeartbeat()
    {
        await using RunnerFixture fixture = await RunnerFixture.CreateAsync("heartbeat");

        CommandResult result = await fixture.RunAgentAsync("delayed-heartbeats");
        AgentRunRecord run = await fixture.GetRunAsync(result.OccurrenceId);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(run.LastHeartbeatAtUtc, Is.GreaterThan(run.StartedAtUtc));
        });
    }

    [Test]
    public async Task SuccessUsesExactConfigurationAndCleansTemporaryConfiguration()
    {
        await using RunnerFixture fixture = await RunnerFixture.CreateAsync("alpha");

        CommandResult result = await fixture.RunAgentAsync("success");
        AgentRunRecord run = await fixture.GetRunAsync(result.OccurrenceId);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(run.Status, Is.EqualTo(AgentRunStatus.Completed));
            Assert.That(run.SummaryText, Does.Contain("alpha"));
            Assert.That(run.ProcessId, Is.GreaterThan(0));
            Assert.That(run.ProcessStartedAtUtc, Is.Not.Null);
            Assert.That(run.ExecutableHash, Has.Length.EqualTo(64));
            Assert.That(File.Exists(Path.Combine(fixture.DataDirectory, "temp", "config", $"{run.Id.Value:D}.json")), Is.False);
        });
    }

    [Test]
    public async Task ArtifactIsCopiedIntoPlatformStorageAndRegistered()
    {
        await using RunnerFixture fixture = await RunnerFixture.CreateAsync("artifact-marker");

        CommandResult result = await fixture.RunAgentAsync("artifact-success");
        AgentRunRecord run = await fixture.GetRunAsync(result.OccurrenceId);
        (long count, string value) = await fixture.QueryAsync(
            "SELECT COUNT(*), COALESCE(MAX(RelativePath), '') FROM RunArtifacts WHERE RunId = $id",
            run.Id.Value);
        (_, string deleteAfterValue) = await fixture.QueryAsync(
            "SELECT COUNT(*), COALESCE(MAX(DeleteAfterUtc), '') FROM RunArtifacts WHERE RunId = $id",
            run.Id.Value);
        DateTimeOffset deleteAfter = DateTimeOffset.Parse(
            deleteAfterValue,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(File.Exists(Path.Combine(fixture.DataDirectory, "artifacts", value.Replace('/', Path.DirectorySeparatorChar))), Is.True);
            Assert.That(deleteAfter, Is.GreaterThan(DateTimeOffset.UtcNow.AddDays(6)));
        });
    }

    [TestCase("malformed-json")]
    [TestCase("out-of-order-sequence")]
    [TestCase("artifact-missing")]
    [TestCase("artifact-tampering")]
    [TestCase("artifact-traversal")]
    [TestCase("no-summary")]
    public async Task FatalProtocolScenariosFailWithoutCrashingRunner(string command)
    {
        await using RunnerFixture fixture = await RunnerFixture.CreateAsync("protocol");

        CommandResult result = await fixture.RunAgentAsync(command);
        AgentRunRecord run = await fixture.GetRunAsync(result.OccurrenceId);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(30));
            Assert.That(run.Status, Is.EqualTo(AgentRunStatus.Failed));
            Assert.That(run.ErrorType, Does.StartWith("protocol."));
        });
    }

    [Test]
    public async Task OversizedLineIsBoundedAndLaterValidLifecycleSucceeds()
    {
        await using RunnerFixture fixture = await RunnerFixture.CreateAsync("oversized");

        CommandResult result = await fixture.RunAgentAsync("oversized-line");
        AgentRunRecord run = await fixture.GetRunAsync(result.OccurrenceId);
        (long count, _) = await fixture.QueryAsync(
            "SELECT COUNT(*), '' FROM AgentRunEvents WHERE RunId = $id AND DataJson LIKE '%protocol.event-too-large%'",
            run.Id.Value);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(run.Status, Is.EqualTo(AgentRunStatus.Completed));
            Assert.That(count, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task UnknownFutureEventIsPreservedWithoutBecomingViolation()
    {
        await using RunnerFixture fixture = await RunnerFixture.CreateAsync("future");

        CommandResult result = await fixture.RunAgentAsync("unknown-future-event");
        AgentRunRecord run = await fixture.GetRunAsync(result.OccurrenceId);
        (long count, _) = await fixture.QueryAsync(
            "SELECT COUNT(*), '' FROM AgentRunEvents WHERE RunId = $id AND EventType = 'protocol.unknown-event'",
            run.Id.Value);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(run.Status, Is.EqualTo(AgentRunStatus.Completed));
            Assert.That(count, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StderrIsRedactedAndBounded()
    {
        await using RunnerFixture fixture = await RunnerFixture.CreateAsync("stderr");

        CommandResult result = await fixture.RunAgentAsync("stderr-burst");
        AgentRunRecord run = await fixture.GetRunAsync(result.OccurrenceId);
        (long count, string messages) = await fixture.QueryAsync(
            "SELECT COUNT(*), COALESCE(GROUP_CONCAT(Message, ' '), '') FROM AgentRunEvents WHERE RunId = $id AND EventType LIKE 'runner.stderr%'",
            run.Id.Value);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(count, Is.EqualTo(101));
            Assert.That(messages, Does.Not.Contain("fixture-secret-token"));
            Assert.That(messages, Does.Contain("suppressed"));
        });
    }

    [Test]
    public async Task TimeoutKillsTheEntireFixtureProcessTree()
    {
        await using RunnerFixture fixture = await RunnerFixture.CreateAsync("timeout", timeoutSeconds: 1);

        CommandResult result = await fixture.RunAgentAsync("hang").WaitAsync(TimeSpan.FromSeconds(15));
        AgentRunRecord run = await fixture.GetRunAsync(result.OccurrenceId);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(21));
            Assert.That(run.Status, Is.EqualTo(AgentRunStatus.TimedOut));
            Assert.That(run.ProcessId.HasValue && IsProcessRunning(run.ProcessId.Value), Is.False);
            Assert.That(Process.GetProcessesByName("HomeBusinessAssistant.TestAgent"), Is.Empty);
        });
    }

    [Test]
    public async Task RequiredPowerLifetimeIsReleasedAfterSuccessFailureAndTimeout()
    {
        var scenarios = new[]
        {
            (Command: "success", Timeout: 5, ExitCode: (int?)null),
            (Command: "fail", Timeout: 5, ExitCode: (int?)40),
            (Command: "hang", Timeout: 1, ExitCode: (int?)null),
        };
        foreach ((string command, int timeout, int? exitCode) in scenarios)
        {
            await using RunnerFixture fixture = await RunnerFixture.CreateAsync(
                $"power-{command}",
                timeout,
                exitCode);
            OccurrenceId occurrenceId = await fixture.CreateOccurrenceAsync(command, keepSystemAwake: true);
            var hook = new TrackingLifetimeHook();

            _ = await fixture.ExecuteOccurrenceAsync(occurrenceId, hook);

            Assert.Multiple(() =>
            {
                Assert.That(hook.Acquisitions, Is.EqualTo(1), command);
                Assert.That(hook.Disposals, Is.EqualTo(1), command);
                Assert.That(hook.LastRequest?.KeepSystemAwake, Is.True, command);
                Assert.That(hook.LastRequest?.KeepDisplayOn, Is.False, command);
            });
        }
    }

    [Test]
    public async Task RequiredPowerLifetimeIsReleasedAfterPersistedCancellation()
    {
        await using RunnerFixture fixture = await RunnerFixture.CreateAsync("power-cancel", timeoutSeconds: 10);
        OccurrenceId occurrenceId = await fixture.CreateOccurrenceAsync("ignore-cancellation", keepSystemAwake: true);
        var hook = new TrackingLifetimeHook();

        Task<CommandResult> execution = fixture.ExecuteOccurrenceAsync(occurrenceId, hook);
        _ = await fixture.WaitForRunningAsync(occurrenceId);
        _ = await fixture.RequestCancellationAsync(occurrenceId);
        CommandResult result = await execution.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(20));
            Assert.That(hook.Acquisitions, Is.EqualTo(1));
            Assert.That(hook.Disposals, Is.EqualTo(1));
        });
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

internal sealed class TrackingLifetimeHook : IExecutionLifetimeHook
{
    private int acquisitions;
    private int disposals;

    public int Acquisitions => Volatile.Read(ref acquisitions);

    public int Disposals => Volatile.Read(ref disposals);

    public ExecutionLifetimeRequest? LastRequest { get; private set; }

    public ValueTask<IAsyncDisposable> AcquireAsync(
        ExecutionLifetimeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastRequest = request;
        Interlocked.Increment(ref acquisitions);
        return ValueTask.FromResult<IAsyncDisposable>(new Handle(this));
    }

    private sealed class Handle(TrackingLifetimeHook owner) : IAsyncDisposable
    {
        private int disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                Interlocked.Increment(ref owner.disposals);
            }

            return ValueTask.CompletedTask;
        }
    }
}

internal sealed record CommandResult(int ExitCode, OccurrenceId OccurrenceId, string Diagnostic = "");

internal sealed class RunnerFixture : IAsyncDisposable
{
    private static readonly AgentId TestAgentId = AgentId.Parse("test-agent");
    private readonly string root;
    private readonly AssistantDatabase database;
    private readonly OccurrenceRepository occurrences;
    private readonly AgentRunRepository runs;

    private RunnerFixture(
        string root,
        string dataDirectory,
        string agentDirectory,
        string manifestDirectory,
        AssistantDatabase database)
    {
        this.root = root;
        DataDirectory = dataDirectory;
        AgentDirectory = agentDirectory;
        ManifestDirectory = manifestDirectory;
        this.database = database;
        occurrences = new(database.ContextFactory, TimeProvider.System);
        runs = new(database.ContextFactory);
    }

    public string DataDirectory { get; }
    public string AgentDirectory { get; }
    public string ManifestDirectory { get; }

    public static async ValueTask<RunnerFixture> CreateAsync(
        string marker,
        int timeoutSeconds = 5,
        int? exitCode = null)
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-runner-tests-ü-{Guid.NewGuid():N}");
        string data = Path.Combine(root, "data");
        string agents = Path.Combine(root, "agents");
        string manifests = Path.Combine(root, "manifests");
        string repository = FindRepositoryRoot();
        Directory.CreateDirectory(Path.Combine(agents, "test-agent"));
        Directory.CreateDirectory(manifests);
        foreach (string path in Directory.GetFiles(Path.Combine(repository, "manifests"), "*.json"))
        {
            File.Copy(path, Path.Combine(manifests, Path.GetFileName(path)));
        }

        string fixtureOutput = FindFixtureOutput(repository);
        foreach (string path in Directory.GetFiles(fixtureOutput))
        {
            File.Copy(path, Path.Combine(agents, "test-agent", Path.GetFileName(path)));
        }

        string sourceManifest = Path.Combine(repository, "tests", "HomeBusinessAssistant.TestAgent", "test-agent.agent-manifest.json");
        string manifestText = await File.ReadAllTextAsync(sourceManifest);
        manifestText = manifestText.Replace("\"defaultTimeoutSeconds\": 5", $"\"defaultTimeoutSeconds\": {timeoutSeconds}", StringComparison.Ordinal);
        string manifestPath = Path.Combine(manifests, "test-agent.agent-manifest.json");
        await File.WriteAllTextAsync(manifestPath, manifestText);

        AssistantDatabase database = await AssistantDatabase.InitializeAsync(
            new(data, ManifestDirectory: manifests), TimeProvider.System);
        AgentManifest manifest = (await AgentManifestLoader.LoadAsync(manifestPath)).Manifest
            ?? throw new InvalidOperationException("The test agent manifest is invalid.");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var agentRepository = new AgentDefinitionRepository(database.ContextFactory, TimeProvider.System);
        _ = await agentRepository.UpsertManifestAsync(new(
            manifest.Id,
            manifest.DisplayName,
            manifest.Description,
            manifest.ManifestVersion.ToString(),
            manifest.Version.Value,
            manifest.Executable,
            "test-agent",
            CanonicalJson.Serialize(JsonSerializer.SerializeToElement(manifest.Capabilities)),
            CanonicalJson.Serialize(JsonSerializer.SerializeToElement(manifest.SupportedCommands)),
            manifest.DefaultConcurrencyPolicy,
            manifest.SupportsScheduling,
            manifest.SupportsManualRun,
            manifest.RequiresInteractiveUserSession,
            Enabled: true,
            CreatedAtUtc: now,
            UpdatedAtUtc: now));
        var configurations = new AgentConfigurationService(
            database.ContextFactory,
            new BasicAgentConfigurationValidator(),
            TimeProvider.System);
        var configuration = new Dictionary<string, object?>
        {
            ["schemaVersion"] = "1.0",
            ["marker"] = marker,
        };
        if (exitCode.HasValue)
        {
            configuration["exitCode"] = exitCode.Value;
        }

        _ = await configurations.SaveAsync(new(
            TestAgentId,
            "1.0",
            JsonSerializer.SerializeToElement(configuration),
            "runner-tests",
            "Create deterministic test configuration.",
            Guid.NewGuid()));
        return new(root, data, agents, manifests, database);
    }

    public async Task<CommandResult> RunAgentAsync(string command)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        int exitCode = await RunnerCommand.ExecuteAsync(
        [
            "run-agent",
            "--agent-id", TestAgentId.Value,
            "--command", command,
            "--data-directory", DataDirectory,
            "--agent-directory", AgentDirectory,
            "--manifest-directory", ManifestDirectory,
        ], output, error);
        string json = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
            ?? throw new InvalidOperationException($"Runner produced no result: {error}");
        using JsonDocument document = JsonDocument.Parse(json);
        OccurrenceId occurrenceId = OccurrenceId.Parse(document.RootElement.GetProperty("occurrenceId").GetString()!);
        return new(exitCode, occurrenceId);
    }

    public async Task<CommandResult> ExecuteOccurrenceAsync(
        OccurrenceId occurrenceId,
        IExecutionLifetimeHook? lifetimeHook = null)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        if (lifetimeHook is not null)
        {
            var bootstrap = new Infrastructure.Execution.RunnerBootstrapSettings(
                DataDirectory,
                AgentDirectory,
                ManifestDirectory);
            await using RunnerRuntime runtime = await RunnerRuntime.CreateAsync(
                bootstrap,
                CancellationToken.None,
                lifetimeHook);
            var invocation = new RunnerInvocation(
                RunnerOperation.Execute,
                bootstrap,
                occurrenceId,
                AgentId: null,
                CommandName: null,
                ArgumentsJson: "{}",
                ReconcileWakeAfterExecution: false,
                WakeTestMinutes: 3);
            int directExitCode = await runtime.ExecuteAsync(invocation, output, CancellationToken.None);
            return new(directExitCode, occurrenceId, output.ToString() + error);
        }

        int exitCode = await RunnerCommand.ExecuteAsync(
        [
            "execute",
            "--occurrence-id", occurrenceId.ToString(),
            "--data-directory", DataDirectory,
            "--agent-directory", AgentDirectory,
            "--manifest-directory", ManifestDirectory,
        ], output, error);
        return new(exitCode, occurrenceId, output.ToString() + error);
    }

    public async ValueTask<OccurrenceId> CreateOccurrenceAsync(
        string command,
        bool keepSystemAwake = false)
    {
        var configurations = new AgentConfigurationService(
            database.ContextFactory,
            new BasicAgentConfigurationValidator(),
            TimeProvider.System);
        AgentConfigurationRecord current = await configurations.GetCurrentAsync(TestAgentId)
            ?? throw new InvalidOperationException("The test configuration is missing.");
        OccurrenceId id = OccurrenceId.New();
        _ = await occurrences.CreateIfAbsentAsync(new(
            id,
            ScheduleId: null,
            TestAgentId,
            command,
            "{}",
            current.CurrentRevisionId,
            DateTimeOffset.UtcNow,
            TriggerType.CommandLine,
            AttemptNumber: 0,
            ParentOccurrenceId: null,
            OccurrenceStatus.Ready,
            RequiresWake: keepSystemAwake,
            KeepSystemAwake: keepSystemAwake,
            KeepDisplayOn: false));
        return id;
    }

    public async Task<AgentRunRecord> WaitForRunningAsync(OccurrenceId occurrenceId)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            AgentRunRecord? run = await runs.GetForOccurrenceAsync(occurrenceId);
            if (run?.Status == AgentRunStatus.Running)
            {
                return run;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("The fixture did not reach Running state.");
    }

    public ValueTask<bool> RequestCancellationAsync(OccurrenceId occurrenceId) =>
        occurrences.RequestCancellationAsync(occurrenceId, DateTimeOffset.UtcNow, "runner-test-cancellation");

    public async ValueTask<AgentRunRecord> GetRunAsync(OccurrenceId occurrenceId) =>
        await runs.GetForOccurrenceAsync(occurrenceId)
            ?? throw new InvalidOperationException("The occurrence has no run.");

    public ValueTask<AgentRunRecord?> TryGetRunAsync(OccurrenceId occurrenceId) =>
        runs.GetForOccurrenceAsync(occurrenceId);

    public async Task<(long Count, string Value)> QueryAsync(string sql, Guid runId)
    {
        await using AssistantDbContext context = await database.ContextFactory.CreateDbContextAsync();
        await context.Database.OpenConnectionAsync();
        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = "$id";
        parameter.Value = runId;
        command.Parameters.Add(parameter);
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        _ = await reader.ReadAsync();
        return (reader.GetInt64(0), reader.GetString(1));
    }

    public ValueTask DisposeAsync()
    {
        string fullRoot = Path.GetFullPath(root);
        if (!fullRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(fullRoot).StartsWith("hba-runner-tests-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to delete an unexpected test directory.");
        }

        if (Directory.Exists(fullRoot))
        {
            Directory.Delete(fullRoot, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private static string FindFixtureOutput(string repository)
    {
        string configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        string path = Path.Combine(repository, "tests", "HomeBusinessAssistant.TestAgent", "bin", configuration, "net10.0-windows");
        return Directory.Exists(path)
            ? path
            : throw new DirectoryNotFoundException("The test agent output was not built.");
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
