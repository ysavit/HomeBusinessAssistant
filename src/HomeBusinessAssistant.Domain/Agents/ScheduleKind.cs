namespace HomeBusinessAssistant.Domain.Agents;

/// <summary>Stable schedule shapes supported by the local platform.</summary>
public enum ScheduleKind
{
    /// <summary>No recurring calculation; occurrences are created explicitly.</summary>
    Manual = 0,
    /// <summary>One occurrence at a configured instant.</summary>
    OneTime = 1,
    /// <summary>A local wall-clock time every day.</summary>
    Daily = 2,
    /// <summary>A local wall-clock time on selected weekdays.</summary>
    SelectedWeekdays = 3,
    /// <summary>A fixed interval between scheduled due times.</summary>
    FixedInterval = 4,
    /// <summary>A fixed delay after the preceding run completes.</summary>
    FixedDelay = 5,
}
