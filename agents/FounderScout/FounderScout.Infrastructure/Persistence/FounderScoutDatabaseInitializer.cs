using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FounderScout.Infrastructure.Persistence;

/// <summary>Coordinates first-use directories, migrations, WAL, and pragma verification.</summary>
public static class FounderScoutDatabaseInitializer
{
    private const string LockFileName = ".founder-scout-bootstrap.lock";

    /// <summary>Initializes the separate Founder Scout data root and migrated database.</summary>
    public static async ValueTask<FounderScoutDatabase> InitializeAsync(
        FounderScoutDatabaseSettings settings,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        FounderScoutDatabaseSettings validated = settings.Validate();
        string root = PrepareDirectory(validated.DataDirectory);
        await using FileStream bootstrapLock = await AcquireLockAsync(root, timeProvider, cancellationToken).ConfigureAwait(false);
        string snapshots = PrepareDirectory(FounderScoutPathPolicy.CombineContained(root, "snapshots"));
        string reports = PrepareDirectory(FounderScoutPathPolicy.CombineContained(root, "reports"));
        string imports = PrepareDirectory(FounderScoutPathPolicy.CombineContained(root, "imports"));
        string errors = PrepareDirectory(FounderScoutPathPolicy.CombineContained(root, "errors"));
        string databasePath = FounderScoutPathPolicy.CombineContained(root, validated.DatabaseFileName);
        var factory = FounderScoutDbContextFactory.Create(databasePath, validated.BusyTimeout!.Value);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using FounderScoutDbContext context = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (Exception exception) when (attempt < 4 && IsTransient(exception))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1)), timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        FounderScoutDatabasePragmas pragmas = await ConfigureAndReadPragmasAsync(
            databasePath,
            validated.BusyTimeout.Value,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(pragmas.JournalMode, "wal", StringComparison.OrdinalIgnoreCase)
            || !pragmas.ForeignKeysEnabled
            || pragmas.BusyTimeoutMilliseconds != (int)Math.Ceiling(validated.BusyTimeout.Value.TotalMilliseconds))
        {
            throw new InvalidOperationException("The Founder Scout SQLite safety pragmas could not be verified.");
        }

        return new(root, databasePath, snapshots, reports, imports, errors, factory, pragmas);
    }

    private static string PrepareDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(fullPath);
        FounderScoutPathPolicy.RejectReparsePoint(fullPath);
        return fullPath;
    }

    private static async ValueTask<FileStream> AcquireLockAsync(
        string root,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        string path = FounderScoutPathPolicy.CombineContained(root, LockFileName);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous);
            }
            catch (IOException) when (attempt < 99)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("The Founder Scout database bootstrap lock could not be acquired.");
    }

    private static async ValueTask<FounderScoutDatabasePragmas> ConfigureAndReadPragmasAsync(
        string databasePath,
        TimeSpan busyTimeout,
        CancellationToken cancellationToken)
    {
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = (int)Math.Ceiling(busyTimeout.TotalSeconds),
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        int timeoutMs = (int)Math.Ceiling(busyTimeout.TotalMilliseconds);
        await ExecuteNonQueryAsync(connection, $"PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout={timeoutMs}; PRAGMA synchronous=NORMAL;", cancellationToken).ConfigureAwait(false);
        string journal = Convert.ToString(await ExecuteScalarAsync(connection, "PRAGMA journal_mode;", cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        bool foreignKeys = Convert.ToInt32(await ExecuteScalarAsync(connection, "PRAGMA foreign_keys;", cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) == 1;
        int actualTimeout = Convert.ToInt32(await ExecuteScalarAsync(connection, "PRAGMA busy_timeout;", cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        int synchronous = Convert.ToInt32(await ExecuteScalarAsync(connection, "PRAGMA synchronous;", cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
        return new(journal, foreignKeys, actualTimeout, synchronous);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<object?> ExecuteScalarAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsTransient(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateException or SqliteException { SqliteErrorCode: 5 or 6 or 19 })
            {
                return true;
            }
        }

        return false;
    }
}
