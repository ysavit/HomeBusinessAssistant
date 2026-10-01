using System.Text.Json;
using FounderScout.Infrastructure.Persistence;
using FounderScout.SimpleCli;

namespace FounderScout.Tests;

internal sealed class SimpleScoutCommandTests
{
    [Test]
    public async Task ListUsesConfiguredDatabaseWithoutCreatingIt()
    {
        string root = Path.Combine(Path.GetTempPath(), $"simple-scout-list-{Guid.NewGuid():N}");
        string configPath = WriteSettings(root);
        using var output = new StringWriter();
        using var error = new StringWriter();

        try
        {
            int exit = await SimpleScoutCommand.ExecuteAsync(
                ["list"], output, error, TimeProvider.System, settingsPath: configPath);

            Assert.Multiple(() =>
            {
                Assert.That(exit, Is.Zero);
                Assert.That(output.ToString(), Does.Contain(Path.Combine(root, "founders.db")));
                Assert.That(File.Exists(Path.Combine(root, "founders.db")), Is.False);
                Assert.That(error.ToString(), Is.Empty);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ListReadsExistingDatabaseWithoutBootstrapWrites()
    {
        string root = Path.Combine(Path.GetTempPath(), $"simple-scout-existing-{Guid.NewGuid():N}");
        string configPath = WriteSettings(root);
        using var output = new StringWriter();
        using var error = new StringWriter();

        try
        {
            _ = await FounderScoutDatabaseInitializer.InitializeAsync(new(root), TimeProvider.System);
            string[] before = DatabaseAndConfigFiles(root);

            int exit = await SimpleScoutCommand.ExecuteAsync(
                ["list"], output, error, TimeProvider.System, settingsPath: configPath);

            Assert.Multiple(() =>
            {
                Assert.That(exit, Is.Zero);
                Assert.That(output.ToString(), Does.Contain("0 saved candidates"));
                Assert.That(DatabaseAndConfigFiles(root), Is.EqualTo(before));
                Assert.That(error.ToString(), Is.Empty);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task RunWithoutConfiguredKeyFailsBeforeCreatingDatabaseOrBrowserProfile()
    {
        string root = Path.Combine(Path.GetTempPath(), $"simple-scout-key-{Guid.NewGuid():N}");
        string configPath = WriteSettings(root);
        using var output = new StringWriter();
        using var error = new StringWriter();

        try
        {
            int exit = await SimpleScoutCommand.ExecuteAsync(
                ["run"], output, error, TimeProvider.System, settingsPath: configPath);

            Assert.Multiple(() =>
            {
                Assert.That(exit, Is.Not.Zero);
                Assert.That(error.ToString(), Does.Contain("SimpleScout:ApiKey"));
                Assert.That(File.Exists(Path.Combine(root, "founders.db")), Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task MoreThanFiveCandidatesIsRejectedBeforeDatabaseOrBrowserWork()
    {
        string root = Path.Combine(Path.GetTempPath(), $"simple-scout-bound-{Guid.NewGuid():N}");
        string configPath = WriteSettings(root, maximum: 6);
        using var output = new StringWriter();
        using var error = new StringWriter();

        try
        {
            int exit = await SimpleScoutCommand.ExecuteAsync(
                ["run"], output, error, TimeProvider.System, settingsPath: configPath);

            Assert.Multiple(() =>
            {
                Assert.That(exit, Is.Not.Zero);
                Assert.That(error.ToString(), Does.Contain("MaxCandidatesPerRun 1..5"));
                Assert.That(File.Exists(Path.Combine(root, "founders.db")), Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("scan")]
    [TestCase("analyze")]
    [TestCase("db-path")]
    [TestCase("run --max 5")]
    public async Task OnlyRunAndListArePublicCommands(string command)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = await SimpleScoutCommand.ExecuteAsync(
            command.Split(' '), output, error, TimeProvider.System);

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Not.Zero);
            Assert.That(error.ToString(), Does.Contain("Usage: SimpleScout run|list"));
        });
    }

    private static string WriteSettings(string root, int maximum = 5)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "appsettings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            SimpleScout = new
            {
                DatabasePath = Path.Combine(root, "founders.db"),
                MaxCandidatesPerRun = maximum,
                ListLimit = 20,
                DelaySeconds = 5,
                Model = "gpt-5.4-mini",
                ApiKey = "",
                FounderContext = "",
            },
        }));
        return path;
    }

    private static string[] DatabaseAndConfigFiles(string root) => Directory.GetFiles(root)
        .Select(Path.GetFileName)
        .Where(name => name is not null && !name.EndsWith("-wal", StringComparison.Ordinal)
            && !name.EndsWith("-shm", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal)
        .ToArray()!;
}
