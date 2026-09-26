using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Persistence;

/// <summary>Coordinates migrations and built-in bootstrap across concurrent Host and Runner processes.</summary>
public static class AssistantDatabaseInitializer
{
    private const string LockFileName = ".assistant-bootstrap.lock";

    /// <summary>Acquires the shared bootstrap file lock, then initializes with bounded transient retries.</summary>
    public static async ValueTask<AssistantDatabase> InitializeAsync(
        AssistantDatabaseSettings settings,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        await using FileStream bootstrapLock = await AcquireBootstrapLockAsync(
            settings.DataDirectory,
            timeProvider,
            cancellationToken).ConfigureAwait(false);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await AssistantDatabase.InitializeAsync(
                    settings,
                    timeProvider,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (attempt < 4 && IsTransientDatabaseRace(exception))
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(100 * (attempt + 1)),
                    timeProvider,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask<FileStream> AcquireBootstrapLockAsync(
        string dataDirectory,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(root);
        string lockPath = Path.Combine(root, LockFileName);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException) when (attempt < 99)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("The central database bootstrap lock could not be acquired.");
    }

    private static bool IsTransientDatabaseRace(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateException
                || current is SqliteException { SqliteErrorCode: 5 or 6 or 19 })
            {
                return true;
            }
        }

        return false;
    }
}
