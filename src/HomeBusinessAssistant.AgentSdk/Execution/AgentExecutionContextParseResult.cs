namespace HomeBusinessAssistant.AgentSdk.Execution;

/// <summary>The result of parsing an agent's command-line invocation.</summary>
public sealed record AgentExecutionContextParseResult(
    AgentExecutionContext? Context,
    IReadOnlyList<AgentCommandLineError> Errors)
{
    /// <summary>Gets whether parsing succeeded.</summary>
    public bool IsSuccess => Context is not null && Errors.Count == 0;

    /// <summary>Gets the process exit code appropriate for this result.</summary>
    public int ExitCode => IsSuccess ? AgentExitCode.Success : AgentExitCode.InvalidArguments;
}
