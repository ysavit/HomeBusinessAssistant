using System.Text.Json;
using HomeBusinessAssistant.Application.SystemIntegration;
using Moq;
using WakeRemote.Application;
using WakeRemote.Infrastructure;

namespace WakeRemote.Tests;

internal sealed class ReadinessProbeTests
{
    private static readonly string[] RdpServiceQuery = ["query", "TermService"];

    [Test]
    public async Task DiagnosticFakeProviderIsAlwaysExplicitlyLabeled()
    {
        var processes = new Mock<IProcessExecutor>(MockBehavior.Strict);
        var probe = new WindowsRemoteAccessProviderProbe(processes.Object, Path.Combine(Path.GetTempPath(), "sc.exe"));

        RemoteProviderProbeResult result = await probe.ProbeAsync(new(
            RemoteProviderKind.DiagnosticFake,
            ServiceNames: [],
            ProcessNames: [],
            CheckLocalListener: false,
            ListenerHost: "127.0.0.1",
            ListenerPort: 3389,
            DiagnosticReady: true));

        Assert.Multiple(() =>
        {
            Assert.That(result.Provider, Is.EqualTo(RemoteProviderKind.DiagnosticFake));
            Assert.That(result.Readiness, Is.EqualTo(RemoteProviderReadiness.Ready));
            Assert.That(result.ReasonCode, Is.EqualTo("remote.diagnostic-fake-ready"));
        });
        processes.VerifyNoOtherCalls();
    }

    [Test]
    public async Task RunningRdpServiceIsReadyWithoutChangingServiceOrFirewall()
    {
        var processes = new Mock<IProcessExecutor>(MockBehavior.Strict);
        processes
            .Setup(executor => executor.ExecuteAsync(
                It.Is<ProcessExecutionRequest>(request =>
                    request.Arguments.SequenceEqual(RdpServiceQuery)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult(
                ExitCode: 0,
                StandardOutput: "STATE              : 4  RUNNING",
                StandardError: string.Empty,
                TimedOut: false));
        var probe = new WindowsRemoteAccessProviderProbe(processes.Object, Path.Combine(Path.GetTempPath(), "sc.exe"));

        RemoteProviderProbeResult result = await probe.ProbeAsync(new(
            RemoteProviderKind.WindowsRdp,
            ServiceNames: ["TermService"],
            ProcessNames: [],
            CheckLocalListener: false,
            ListenerHost: "127.0.0.1",
            ListenerPort: 3389,
            DiagnosticReady: false));

        Assert.That(result.Readiness, Is.EqualTo(RemoteProviderReadiness.Ready));
        processes.VerifyAll();
    }

    [Test]
    public async Task StoppedChromeServiceReportsNotReadyWithoutLeakingConfiguredName()
    {
        var processes = new Mock<IProcessExecutor>(MockBehavior.Strict);
        processes
            .Setup(executor => executor.ExecuteAsync(
                It.IsAny<ProcessExecutionRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult(
                ExitCode: 0,
                StandardOutput: "STATE              : 1  STOPPED",
                StandardError: string.Empty,
                TimedOut: false));
        var probe = new WindowsRemoteAccessProviderProbe(processes.Object, Path.Combine(Path.GetTempPath(), "sc.exe"));

        RemoteProviderProbeResult result = await probe.ProbeAsync(new(
            RemoteProviderKind.ChromeRemoteDesktop,
            ServiceNames: ["PrivateChromeServiceName"],
            ProcessNames: [],
            CheckLocalListener: false,
            ListenerHost: "127.0.0.1",
            ListenerPort: 3389,
            DiagnosticReady: false));

        Assert.Multiple(() =>
        {
            Assert.That(result.Readiness, Is.EqualTo(RemoteProviderReadiness.NotReady));
            Assert.That(result.ReasonCode, Is.EqualTo("remote.provider-not-running"));
            Assert.That(JsonSerializer.Serialize(result), Does.Not.Contain("PrivateChromeServiceName"));
        });
    }
}
