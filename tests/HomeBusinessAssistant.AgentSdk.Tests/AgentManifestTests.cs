using HomeBusinessAssistant.AgentSdk.Manifest;
using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.AgentSdk.Tests;

internal sealed class AgentManifestTests
{
    [Test]
    public async Task LoaderAcceptsValidManifestAndUnknownProperties()
    {
        string path = CreateTemporaryPath();
        try
        {
            await File.WriteAllTextAsync(path, ValidManifestJson(extraProperty: "\"futureProperty\":42,")).ConfigureAwait(false);

            AgentManifestLoadResult result = await AgentManifestLoader.LoadAsync(path).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.Errors, Is.Empty);
                Assert.That(result.Manifest?.Id, Is.EqualTo(AgentId.Parse("founder-scout")));
                Assert.That(result.Manifest?.ManifestVersion, Is.EqualTo(AgentProtocolVersion.CurrentManifest));
                Assert.That(result.Manifest?.ConfigurationSchemaVersion, Is.EqualTo(new AgentProtocolVersion(1, 0)));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestCase("../FounderScout.exe")]
    [TestCase("tools/../../FounderScout.exe")]
    [TestCase("C:\\agents\\FounderScout.exe")]
    [TestCase("/opt/agents/FounderScout")]
    public async Task LoaderRejectsUnsafeExecutablePaths(string executable)
    {
        string path = CreateTemporaryPath();
        try
        {
            string json = ValidManifestJson().Replace("FounderScout.exe", executable.Replace("\\", "\\\\", StringComparison.Ordinal), StringComparison.Ordinal);
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);

            AgentManifestLoadResult result = await AgentManifestLoader.LoadAsync(path).ConfigureAwait(false);

            Assert.That(result.Errors.Select(error => error.Code), Does.Contain("manifest.invalidExecutable"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task LoaderReportsPathWithoutLeakingMalformedContent()
    {
        const string sensitiveMarker = "do-not-repeat-this-secret";
        string path = CreateTemporaryPath();
        try
        {
            await File.WriteAllTextAsync(path, $"{{\"id\":\"{sensitiveMarker}\",\"defaultTimeoutSeconds\":\"wrong\"}}").ConfigureAwait(false);

            AgentManifestLoadResult result = await AgentManifestLoader.LoadAsync(path).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.Errors, Is.Not.Empty);
                Assert.That(result.Errors.Any(error => error.Path.Contains("defaultTimeoutSeconds", StringComparison.Ordinal)), Is.True);
                Assert.That(string.Join('|', result.Errors.Select(error => error.Message)), Does.Not.Contain(sensitiveMarker));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ValidatorRejectsDuplicateCommandsAndUnsafeTimeout()
    {
        AgentManifest manifest = new(
            AgentProtocolVersion.CurrentManifest,
            AgentId.Parse("founder-scout"),
            "Founder Scout",
            "Diagnostic manifest.",
            AgentVersion.Parse("1.0.0"),
            "FounderScout.exe",
            ["run", "run"],
            ["diagnose"],
            0,
            ConcurrencyPolicy.Forbid,
            SupportsScheduling: true,
            SupportsManualRun: true,
            RequiresInteractiveUserSession: true,
            ConfigurationSchemaVersion: new AgentProtocolVersion(1, 0));

        var result = AgentManifestValidator.Validate(manifest);

        string[] errorCodes = result.Errors.Select(error => error.Code).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(errorCodes, Does.Contain("manifest.duplicateCommand"));
            Assert.That(errorCodes, Does.Contain("manifest.invalidTimeout"));
        });
    }

    [Test]
    public void SchemaIsCopiedToConsumerOutput()
    {
        string schemaPath = Path.Combine(AppContext.BaseDirectory, "agent-manifest.schema.json");

        Assert.That(File.Exists(schemaPath), Is.True, schemaPath);
        Assert.That(() => System.Text.Json.JsonDocument.Parse(File.ReadAllText(schemaPath)), Throws.Nothing);
    }

    private static string CreateTemporaryPath() => Path.Combine(Path.GetTempPath(), $"agent-manifest-{Guid.NewGuid():N}.json");

    private static string ValidManifestJson(string extraProperty = "") => $$"""
        {
          {{extraProperty}}
          "manifestVersion": "1.0",
          "id": "founder-scout",
          "displayName": "Founder Scout",
          "description": "A diagnostic agent manifest.",
          "version": "1.2.3-beta.1+win-x64",
          "executable": "FounderScout.exe",
          "supportedCommands": ["protocol-demo"],
          "capabilities": ["diagnostics"],
          "defaultTimeoutSeconds": 60,
          "defaultConcurrencyPolicy": "Forbid",
          "supportsScheduling": false,
          "supportsManualRun": true,
          "requiresInteractiveUserSession": true,
          "configurationSchemaVersion": "1.0"
        }
        """;
}
