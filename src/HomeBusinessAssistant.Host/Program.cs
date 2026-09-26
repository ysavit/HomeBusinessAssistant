using HomeBusinessAssistant.Host.Desktop;
using HomeBusinessAssistant.Host.Logging;
using HomeBusinessAssistant.Windows.Desktop;

namespace HomeBusinessAssistant.Host;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        bool shutdownRequested = args.Contains("--shutdown", StringComparer.Ordinal);
        HostBootstrapSettings? settings = null;
        Serilog.ILogger? logger = null;
        try
        {
            settings = HostBootstrapSettings.Load(args);
            logger = HostLogFactory.Create(settings.DataDirectory);
        }
        catch (Exception exception)
        {
            string fallbackData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HomeBusinessAssistant",
                "data");
            logger ??= HostLogFactory.Create(fallbackData);
            logger.Fatal("Host bootstrap validation failed with exception type {ExceptionType}.", exception.GetType().Name);
            MessageBox.Show(
                "Home Business Assistant configuration is invalid. Verify the loopback URL, local paths, and manifest directory.",
                "Home Business Assistant configuration error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            (logger as IDisposable)?.Dispose();
            return 1;
        }

        using var unhandled = new UnhandledErrorCapture(logger);
        var singleInstance = new WindowsSingleInstanceCoordinator(capabilityDirectory: settings.DataDirectory);
        if (!singleInstance.TryAcquirePrimary())
        {
            bool signaled = singleInstance.SignalPrimaryAsync(
                    shutdownRequested ? HostInstanceCommand.Shutdown : HostInstanceCommand.OpenDashboard)
                .AsTask().GetAwaiter().GetResult();
            singleInstance.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (!signaled)
            {
                logger.Warning("The existing Host instance did not accept the bounded activation command.");
                MessageBox.Show(
                    "Home Business Assistant is already running but did not respond. Use its tray icon, or end the unhealthy process before trying again.",
                    "Home Business Assistant is already running",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                (logger as IDisposable)?.Dispose();
                return 2;
            }

            (logger as IDisposable)?.Dispose();
            return 0;
        }

        if (shutdownRequested)
        {
            singleInstance.DisposeAsync().AsTask().GetAwaiter().GetResult();
            (logger as IDisposable)?.Dispose();
            return 0;
        }

        var context = new TrayApplicationContext(args, settings, logger, singleInstance);
        try
        {
            System.Windows.Forms.Application.Run(context);
            context.ShutdownAsync().AsTask().GetAwaiter().GetResult();
            return context.ExitCode;
        }
        finally
        {
            context.Dispose();
            singleInstance.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}

internal sealed class UnhandledErrorCapture : IDisposable
{
    private readonly Serilog.ILogger logger;

    public UnhandledErrorCapture(Serilog.ILogger logger)
    {
        this.logger = logger;
        System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        System.Windows.Forms.Application.ThreadException += ThreadException;
        TaskScheduler.UnobservedTaskException += UnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += UnhandledException;
    }

    public void Dispose()
    {
        System.Windows.Forms.Application.ThreadException -= ThreadException;
        TaskScheduler.UnobservedTaskException -= UnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= UnhandledException;
    }

    private void ThreadException(object sender, ThreadExceptionEventArgs eventArgs) =>
        Record("winforms", eventArgs.Exception);

    private void UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        Record("task", eventArgs.Exception);
        eventArgs.SetObserved();
    }

    private void UnhandledException(object sender, UnhandledExceptionEventArgs eventArgs) =>
        Record("appdomain", eventArgs.ExceptionObject as Exception);

    private void Record(string source, Exception? exception) =>
        logger.Error(
            "Unhandled {Source} error captured. ExceptionType={ExceptionType}.",
            source,
            exception?.GetType().Name ?? "Unknown");
}
