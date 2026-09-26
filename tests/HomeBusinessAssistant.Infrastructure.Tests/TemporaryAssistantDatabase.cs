using System.Text.Json;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace HomeBusinessAssistant.Infrastructure.Tests;

internal sealed class TemporaryAssistantDatabase : IAsyncDisposable
{
    private TemporaryAssistantDatabase(string root, AssistantDatabase database, TestTimeProvider timeProvider)
    {
        Root = root;
        Database = database;
        TimeProvider = timeProvider;
    }

    public string Root { get; }

    public AssistantDatabase Database { get; }

    public TestTimeProvider TimeProvider { get; }

    public static async ValueTask<TemporaryAssistantDatabase> CreateAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-tests-{Guid.NewGuid():N}");
        string manifestDirectory = Path.Combine(FindRepositoryRoot(), "manifests");
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 8, 29, 18, 0, 0, TimeSpan.Zero));
        AssistantDatabase database = await AssistantDatabase.InitializeAsync(
            new AssistantDatabaseSettings(root, ManifestDirectory: manifestDirectory),
            timeProvider);
        return new TemporaryAssistantDatabase(root, database, timeProvider);
    }

    public static async ValueTask<TemporaryAssistantDatabase> CreatePopulatedStage17Async()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string repositoryRoot = FindRepositoryRoot();
        string manifestDirectory = Path.Combine(repositoryRoot, "manifests");
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 8, 31, 23, 0, 0, TimeSpan.Zero));
        string databasePath = Path.Combine(root, "assistant.db");
        AssistantDbContextFactory factory = AssistantDbContextFactory.Create(databasePath, TimeSpan.FromSeconds(5));
        await using (AssistantDbContext context = await factory.CreateDbContextAsync())
        {
            IMigrator migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20260831181441_AddOperationalAttentionAndSummaries");
            string agentId = "founder-scout";
            string displayName = "Founder Scout";
            string emptyJson = "[]";
            string concurrencyPolicy = "Forbid";
            DateTimeOffset nowUtc = timeProvider.GetUtcNow();
            _ = await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO AgentDefinitions
                    (Id, DisplayName, Description, ManifestVersion, InstalledVersion,
                     ExecutableRelativePath, WorkingDirectoryRelativePath, CapabilitiesJson,
                     Enabled, CreatedAtUtc, UpdatedAtUtc, DefaultConcurrencyPolicy,
                     RequiresInteractiveUserSession, SupportedCommandsJson, SupportsManualRun, SupportsScheduling)
                VALUES
                    ({agentId}, {displayName}, {"Existing Stage 17 agent"}, {"1.0"}, {"1.0.0"},
                     {"FounderScout.exe"}, {agentId}, {emptyJson},
                     {true}, {nowUtc}, {nowUtc}, {concurrencyPolicy},
                     {true}, {emptyJson}, {true}, {true});
                """);
        }

        var configurations = new AgentConfigurationService(
            factory,
            new BasicAgentConfigurationValidator(),
            timeProvider);
        _ = await configurations.SaveAsync(new SaveConfigurationRequest(
            AgentId.Parse("founder-scout"),
            "1.0",
            JsonSerializer.Deserialize<JsonElement>("{\"schemaVersion\":\"1.0\"}"),
            "local-web",
            "Existing owner configuration",
            Guid.NewGuid()));

        AssistantDatabase database = await AssistantDatabase.InitializeAsync(
            new AssistantDatabaseSettings(root, ManifestDirectory: manifestDirectory),
            timeProvider);
        return new TemporaryAssistantDatabase(root, database, timeProvider);
    }

    public async ValueTask DisposeAsync()
    {
        string fullRoot = Path.GetFullPath(Root);
        string tempRoot = Path.GetFullPath(Path.GetTempPath());
        if (!fullRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(fullRoot).StartsWith("hba-tests-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to delete an unexpected test directory.");
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        for (var attempt = 0; Directory.Exists(fullRoot); attempt++)
        {
            try
            {
                Directory.Delete(fullRoot, recursive: true);
            }
            catch (IOException) when (attempt < 9)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            }
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

internal sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => utcNow;

    public void Advance(TimeSpan duration) => utcNow = utcNow.Add(duration);
}
