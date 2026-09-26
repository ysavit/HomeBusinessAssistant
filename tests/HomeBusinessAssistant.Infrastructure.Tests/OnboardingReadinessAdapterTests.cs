using HomeBusinessAssistant.Application.Onboarding;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Onboarding;

namespace HomeBusinessAssistant.Infrastructure.Tests;

internal sealed class OnboardingReadinessAdapterTests
{
    [Test]
    public async Task CompatibleRunnerWithNoInstalledAgentPackagesReturnsExplicitNoAgentsBlocker()
    {
        string repositoryRoot = FindRepositoryRoot();
        string agentDirectory = Path.Combine(Path.GetTempPath(), $"hba-onboarding-packages-{Guid.NewGuid():N}");
        Directory.CreateDirectory(agentDirectory);
        try
        {
            string runner = Path.Combine(
                repositoryRoot,
                "src",
                "HomeBusinessAssistant.Runner",
                "bin",
                "Release",
                "net10.0-windows",
                "HomeBusinessAssistant.Runner.exe");
            var check = new RunnerAndPackagesOnboardingCheck(
                new EmptyAgentDefinitionRepository(),
                repositoryRoot,
                runner,
                agentDirectory,
                TimeProvider.System);

            OnboardingReadinessCheckResult result = await check.EvaluateAsync();

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(OnboardingCheckStatus.Blocked));
                Assert.That(result.ReasonCode, Is.EqualTo("onboarding.no-agents"));
                Assert.That(result.Details.GetProperty("runnerPresent").GetBoolean(), Is.True);
                Assert.That(result.Details.GetProperty("runnerCompatible").GetBoolean(), Is.True);
                Assert.That(result.Details.GetProperty("validAgentCount").GetInt32(), Is.Zero);
            });
        }
        finally
        {
            Directory.Delete(agentDirectory, recursive: true);
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

    private sealed class EmptyAgentDefinitionRepository : IAgentDefinitionRepository
    {
        public ValueTask<AgentDefinitionRecord?> GetAsync(AgentId agentId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AgentDefinitionRecord?>(null);

        public ValueTask<IReadOnlyList<AgentDefinitionRecord>> GetAllAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<AgentDefinitionRecord>>([]);

        public ValueTask<IReadOnlyList<AgentDefinitionRecord>> GetEnabledAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<AgentDefinitionRecord>>([]);

        public ValueTask<AgentDefinitionRecord> UpsertManifestAsync(AgentDefinitionRecord definition, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> SetEnabledAsync(AgentId agentId, bool enabled, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<bool> SetEnabledBySystemAsync(AgentId agentId, bool enabled, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
