using HomeBusinessAssistant.AgentSdk.Contracts;

namespace HomeBusinessAssistant.AgentSdk.Protocol;

/// <summary>Reports a structured failure while serializing an event.</summary>
public sealed class AgentProtocolSerializationException : InvalidOperationException
{
    /// <summary>Creates an exception for one structured serialization error.</summary>
    public AgentProtocolSerializationException(ContractValidationError error)
        : base(error.Message)
    {
        Error = error;
    }

    /// <summary>Gets the stable structured error.</summary>
    public ContractValidationError Error { get; }
}
