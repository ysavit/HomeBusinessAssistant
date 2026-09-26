using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using HomeBusinessAssistant.Application.SystemIntegration;
using WakeRemote.Application;

namespace WakeRemote.Infrastructure;

/// <summary>Conservative local interface, DNS, and optional TCP readiness probe.</summary>
public sealed class LocalNetworkReadinessProbe : INetworkReadinessProbe
{
    /// <inheritdoc />
    public async ValueTask<NetworkReadinessAttempt> ProbeAsync(
        WakeRemoteConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return new(false, "network.unavailable");
            }

            bool suitable = NetworkInterface.GetAllNetworkInterfaces().Any(candidate =>
                candidate.OperationalStatus == OperationalStatus.Up
                && candidate.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                && candidate.GetIPProperties().UnicastAddresses.Any(address =>
                    !IPAddress.IsLoopback(address.Address)
                    && address.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6));
            if (!suitable)
            {
                return new(false, "network.no-suitable-interface");
            }
        }
        catch (NetworkInformationException)
        {
            return new(false, "network.interface-inspection-failed");
        }

        if (configuration.DnsProbe.Enabled)
        {
            try
            {
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(
                    configuration.DnsProbe.HostName!,
                    cancellationToken).ConfigureAwait(false);
                if (addresses.Length == 0)
                {
                    return new(false, "network.dns-no-address");
                }
            }
            catch (Exception exception) when (exception is SocketException or ArgumentException)
            {
                return new(false, "network.dns-failed");
            }
        }

        if (configuration.TcpProbe.Enabled)
        {
            try
            {
                using TcpClient client = new();
                await client.ConnectAsync(
                    configuration.TcpProbe.Host!,
                    configuration.TcpProbe.Port,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is SocketException or ArgumentException)
            {
                return new(false, "network.tcp-failed");
            }
        }

        return new(true, "network.ready");
    }
}

/// <summary>Read-only local process/service/listener checks for Chrome Remote Desktop and Windows RDP.</summary>
public sealed class WindowsRemoteAccessProviderProbe(
    IProcessExecutor processExecutor,
    string serviceControlExecutable) : IRemoteAccessProviderProbe
{
    /// <summary>Creates a probe using the Windows system service-control executable.</summary>
    public static WindowsRemoteAccessProviderProbe CreateDefault(IProcessExecutor processExecutor)
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return new(processExecutor, Path.Combine(windows, "System32", "sc.exe"));
    }

    /// <inheritdoc />
    public async ValueTask<RemoteProviderProbeResult> ProbeAsync(
        RemoteProviderSettings configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Kind == RemoteProviderKind.DiagnosticFake)
        {
            return new(
                configuration.Kind,
                configuration.DiagnosticReady ? RemoteProviderReadiness.Ready : RemoteProviderReadiness.NotReady,
                configuration.DiagnosticReady ? "remote.diagnostic-fake-ready" : "remote.diagnostic-fake-not-ready");
        }

        if (configuration.ServiceNames.Count + configuration.ProcessNames.Count == 0)
        {
            return new(configuration.Kind, RemoteProviderReadiness.NotConfigured, "remote.not-configured");
        }

        var inspected = false;
        var present = false;
        foreach (string processName in configuration.ProcessNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using Process? process = Process.GetProcessesByName(processName).FirstOrDefault();
                inspected = true;
                if (process is not null && !process.HasExited)
                {
                    present = true;
                    break;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // A configured service may still supply reliable evidence.
            }
        }

        foreach (string serviceName in configuration.ServiceNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessExecutionResult result;
            try
            {
                result = await processExecutor.ExecuteAsync(new(
                    Path.GetFullPath(serviceControlExecutable),
                    ["query", serviceName],
                    TimeSpan.FromSeconds(5),
                    MaximumOutputCharacters: 8_192), cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            inspected = true;
            if (!result.TimedOut
                && result.ExitCode == 0
                && result.StandardOutput.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
            {
                present = true;
                break;
            }
        }

        if (!inspected)
        {
            return new(configuration.Kind, RemoteProviderReadiness.Unknown, "remote.inspection-unavailable");
        }

        if (!present)
        {
            return new(configuration.Kind, RemoteProviderReadiness.NotReady, "remote.provider-not-running");
        }

        if (configuration.CheckLocalListener && !await IsListenerReadyAsync(
            configuration.ListenerHost,
            configuration.ListenerPort,
            cancellationToken).ConfigureAwait(false))
        {
            return new(configuration.Kind, RemoteProviderReadiness.NotReady, "remote.local-listener-not-ready");
        }

        return new(configuration.Kind, RemoteProviderReadiness.Ready, "remote.provider-ready");
    }

    private static async ValueTask<bool> IsListenerReadyAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        try
        {
            using TcpClient client = new();
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
