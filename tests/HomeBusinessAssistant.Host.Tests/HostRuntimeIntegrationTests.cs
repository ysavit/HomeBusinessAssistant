using HomeBusinessAssistant.Host.Logging;

namespace HomeBusinessAssistant.Host.Tests;

internal sealed class HostRuntimeIntegrationTests
{
    [Test]
    public async Task RuntimeMigratesRecoversAndReleasesResourcesWithoutTouchingTaskScheduler()
    {
        string repositoryRoot = FindRepositoryRoot();
        string root = Path.Combine(Path.GetTempPath(), $"hba-host-runtime-{Guid.NewGuid():N}");
        string data = Path.Combine(root, "data");
        string agents = Path.Combine(root, "agents");
        Directory.CreateDirectory(agents);
        var settings = new HostBootstrapSettings(
            repositoryRoot,
            data,
            agents,
            Path.Combine(repositoryRoot, "manifests"),
            Path.Combine(
                repositoryRoot,
                "src",
                "HomeBusinessAssistant.Runner",
                "bin",
                "Release",
                "net10.0-windows",
                "HomeBusinessAssistant.Runner.exe"),
            Path.Combine(repositoryRoot, "src", "HomeBusinessAssistant.Runner", "bin", "Release", "net10.0-windows"),
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

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(Path.Combine(data, "assistant.db")), Is.True);
                Assert.That(runtime.HealthState.DatabaseInitialized, Is.True);
                Assert.That(runtime.HealthState.DataDirectoriesWritable, Is.True);
                Assert.That(runtime.HealthState.RunnerPresent, Is.True);
                Assert.That(runtime.KeepAwake.GetState().IsActive, Is.False);
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
