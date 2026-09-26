using FounderScout.Infrastructure.Persistence;

namespace FounderScout.Tests;

internal sealed class TemporaryFounderScoutDatabase : IAsyncDisposable
{
    private TemporaryFounderScoutDatabase(string root, FounderScoutDatabase database)
    {
        Root = root;
        Database = database;
    }

    public string Root { get; }
    public FounderScoutDatabase Database { get; }

    public static async ValueTask<TemporaryFounderScoutDatabase> CreateAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-founder-tests-{Guid.NewGuid():N}");
        FounderScoutDatabase database = await FounderScoutDatabaseInitializer.InitializeAsync(
            new FounderScoutDatabaseSettings(root),
            TimeProvider.System);
        return new(root, database);
    }

    public FounderScoutRepository CreateRepository(TimeProvider? timeProvider = null) =>
        new(Database.ContextFactory, timeProvider ?? TimeProvider.System);

    public ValueTask DisposeAsync()
    {
        string fullRoot = Path.GetFullPath(Root);
        if (!fullRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(fullRoot).StartsWith("hba-founder-tests-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to delete an unexpected Founder Scout test directory.");
        }

        if (Directory.Exists(fullRoot))
        {
            Directory.Delete(fullRoot, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
