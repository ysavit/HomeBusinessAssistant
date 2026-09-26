using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Host.Orchestration;

namespace HomeBusinessAssistant.Host.Desktop;

/// <summary>Windows Forms NotifyIcon/menu projection over the testable tray controller.</summary>
public sealed class TrayShell : IDisposable
{
    private readonly TrayController controller;
    private readonly HostNotificationHub notifications;
    private readonly Func<ValueTask> requestExit;
    private readonly SynchronizationContext uiContext;
    private readonly NotifyIcon notifyIcon;
    private readonly ContextMenuStrip menu;
    private readonly ToolStripMenuItem founderRun;
    private readonly ToolStripMenuItem wakeRun;
    private readonly ToolStripMenuItem releaseKeepAwake;
    private readonly ToolStripMenuItem nextRuns;
    private readonly ToolStripMenuItem pauseAll;
    private readonly ToolStripMenuItem resumeAll;
    private string? lastNotificationPath;
    private int disposed;

    /// <summary>Creates and displays the tray icon; all long commands remain asynchronous.</summary>
    public TrayShell(
        TrayController controller,
        HostNotificationHub notifications,
        Func<ValueTask> requestExit)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(requestExit);
        this.controller = controller;
        this.notifications = notifications;
        this.requestExit = requestExit;
        uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("The tray shell requires the Windows Forms synchronization context.");

        menu = new ContextMenuStrip();
        menu.Items.Add(CreateItem("Open Dashboard", async () => await controller.OpenDashboardAsync().ConfigureAwait(true)));
        menu.Items.Add(CreateItem("System Status", async () => await controller.ShowStatusAsync().ConfigureAwait(true)));
        var runAgent = new ToolStripMenuItem("Run Agent");
        founderRun = CreateItem("Founder Scout", async () => _ = await controller.RunFounderScoutAsync().ConfigureAwait(true));
        wakeRun = CreateItem("Wake & Remote diagnostic", async () => _ = await controller.RunWakeRemoteDiagnosticAsync().ConfigureAwait(true));
        runAgent.DropDownItems.Add(founderRun);
        runAgent.DropDownItems.Add(wakeRun);
        menu.Items.Add(runAgent);
        var keepAwake = new ToolStripMenuItem("Keep Awake");
        keepAwake.DropDownItems.Add(CreateItem("30 minutes", async () => _ = await controller.KeepAwakeForAsync(TimeSpan.FromMinutes(30)).ConfigureAwait(true)));
        keepAwake.DropDownItems.Add(CreateItem("1 hour", async () => _ = await controller.KeepAwakeForAsync(TimeSpan.FromHours(1)).ConfigureAwait(true)));
        keepAwake.DropDownItems.Add(CreateItem("Until…", KeepAwakeUntilAsync));
        menu.Items.Add(keepAwake);
        releaseKeepAwake = CreateItem("Release Keep-Awake", async () => await controller.ReleaseKeepAwakeAsync().ConfigureAwait(true));
        menu.Items.Add(releaseKeepAwake);
        nextRuns = new ToolStripMenuItem("Next Scheduled Runs");
        menu.Items.Add(nextRuns);
        menu.Items.Add(new ToolStripSeparator());
        pauseAll = CreateItem("Pause All Agents", async () => await controller.PauseAllAsync().ConfigureAwait(true));
        resumeAll = CreateItem("Resume All Agents", async () => await controller.ResumeAllAsync().ConfigureAwait(true));
        menu.Items.Add(pauseAll);
        menu.Items.Add(resumeAll);
        menu.Items.Add(CreateItem("View Latest Summary", async () => await controller.ViewLatestSummaryAsync().ConfigureAwait(true)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateItem("Exit", ConfirmAndExitAsync));
        menu.Opening += MenuOpening;

        notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = TrayIconFactory.Create(),
            Text = "Home Business Assistant",
            Visible = true,
        };
        notifyIcon.DoubleClick += OpenDashboard;
        notifyIcon.BalloonTipClicked += NotificationClicked;
        notifications.Published += NotificationPublished;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        notifications.Published -= NotificationPublished;
        menu.Opening -= MenuOpening;
        notifyIcon.DoubleClick -= OpenDashboard;
        notifyIcon.BalloonTipClicked -= NotificationClicked;
        notifyIcon.Visible = false;
        notifyIcon.Icon?.Dispose();
        notifyIcon.Dispose();
        menu.Dispose();
    }

    private async void MenuOpening(object? sender, System.ComponentModel.CancelEventArgs eventArgs)
    {
        try
        {
            TrayControllerState state = await controller.GetStateAsync().ConfigureAwait(true);
            HostAgentStatus? founder = state.Platform.Agents.SingleOrDefault(item => item.AgentId.Value == "founder-scout");
            HostAgentStatus? wake = state.Platform.Agents.SingleOrDefault(item => item.AgentId.Value == "wake-remote");
            founderRun.Enabled = founder is { Enabled: true, SupportsManualRun: true, HasConfiguration: true };
            wakeRun.Enabled = wake is { Enabled: true, SupportsManualRun: true, HasConfiguration: true };
            releaseKeepAwake.Enabled = state.KeepAwake.IsActive;
            pauseAll.Enabled = !state.GlobalPause.IsPaused;
            resumeAll.Enabled = state.GlobalPause.IsPaused;
            nextRuns.DropDownItems.Clear();
            foreach (HostOccurrenceStatus occurrence in state.Platform.NextOccurrences.Take(5))
            {
                nextRuns.DropDownItems.Add(new ToolStripMenuItem(
                    $"{occurrence.DueAtUtc.ToLocalTime():g} · {occurrence.AgentDisplayName}")
                {
                    Enabled = false,
                });
            }

            if (nextRuns.DropDownItems.Count == 0)
            {
                nextRuns.DropDownItems.Add(new ToolStripMenuItem("No pending occurrences") { Enabled = false });
            }
        }
        catch (Exception)
        {
            eventArgs.Cancel = false;
            ShowNotification(new("tray.state-unavailable", "Status unavailable", "The tray state could not be refreshed.", true));
        }
    }

    private async void OpenDashboard(object? sender, EventArgs eventArgs)
    {
        try
        {
            await controller.OpenDashboardAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            ShowNotification(new("dashboard.open-failed", "Dashboard could not be opened", "Use the local 127.0.0.1 URL.", true));
        }
    }

    private ToolStripMenuItem CreateItem(string text, Func<ValueTask> action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += async (_, _) =>
        {
            item.Enabled = false;
            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception)
            {
                ShowNotification(new("tray.command-failed", "Command failed", "The requested tray action did not complete.", true));
            }
            finally
            {
                item.Enabled = true;
            }
        };
        return item;
    }

    private async ValueTask KeepAwakeUntilAsync()
    {
        using var dialog = new KeepAwakeUntilDialog();
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        TimeSpan duration = dialog.SelectedLocalTime.ToUniversalTime() - DateTimeOffset.UtcNow;
        if (duration < TimeSpan.FromMinutes(1) || duration > TimeSpan.FromHours(24))
        {
            MessageBox.Show(
                "Choose a time between one minute and 24 hours from now.",
                "Keep Awake",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _ = await controller.KeepAwakeForAsync(duration).ConfigureAwait(true);
    }

    private async ValueTask ConfirmAndExitAsync()
    {
        TrayControllerState state = await controller.GetStateAsync().ConfigureAwait(true);
        if (state.RequiresExitConfirmation
            && MessageBox.Show(
                "An agent run or manual keep-awake session is active. Exit the Host and release its local resources? Running agent processes remain supervised by Runner.",
                "Exit Home Business Assistant",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        await requestExit().ConfigureAwait(true);
    }

    private void NotificationPublished(object? sender, HostNotification notification) =>
        uiContext.Post(_ =>
        {
            lastNotificationPath = notification.LocalPath;
            ShowNotification(notification);
        }, null);

    private async void NotificationClicked(object? sender, EventArgs eventArgs)
    {
        try
        {
            await controller.OpenLocalPathAsync(lastNotificationPath ?? "/").ConfigureAwait(true);
        }
        catch (Exception)
        {
            await controller.OpenDashboardAsync().ConfigureAwait(true);
        }
    }

    private void ShowNotification(HostNotification notification)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return;
        }

        notifyIcon.ShowBalloonTip(
            5_000,
            notification.Title,
            notification.Message,
            notification.IsHighPriority ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }
}

internal sealed class KeepAwakeUntilDialog : Form
{
    private readonly DateTimePicker picker;

    public KeepAwakeUntilDialog()
    {
        Text = "Keep Awake Until";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new(330, 120);
        picker = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "ddd, MMM d · h:mm tt",
            MinDate = DateTime.Now.AddMinutes(1),
            MaxDate = DateTime.Now.AddHours(24),
            Value = DateTime.Now.AddHours(2),
            Location = new(18, 18),
            Width = 294,
        };
        var confirm = new Button { Text = "Keep awake", DialogResult = DialogResult.OK, Location = new(126, 70), Width = 90 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new(222, 70), Width = 90 };
        Controls.Add(picker);
        Controls.Add(confirm);
        Controls.Add(cancel);
        AcceptButton = confirm;
        CancelButton = cancel;
    }

    public DateTimeOffset SelectedLocalTime => new(picker.Value);
}

internal static class TrayIconFactory
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var background = new SolidBrush(Color.FromArgb(15, 107, 85));
            graphics.FillRoundedRectangle(background, new Rectangle(1, 1, 30, 30), 8);
            using var stroke = new Pen(Color.White, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawLine(stroke, 10, 9, 10, 23);
            graphics.DrawLine(stroke, 22, 9, 22, 23);
            graphics.DrawLine(stroke, 10, 16, 22, 16);
        }

        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        byte[] image = png.ToArray();
        using var iconStream = new MemoryStream();
        using (var writer = new BinaryWriter(iconStream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write((byte)32);
            writer.Write((byte)32);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)image.Length);
            writer.Write((uint)22);
            writer.Write(image);
        }

        iconStream.Position = 0;
        using var icon = new Icon(iconStream);
        return (Icon)icon.Clone();
    }

    private static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle rectangle, int radius)
    {
        int diameter = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
