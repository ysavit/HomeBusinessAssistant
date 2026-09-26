namespace HomeBusinessAssistant.Domain.Agents;

/// <summary>Stable scheduler-owned lifecycle states for a durable occurrence.</summary>
public enum OccurrenceStatus
{
    /// <summary>The occurrence is durable but is not yet runnable.</summary>
    Planned = 0,
    /// <summary>The occurrence may be claimed when it is due.</summary>
    Ready = 1,
    /// <summary>A runner has atomically claimed the occurrence.</summary>
    Claimed = 2,
    /// <summary>The runner is preparing the agent process.</summary>
    Starting = 3,
    /// <summary>The agent process is running.</summary>
    Running = 4,
    /// <summary>The scheduler deliberately chose not to execute the occurrence.</summary>
    Skipped = 5,
    /// <summary>Cancellation has been requested and awaits authoritative completion.</summary>
    CancellationRequested = 6,
    /// <summary>The associated run completed successfully.</summary>
    Completed = 7,
    /// <summary>The associated run failed.</summary>
    Failed = 8,
    /// <summary>The associated run exceeded its timeout.</summary>
    TimedOut = 9,
    /// <summary>The associated run was cancelled.</summary>
    Cancelled = 10,
    /// <summary>The associated run was recovered as abandoned.</summary>
    Abandoned = 11,
}
