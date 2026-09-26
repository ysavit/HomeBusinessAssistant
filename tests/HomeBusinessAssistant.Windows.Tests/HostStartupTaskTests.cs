using System.Xml.Linq;
using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.SystemIntegration;
using HomeBusinessAssistant.Windows.Desktop;

namespace HomeBusinessAssistant.Windows.Tests;

/// <summary>Verifies the managed Host-at-logon XML contract without registering a real task.</summary>
[TestFixture]
public sealed class HostStartupTaskTests
{
    private static readonly string[] ExpectedOwnershipConflictOperations = ["/Query", "/Query"];

    /// <summary>Ensures paths with spaces, current-user logon, mutex policy, and non-waking behavior survive XML generation.</summary>
    [Test]
    public void XmlUsesInteractiveLogonAndNeverWakeToRun()
    {
        var request = new HostStartupTaskRequest(
            @"C:\Local Apps\Home Business Assistant\app\HomeBusinessAssistant.Host.exe",
            @"C:\Local Apps\Home Business Assistant\app",
            @"C:\Local Apps\Home Business Assistant\config\appsettings.json",
            @"EXAMPLE\User Name",
            TimeSpan.FromSeconds(15));

        XDocument document = HostStartupTaskXmlGenerator.Generate(request);
        XNamespace ns = HostStartupTaskXmlGenerator.TaskNamespace;
        XElement root = document.Root!;
        XElement action = root.Element(ns + "Actions")!.Element(ns + "Exec")!;

        Assert.Multiple(() =>
        {
            Assert.That(root.Element(ns + "RegistrationInfo")!.Element(ns + "URI")!.Value, Is.EqualTo(ManagedHostStartupTask.Name));
            Assert.That(root.Element(ns + "Triggers")!.Elements(ns + "LogonTrigger").Count(), Is.EqualTo(1));
            Assert.That(root.Element(ns + "Triggers")!.Element(ns + "LogonTrigger")!.Element(ns + "Delay")!.Value, Is.EqualTo("PT15S"));
            Assert.That(root.Element(ns + "Settings")!.Element(ns + "WakeToRun")!.Value, Is.EqualTo("false"));
            Assert.That(root.Element(ns + "Settings")!.Element(ns + "MultipleInstancesPolicy")!.Value, Is.EqualTo("IgnoreNew"));
            Assert.That(root.Element(ns + "Principals")!.Element(ns + "Principal")!.Element(ns + "LogonType")!.Value, Is.EqualTo("InteractiveToken"));
            Assert.That(action.Element(ns + "Command")!.Value, Is.EqualTo(request.HostExecutablePath));
            Assert.That(action.Element(ns + "Arguments")!.Value, Does.Contain('"' + request.BootstrapConfigurationPath + '"'));
            Assert.That(document.ToString(), Does.Not.Contain("Password"));
        });
    }

    /// <summary>Ensures behavior changes alter the stable task fingerprint.</summary>
    [Test]
    public void FingerprintIncludesDelayAndBootstrapPath()
    {
        var first = new HostStartupTaskRequest(@"C:\App\HomeBusinessAssistant.Host.exe", @"C:\App", @"C:\Config\appsettings.json", @"EXAMPLE\User", TimeSpan.Zero);
        HostStartupTaskRequest second = first with { Delay = TimeSpan.FromSeconds(30) };
        HostStartupTaskRequest third = first with { BootstrapConfigurationPath = @"C:\Config Two\appsettings.json" };

        Assert.Multiple(() =>
        {
            Assert.That(second.GetFingerprint(), Is.Not.EqualTo(first.GetFingerprint()));
            Assert.That(third.GetFingerprint(), Is.Not.EqualTo(first.GetFingerprint()));
        });
    }

    /// <summary>Ensures reconcile and uninstall never replace or delete a reserved-name task with no ownership marker.</summary>
    [Test]
    public void ReconcileAndRemoveRefuseAnUnmanagedReservedTask()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-host-task-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var processes = new UnmanagedTaskExecutor();
            var scheduler = new SchtasksHostStartupTaskScheduler(processes, root, @"C:\Windows\System32\schtasks.exe");
            var request = new HostStartupTaskRequest(@"C:\App\HomeBusinessAssistant.Host.exe", @"C:\App", @"C:\Config\appsettings.json", @"EXAMPLE\User", TimeSpan.Zero);

            InvalidOperationException reconcile = Assert.ThrowsAsync<InvalidOperationException>(async () => await scheduler.ReconcileAsync(request))!;
            InvalidOperationException remove = Assert.ThrowsAsync<InvalidOperationException>(async () => await scheduler.RemoveAsync())!;

            Assert.Multiple(() =>
            {
                Assert.That(reconcile.Message, Does.StartWith("startup.task.ownership-conflict"));
                Assert.That(remove.Message, Does.StartWith("startup.task.ownership-conflict"));
                Assert.That(processes.Operations, Is.EqualTo(ExpectedOwnershipConflictOperations));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class UnmanagedTaskExecutor : IProcessExecutor
    {
        public List<string> Operations { get; } = [];

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken = default)
        {
            Operations.Add(request.Arguments[0]);
            const string xml = "<Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><RegistrationInfo><URI>\\HomeBusinessAssistant\\HostAtLogon</URI><Description>Third-party task</Description></RegistrationInfo><Triggers/><Settings/><Actions/></Task>";
            return Task.FromResult(new ProcessExecutionResult(0, xml, string.Empty, false));
        }
    }
}
