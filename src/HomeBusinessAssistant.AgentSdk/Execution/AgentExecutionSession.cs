using HomeBusinessAssistant.AgentSdk.Contracts;
using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.AgentSdk.Execution;

/// <summary>Provides a minimal started/summary/completed lifecycle around one agent operation.</summary>
public sealed class AgentExecutionSession
{
    private readonly IAgentEventWriter _writer;
    private int _executed;

    /// <summary>Creates a single-use execution session.</summary>
    public AgentExecutionSession(IAgentEventWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    /// <summary>Runs one operation and reliably emits its lifecycle records.</summary>
    public async ValueTask<int> ExecuteAsync(
        string command,
        Func<IAgentEventWriter, CancellationToken, ValueTask<AgentExecutionResult>> operation,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (Interlocked.Exchange(ref _executed, 1) != 0)
        {
            throw new AgentEventWriteException(
            [
                new ContractValidationError(
                    "session.alreadyExecuted",
                    "$",
                    "An execution session can run only one operation."),
            ]);
        }

        await _writer.WriteStartedAsync(new StartedPayload(command, metadata), CancellationToken.None).ConfigureAwait(false);

        AgentExecutionResult result;
        try
        {
            result = await operation(_writer, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = new AgentExecutionResult(
                AgentExitCode.Cancelled,
                AgentRunStatus.Cancelled,
                "The agent operation was cancelled.");
        }
        catch (Exception)
        {
            await _writer.WriteErrorAsync(
                new ErrorPayload(
                    "agent.unhandled-failure",
                    "The agent operation failed unexpectedly.",
                    Transient: false),
                CancellationToken.None).ConfigureAwait(false);
            result = new AgentExecutionResult(
                AgentExitCode.UnhandledFailure,
                AgentRunStatus.Failed,
                "The agent operation failed unexpectedly.");
        }

        await _writer.WriteSummaryAsync(
            new SummaryPayload(result.Status, result.Summary, result.Data),
            CancellationToken.None).ConfigureAwait(false);
        await _writer.WriteCompletedAsync(
            new CompletedPayload(result.ExitCode),
            CancellationToken.None).ConfigureAwait(false);
        return result.ExitCode;
    }
}
