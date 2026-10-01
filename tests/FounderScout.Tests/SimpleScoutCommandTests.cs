using FounderScout.SimpleCli;

namespace FounderScout.Tests;

internal sealed class SimpleScoutCommandTests
{
    [Test]
    public async Task DbPathReportsSharedFounderDatabaseWithoutCreatingIt()
    {
        string root = Path.Combine(Path.GetTempPath(), $"simple-scout-path-{Guid.NewGuid():N}");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = await SimpleScoutCommand.ExecuteAsync(
            ["db-path", "--data-root", root], output, error, TimeProvider.System);

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero);
            Assert.That(output.ToString().Trim(), Is.EqualTo(Path.Combine(root, "agents", "founder-scout", "founders.db")));
            Assert.That(Directory.Exists(root), Is.False);
            Assert.That(error.ToString(), Is.Empty);
        });
    }

    [Test]
    public async Task ListExplainsEmptyDatabaseWithoutStartingBrowser()
    {
        string root = Path.Combine(Path.GetTempPath(), $"simple-scout-list-{Guid.NewGuid():N}");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = await SimpleScoutCommand.ExecuteAsync(
            ["list", "--data-root", root], output, error, TimeProvider.System);

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero);
            Assert.That(output.ToString(), Does.Contain("No Founder Scout database yet"));
            Assert.That(Directory.Exists(root), Is.False);
            Assert.That(error.ToString(), Is.Empty);
        });
    }

    [Test]
    public async Task InvalidBoundFailsBeforeCreatingDataOrOpeningBrowser()
    {
        string root = Path.Combine(Path.GetTempPath(), $"simple-scout-invalid-{Guid.NewGuid():N}");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = await SimpleScoutCommand.ExecuteAsync(
            ["run", "--max", "0", "--data-root", root], output, error, TimeProvider.System);

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Not.Zero);
            Assert.That(error.ToString(), Does.Contain("Usage:"));
            Assert.That(Directory.Exists(root), Is.False);
        });
    }
}
