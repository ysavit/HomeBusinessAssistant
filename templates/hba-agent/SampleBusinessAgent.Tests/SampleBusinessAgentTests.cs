using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Execution;
using HomeBusinessAssistant.AgentSdk.Protocol;
using NUnit.Framework;
using SampleBusinessAgent.Agent;
using SampleBusinessAgent.Application;
using SampleBusinessAgent.Infrastructure;

namespace SampleBusinessAgent.Tests;

/// <summary>Verifies the checked-in sample's safe extension conventions.</summary>
public sealed class SampleBusinessAgentTests
{
    private static readonly string[] TextExtensions = [".txt"];

    /// <summary>Rejects path traversal and inline secret values.</summary>
    [Test]
    public void ConfigurationRejectsTraversalAndInlineSecret()
    {
        JsonElement value = JsonSerializer.SerializeToElement(new
        {
            schemaVersion = "1.0",
            sourceRelativePath = "../outside",
            maximumFiles = 10,
            includedExtensions = TextExtensions,
            requireAtLeastOneFile = false,
            notificationSecretReference = "plain-secret",
        });

        SampleBusinessAgentConfigurationParseResult result = SampleBusinessAgentConfiguration.Parse(value);

        Assert.That(result.Configuration, Is.Null);
        Assert.That(result.ErrorCodes, Does.Contain("sample.configuration.sourceRelativePath"));
        Assert.That(result.ErrorCodes, Does.Contain("sample.configuration.notificationSecretReference"));
    }

    /// <summary>Confines and bounds the local report.</summary>
    [Test]
    public async Task ReportStaysInAssignedRootsAndIsBounded()
    {
        string root = Path.Combine(Path.GetTempPath(), "hba-sample-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "data");
        string artifacts = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(Path.Combine(data, "inbox"));
        Directory.CreateDirectory(artifacts);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(data, "inbox", "a.txt"), "a");
            await File.WriteAllTextAsync(Path.Combine(data, "inbox", "b.txt"), "b");
            var configuration = new SampleBusinessAgentConfiguration("1.0", "inbox", 1, [".txt"], false, "secret://sample-business-agent/notification-token");

            SampleFolderReport result = await LocalFolderReportService.CreateAsync(configuration, data, artifacts);

            Assert.That(result.MatchingFiles, Is.EqualTo(1));
            Assert.That(result.Truncated, Is.True);
            Assert.That(File.Exists(Path.Combine(artifacts, result.ArtifactRelativePath)), Is.True);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Emits a complete protocol and artifact lifecycle.</summary>
    [Test]
    public async Task CommandEmitsStandardLifecycleAndArtifact()
    {
        string root = Path.Combine(Path.GetTempPath(), "hba-sample-command-" + Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "data");
        string artifacts = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(Path.Combine(data, "inbox"));
        Directory.CreateDirectory(artifacts);
        string input = Path.Combine(root, "input.json");
        await File.WriteAllTextAsync(input, AgentExecutionInput.Serialize(JsonSerializer.Serialize(new
        {
            schemaVersion = "1.0",
            sourceRelativePath = "inbox",
            maximumFiles = 10,
            includedExtensions = TextExtensions,
            requireAtLeastOneFile = false,
            notificationSecretReference = "secret://sample-business-agent/notification-token",
        }), "{}"));
        var output = new StringWriter();
        try
        {
            Guid run = Guid.NewGuid();
            Guid occurrence = Guid.NewGuid();
            string[] arguments = ["run", "--run-id", run.ToString(), "--occurrence-id", occurrence.ToString(), "--agent-id", "sample-business-agent", "--config-file", input, "--data-directory", data, "--artifact-directory", artifacts, "--protocol-version", "1.0"];

            int exitCode = await SampleBusinessAgentCommand.ExecuteAsync(arguments, output, TextWriter.Null, TimeProvider.System);
            string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

            Assert.That(exitCode, Is.EqualTo(AgentExitCode.Success));
            Assert.That(lines.Select(AgentEventSerializer.Deserialize).All(result => result.Event is not null), Is.True);
            Assert.That(lines.Select(AgentEventSerializer.Deserialize).Any(result => result.Event is ArtifactAgentEvent), Is.True);
            Assert.That(lines.Select(AgentEventSerializer.Deserialize).Any(result => result.Event is SummaryAgentEvent), Is.True);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Propagates cancellation before local work.</summary>
    [Test]
    public void ReportHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var configuration = new SampleBusinessAgentConfiguration("1.0", "inbox", 1, [".txt"], false, "secret://sample-business-agent/notification-token");
        Assert.CatchAsync<OperationCanceledException>(async () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "hba-sample-cancel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { _ = await LocalFolderReportService.CreateAsync(configuration, root, root, cancellation.Token); }
            finally { Directory.Delete(root, recursive: true); }
        });
    }
}
