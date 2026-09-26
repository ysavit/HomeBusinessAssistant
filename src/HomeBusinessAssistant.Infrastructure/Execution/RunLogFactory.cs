using System.Globalization;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Persistence;
using Serilog;
using Serilog.Core;

namespace HomeBusinessAssistant.Infrastructure.Execution;

/// <summary>Creates bounded structured rolling-file loggers for Runner supervision.</summary>
public sealed class RunLogFactory
{
    private readonly string logPath;

    /// <summary>Creates a logging factory beneath the platform data root.</summary>
    public RunLogFactory(string dataDirectory)
    {
        string logDirectory = StoragePathPolicy.CombineContained(Path.GetFullPath(dataDirectory), "logs");
        Directory.CreateDirectory(logDirectory);
        StoragePathPolicy.RejectExistingReparsePoints(dataDirectory, logDirectory);
        logPath = StoragePathPolicy.CombineContained(logDirectory, "runner-.log");
    }

    /// <summary>Creates one correlated logger and disposable flush boundary.</summary>
    public RunLog Create(AgentRunId runId, OccurrenceId occurrenceId, AgentId agentId)
    {
        Logger logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("RunId", runId.ToString())
            .Enrich.WithProperty("OccurrenceId", occurrenceId.ToString())
            .Enrich.WithProperty("AgentId", agentId.Value)
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 10L * 1024 * 1024,
                formatProvider: CultureInfo.InvariantCulture,
                rollOnFileSizeLimit: true,
                shared: true,
                flushToDiskInterval: TimeSpan.FromSeconds(1),
                outputTemplate: "{Timestamp:O} [{Level:u3}] run={RunId} occurrence={OccurrenceId} agent={AgentId} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        return new RunLog(logger);
    }
}

/// <summary>Owns a per-run correlated Serilog pipeline.</summary>
public sealed class RunLog(Logger logger) : IDisposable
{
    /// <summary>Gets the structured logger.</summary>
    public ILogger Logger { get; } = logger;

    /// <inheritdoc />
    public void Dispose() => logger.Dispose();
}
