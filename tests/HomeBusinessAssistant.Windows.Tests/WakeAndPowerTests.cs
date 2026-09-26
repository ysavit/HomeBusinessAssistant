using System.Collections.Concurrent;
using System.Xml.Linq;
using HomeBusinessAssistant.Application.Power;
using HomeBusinessAssistant.Application.SystemIntegration;
using HomeBusinessAssistant.Application.Wake;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Windows.Power;
using HomeBusinessAssistant.Windows.Wake;

namespace HomeBusinessAssistant.Windows.Tests;

internal sealed class WakeAndPowerTests
{
    [Test]
    public void XmlContainsExactUtcWakeRunnerAndEscapedUnicodePolicy()
    {
        WakeTaskRequest request = CreateRequest(
            dueUtc: new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero),
            dueLocal: new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(-5)),
            runnerPath: @"C:\Apps & Tools\助手\HomeBusinessAssistant.Runner.exe");

        XDocument document = WakeTaskXmlGenerator.Generate(request);
        XNamespace ns = WakeTaskXmlGenerator.TaskNamespace;
        XElement root = document.Root!;
        string description = root.Element(ns + "RegistrationInfo")!.Element(ns + "Description")!.Value;
        XElement principal = root.Element(ns + "Principals")!.Element(ns + "Principal")!;
        XElement settings = root.Element(ns + "Settings")!;
        XElement action = root.Element(ns + "Actions")!.Element(ns + "Exec")!;

        Assert.Multiple(() =>
        {
            Assert.That(root.Attribute("version")?.Value, Is.EqualTo("1.4"));
            Assert.That(root.Element(ns + "RegistrationInfo")!.Element(ns + "URI")!.Value, Is.EqualTo(ManagedWakeTask.Name));
            Assert.That(root.Element(ns + "Triggers")!.Elements(ns + "TimeTrigger"), Has.Exactly(1).Items);
            Assert.That(root.Descendants(ns + "StartBoundary").Single().Value, Is.EqualTo("2026-11-01T06:30:00Z"));
            Assert.That(root.Descendants(ns + "TimeTrigger").Single().Element(ns + "Enabled")!.Value, Is.EqualTo("true"));
            Assert.That(principal.Element(ns + "UserId")!.Value, Is.EqualTo(request.CurrentUserId));
            Assert.That(principal.Element(ns + "LogonType")!.Value, Is.EqualTo("InteractiveToken"));
            Assert.That(principal.Element(ns + "RunLevel")!.Value, Is.EqualTo("LeastPrivilege"));
            Assert.That(settings.Element(ns + "WakeToRun")!.Value, Is.EqualTo("true"));
            Assert.That(settings.Element(ns + "MultipleInstancesPolicy")!.Value, Is.EqualTo("IgnoreNew"));
            Assert.That(settings.Element(ns + "StartWhenAvailable")!.Value, Is.EqualTo("true"));
            Assert.That(settings.Element(ns + "DisallowStartIfOnBatteries")!.Value, Is.EqualTo("false"));
            Assert.That(settings.Element(ns + "StopIfGoingOnBatteries")!.Value, Is.EqualTo("false"));
            Assert.That(settings.Element(ns + "ExecutionTimeLimit")!.Value, Is.EqualTo("PT15M"));
            Assert.That(action.Element(ns + "Command")!.Value, Is.EqualTo(request.RunnerExecutablePath));
            Assert.That(action.Element(ns + "WorkingDirectory")!.Value, Is.EqualTo(request.RunnerWorkingDirectory));
            Assert.That(action.Element(ns + "Arguments")!.Value, Does.Contain($"execute --occurrence-id {request.OccurrenceId}"));
            Assert.That(action.Element(ns + "Arguments")!.Value, Does.Contain("--reconcile-wake-after true"));
            Assert.That(action.Element(ns + "Arguments")!.Value, Does.Not.Contain("super-secret"));
            Assert.That(description, Does.Contain("DueLocal=2026-11-01T01:30:00.0000000-05:00"));
            Assert.That(description, Does.Contain("TimeZoneId=Central Standard Time"));
            Assert.That(document.ToString(), Does.Contain("&amp;"));
            Assert.That(document.ToString(), Does.Contain("助手"));
        });
    }

    [Test]
    public void UtcBoundaryRepresentsBothDstOverlapInstantsWithoutAmbiguity()
    {
        WakeTaskRequest first = CreateRequest(
            new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(-5)));
        WakeTaskRequest second = CreateRequest(
            new DateTimeOffset(2026, 11, 1, 7, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(-6)));
        XNamespace ns = WakeTaskXmlGenerator.TaskNamespace;

        Assert.Multiple(() =>
        {
            Assert.That(WakeTaskXmlGenerator.Generate(first).Descendants(ns + "StartBoundary").Single().Value,
                Is.EqualTo("2026-11-01T06:30:00Z"));
            Assert.That(WakeTaskXmlGenerator.Generate(second).Descendants(ns + "StartBoundary").Single().Value,
                Is.EqualTo("2026-11-01T07:30:00Z"));
            Assert.That(first.GetFingerprint(), Is.Not.EqualTo(second.GetFingerprint()));
        });
    }

    [Test]
    public async Task SchtasksBridgeCreatesVerifiesUpdatesAndAlwaysDeletesTempXml()
    {
        string root = CreateTemporaryRoot();
        try
        {
            var process = new StatefulSchtasksExecutor();
            var bridge = new SchtasksWakeTaskSchedulerBridge(
                process,
                new(root, @"C:\Windows\System32\schtasks.exe", TimeSpan.FromSeconds(5), 65_536),
                static () => { });
            WakeTaskRequest first = CreateRequest();

            await bridge.ReconcileAsync(first);
            WakeTaskState firstState = await bridge.GetStateAsync();
            WakeTaskRequest replacement = first with
            {
                OccurrenceId = OccurrenceId.New(),
                ConfigurationRevisionId = Guid.NewGuid(),
                DueAtUtc = first.DueAtUtc.AddMinutes(10),
                DueLocal = first.DueLocal.AddMinutes(10),
                CorrelationId = Guid.NewGuid(),
            };
            await bridge.ReconcileAsync(replacement);
            WakeTaskState secondState = await bridge.GetStateAsync();

            Assert.Multiple(() =>
            {
                Assert.That(firstState.Exists && firstState.IsManaged, Is.True);
                Assert.That(firstState.OccurrenceId, Is.EqualTo(first.OccurrenceId));
                Assert.That(secondState.OccurrenceId, Is.EqualTo(replacement.OccurrenceId));
                Assert.That(process.CreateCalls, Is.EqualTo(2));
                Assert.That(process.LastXmlPath, Is.Not.Null);
                Assert.That(File.Exists(process.LastXmlPath), Is.False);
                Assert.That(Directory.GetFiles(Path.Combine(root, "temp", "wake-tasks")), Is.Empty);
            });

            await bridge.RemoveAsync();
            await bridge.RemoveAsync();
            Assert.That((await bridge.GetStateAsync()).Exists, Is.False);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void SchtasksPermissionFailureIsStructuredAndContainsNoCommandLine()
    {
        string root = CreateTemporaryRoot();
        var process = new ConstantProcessExecutor(new(
            1,
            string.Empty,
            "ERROR: Access is denied.",
            TimedOut: false));
        var bridge = new SchtasksWakeTaskSchedulerBridge(
            process,
            new(root, @"C:\Windows\System32\schtasks.exe", TimeSpan.FromSeconds(5), 65_536));

        WakeTaskBridgeException exception = Assert.ThrowsAsync<WakeTaskBridgeException>(
            async () => await bridge.GetStateAsync())!;

        Assert.Multiple(() =>
        {
            Assert.That(exception.Error.Category, Is.EqualTo(WakeTaskErrorCategory.PermissionDenied));
            Assert.That(exception.Error.Code, Is.EqualTo("wake.task-query-failed"));
            Assert.That(exception.Message, Does.Not.Contain("/Query"));
        });
        Directory.Delete(root, recursive: true);
    }

    [Test]
    public void WakeReconcileAndRemoveRefuseAnUnmanagedReservedTask()
    {
        string root = CreateTemporaryRoot();
        try
        {
            const string xml = "<Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\"><RegistrationInfo><URI>\\HomeBusinessAssistant\\NextWake</URI><Description>Third-party task</Description></RegistrationInfo><Triggers/><Settings/><Actions/></Task>";
            var process = new ConstantProcessExecutor(new(0, xml, string.Empty, false));
            var bridge = new SchtasksWakeTaskSchedulerBridge(
                process,
                new(root, @"C:\Windows\System32\schtasks.exe", TimeSpan.FromSeconds(5), 65_536),
                static () => { });

            WakeTaskBridgeException reconcile = Assert.ThrowsAsync<WakeTaskBridgeException>(async () => await bridge.ReconcileAsync(CreateRequest()))!;
            WakeTaskBridgeException remove = Assert.ThrowsAsync<WakeTaskBridgeException>(async () => await bridge.RemoveAsync())!;

            Assert.Multiple(() =>
            {
                Assert.That(reconcile.Error.Code, Is.EqualTo("wake.task-ownership-conflict"));
                Assert.That(remove.Error.Code, Is.EqualTo("wake.task-ownership-conflict"));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void FailedRegistrationStillDeletesTemporaryXml()
    {
        string root = CreateTemporaryRoot();
        try
        {
            var process = new StatefulSchtasksExecutor { FailCreate = true };
            var bridge = new SchtasksWakeTaskSchedulerBridge(
                process,
                new(root, @"C:\Windows\System32\schtasks.exe", TimeSpan.FromSeconds(5), 65_536),
                static () => { });

            WakeTaskBridgeException exception = Assert.ThrowsAsync<WakeTaskBridgeException>(
                async () => await bridge.ReconcileAsync(CreateRequest()))!;

            Assert.Multiple(() =>
            {
                Assert.That(exception.Error.Code, Is.EqualTo("wake.task-registration-failed"));
                Assert.That(process.LastXmlPath, Is.Not.Null);
                Assert.That(File.Exists(process.LastXmlPath), Is.False);
                Assert.That(Directory.GetFiles(Path.Combine(root, "temp", "wake-tasks")), Is.Empty);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task NestedPowerHandlesKeepCombinedFlagsUntilFinalRelease()
    {
        var native = new RecordingNativeApi();
        await using var service = new WindowsPowerRequestService(native, TimeProvider.System);

        IAsyncDisposable system = await service.AcquireSystemRequiredAsync("system work", keepDisplayOn: false);
        IAsyncDisposable display = await service.AcquireSystemRequiredAsync("visible work", keepDisplayOn: true);
        await system.DisposeAsync();
        await display.DisposeAsync();

        Assert.That(native.States, Is.EqualTo(new[]
        {
            WindowsExecutionState.Continuous | WindowsExecutionState.SystemRequired,
            WindowsExecutionState.Continuous | WindowsExecutionState.SystemRequired | WindowsExecutionState.DisplayRequired,
            WindowsExecutionState.Continuous | WindowsExecutionState.SystemRequired | WindowsExecutionState.DisplayRequired,
            WindowsExecutionState.Continuous,
        }));
    }

    [Test]
    public void PowerAcquisitionFailureIsActionableAndDoesNotCreateHandle()
    {
        var native = new RecordingNativeApi { Succeed = false };
        var service = new WindowsPowerRequestService(native, TimeProvider.System);

        PowerRequestException exception = Assert.ThrowsAsync<PowerRequestException>(
            async () => await service.AcquireSystemRequiredAsync("required work", false))!;

        Assert.That(exception.Code, Is.EqualTo("power.acquire-failed"));
        service.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Test]
    public async Task DiagnosticsAggregateFourCommandsAndRedactBoundedText()
    {
        var process = new DiagnosticProcessExecutor();
        var service = new PowercfgDiagnosticsService(
            process,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 30, 16, 0, 0, TimeSpan.Zero)),
            @"C:\Windows\System32\powercfg.exe",
            TimeSpan.FromSeconds(5),
            2_048);

        PowerDiagnosticsSnapshot snapshot = await service.CaptureAsync();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Results, Has.Count.EqualTo(4));
            Assert.That(process.ArgumentSets, Does.Contain("/a"));
            Assert.That(process.ArgumentSets, Does.Contain("/waketimers"));
            Assert.That(process.ArgumentSets, Does.Contain("/lastwake"));
            Assert.That(process.ArgumentSets, Does.Contain("/devicequery wake_armed"));
            Assert.That(snapshot.HasReportedWakeTimer, Is.False);
            Assert.That(snapshot.HasArmedWakeDevice, Is.True);
            Assert.That(snapshot.Results.All(item => !item.Output.Contains("fixture-token", StringComparison.Ordinal)), Is.True);
            Assert.That(snapshot.Results.Any(item => item.Output.Contains("[REDACTED]", StringComparison.Ordinal)), Is.True);
        });
    }

    private static WakeTaskRequest CreateRequest(
        DateTimeOffset? dueUtc = null,
        DateTimeOffset? dueLocal = null,
        string runnerPath = @"C:\Apps\HomeBusinessAssistant.Runner.exe") => new(
            OccurrenceId.New(),
            Guid.NewGuid(),
            dueUtc ?? new DateTimeOffset(2026, 8, 30, 18, 0, 0, TimeSpan.Zero),
            dueLocal ?? new DateTimeOffset(2026, 8, 30, 13, 0, 0, TimeSpan.FromHours(-5)),
            "Central Standard Time",
            runnerPath,
            Path.GetDirectoryName(runnerPath)!,
            new(
                @"C:\Data Root",
                @"C:\Agent Root\助手",
                @"C:\Manifest Root",
                "assistant.db"),
            TimeSpan.FromMinutes(15),
            @"WORKGROUP\User & Operator",
            WakeTaskUserSessionPolicy.CurrentInteractiveUser,
            StartWhenAvailable: true,
            AllowStartOnBatteries: true,
            StopIfGoingOnBatteries: false,
            Guid.NewGuid());

    private static string CreateTemporaryRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-wake-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class StatefulSchtasksExecutor : IProcessExecutor
    {
        private string? registeredXml;

        public int CreateCalls { get; private set; }

        public string? LastXmlPath { get; private set; }

        public bool FailCreate { get; set; }

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Arguments[0] == "/Query")
            {
                return Task.FromResult(registeredXml is null
                    ? new ProcessExecutionResult(1, string.Empty, "ERROR: The system cannot find the file specified.", false)
                    : new ProcessExecutionResult(0, registeredXml, string.Empty, false));
            }

            if (request.Arguments[0] == "/Create")
            {
                CreateCalls++;
                int index = request.Arguments.IndexOf("/XML");
                LastXmlPath = request.Arguments[index + 1];
                registeredXml = File.ReadAllText(LastXmlPath);
                if (FailCreate)
                {
                    registeredXml = null;
                    return Task.FromResult(new ProcessExecutionResult(1, string.Empty, "Registration failed.", false));
                }

                return Task.FromResult(new ProcessExecutionResult(0, "SUCCESS", string.Empty, false));
            }

            if (request.Arguments[0] == "/Delete")
            {
                if (registeredXml is null)
                {
                    return Task.FromResult(new ProcessExecutionResult(1, string.Empty, "ERROR: The system cannot find the file specified.", false));
                }

                registeredXml = null;
                return Task.FromResult(new ProcessExecutionResult(0, "SUCCESS", string.Empty, false));
            }

            throw new InvalidOperationException("Unexpected schtasks operation.");
        }
    }

    private sealed class ConstantProcessExecutor(ProcessExecutionResult result) : IProcessExecutor
    {
        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class RecordingNativeApi : IWindowsExecutionStateNativeApi
    {
        public ConcurrentQueue<WindowsExecutionState> States { get; } = new();

        public bool Succeed { get; set; } = true;

        public bool TrySet(WindowsExecutionState state)
        {
            States.Enqueue(state);
            return Succeed;
        }
    }

    private sealed class DiagnosticProcessExecutor : IProcessExecutor
    {
        public List<string> ArgumentSets { get; } = [];

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken = default)
        {
            string arguments = string.Join(' ', request.Arguments);
            ArgumentSets.Add(arguments);
            string output = arguments switch
            {
                "/waketimers" => "There are no active wake timers in the system. Bearer fixture-token",
                "/devicequery wake_armed" => "Synthetic Keyboard",
                _ => "Diagnostic output",
            };
            return Task.FromResult(new ProcessExecutionResult(0, output, string.Empty, false));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }
}

internal static class ReadOnlyListExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (string.Equals(values[index], value, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
