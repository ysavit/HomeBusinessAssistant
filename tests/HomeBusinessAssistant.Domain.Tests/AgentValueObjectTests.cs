using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Domain.Tests;

internal sealed class AgentValueObjectTests
{
    [TestCase("a")]
    [TestCase("founder-scout")]
    [TestCase("agent-42")]
    public void AgentIdAcceptsCanonicalValues(string value)
    {
        AgentId identifier = AgentId.Parse(value);

        Assert.That(identifier.Value, Is.EqualTo(value));
    }

    [TestCase("")]
    [TestCase("Founder-Scout")]
    [TestCase("-founder")]
    [TestCase("founder-")]
    [TestCase("founder_scout")]
    public void AgentIdRejectsNonCanonicalValues(string value)
    {
        bool parsed = AgentId.TryParse(value, out _);

        Assert.That(parsed, Is.False);
    }

    [TestCase("0.0.0")]
    [TestCase("1.2.3")]
    [TestCase("1.2.3-alpha.1+win-x64")]
    public void AgentVersionAcceptsSemanticVersions(string value)
    {
        AgentVersion version = AgentVersion.Parse(value);

        Assert.That(version.Value, Is.EqualTo(value));
    }

    [TestCase("1")]
    [TestCase("1.2")]
    [TestCase("01.2.3")]
    [TestCase("1.2.3-01")]
    [TestCase("1.2.3+")]
    public void AgentVersionRejectsInvalidSemanticVersions(string value)
    {
        Assert.That(AgentVersion.TryParse(value, out _), Is.False);
    }

    [Test]
    public void RunAndOccurrenceIdentifiersRejectEmptyGuids()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => AgentRunId.FromGuid(Guid.Empty), Throws.ArgumentException);
            Assert.That(() => OccurrenceId.FromGuid(Guid.Empty), Throws.ArgumentException);
            Assert.That(AgentRunId.TryParse(Guid.Empty.ToString("D"), out _), Is.False);
            Assert.That(OccurrenceId.TryParse(Guid.Empty.ToString("D"), out _), Is.False);
        });
    }

    [Test]
    public void PersistedEnumsHaveStableExplicitValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That((int)AgentRunStatus.Abandoned, Is.EqualTo(8));
            Assert.That((int)TriggerType.Recovery, Is.EqualTo(6));
            Assert.That((int)ConcurrencyPolicy.AllowParallel, Is.EqualTo(2));
            Assert.That((int)WakePolicy.Required, Is.EqualTo(2));
            Assert.That((int)MisfirePolicy.RunNextScheduled, Is.EqualTo(2));
            Assert.That((int)ScheduleKind.FixedDelay, Is.EqualTo(5));
            Assert.That((int)OccurrenceStatus.Abandoned, Is.EqualTo(11));
        });
    }
}
