using System.Collections.Concurrent;

namespace HomeBusinessAssistant.Host.Health;

/// <summary>One loop's bounded, in-memory operational health.</summary>
public sealed record HostLoopHealth(
    string Name,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? LastSuccessAtUtc,
    string? LastErrorCode,
    bool IsRunning);

/// <summary>Thread-safe Host startup and orchestration health shared by tray, web, and health checks.</summary>
public sealed class HostHealthState(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, HostLoopHealth> loops = new(StringComparer.Ordinal);
    private int databaseInitialized;
    private int dataDirectoriesWritable;
    private int schedulerInitialized;
    private int runnerPresent;

    /// <summary>Gets whether migrations/bootstrap completed.</summary>
    public bool DatabaseInitialized => Volatile.Read(ref databaseInitialized) != 0;

    /// <summary>Gets whether required local data directories passed their write probe.</summary>
    public bool DataDirectoriesWritable => Volatile.Read(ref dataDirectoriesWritable) != 0;

    /// <summary>Gets whether schedule reconciliation completed at least one iteration.</summary>
    public bool SchedulerInitialized => Volatile.Read(ref schedulerInitialized) != 0;

    /// <summary>Gets whether the configured Runner executable currently exists.</summary>
    public bool RunnerPresent => Volatile.Read(ref runnerPresent) != 0;

    /// <summary>Gets immutable current loop snapshots in stable name order.</summary>
    public IReadOnlyList<HostLoopHealth> Loops => loops.Values.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();

    /// <summary>Records startup bootstrap state.</summary>
    public void RecordBootstrap(bool databaseReady, bool directoriesWritable, bool runnerAvailable)
    {
        Volatile.Write(ref databaseInitialized, databaseReady ? 1 : 0);
        Volatile.Write(ref dataDirectoriesWritable, directoriesWritable ? 1 : 0);
        Volatile.Write(ref runnerPresent, runnerAvailable ? 1 : 0);
    }

    /// <summary>Refreshes Runner presence for readiness and dashboard display.</summary>
    public void RecordRunnerPresence(bool present) => Volatile.Write(ref runnerPresent, present ? 1 : 0);

    /// <summary>Records the start of a sequential loop iteration.</summary>
    public void RecordLoopStarted(string name)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        loops.AddOrUpdate(
            name,
            _ => new(name, nowUtc, null, null, true),
            (_, prior) => prior with { LastAttemptAtUtc = nowUtc, LastErrorCode = null, IsRunning = true });
    }

    /// <summary>Records a successful loop iteration.</summary>
    public void RecordLoopSucceeded(string name)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        loops.AddOrUpdate(
            name,
            _ => new(name, nowUtc, nowUtc, null, false),
            (_, prior) => prior with { LastSuccessAtUtc = nowUtc, LastErrorCode = null, IsRunning = false });
        if (string.Equals(name, HostLoopNames.Schedule, StringComparison.Ordinal))
        {
            Volatile.Write(ref schedulerInitialized, 1);
        }
    }

    /// <summary>Records a safe loop error code without exception details.</summary>
    public void RecordLoopFailed(string name, string code)
    {
        loops.AddOrUpdate(
            name,
            _ => new(name, timeProvider.GetUtcNow().ToUniversalTime(), null, code, false),
            (_, prior) => prior with { LastErrorCode = code, IsRunning = false });
    }
}

/// <summary>Stable names for the four Stage 07 background loops.</summary>
public static class HostLoopNames
{
    /// <summary>Schedule planning and due dispatch.</summary>
    public const string Schedule = "schedule";
    /// <summary>Windows wake-task reconciliation.</summary>
    public const string Wake = "wake";
    /// <summary>Stale run and temporary-input recovery.</summary>
    public const string Recovery = "recovery";
    /// <summary>Local attention notification polling.</summary>
    public const string Notifications = "notifications";
}
