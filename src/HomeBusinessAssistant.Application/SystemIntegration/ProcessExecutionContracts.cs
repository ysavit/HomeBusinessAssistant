namespace HomeBusinessAssistant.Application.SystemIntegration;

/// <summary>A shell-free, bounded local process request.</summary>
public sealed record ProcessExecutionRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    int MaximumOutputCharacters);

/// <summary>Bounded process completion data.</summary>
public sealed record ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut);

/// <summary>Executes a local process without a command shell.</summary>
public interface IProcessExecutor
{
    /// <summary>Executes one process using argument-list separation and bounded redirected output.</summary>
    Task<ProcessExecutionResult> ExecuteAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken = default);
}
