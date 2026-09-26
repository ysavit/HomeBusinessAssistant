using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Manifest;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Infrastructure.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Runner;

internal static class SupervisionDemoCommand
{
    public static async Task<int> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (!TryReadOptions(arguments, out string? dataDirectory, out string? fixtureDirectory)
            || dataDirectory is null
            || fixtureDirectory is null)
        {
            await error.WriteLineAsync("Usage: supervision-demo --data-directory <empty-path> --fixture-directory <test-agent-output>").ConfigureAwait(false);
            return Infrastructure.Execution.RunnerExitCode.InvalidArguments;
        }

        string data = Path.GetFullPath(dataDirectory);
        string fixture = Path.GetFullPath(fixtureDirectory);
        string root = Path.GetDirectoryName(data)
            ?? throw new InvalidOperationException("The smoke data directory has no parent.");
        string agents = Path.Combine(root, "smoke-install", "agents");
        string manifests = Path.Combine(root, "smoke-install", "manifests");
        if (Directory.Exists(data) || Directory.Exists(agents) || Directory.Exists(manifests))
        {
            await error.WriteLineAsync("The smoke data and install paths must not already exist.").ConfigureAwait(false);
            return Infrastructure.Execution.RunnerExitCode.InvalidExecutionEnvironment;
        }

        Directory.CreateDirectory(Path.Combine(agents, "test-agent"));
        Directory.CreateDirectory(manifests);
        foreach (string path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "manifests"), "*.json"))
        {
            File.Copy(path, Path.Combine(manifests, Path.GetFileName(path)));
        }

        foreach (string path in Directory.GetFiles(fixture))
        {
            File.Copy(path, Path.Combine(agents, "test-agent", Path.GetFileName(path)));
        }

        string fixtureManifest = Path.Combine(fixture, "test-agent.agent-manifest.json");
        string manifestText = await File.ReadAllTextAsync(fixtureManifest, cancellationToken).ConfigureAwait(false);
        manifestText = manifestText.Replace("\"defaultTimeoutSeconds\": 5", "\"defaultTimeoutSeconds\": 1", StringComparison.Ordinal);
        string manifestPath = Path.Combine(manifests, "test-agent.agent-manifest.json");
        await File.WriteAllTextAsync(manifestPath, manifestText, cancellationToken).ConfigureAwait(false);

        AssistantDatabase database = await AssistantDatabase.InitializeAsync(
            new(data, ManifestDirectory: manifests), TimeProvider.System, cancellationToken).ConfigureAwait(false);
        AgentManifest manifest = (await AgentManifestLoader.LoadAsync(manifestPath, cancellationToken).ConfigureAwait(false)).Manifest
            ?? throw new InvalidOperationException("The smoke manifest is invalid.");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var definitions = new AgentDefinitionRepository(database.ContextFactory, TimeProvider.System);
        _ = await definitions.UpsertManifestAsync(new(
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
            now,
            now), cancellationToken).ConfigureAwait(false);
        var configurations = new AgentConfigurationService(
            database.ContextFactory,
            new BasicAgentConfigurationValidator(),
            TimeProvider.System);
        _ = await configurations.SaveAsync(new(
            manifest.Id,
            "1.0",
            JsonSerializer.SerializeToElement(new { schemaVersion = "1.0", marker = "cli-smoke" }),
            "supervision-demo",
            "Create the temporary smoke configuration.",
            Guid.NewGuid()), cancellationToken).ConfigureAwait(false);

        int success = await RunScenarioAsync("success", data, agents, manifests, output, error, cancellationToken).ConfigureAwait(false);
        int artifact = await RunScenarioAsync("artifact-success", data, agents, manifests, output, error, cancellationToken).ConfigureAwait(false);
        int timeout = await RunScenarioAsync("hang", data, agents, manifests, output, error, cancellationToken).ConfigureAwait(false);
        await using AssistantDbContext context = await database.ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        (long runs, long metrics, long artifactsCount, string summaries) = await QueryResultsAsync(
            context.Database.GetDbConnection(), cancellationToken).ConfigureAwait(false);
        bool childRemains = Process.GetProcessesByName("HomeBusinessAssistant.TestAgent").Any(process =>
        {
            process.Dispose();
            return true;
        });
        await output.WriteLineAsync(JsonSerializer.Serialize(new
        {
            smoke = "runner-process-supervision",
            runs,
            metrics,
            artifacts = artifactsCount,
            summaries,
            exitCodes = new { success, artifact, timeout },
            childProcessRemains = childRemains,
        })).ConfigureAwait(false);
        return success == 0 && artifact == 0 && timeout == 21 && runs == 3 && metrics >= 1 && artifactsCount == 1 && !childRemains
            ? Infrastructure.Execution.RunnerExitCode.Success
            : Infrastructure.Execution.RunnerExitCode.RunnerFailure;
    }

    private static async Task<int> RunScenarioAsync(
        string command,
        string data,
        string agents,
        string manifests,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken) => await RunnerCommand.ExecuteAsync(
        [
            "run-agent", "--agent-id", "test-agent", "--command", command,
            "--data-directory", data, "--agent-directory", agents, "--manifest-directory", manifests,
        ], output, error, cancellationToken).ConfigureAwait(false);

    private static bool TryReadOptions(
        IReadOnlyList<string> arguments,
        out string? dataDirectory,
        out string? fixtureDirectory)
    {
        dataDirectory = null;
        fixtureDirectory = null;
        if (arguments.Count != 4)
        {
            return false;
        }

        for (var index = 0; index < arguments.Count; index += 2)
        {
            if (arguments[index] == "--data-directory")
            {
                dataDirectory = arguments[index + 1];
            }
            else if (arguments[index] == "--fixture-directory")
            {
                fixtureDirectory = arguments[index + 1];
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<(long Runs, long Metrics, long Artifacts, string Summaries)> QueryResultsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM AgentRuns),
                (SELECT COUNT(*) FROM AgentRunMetrics),
                (SELECT COUNT(*) FROM RunArtifacts),
                COALESCE((SELECT GROUP_CONCAT(Status || ':' || SummaryText, ' | ') FROM AgentRuns), '')
            """;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        _ = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3));
    }
}
