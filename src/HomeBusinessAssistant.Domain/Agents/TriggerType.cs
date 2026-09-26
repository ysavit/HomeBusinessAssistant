namespace HomeBusinessAssistant.Domain.Agents;

/// <summary>Stable sources that can create an agent occurrence.</summary>
public enum TriggerType
{
    /// <summary>A user requested the run from the local web UI.</summary>
    ManualUi = 0,
    /// <summary>A user requested the run from the tray menu.</summary>
    TrayMenu = 1,
    /// <summary>A normal schedule created the occurrence.</summary>
    Schedule = 2,
    /// <summary>A wake-enabled schedule created the occurrence.</summary>
    WakeSchedule = 3,
    /// <summary>An explicit bounded retry created the occurrence.</summary>
    Retry = 4,
    /// <summary>A direct command-line request created the occurrence.</summary>
    CommandLine = 5,
    /// <summary>Recovery logic created or resumed the occurrence.</summary>
    Recovery = 6,
}
