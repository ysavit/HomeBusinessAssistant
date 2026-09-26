namespace HomeBusinessAssistant.Domain.Agents;

/// <summary>Stable policy for waking a sleeping workstation.</summary>
public enum WakePolicy
{
    /// <summary>Never request an operating-system wake.</summary>
    Never = 0,
    /// <summary>Request a wake only when the machine would otherwise be sleeping.</summary>
    IfSleeping = 1,
    /// <summary>Require a wake-capable bridge for the schedule.</summary>
    Required = 2,
}
