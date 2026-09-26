namespace HomeBusinessAssistant.AgentSdk.Contracts;

/// <summary>The result of validating a process-boundary contract.</summary>
public sealed record ContractValidationResult(IReadOnlyList<ContractValidationError> Errors)
{
    /// <summary>Gets a value indicating whether validation succeeded.</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>Creates a successful result.</summary>
    public static ContractValidationResult Success { get; } = new(Array.Empty<ContractValidationError>());
}
