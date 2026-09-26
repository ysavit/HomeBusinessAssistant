using System.ComponentModel;
using System.Diagnostics;
using HomeBusinessAssistant.Application.Desktop;

namespace HomeBusinessAssistant.Windows.Desktop;

/// <summary>Opens one prevalidated IPv4-loopback dashboard origin with the default Windows shell.</summary>
public sealed class WindowsLocalDashboardLauncher : ILocalDashboardLauncher
{
    private readonly Uri dashboardUri;
    private readonly Func<ProcessStartInfo, bool> start;

    /// <summary>Creates a launcher for one exact local dashboard URL.</summary>
    public WindowsLocalDashboardLauncher(
        string dashboardUrl,
        Func<ProcessStartInfo, bool>? start = null)
    {
        if (!Uri.TryCreate(dashboardUrl, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || uri.Host != "127.0.0.1"
            || !uri.IsDefaultPort && uri.Port is < 1 or > 65_535
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("The dashboard launcher requires one HTTP 127.0.0.1 origin.", nameof(dashboardUrl));
        }

        dashboardUri = uri;
        this.start = start ?? StartWithShell;
    }

    /// <inheritdoc />
    public ValueTask<bool> OpenAsync(CancellationToken cancellationToken = default)
        => OpenPathAsync("/", cancellationToken);

    /// <inheritdoc />
    public ValueTask<bool> OpenPathAsync(string localPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(localPath) || localPath.Length > 512 || localPath[0] != '/'
            || localPath.StartsWith("//", StringComparison.Ordinal) || localPath.Contains("..", StringComparison.Ordinal)
            || localPath.Contains('\\') || Uri.TryCreate(localPath, UriKind.Absolute, out _))
        {
            return ValueTask.FromResult(false);
        }

        Uri destination = new(dashboardUri, localPath);
        if (destination.Scheme != Uri.UriSchemeHttp || destination.Host != "127.0.0.1" || destination.Port != dashboardUri.Port)
        {
            return ValueTask.FromResult(false);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = destination.AbsoluteUri,
            UseShellExecute = true,
        };
        try
        {
            return ValueTask.FromResult(start(startInfo));
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return ValueTask.FromResult(false);
        }
    }

    private static bool StartWithShell(ProcessStartInfo startInfo)
    {
        using Process? process = Process.Start(startInfo);
        return process is not null;
    }
}
