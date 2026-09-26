namespace HomeBusinessAssistant.Domain.Agents;

/// <summary>Stable policy applied when an agent already has active work.</summary>
public enum ConcurrencyPolicy
{
    /// <summary>Reject an occurrence while another run is active.</summary>
    Forbid = 0,
    /// <summary>Retain at most one pending occurrence while another run is active.</summary>
    QueueOne = 1,
    /// <summary>Allow multiple runs of the agent at the same time.</summary>
    AllowParallel = 2,
}
