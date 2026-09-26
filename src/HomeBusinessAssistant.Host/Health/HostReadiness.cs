using HomeBusinessAssistant.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HomeBusinessAssistant.Host.Health;

/// <summary>A bounded local readiness result suitable for health output.</summary>
public sealed record HostReadinessResult(bool IsReady, IReadOnlyList<string> ReasonCodes);

/// <summary>Evaluates current database, migration, directory, scheduler, and Runner readiness.</summary>
public interface IHostReadinessEvaluator
{
    /// <summary>Performs only local bounded checks.</summary>
    ValueTask<HostReadinessResult> EvaluateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Production local readiness evaluator.</summary>
public sealed class HostReadinessEvaluator(
    AssistantDatabase database,
    HostBootstrapSettings settings,
    HostHealthState healthState) : IHostReadinessEvaluator
{
    /// <inheritdoc />
    public async ValueTask<HostReadinessResult> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var reasons = new List<string>();
        if (!healthState.DatabaseInitialized)
        {
            reasons.Add("database.not-initialized");
        }

        try
        {
            AssistantDatabaseReadiness databaseReadiness = await new AssistantDatabaseReadinessProbe(database.ContextFactory)
                .CheckAsync(cancellationToken).ConfigureAwait(false);
            if (!databaseReadiness.CanConnect)
            {
                reasons.Add("database.unavailable");
            }
            else if (databaseReadiness.HasPendingMigrations)
            {
                reasons.Add("database.migrations-pending");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            reasons.Add("database.check-failed");
        }

        bool writable = ProbeDirectories(database.DataDirectory);
        if (!writable)
        {
            reasons.Add("data.not-writable");
        }

        if (!healthState.SchedulerInitialized)
        {
            reasons.Add("scheduler.not-initialized");
        }

        bool runnerPresent = File.Exists(settings.RunnerExecutablePath);
        healthState.RecordRunnerPresence(runnerPresent);
        if (!runnerPresent)
        {
            reasons.Add("runner.path-missing");
        }

        return new(reasons.Count == 0, reasons);
    }

    /// <summary>Creates required data subdirectories and verifies a transient local write in each.</summary>
    public static bool ProbeDirectories(string dataDirectory)
    {
        try
        {
            string[] paths =
            [
                Path.GetFullPath(dataDirectory),
                Path.Combine(dataDirectory, "logs"),
                Path.Combine(dataDirectory, "temp"),
                Path.Combine(dataDirectory, "artifacts"),
            ];
            foreach (string path in paths)
            {
                Directory.CreateDirectory(path);
                string probePath = Path.Combine(path, $".write-probe-{Guid.NewGuid():N}.tmp");
                using var probe = new FileStream(
                    probePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose);
                probe.WriteByte(0x48);
                probe.Flush(flushToDisk: true);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>Health-check adapter for the current Host readiness evaluator.</summary>
public sealed class HostReadinessHealthCheck(IHostReadinessEvaluator evaluator) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        HostReadinessResult result = await evaluator.EvaluateAsync(cancellationToken).ConfigureAwait(false);
        return result.IsReady
            ? HealthCheckResult.Healthy("The local control plane is ready.")
            : HealthCheckResult.Unhealthy(
                "The local control plane is not ready.",
                data: new Dictionary<string, object> { ["reasonCodes"] = result.ReasonCodes.ToArray() });
    }
}

/// <summary>Always-healthy process-liveness check.</summary>
public sealed class HostLivenessHealthCheck : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(HealthCheckResult.Healthy("The Host process is alive."));
}

internal sealed class StaticHostReadinessEvaluator(bool isReady) : IHostReadinessEvaluator
{
    public ValueTask<HostReadinessResult> EvaluateAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new HostReadinessResult(isReady, isReady ? [] : ["host.not-configured"]));
}
