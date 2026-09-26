namespace FounderScout.Infrastructure.Persistence;

/// <summary>Validated bootstrap settings for the separate Founder Scout database.</summary>
public sealed record FounderScoutDatabaseSettings(
    string DataDirectory,
    string DatabaseFileName = "founders.db",
    TimeSpan? BusyTimeout = null)
{
    /// <summary>Validates and resolves the settings without creating files.</summary>
    public FounderScoutDatabaseSettings Validate()
    {
        if (string.IsNullOrWhiteSpace(DataDirectory))
        {
            throw new ArgumentException("A Founder Scout data directory is required.", nameof(DataDirectory));
        }

        string root = Path.GetFullPath(DataDirectory);
        if (!Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException("The Founder Scout data directory must be absolute.", nameof(DataDirectory));
        }

        if (string.IsNullOrWhiteSpace(DatabaseFileName)
            || DatabaseFileName.Length > 128
            || Path.GetFileName(DatabaseFileName) != DatabaseFileName
            || !DatabaseFileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The Founder Scout database file name must be a bounded .db leaf name.", nameof(DatabaseFileName));
        }

        TimeSpan timeout = BusyTimeout ?? TimeSpan.FromSeconds(5);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(60))
        {
            throw new ArgumentOutOfRangeException(nameof(BusyTimeout), "The SQLite busy timeout must be between zero and sixty seconds.");
        }

        return this with { DataDirectory = root, BusyTimeout = timeout };
    }
}

/// <summary>The verified SQLite connection behavior for diagnostics.</summary>
public sealed record FounderScoutDatabasePragmas(
    string JournalMode,
    bool ForeignKeysEnabled,
    int BusyTimeoutMilliseconds,
    int SynchronousMode);

/// <summary>Initialized Founder Scout storage roots and context factory.</summary>
public sealed record FounderScoutDatabase(
    string DataDirectory,
    string DatabasePath,
    string SnapshotsDirectory,
    string ReportsDirectory,
    string ImportsDirectory,
    string ErrorsDirectory,
    FounderScoutDbContextFactory ContextFactory,
    FounderScoutDatabasePragmas Pragmas);
