using System.Diagnostics;

namespace HomeBusinessAssistant.Host;

/// <summary>Provides process-scoped Host runtime facts without confusing them with Windows uptime.</summary>
internal static class HostProcessInfo
{
    private static readonly DateTimeOffset StartedAtUtc =
        new DateTimeOffset(Process.GetCurrentProcess().StartTime).ToUniversalTime();

    /// <summary>Gets elapsed process lifetime from the supplied clock.</summary>
    public static TimeSpan GetUptime(TimeProvider? timeProvider = null)
    {
        TimeSpan elapsed = (timeProvider ?? TimeProvider.System).GetUtcNow() - StartedAtUtc;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }
}
