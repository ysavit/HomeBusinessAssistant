using System.Globalization;
using HomeBusinessAssistant.Application.Audit;
using Serilog;
using Serilog.Events;

namespace HomeBusinessAssistant.Host.Logging;

/// <summary>Creates the single rolling-file Serilog pipeline used by the interactive Host.</summary>
public static class HostLogFactory
{
    /// <summary>Creates a 30-file daily rolling logger under the validated data directory.</summary>
    public static Serilog.ILogger Create(string dataDirectory)
    {
        string logDirectory = Path.Combine(Path.GetFullPath(dataDirectory), "logs");
        Directory.CreateDirectory(logDirectory);
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .Enrich.WithProperty("Application", "HomeBusinessAssistant.Host")
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            .WriteTo.File(
                Path.Combine(logDirectory, "host-.log"),
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                shared: true,
                flushToDiskInterval: TimeSpan.FromSeconds(2))
            .CreateLogger();
    }
}

/// <summary>Routes Microsoft logging through the one Host Serilog pipeline.</summary>
public sealed class HostSerilogLoggerProvider(Serilog.ILogger logger) : ILoggerProvider
{
    /// <inheritdoc />
    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
        new Adapter(logger.ForContext("Category", categoryName));

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class Adapter(Serilog.ILogger logger) : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logger.IsEnabled(Map(logLevel));

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = AuditRedactor.RedactText(formatter(state, exception), 8_192);
            logger.Write(
                Map(logLevel),
                "{Message} EventId={EventId} ExceptionType={ExceptionType}",
                message,
                eventId.Id,
                exception?.GetType().Name);
        }

        private static LogEventLevel Map(LogLevel level) => level switch
        {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            LogLevel.Critical => LogEventLevel.Fatal,
            _ => LogEventLevel.Verbose,
        };
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
