namespace HomeBusinessAssistant.Domain.Agents;

/// <summary>Stable lifecycle states for a durable agent run.</summary>
public enum AgentRunStatus
{
    /// <summary>The occurrence exists but has not been claimed.</summary>
    Pending = 0,
    /// <summary>A runner has atomically claimed the occurrence.</summary>
    Claimed = 1,
    /// <summary>The runner is preparing to launch the agent.</summary>
    Starting = 2,
    /// <summary>The agent process is actively running.</summary>
    Running = 3,
    /// <summary>The run finished successfully.</summary>
    Completed = 4,
    /// <summary>The run finished unsuccessfully.</summary>
    Failed = 5,
    /// <summary>The run exceeded its configured timeout.</summary>
    TimedOut = 6,
    /// <summary>The run was cancelled.</summary>
    Cancelled = 7,
    /// <summary>The run was left stale and recovered as abandoned.</summary>
    Abandoned = 8,
}
