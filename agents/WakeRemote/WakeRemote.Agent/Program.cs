using WakeRemote.Agent;

return await WakeRemoteCommand.ExecuteAsync(
    args,
    Console.Out,
    Console.Error,
    TimeProvider.System).ConfigureAwait(false);
