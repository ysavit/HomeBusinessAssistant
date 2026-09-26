using SampleBusinessAgent.Agent;

return await SampleBusinessAgentCommand.ExecuteAsync(
    args,
    Console.Out,
    Console.Error,
    TimeProvider.System).ConfigureAwait(false);
