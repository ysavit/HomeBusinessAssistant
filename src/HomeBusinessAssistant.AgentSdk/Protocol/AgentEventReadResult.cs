using HomeBusinessAssistant.AgentSdk.Contracts;

namespace HomeBusinessAssistant.AgentSdk.Protocol;

/// <summary>The safe result of deserializing one protocol line.</summary>
public sealed record AgentEventReadResult(
    AgentEvent? Event,
    IReadOnlyList<ContractValidationError> Errors)
{
    /// <summary>Gets whether one event was deserialized.</summary>
    public bool IsSuccess => Event is not null && Errors.Count == 0;
}
