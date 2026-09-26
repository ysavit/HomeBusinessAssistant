using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Execution;

namespace HomeBusinessAssistant.AgentSdk.Tests;

internal sealed class AgentExecutionInputTests
{
    [Test]
    public async Task RoundTripPreservesConfigurationAndOccurrenceObjects()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-agent-input-{Guid.NewGuid():N}");
        string path = Path.Combine(root, "input.json");
        Directory.CreateDirectory(root);
        try
        {
            string serialized = AgentExecutionInput.Serialize(
                "{\"schemaVersion\":\"1.0\",\"marker\":\"config\"}",
                "{\"windowInstanceId\":\"window-1\"}");
            await File.WriteAllTextAsync(path, serialized);

            AgentExecutionInput loaded = await AgentExecutionInput.LoadAsync(path);

            Assert.Multiple(() =>
            {
                Assert.That(loaded.SchemaVersion, Is.EqualTo("1.0"));
                Assert.That(loaded.Configuration.GetProperty("marker").GetString(), Is.EqualTo("config"));
                Assert.That(loaded.OccurrenceArguments.GetProperty("windowInstanceId").GetString(), Is.EqualTo("window-1"));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void SerializerRejectsNonObjectInputs()
    {
        Assert.That(
            () => AgentExecutionInput.Serialize("[]", "{}"),
            Throws.InstanceOf<JsonException>());
    }

    [Test]
    public async Task RoundTripResolvesShortLivedSecretOnlyByOpaqueReference()
    {
        string root = Path.Combine(Path.GetTempPath(), $"hba-agent-input-{Guid.NewGuid():N}");
        string path = Path.Combine(root, "input.json");
        Directory.CreateDirectory(root);
        try
        {
            string serialized = AgentExecutionInput.Serialize(
                "{\"apiKeySecretReference\":\"secret://founder-scout/provider-key\"}",
                "{}",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["secret://founder-scout/provider-key"] = "synthetic-private-value",
                });
            await File.WriteAllTextAsync(path, serialized);

            AgentExecutionInput loaded = await AgentExecutionInput.LoadAsync(path);

            Assert.Multiple(() =>
            {
                Assert.That(loaded.GetResolvedSecret("secret://founder-scout/provider-key"), Is.EqualTo("synthetic-private-value"));
                Assert.That(loaded.GetResolvedSecret("secret://founder-scout/unknown"), Is.Null);
                Assert.That(loaded.Configuration.GetRawText(), Does.Not.Contain("synthetic-private-value"));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
