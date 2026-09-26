using System.Data.Common;
using System.Text.Json;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using WakeRemote.Application;

namespace HomeBusinessAssistant.Runner.Tests;

internal sealed class WakeRemoteRunnerIntegrationTests
{
    [Test]
    public async Task DiagnosticFakeAvailabilityWindowRunsAsRealChildThroughRunner()
    {
        int durationSeconds = int.TryParse(
            Environment.GetEnvironmentVariable("HBA_WAKE_REMOTE_SMOKE_SECONDS"),
            out int configured)
            && configured is >= 8 and <= 60
                ? configured
                : 8;
        await using WakeRemoteRunnerFixture fixture = await WakeRemoteRunnerFixture.CreateAsync(durationSeconds);

        (int exitCode, OccurrenceId occurrenceId, string diagnostic) = await fixture.ExecuteAsync();
        AgentRunRecord run = await fixture.GetRunAsync(occurrenceId);
        IReadOnlyList<string> metrics = await fixture.GetMetricsAsync(run.Id);
        int heartbeatCount = await fixture.GetHeartbeatCountAsync(run.Id);
        ScheduleOccurrenceRecord? nextWake = await fixture.GetEarliestWakeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero, diagnostic);
            Assert.That(run.Status, Is.EqualTo(AgentRunStatus.Completed), diagnostic);
            Assert.That(run.SummaryJson, Does.Contain("DiagnosticFake"));
            Assert.That(metrics, Does.Contain("wake_delay_seconds"));
            Assert.That(metrics, Does.Contain("network_ready"));
            Assert.That(metrics, Does.Contain("remote.ready"));
            Assert.That(metrics, Does.Contain("power_handle_released"));
            Assert.That(heartbeatCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(nextWake, Is.Null);
            Assert.That(File.Exists(Path.Combine(
                fixture.DataDirectory,
                "temp",
                "config",
                $"{run.Id.Value:D}.json")), Is.False);
        });
    }

    private sealed class WakeRemoteRunnerFixture : IAsyncDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly string root;
        private readonly AssistantDatabase database;
        private readonly OccurrenceRepository occurrences;
        private readonly AgentRunRepository runs;
        private readonly OccurrenceId occurrenceId;

        private WakeRemoteRunnerFixture(
            string root,
            string dataDirectory,
            string agentDirectory,
            string manifestDirectory,
            AssistantDatabase database,
            OccurrenceId occurrenceId)
        {
            this.root = root;
            DataDirectory = dataDirectory;
            AgentDirectory = agentDirectory;
            ManifestDirectory = manifestDirectory;
            this.database = database;
            this.occurrenceId = occurrenceId;
            occurrences = new(database.ContextFactory, TimeProvider.System);
            runs = new(database.ContextFactory);
        }

        public string DataDirectory { get; }
        public string AgentDirectory { get; }
        public string ManifestDirectory { get; }

        public static async ValueTask<WakeRemoteRunnerFixture> CreateAsync(int durationSeconds)
        {
            string root = Path.Combine(Path.GetTempPath(), $"hba-wake-runner-tests-{Guid.NewGuid():N}");
            string data = Path.Combine(root, "data");
            string agents = Path.Combine(root, "agents");
            string manifests = Path.Combine(root, "manifests");
            string wakeDirectory = Path.Combine(agents, "wake-remote");
            string repository = FindRepositoryRoot();
            Directory.CreateDirectory(wakeDirectory);
            Directory.CreateDirectory(manifests);
            foreach (string path in Directory.GetFiles(Path.Combine(repository, "manifests"), "*.json"))
            {
                File.Copy(path, Path.Combine(manifests, Path.GetFileName(path)));
            }

            string wakeOutput = Path.Combine(
                repository,
                "agents",
                "WakeRemote",
                "WakeRemote.Agent",
                "bin",
                "Release",
                "net10.0-windows");
            foreach (string path in Directory.GetFiles(wakeOutput))
            {
                File.Copy(path, Path.Combine(wakeDirectory, Path.GetFileName(path)));
            }

            AssistantDatabase database = await AssistantDatabase.InitializeAsync(
                new(data, ManifestDirectory: manifests),
                TimeProvider.System);
            var configurations = new AgentConfigurationService(
                database.ContextFactory,
                new CompositeAgentConfigurationValidator([
                    new BasicAgentConfigurationValidator(),
                    new WakeRemoteConfigurationValidator(),
                ]),
                TimeProvider.System);
            WakeRemoteConfiguration configuration = CreateDiagnosticConfiguration();
            SaveConfigurationResult saved = await configurations.SaveAsync(new(
                WakeRemoteDefaults.AgentId,
                WakeRemoteConfiguration.CurrentSchemaVersion,
                JsonSerializer.SerializeToElement(configuration, JsonOptions),
                "wake-remote-runner-test",
                "Create explicitly fake diagnostic provider configuration.",
                Guid.NewGuid()));
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
            OccurrenceId occurrenceId = OccurrenceId.New();
            var occurrenceRepository = new OccurrenceRepository(database.ContextFactory, TimeProvider.System);
            _ = await occurrenceRepository.CreateIfAbsentAsync(new(
                occurrenceId,
                ScheduleId: null,
                WakeRemoteDefaults.AgentId,
                "run",
                WakeRemoteInput.SerializeOccurrence(new(
                    $"diagnostic-fake-{occurrenceId}",
                    nowUtc,
                    nowUtc.AddSeconds(durationSeconds),
                    RemoteProviderRequired: true,
                    AllowImmediateMisfire: true)),
                saved.Revision.Id,
                nowUtc,
                TriggerType.CommandLine,
                AttemptNumber: 0,
                ParentOccurrenceId: null,
                OccurrenceStatus.Ready,
                RequiresWake: true,
                KeepSystemAwake: true,
                KeepDisplayOn: false));
            return new(root, data, agents, manifests, database, occurrenceId);
        }

        public async Task<(int ExitCode, OccurrenceId OccurrenceId, string Diagnostic)> ExecuteAsync()
        {
            using StringWriter output = new();
            using StringWriter error = new();
            int exitCode = await RunnerCommand.ExecuteAsync(
            [
                "execute",
                "--occurrence-id", occurrenceId.ToString(),
                "--data-directory", DataDirectory,
                "--agent-directory", AgentDirectory,
                "--manifest-directory", ManifestDirectory,
            ], output, error);
            return (exitCode, occurrenceId, output.ToString() + error);
        }

        public async ValueTask<AgentRunRecord> GetRunAsync(OccurrenceId id) =>
            await runs.GetForOccurrenceAsync(id)
            ?? throw new InvalidOperationException("Wake Remote execution did not create a run.");

        public async ValueTask<IReadOnlyList<string>> GetMetricsAsync(AgentRunId runId)
        {
            var result = new List<string>();
            await using AssistantDbContext context = await database.ContextFactory.CreateDbContextAsync();
            await context.Database.OpenConnectionAsync();
            await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT Name FROM AgentRunMetrics WHERE RunId = $id ORDER BY TimestampUtc, Name";
            AddRunParameter(command, runId);
            await using DbDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(reader.GetString(0));
            }

            return result;
        }

        public async ValueTask<int> GetHeartbeatCountAsync(AgentRunId runId)
        {
            await using AssistantDbContext context = await database.ContextFactory.CreateDbContextAsync();
            await context.Database.OpenConnectionAsync();
            await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM AgentRunEvents WHERE RunId = $id AND EventType = 'heartbeat'";
            AddRunParameter(command, runId);
            object? value = await command.ExecuteScalarAsync();
            return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        public ValueTask<ScheduleOccurrenceRecord?> GetEarliestWakeAsync() =>
            occurrences.GetEarliestWakeAsync(DateTimeOffset.UtcNow);

        public ValueTask DisposeAsync()
        {
            string fullRoot = Path.GetFullPath(root);
            if (!fullRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(fullRoot).StartsWith("hba-wake-runner-tests-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to delete an unexpected Wake Remote test directory.");
            }

            if (Directory.Exists(fullRoot))
            {
                Directory.Delete(fullRoot, recursive: true);
            }

            return ValueTask.CompletedTask;
        }

        private static WakeRemoteConfiguration CreateDiagnosticConfiguration() => new(
            WakeRemoteConfiguration.CurrentSchemaVersion,
            TimeZoneInfo.Local.Id,
            NetworkReadyTimeoutSeconds: 5,
            NetworkProbeIntervalSeconds: 1,
            WindowHeartbeatIntervalSeconds: 1,
            MaximumWakeStalenessSeconds: 300,
            KeepDisplayOn: false,
            ReleaseToNormalPowerPolicyAfterWindow: true,
            ForceSleepAfterWindow: false,
            new(
                RemoteProviderKind.DiagnosticFake,
                ServiceNames: [],
                ProcessNames: [],
                CheckLocalListener: false,
                ListenerHost: "127.0.0.1",
                ListenerPort: 3389,
                DiagnosticReady: true),
            new(false, null),
            new(false, null, 0));

        private static void AddRunParameter(DbCommand command, AgentRunId runId)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "$id";
            parameter.Value = runId.Value;
            command.Parameters.Add(parameter);
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
    }
}
