using System.Diagnostics;
using System.Text.Json;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Host.Logging;
using HomeBusinessAssistant.Infrastructure.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;

namespace HomeBusinessAssistant.Host.Tests;

internal sealed class TrayRunnerIntegrationTests
{
    [Test]
    public async Task TrayDiagnosticCreatesOccurrenceAndRunsRealWakeRemoteThroughRunner()
    {
        string repositoryRoot = FindRepositoryRoot();
        string root = Path.Combine(Path.GetTempPath(), $"hba-tray-runner-{Guid.NewGuid():N}");
        string data = Path.Combine(root, "data");
        string agents = Path.Combine(root, "agents");
        string wakeTarget = Path.Combine(agents, "wake-remote");
        Directory.CreateDirectory(wakeTarget);
        CopyDirectory(
            Path.Combine(repositoryRoot, "agents", "WakeRemote", "WakeRemote.Agent", "bin", "Release", "net10.0-windows"),
            wakeTarget);
        string runnerDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "HomeBusinessAssistant.Runner",
            "bin",
            "Release",
            "net10.0-windows");
        var settings = new HostBootstrapSettings(
            repositoryRoot,
            data,
            agents,
            Path.Combine(repositoryRoot, "manifests"),
            Path.Combine(runnerDirectory, "HomeBusinessAssistant.Runner.exe"),
            runnerDirectory,
            "assistant.db",
            "http://127.0.0.1:5180",
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(15));
        var logger = HostLogFactory.Create(data);
        try
        {
            await using HostRuntime runtime = await HostRuntime.CreateAsync(settings, logger);
            await runtime.InitializeAsync();

            var dispatched = await runtime.TrayController.RunWakeRemoteDiagnosticAsync();
            AssistantDbContextFactory contextFactory = AssistantDbContextFactory.Create(
                Path.Combine(data, "assistant.db"),
                TimeSpan.FromSeconds(5));
            var occurrences = new OccurrenceRepository(contextFactory, TimeProvider.System);
            var runs = new AgentRunRepository(contextFactory);
            ScheduleOccurrenceRecord? occurrence = null;
            AgentRunRecord? run = null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!timeout.IsCancellationRequested)
            {
                occurrence = await occurrences.GetAsync(dispatched.OccurrenceId, timeout.Token);
                run = await runs.GetForOccurrenceAsync(dispatched.OccurrenceId, timeout.Token);
                if (occurrence is { Status: OccurrenceStatus.Completed }
                    && run is { Status: AgentRunStatus.Completed })
                {
                    break;
                }

                await Task.Delay(100, timeout.Token);
            }

            if (dispatched.RunnerProcessId is int processId)
            {
                await WaitForExitAsync(processId, timeout.Token);
            }

            using JsonDocument summary = JsonDocument.Parse(run?.SummaryJson ?? "{}");
            Assert.Multiple(() =>
            {
                Assert.That(dispatched.RunnerStarted, Is.True);
                Assert.That(occurrence, Is.Not.Null);
                Assert.That(occurrence!.TriggerType, Is.EqualTo(TriggerType.TrayMenu));
                Assert.That(occurrence.Status, Is.EqualTo(OccurrenceStatus.Completed));
                Assert.That(run, Is.Not.Null);
                Assert.That(run!.Status, Is.EqualTo(AgentRunStatus.Completed));
                Assert.That(summary.RootElement.GetProperty("schemaVersion").GetString(), Is.EqualTo("1.0"));
                Assert.That(summary.RootElement.GetProperty("remoteProvider").GetString(), Is.Not.Empty);
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task WaitForExitAsync(int processId, CancellationToken cancellationToken)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (ArgumentException)
        {
            // The process exited before the test could acquire a handle.
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HomeBusinessAssistant.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
