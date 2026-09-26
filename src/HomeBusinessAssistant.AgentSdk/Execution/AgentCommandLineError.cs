namespace HomeBusinessAssistant.AgentSdk.Execution;

/// <summary>A safe command-line validation error.</summary>
public sealed record AgentCommandLineError(string Code, string Argument, string Message);
