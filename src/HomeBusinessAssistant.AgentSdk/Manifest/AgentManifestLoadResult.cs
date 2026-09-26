using HomeBusinessAssistant.AgentSdk.Contracts;

namespace HomeBusinessAssistant.AgentSdk.Manifest;

/// <summary>The result of loading and validating a manifest file.</summary>
public sealed record AgentManifestLoadResult(
    AgentManifest? Manifest,
    IReadOnlyList<ContractValidationError> Errors)
{
    /// <summary>Gets whether a valid manifest was loaded.</summary>
    public bool IsSuccess => Manifest is not null && Errors.Count == 0;
}
