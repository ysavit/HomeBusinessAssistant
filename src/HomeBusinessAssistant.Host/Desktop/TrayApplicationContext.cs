using System.Collections.Concurrent;
using HomeBusinessAssistant.Windows.Desktop;

namespace HomeBusinessAssistant.Host.Desktop;

/// <summary>Owns asynchronous Host startup, tray lifetime, Kestrel, IPC activation, and graceful shutdown.</summary>
public sealed class TrayApplicationContext : ApplicationContext, IAsyncDisposable
{
    private readonly string[] args;
    private readonly HostBootstrapSettings settings;
    private readonly Serilog.ILogger logger;
    private readonly WindowsSingleInstanceCoordinator singleInstance;
    private readonly ConcurrentQueue<HostInstanceCommand> pendingCommands = new();
    private readonly TaskCompletionSource shutdownCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SynchronizationContext? uiContext;
    private HostRuntime? runtime;
    private WebApplication? webApplication;
    private TrayShell? tray;
    private int shutdownStarted;

    /// <summary>Creates a primary-instance context and schedules non-blocking initialization after the message loop starts.</summary>
    public TrayApplicationContext(
        string[] args,
        HostBootstrapSettings settings,
        Serilog.ILogger logger,
        WindowsSingleInstanceCoordinator singleInstance)
    {
        this.args = args;
        this.settings = settings;
        this.logger = logger;
        this.singleInstance = singleInstance;
        singleInstance.StartListening(HandleInstanceCommandAsync);
        System.Windows.Forms.Application.Idle += InitializeOnIdle;
    }

    /// <summary>Gets the process exit code selected by startup/shutdown.</summary>
    public int ExitCode { get; private set; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
        Dispose();
    }

    /// <summary>Stops Kestrel/loops, releases power, and disposes the tray icon exactly once.</summary>
    public async ValueTask ShutdownAsync()
    {
        if (Interlocked.Exchange(ref shutdownStarted, 1) != 0)
        {
            await shutdownCompletion.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            tray?.Dispose();
            tray = null;
            if (webApplication is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try
                {
                    await webApplication.StopAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    logger.Warning("Host web shutdown exceeded the bounded timeout.");
                }

                await webApplication.DisposeAsync().ConfigureAwait(false);
                webApplication = null;
            }

            if (runtime is not null)
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
                runtime = null;
            }
            else if (logger is IDisposable disposableLogger)
            {
                disposableLogger.Dispose();
            }
        }
        finally
        {
            shutdownCompletion.TrySetResult();
        }
    }

    private async void InitializeOnIdle(object? sender, EventArgs eventArgs)
    {
        System.Windows.Forms.Application.Idle -= InitializeOnIdle;
        uiContext = SynchronizationContext.Current;
        try
        {
            runtime = await HostRuntime.CreateAsync(settings, logger).ConfigureAwait(true);
            await runtime.InitializeAsync().ConfigureAwait(true);
            string[] webArgs = [.. args, "--urls", settings.Url];
            webApplication = HostApplication.Build(webArgs, runtime.WebComposition);
            await webApplication.StartAsync().ConfigureAwait(true);
            tray = new TrayShell(runtime.TrayController, runtime.Notifications, ExitFromTrayAsync);
            logger.Information("Host started on {DashboardUrl} with tray and orchestration loops.", settings.Url);
            while (pendingCommands.TryDequeue(out HostInstanceCommand command))
            {
                await ExecuteInstanceCommandAsync(command).ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            ExitCode = 1;
            logger.Fatal(
                "Host startup failed with exception type {ExceptionType}. See configuration and local log guidance.",
                exception.GetType().Name);
            MessageBox.Show(
                "Home Business Assistant could not start. Check the local data/logs folder, loopback URL, Runner path, and manifest directory.",
                "Home Business Assistant startup failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            await ShutdownAsync().ConfigureAwait(true);
            ExitThread();
        }
    }

    private ValueTask HandleInstanceCommandAsync(HostInstanceCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SynchronizationContext? context = uiContext;
        if (context is null || runtime is null)
        {
            pendingCommands.Enqueue(command);
            return ValueTask.CompletedTask;
        }

        context.Post(async _ => await ExecuteInstanceCommandAsync(command).ConfigureAwait(true), null);
        return ValueTask.CompletedTask;
    }

    private async Task ExecuteInstanceCommandAsync(HostInstanceCommand command)
    {
        if (runtime is null)
        {
            pendingCommands.Enqueue(command);
            return;
        }

        switch (command)
        {
            case HostInstanceCommand.OpenDashboard:
                await runtime.TrayController.OpenDashboardAsync().ConfigureAwait(true);
                break;
            case HostInstanceCommand.ShowStatus:
                await runtime.TrayController.ShowStatusAsync().ConfigureAwait(true);
                break;
            case HostInstanceCommand.Shutdown:
                await ShutdownAsync().ConfigureAwait(true);
                ExitThread();
                break;
            default:
                throw new InvalidOperationException("The single-instance command is not supported.");
        }
    }

    private async ValueTask ExitFromTrayAsync()
    {
        await ShutdownAsync().ConfigureAwait(true);
        ExitThread();
    }
}
