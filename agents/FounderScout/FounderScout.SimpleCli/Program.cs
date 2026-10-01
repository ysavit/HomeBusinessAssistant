using FounderScout.SimpleCli;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    return await SimpleScoutCommand.ExecuteAsync(
        args, Console.Out, Console.Error, TimeProvider.System, cancellation.Token).ConfigureAwait(false);
}
catch (OperationCanceledException)
{
    await Console.Error.WriteLineAsync("Scout cancelled. Profiles saved before cancellation remain in the local database.").ConfigureAwait(false);
    return 130;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
{
    await Console.Error.WriteLineAsync($"Scout could not continue ({exception.GetType().Name}). Check the local data directory and ensure another Scout run is not using the browser profile.").ConfigureAwait(false);
    return 1;
}
