using HomeBusinessAssistant.AgentSdk.Execution;
using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.AgentSdk.Diagnostics;

/// <summary>Writes a short diagnostic protocol stream without performing agent business work.</summary>
public static class AgentProtocolDemo
{
    /// <summary>Runs the protocol diagnostic for one agent.</summary>
    public static async ValueTask<int> RunAsync(
        string agentId,
        TextWriter output,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(timeProvider);
        var parsedAgentId = AgentId.Parse(agentId);

        using var writer = new AgentEventWriter(output, timeProvider, AgentRunId.New());
        var session = new AgentExecutionSession(writer);
        return await session.ExecuteAsync(
            "protocol-demo",
            async (eventWriter, token) =>
            {
                await eventWriter.WriteProgressAsync(
                    new ProgressPayload(
                        Current: 1,
                        Total: 1,
                        Percentage: 100,
                        Phase: "diagnostic",
                        Message: "Protocol diagnostic event generated."),
                    token).ConfigureAwait(false);
                await eventWriter.WriteMetricAsync(
                    new MetricPayload(
                        "protocol.events-generated",
                        NumericValue: 2,
                        Unit: "events"),
                    token).ConfigureAwait(false);

                return new AgentExecutionResult(
                    AgentExitCode.Success,
                    AgentRunStatus.Completed,
                    $"{parsedAgentId.Value} protocol diagnostic completed.");
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["agent-id"] = parsedAgentId.Value,
                ["mode"] = "diagnostic",
            },
            cancellationToken).ConfigureAwait(false);
    }
}
