namespace HomeBusinessAssistant.AgentSdk.Contracts;

/// <summary>A stable, safe validation error that does not contain source content.</summary>
public sealed record ContractValidationError(string Code, string Path, string Message);
