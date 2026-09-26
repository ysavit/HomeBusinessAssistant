using HomeBusinessAssistant.Application.Audit;
using HomeBusinessAssistant.Application.Power;
using HomeBusinessAssistant.Application.SystemIntegration;

namespace HomeBusinessAssistant.Windows.Power;

/// <summary>Runs only the four supported read-only <c>powercfg</c> diagnostics.</summary>
public sealed class PowercfgDiagnosticsService(
    IProcessExecutor processes,
    TimeProvider timeProvider,
    string powercfgExecutable,
    TimeSpan commandTimeout,
    int maximumOutputCharacters) : IPowerDiagnosticsService
{
    private static readonly (PowerDiagnosticKind Kind, string[] Arguments)[] Commands =
    [
        (PowerDiagnosticKind.AvailableSleepStates, ["/a"]),
        (PowerDiagnosticKind.WakeTimers, ["/waketimers"]),
        (PowerDiagnosticKind.LastWake, ["/lastwake"]),
        (PowerDiagnosticKind.WakeArmedDevices, ["/devicequery", "wake_armed"]),
    ];

    /// <summary>Creates the service using the Windows system <c>powercfg.exe</c>.</summary>
    public static PowercfgDiagnosticsService CreateDefault(
        IProcessExecutor processes,
        TimeProvider timeProvider)
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return new(
            processes,
            timeProvider,
            Path.Combine(windows, "System32", "powercfg.exe"),
            TimeSpan.FromSeconds(15),
            65_536);
    }

    /// <inheritdoc />
    public async Task<PowerDiagnosticsSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        Validate();
        var results = new List<PowerDiagnosticResult>(Commands.Length);
        foreach ((PowerDiagnosticKind kind, string[] arguments) in Commands)
        {
            ProcessExecutionResult result = await processes.ExecuteAsync(new(
                Path.GetFullPath(powercfgExecutable),
                arguments,
                commandTimeout,
                maximumOutputCharacters), cancellationToken).ConfigureAwait(false);
            string output = AuditRedactor.RedactText(result.StandardOutput, maximumOutputCharacters);
            string error = AuditRedactor.RedactText(result.StandardError, maximumOutputCharacters);
            results.Add(new(
                kind,
                result.ExitCode,
                result.TimedOut,
                output,
                error,
                IndicatesNoActiveItems(kind, output)));
        }

        PowerDiagnosticResult sleep = results.Single(item => item.Kind == PowerDiagnosticKind.AvailableSleepStates);
        PowerDiagnosticResult timers = results.Single(item => item.Kind == PowerDiagnosticKind.WakeTimers);
        PowerDiagnosticResult devices = results.Single(item => item.Kind == PowerDiagnosticKind.WakeArmedDevices);
        return new(
            timeProvider.GetUtcNow().ToUniversalTime(),
            results,
            sleep.ExitCode == 0 && !sleep.TimedOut,
            timers.ExitCode == 0 && !timers.TimedOut,
            timers.ExitCode == 0 && !timers.TimedOut && !timers.IndicatesNoActiveItems && !string.IsNullOrWhiteSpace(timers.Output),
            devices.ExitCode == 0 && !devices.TimedOut && !devices.IndicatesNoActiveItems && !string.IsNullOrWhiteSpace(devices.Output));
    }

    private static bool IndicatesNoActiveItems(PowerDiagnosticKind kind, string output) => kind switch
    {
        PowerDiagnosticKind.WakeTimers => output.Contains("no active wake timers", StringComparison.OrdinalIgnoreCase),
        PowerDiagnosticKind.WakeArmedDevices => string.IsNullOrWhiteSpace(output)
            || string.Equals(output.Trim(), "NONE", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(powercfgExecutable)
            || commandTimeout < TimeSpan.FromSeconds(1)
            || commandTimeout > TimeSpan.FromMinutes(5)
            || maximumOutputCharacters is < 1 or > 1_048_576)
        {
            throw new ArgumentException("The power diagnostics options are invalid.");
        }
    }
}
