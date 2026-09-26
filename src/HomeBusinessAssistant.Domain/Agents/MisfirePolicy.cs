namespace HomeBusinessAssistant.Domain.Agents;

/// <summary>Stable handling for a schedule occurrence missed while the platform was unavailable.</summary>
public enum MisfirePolicy
{
    /// <summary>Do not run a missed occurrence.</summary>
    Skip = 0,
    /// <summary>Run a missed occurrence as soon as it is observed.</summary>
    RunImmediately = 1,
    /// <summary>Wait for the next regularly scheduled occurrence.</summary>
    RunNextScheduled = 2,
}
