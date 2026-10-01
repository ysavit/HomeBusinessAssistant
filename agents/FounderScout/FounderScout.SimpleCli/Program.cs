using FounderScout.SimpleCli;
using Microsoft.Data.Sqlite;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    return await SimpleScoutCommand.ExecuteAsync(
        args, Console.Out, Console.Error, TimeProvider.System, cancellationToken: cancellation.Token).ConfigureAwait(false);
}
catch (OperationCanceledException)
{
    await Console.Error.WriteLineAsync("Scout cancelled. Profiles saved before cancellation remain in the local database.").ConfigureAwait(false);
    return 130;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or SqliteException)
{
    await Console.Error.WriteLineAsync($"Scout could not continue ({exception.GetType().Name}). Check the configured database path, its permissions, and whether another Scout run is using the browser profile.").ConfigureAwait(false);
    return 1;
}
