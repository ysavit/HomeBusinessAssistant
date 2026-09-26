using System.Diagnostics;
using System.Text;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Windows.Desktop;
using HomeBusinessAssistant.Windows.Processes;

namespace HomeBusinessAssistant.Windows.Tests;

internal sealed class DesktopHostAdapterTests
{
    private static readonly string[] ExpectedNotificationRoutes =
    [
        "http://127.0.0.1:5180/Attention",
        "http://127.0.0.1:5180/Runs/Details?id=00000000-0000-0000-0000-000000000001",
    ];

    [Test]
    public void InstanceCommandCodecAcceptsOnlyBoundedAllowListedCommands()
    {
        byte[] encoded = HostInstanceCommandCodec.Encode(HostInstanceCommand.OpenDashboard);

        Assert.Multiple(() =>
        {
            Assert.That(HostInstanceCommandCodec.TryDecode(encoded, out HostInstanceCommand command), Is.True);
            Assert.That(command, Is.EqualTo(HostInstanceCommand.OpenDashboard));
            Assert.That(HostInstanceCommandCodec.TryDecode(Encoding.UTF8.GetBytes("RunAnything\n"), out _), Is.False);
            Assert.That(HostInstanceCommandCodec.TryDecode(new byte[HostInstanceCommandCodec.MaximumMessageBytes + 1], out _), Is.False);
            Assert.That(HostInstanceCommandCodec.TryDecode([0xff, 0xfe], out _), Is.False);
        });
    }

    [Test]
    public void SecondaryCoordinatorSignalsTheCurrentUserPrimary()
    {
        string identity = $"test_{Guid.NewGuid():N}";
        var received = new TaskCompletionSource<HostInstanceCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
        var primary = new WindowsSingleInstanceCoordinator(identity, TimeSpan.FromSeconds(2));
        Assert.That(primary.TryAcquirePrimary(), Is.True);
        primary.StartListening((command, _) =>
        {
            received.TrySetResult(command);
            return ValueTask.CompletedTask;
        });

        Task<bool> secondaryTask = Task.Run(async () =>
        {
            await using var secondary = new WindowsSingleInstanceCoordinator(identity, TimeSpan.FromSeconds(2));
            Assert.That(secondary.TryAcquirePrimary(), Is.False);
            return await secondary.SignalPrimaryAsync(HostInstanceCommand.OpenDashboard);
        });

        Assert.Multiple(() =>
        {
            Assert.That(secondaryTask.GetAwaiter().GetResult(), Is.True);
            Assert.That(received.Task.Wait(TimeSpan.FromSeconds(3)), Is.True);
            Assert.That(received.Task.Result, Is.EqualTo(HostInstanceCommand.OpenDashboard));
        });
        primary.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Test]
    public async Task RunnerLauncherUsesArgumentListWithSpaceContainingPaths()
    {
        string root = Path.Combine(Path.GetTempPath(), $"HBA runner adapter {Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string runner = Path.Combine(root, "central runner.exe");
        string data = Path.Combine(root, "user data");
        string agents = Path.Combine(root, "agent files");
        string manifests = Path.Combine(root, "manifest files");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(agents);
        Directory.CreateDirectory(manifests);
        await File.WriteAllBytesAsync(runner, [0x4d, 0x5a]);
        ProcessStartInfo? captured = null;
        try
        {
            var launcher = new WindowsRunnerProcessLauncher(new(
                root,
                runner,
                root,
                data,
                agents,
                manifests,
                "assistant.db"), startInfo =>
                {
                    captured = startInfo;
                    return 4242;
                });
            OccurrenceId occurrenceId = OccurrenceId.New();

            var result = await launcher.LaunchAsync(occurrenceId);

            Assert.Multiple(() =>
            {
                Assert.That(result.Started, Is.True);
                Assert.That(result.ProcessId, Is.EqualTo(4242));
                Assert.That(captured, Is.Not.Null);
                Assert.That(captured!.UseShellExecute, Is.False);
                Assert.That(captured.CreateNoWindow, Is.True);
                Assert.That(captured.FileName, Is.EqualTo(runner));
                Assert.That(captured.ArgumentList, Does.Contain(data));
                Assert.That(captured.ArgumentList, Does.Contain(occurrenceId.ToString()));
                Assert.That(captured.Arguments, Is.Empty);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task DashboardLauncherAcceptsOnlyTheExactLoopbackOrigin()
    {
        ProcessStartInfo? captured = null;
        var launcher = new WindowsLocalDashboardLauncher(
            "http://127.0.0.1:5180/",
            startInfo =>
            {
                captured = startInfo;
                return true;
            });

        bool opened = await launcher.OpenAsync();

        Assert.Multiple(() =>
        {
            Assert.That(opened, Is.True);
            Assert.That(captured!.FileName, Is.EqualTo("http://127.0.0.1:5180/"));
            Assert.That(captured.UseShellExecute, Is.True);
            Assert.That(
                () => new WindowsLocalDashboardLauncher("https://example.com/"),
                Throws.ArgumentException);
        });
    }

    [Test]
    public async Task DashboardLauncherAllowsOnlyValidatedLocalNotificationRoutes()
    {
        var destinations = new List<string>();
        var launcher = new WindowsLocalDashboardLauncher(
            "http://127.0.0.1:5180/",
            startInfo =>
            {
                destinations.Add(startInfo.FileName);
                return true;
            });

        bool attention = await launcher.OpenPathAsync("/Attention");
        bool run = await launcher.OpenPathAsync("/Runs/Details?id=00000000-0000-0000-0000-000000000001");
        bool external = await launcher.OpenPathAsync("//example.com/");
        bool traversal = await launcher.OpenPathAsync("/Runs/../Settings");

        Assert.Multiple(() =>
        {
            Assert.That(attention, Is.True);
            Assert.That(run, Is.True);
            Assert.That(external, Is.False);
            Assert.That(traversal, Is.False);
            Assert.That(destinations, Is.EqualTo(ExpectedNotificationRoutes));
        });
    }
}
