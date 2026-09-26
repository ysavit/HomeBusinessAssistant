using FounderScout.Agent;

return await FounderScoutCommand.ExecuteAsync(
    args,
    Console.Out,
    Console.Error,
    TimeProvider.System).ConfigureAwait(false);
