using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Configuration;
using HomeBusinessAssistant.Infrastructure.Persistence.Repositories;

namespace HomeBusinessAssistant.Infrastructure.Tests;

internal sealed class AgentRegistryScannerTests
{
    [Test]
    public async Task ScanRegistersDisabledSeedsDefaultsAndPreservesHistoryWhenPackageDisappears()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        string repository = FindRepositoryRoot();
        string agentsRoot = Path.Combine(temporary.Root, "installed-agents");
        string packageRoot = Path.Combine(agentsRoot, "sample-business-agent");
        Directory.CreateDirectory(packageRoot);
        File.Copy(Path.Combine(repository, "agents", "SampleBusinessAgent", "manifest.json"), Path.Combine(packageRoot, "manifest.json"));
        File.Copy(Path.Combine(repository, "agents", "SampleBusinessAgent", "configuration.schema.json"), Path.Combine(packageRoot, "configuration.schema.json"));
        File.Copy(typeof(AgentRegistryScanner).Assembly.Location, Path.Combine(packageRoot, "SampleBusinessAgent.exe"));
        var definitions = new AgentDefinitionRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        var catalog = new FileAgentConfigurationSchemaCatalog(agentsRoot);
        var configurations = new AgentConfigurationService(
            temporary.Database.ContextFactory,
            new CompositeAgentConfigurationValidator([
                new BasicAgentConfigurationValidator(),
                new GenericAgentConfigurationValidator(catalog, new HashSet<AgentId>()),
            ]),
            temporary.TimeProvider);
        var scanner = new AgentRegistryScanner(
            agentsRoot,
            definitions,
            catalog,
            configurations,
            new AuditWriter(temporary.Database.ContextFactory, temporary.TimeProvider),
            temporary.TimeProvider);

        AgentRegistryScanResult installed = await scanner.ScanAsync();
        AgentId id = AgentId.Parse("sample-business-agent");
        AgentDefinitionRecord definition = (await definitions.GetAsync(id))!;
        AgentConfigurationRecord configuration = (await configurations.GetCurrentAsync(id))!;

        Assert.Multiple(() =>
        {
            Assert.That(installed.Items.Single(item => item.AgentId == id.Value).Status, Is.EqualTo(AgentPackageScanStatus.Installed));
            Assert.That(definition.Enabled, Is.False);
            Assert.That(configuration.CurrentRevision.CanonicalConfigurationJson, Does.Contain("notificationSecretReference"));
        });

        _ = await definitions.SetEnabledAsync(id, true);
        Directory.Delete(packageRoot, recursive: true);
        AgentRegistryScanResult removed = await scanner.ScanAsync();
        AgentDefinitionRecord disabled = (await definitions.GetAsync(id))!;
        AgentConfigurationRecord? preserved = await configurations.GetCurrentAsync(id);

        Assert.Multiple(() =>
        {
            Assert.That(removed.Items.Single(item => item.AgentId == id.Value).Status, Is.EqualTo(AgentPackageScanStatus.Removed));
            Assert.That(disabled.Enabled, Is.False);
            Assert.That(preserved, Is.Not.Null);
        });
    }

    [Test]
    public async Task ScanRejectsMismatchedDirectoryIdentityWithoutExecutingContent()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        string agentsRoot = Path.Combine(temporary.Root, "installed-agents");
        string packageRoot = Path.Combine(agentsRoot, "wrong-id");
        Directory.CreateDirectory(packageRoot);
        File.Copy(Path.Combine(FindRepositoryRoot(), "agents", "SampleBusinessAgent", "manifest.json"), Path.Combine(packageRoot, "manifest.json"));
        File.Copy(Path.Combine(FindRepositoryRoot(), "agents", "SampleBusinessAgent", "configuration.schema.json"), Path.Combine(packageRoot, "configuration.schema.json"));
        File.Copy(typeof(AgentRegistryScanner).Assembly.Location, Path.Combine(packageRoot, "SampleBusinessAgent.exe"));
        var definitions = new AgentDefinitionRepository(temporary.Database.ContextFactory, temporary.TimeProvider);
        var catalog = new FileAgentConfigurationSchemaCatalog(agentsRoot);
        var configurations = new AgentConfigurationService(temporary.Database.ContextFactory, new BasicAgentConfigurationValidator(), temporary.TimeProvider);
        var scanner = new AgentRegistryScanner(
            agentsRoot,
            definitions,
            catalog,
            configurations,
            new AuditWriter(temporary.Database.ContextFactory, temporary.TimeProvider),
            temporary.TimeProvider);

        AgentRegistryScanResult result = await scanner.ScanAsync();

        Assert.That(result.InvalidCount, Is.EqualTo(1));
        Assert.That(await definitions.GetAsync(AgentId.Parse("sample-business-agent")), Is.Null);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HomeBusinessAssistant.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
