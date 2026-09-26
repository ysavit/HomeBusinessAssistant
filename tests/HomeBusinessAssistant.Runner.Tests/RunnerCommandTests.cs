namespace HomeBusinessAssistant.Runner.Tests;

internal sealed class RunnerCommandTests
{
    [Test]
    public void ExecuteReportsCurrentOperationsAndExitsSuccessfully()
    {
        using StringWriter output = new();

        int exitCode = RunnerCommand.Execute(output);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(output.ToString(), Does.Contain("local operations"));
            Assert.That(output.ToString(), Does.Contain("run-agent"));
            Assert.That(output.ToString(), Does.Contain("scan-agents"));
            Assert.That(output.ToString(), Does.Contain("reconcile-wake"));
            Assert.That(output.ToString(), Does.Contain("restore-backup"));
        });
    }

    [Test]
    public async Task InvalidCommandLineUsesStableExitCode()
    {
        using StringWriter output = new();
        using StringWriter error = new();

        int exitCode = await RunnerCommand.ExecuteAsync(
            ["execute", "--occurrence-id", "not-a-guid"], output, error);

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(2));
            Assert.That(error.ToString(), Does.Contain("--occurrence-id"));
        });
    }

    [Test]
    public async Task MissingRestoreSetReturnsBoundedFailureWithoutStackTrace()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-runner-tests-{Guid.NewGuid():N}");
        string manifests = Path.Combine(AppContext.BaseDirectory, "manifests");
        using StringWriter output = new();
        using StringWriter error = new();
        try
        {
            int exitCode = await RunnerCommand.ExecuteAsync(
            [
                "restore-backup", "--backup-set", "missing-set", "--maintenance", "true", "--confirm", "RESTORE",
                "--data-directory", root,
                "--agent-directory", Path.Combine(root, "agents"),
                "--manifest-directory", manifests,
                "--database-file-name", "assistant.db",
            ], output, error);

            Assert.Multiple(() =>
            {
                Assert.That(exitCode, Is.Not.Zero);
                Assert.That(error.ToString(), Does.Contain("InvalidDataException"));
                Assert.That(error.ToString(), Does.Contain("bounded structured Runner log"));
                Assert.That(error.ToString(), Does.Not.Contain(" at "));
                Assert.That(error.ToString(), Does.Not.Contain(root));
            });
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
