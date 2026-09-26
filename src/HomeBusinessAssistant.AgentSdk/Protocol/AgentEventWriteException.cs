using HomeBusinessAssistant.AgentSdk.Contracts;

namespace HomeBusinessAssistant.AgentSdk.Protocol;

/// <summary>Reports structured validation or state errors from an event writer.</summary>
public sealed class AgentEventWriteException : InvalidOperationException
{
    /// <summary>Creates an exception containing one or more structured writer errors.</summary>
    public AgentEventWriteException(IReadOnlyList<ContractValidationError> errors)
        : base(errors.Count == 0 ? "The event could not be written." : errors[0].Message)
    {
        Errors = errors;
    }

    /// <summary>Gets the stable structured errors.</summary>
    public IReadOnlyList<ContractValidationError> Errors { get; }
}
