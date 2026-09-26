using System.Data.Common;
using System.Text.Json;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Configuration;
using HomeBusinessAssistant.Infrastructure.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Runner.Tests;

internal sealed class SampleBusinessAgentRunnerIntegrationTests
{
    [Test]
    public async Task ScannerRegistersDisabledAndRunnerPersistsRealProtocolArtifactAndHashes()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-sample-runner-{Guid.NewGuid():N}");
        string data = Path.Combine(root, "data");
        string agents = Path.Combine(root, "agents");
        string manifests = Path.Combine(root, "manifests");
        string package = Path.Combine(agents, "sample-business-agent");
        Directory.CreateDirectory(package);
        Directory.CreateDirectory(manifests);
        string repository = FindRepositoryRoot();
        CopyDirectory(Path.Combine(repository, "agents", "SampleBusinessAgent", "SampleBusinessAgent.Agent", "bin", "Release", "net10.0"), package);
        File.Copy(Path.Combine(repository, "agents", "SampleBusinessAgent", "manifest.json"), Path.Combine(package, "manifest.json"), true);
        File.Copy(Path.Combine(repository, "agents", "SampleBusinessAgent", "configuration.schema.json"), Path.Combine(package, "configuration.schema.json"), true);
        File.Copy(Path.Combine(repository, "manifests", "founder-scout.agent-manifest.json"), Path.Combine(manifests, "founder-scout.agent-manifest.json"));
        File.Copy(Path.Combine(repository, "manifests", "wake-remote.agent-manifest.json"), Path.Combine(manifests, "wake-remote.agent-manifest.json"));
        try
        {
            using var scanOutput = new StringWriter();
            int scanExit = await RunnerCommand.ExecuteAsync(
                ["scan-agents", "--data-directory", data, "--agent-directory", agents, "--manifest-directory", manifests],
                scanOutput,
                TextWriter.Null);
            AssistantDatabase database = await AssistantDatabase.InitializeAsync(new(data, ManifestDirectory: manifests), TimeProvider.System);
            var definitions = new AgentDefinitionRepository(database.ContextFactory, TimeProvider.System);
            AgentId agentId = AgentId.Parse("sample-business-agent");
            Assert.That((await definitions.GetAsync(agentId))!.Enabled, Is.False);
            _ = await definitions.SetEnabledAsync(agentId, true);
            var catalog = new FileAgentConfigurationSchemaCatalog(agents);
            var configurations = new AgentConfigurationService(
                database.ContextFactory,
                new CompositeAgentConfigurationValidator([
                    new BasicAgentConfigurationValidator(),
                    new GenericAgentConfigurationValidator(catalog, new HashSet<AgentId>()),
                ]),
                TimeProvider.System);
            AgentConfigurationRecord seeded = (await configurations.GetCurrentAsync(agentId))!;
            using JsonDocument seededDocument = JsonDocument.Parse(seeded.CurrentRevision.CanonicalConfigurationJson);
            var configuredValues = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(seededDocument.RootElement)!;
            configuredValues["maximumFiles"] = JsonSerializer.SerializeToElement(12);
            SaveConfigurationResult configured = await configurations.SaveAsync(new(
                agentId,
                "1.0",
                JsonSerializer.SerializeToElement(configuredValues),
                "stage-17-runner-test",
                "Configured generated-agent-compatible sample settings.",
                Guid.NewGuid(),
                seeded.CurrentRevision.RevisionNumber,
                seeded.CurrentRevision.ConfigurationHash));
            var schedules = new ScheduleRepository(database.ContextFactory, TimeProvider.System);
            var scheduleValidator = new AgentScheduleValidator(
                definitions,
                configurations,
                schedules,
                new ScheduleTimeZoneService());
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var schedule = new AgentScheduleRecord(
                Guid.NewGuid(),
                agentId,
                "Stage 17 sample schedule",
                "run",
                "{}",
                ScheduleKind.Manual,
                ScheduleDefinitionJson.Serialize(new ManualScheduleDefinition()),
                TimeZoneInfo.Local.Id,
                MisfirePolicy.Skip,
                ConcurrencyPolicy.Forbid,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromSeconds(30),
                RetryPolicyJson.Serialize(RetryPolicyDefinition.None),
                WakePolicy.Never,
                configured.Revision.Id,
                AllowDisabledAgent: false,
                IsEnabled: true,
                IsPaused: false,
                PausedUntilUtc: null,
                now,
                now,
                ConcurrencyToken: 0);
            AgentScheduleValidationResult scheduleValidation = await scheduleValidator.ValidateAsync(schedule);
            Assert.That(scheduleValidation.Errors, Is.Empty);
            AgentScheduleRecord savedSchedule = await schedules.SaveAsync(schedule, expectedConcurrencyToken: null);

            using var runOutput = new StringWriter();
            int runExit = await RunnerCommand.ExecuteAsync(
                ["run-agent", "--agent-id", agentId.Value, "--command", "run", "--data-directory", data, "--agent-directory", agents, "--manifest-directory", manifests],
                runOutput,
                TextWriter.Null);
            using JsonDocument result = JsonDocument.Parse(runOutput.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Last());
            Guid runId = Guid.Parse(result.RootElement.GetProperty("runId").GetString()!);
            (long protocolEvents, string executableHash) = await QueryAsync(
                database,
                "SELECT (SELECT COUNT(*) FROM AgentRunEvents WHERE RunId = $id AND Sequence > 0), (SELECT ExecutableHash FROM AgentRuns WHERE Id = $id)",
                runId);
            (long artifacts, string artifactHash) = await QueryAsync(
                database,
                "SELECT COUNT(*), COALESCE(MAX(Sha256), '') FROM RunArtifacts WHERE RunId = $id",
                runId);
            (long revisions, string configurationHash) = await QueryAsync(
                database,
                "SELECT COUNT(*), COALESCE(MAX(ConfigurationHash), '') FROM ConfigurationRevisions WHERE AgentId = 'sample-business-agent'",
                runId);

            Assert.Multiple(() =>
            {
                Assert.That(scanExit, Is.Zero);
                Assert.That(runExit, Is.Zero);
                Assert.That(result.RootElement.GetProperty("status").GetString(), Is.EqualTo("Completed"));
                Assert.That(protocolEvents, Is.GreaterThanOrEqualTo(6));
                Assert.That(artifacts, Is.EqualTo(1));
                Assert.That(executableHash, Has.Length.EqualTo(64));
                Assert.That(artifactHash, Has.Length.EqualTo(64));
                Assert.That(revisions, Is.EqualTo(2));
                Assert.That(configurationHash, Has.Length.EqualTo(64));
                Assert.That(savedSchedule.Id, Is.EqualTo(schedule.Id));
            });
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(long Count, string Value)> QueryAsync(AssistantDatabase database, string sql, Guid runId)
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

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HomeBusinessAssistant.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
