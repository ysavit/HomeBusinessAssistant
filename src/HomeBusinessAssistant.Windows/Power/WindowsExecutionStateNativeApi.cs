using System.Runtime.InteropServices;

namespace HomeBusinessAssistant.Windows.Power;

/// <summary>Documented <c>SetThreadExecutionState</c> flags used by the platform.</summary>
[Flags]
public enum WindowsExecutionState : uint
{
    /// <summary>Persists the supplied requirements until the next continuous call.</summary>
    Continuous = 0x80000000,
    /// <summary>Prevents idle system sleep.</summary>
    SystemRequired = 0x00000001,
    /// <summary>Prevents idle display power-off.</summary>
    DisplayRequired = 0x00000002,
}

/// <summary>Mockable native boundary for Windows execution-state requests.</summary>
public interface IWindowsExecutionStateNativeApi
{
    /// <summary>Sets execution state for the calling thread and reports success.</summary>
    bool TrySet(WindowsExecutionState state);
}

/// <summary>Kernel32 execution-state adapter.</summary>
public sealed partial class WindowsExecutionStateNativeApi : IWindowsExecutionStateNativeApi
{
    /// <inheritdoc />
    public bool TrySet(WindowsExecutionState state) => SetThreadExecutionState(state) != 0;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint SetThreadExecutionState(WindowsExecutionState executionState);
}
