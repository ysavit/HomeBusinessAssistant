using HomeBusinessAssistant.Infrastructure.Persistence;

namespace HomeBusinessAssistant.Infrastructure.Tests;

internal sealed class HostPlatformStatusReaderTests
{
    private static readonly string[] ExpectedAgentIds = ["founder-scout", "wake-remote"];

    [Test]
    public async Task FreshDatabaseReturnsRealAgentsAndHonestEmptyOperationalState()
    {
        await using TemporaryAssistantDatabase temporary = await TemporaryAssistantDatabase.CreateAsync();
        var reader = new HostPlatformStatusReader(temporary.Database.ContextFactory);

        var status = await reader.ReadAsync(10);

        Assert.Multiple(() =>
        {
            Assert.That(status.Agents.Select(item => item.AgentId.Value), Is.EquivalentTo(ExpectedAgentIds));
            Assert.That(status.Agents, Is.All.Matches<HomeBusinessAssistant.Application.Desktop.HostAgentStatus>(item => !item.HasConfiguration));
            Assert.That(status.NextOccurrences, Is.Empty);
            Assert.That(status.ActiveRuns, Is.Empty);
            Assert.That(status.RecentFailures, Is.Empty);
            Assert.That(status.LatestSummary, Is.Null);
        });
    }
}
