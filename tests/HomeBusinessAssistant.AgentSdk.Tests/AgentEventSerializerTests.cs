using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Execution;
using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.AgentSdk.Tests;

internal sealed class AgentEventSerializerTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 29, 18, 0, 0, TimeSpan.Zero);
    private static readonly AgentRunId RunId = AgentRunId.FromGuid(Guid.Parse("7a209a0c-c3b5-4be6-8d8a-1b85b948699f"));

    public static IEnumerable<AgentEvent> KnownEvents()
    {
        yield return new StartedAgentEvent(
            AgentProtocolVersion.Current,
            AgentEventTypes.Started,
            RunId,
            1,
            Timestamp,
            new StartedPayload("run", new Dictionary<string, string> { ["agent-id"] = "founder-scout" }));
        yield return new HeartbeatAgentEvent(
            AgentProtocolVersion.Current,
            AgentEventTypes.Heartbeat,
            RunId,
            2,
            Timestamp,
            new HeartbeatPayload("capture", "Still running."));
        yield return new ProgressAgentEvent(
            AgentProtocolVersion.Current,
            AgentEventTypes.Progress,
            RunId,
            3,
            Timestamp,
            new ProgressPayload(5, 20, 25, "analysis", "Analyzing candidates."));
        yield return new MetricAgentEvent(
            AgentProtocolVersion.Current,
            AgentEventTypes.Metric,
            RunId,
            4,
            Timestamp,
            new MetricPayload("profiles.analyzed", NumericValue: 5, Unit: "profiles"));
        yield return new CheckpointAgentEvent(
            AgentProtocolVersion.Current,
            AgentEventTypes.Checkpoint,
            RunId,
            5,
            Timestamp,
            new CheckpointPayload("capture-page", JsonElementFrom("{\"page\":2}")));
        yield return new ArtifactAgentEvent(
            AgentProtocolVersion.Current,
            AgentEventTypes.Artifact,
            RunId,
            6,
            Timestamp,
            new ArtifactPayload("report", "reports/top-candidates.html", "text/html", "Candidate report."));
        yield return new WarningAgentEvent(
            AgentProtocolVersion.Current,
            AgentEventTypes.Warning,
            RunId,
            7,
            Timestamp,
            new WarningPayload("capture.partial", "One profile was incomplete."));
        yield return new ErrorAgentEvent(
            AgentProtocolVersion.Current,
            AgentEventTypes.Error,
            RunId,
            8,
            Timestamp,
            new ErrorPayload("network.timeout", "The request timed out.", Transient: true));
        yield return new SummaryAgentEvent(
            AgentProtocolVersion.Current,
            AgentEventTypes.Summary,
            RunId,
            9,
            Timestamp,
            new SummaryPayload(AgentRunStatus.Completed, "Five profiles analyzed.", JsonElementFrom("{\"analyzed\":5}")));
        yield return new CompletedAgentEvent(
            AgentProtocolVersion.Current,
            AgentEventTypes.Completed,
            RunId,
            10,
            Timestamp,
            new CompletedPayload(AgentExitCode.Success));
    }

    [TestCaseSource(nameof(KnownEvents))]
    public void EveryKnownEventRoundTripsAndValidates(AgentEvent original)
    {
        string json = AgentEventSerializer.Serialize(original);

        AgentEventReadResult read = AgentEventSerializer.Deserialize(json);
        var validation = AgentEventValidator.Validate(read.Event!, new TestTimeProvider(Timestamp));

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.StartWith("{").And.EndWith("}"));
            Assert.That(json, Does.Not.Contain('\n').And.Not.Contain('\r'));
            Assert.That(read.IsSuccess, Is.True);
            Assert.That(read.Event?.GetType(), Is.EqualTo(original.GetType()));
            Assert.That(read.Event?.Type, Is.EqualTo(original.Type));
            Assert.That(read.Event?.RunId, Is.EqualTo(original.RunId));
            Assert.That(read.Event?.Sequence, Is.EqualTo(original.Sequence));
            Assert.That(validation.IsValid, Is.True, string.Join(" | ", validation.Errors));
        });
    }

    [Test]
    public void UnknownEventPreservesRawPayload()
    {
        string json = "{\"protocolVersion\":\"1.0\",\"type\":\"future-event\",\"runId\":\""
            + RunId
            + "\",\"sequence\":11,\"timestampUtc\":\"2026-08-29T18:00:00Z\",\"payload\":{\"newValue\":42,\"enabled\":true}}";

        AgentEventReadResult read = AgentEventSerializer.Deserialize(json);
        var unknown = read.Event as UnknownAgentEvent;
        string serialized = AgentEventSerializer.Serialize(unknown!);
        AgentEventReadResult reread = AgentEventSerializer.Deserialize(serialized);

        Assert.Multiple(() =>
        {
            Assert.That(read.IsSuccess, Is.True);
            Assert.That(unknown, Is.Not.Null);
            Assert.That(unknown!.Payload.GetProperty("newValue").GetInt32(), Is.EqualTo(42));
            Assert.That(reread.Event, Is.TypeOf<UnknownAgentEvent>());
            Assert.That(((UnknownAgentEvent)reread.Event!).Payload.GetProperty("enabled").GetBoolean(), Is.True);
        });
    }

    [Test]
    public void DeserializeRejectsOversizedAndMultilineRecords()
    {
        string oversized = new('x', AgentEventSerializer.MaximumEventSizeBytes + 1);

        AgentEventReadResult oversizedResult = AgentEventSerializer.Deserialize(oversized);
        AgentEventReadResult multilineResult = AgentEventSerializer.Deserialize("{}\n{}");

        Assert.Multiple(() =>
        {
            Assert.That(oversizedResult.Errors.Single().Code, Is.EqualTo("protocol.eventTooLarge"));
            Assert.That(multilineResult.Errors.Single().Code, Is.EqualTo("protocol.multilineEvent"));
        });
    }

    [Test]
    public void ValidatorRejectsInvalidEnvelopeMetricAndArtifact()
    {
        var clock = new TestTimeProvider(Timestamp);
        AgentEvent invalidMetric = new MetricAgentEvent(
            new AgentProtocolVersion(2, 0),
            AgentEventTypes.Metric,
            default,
            0,
            Timestamp.AddMinutes(6),
            new MetricPayload("bad metric", NumericValue: double.PositiveInfinity, TextValue: "both"));
        AgentEvent invalidArtifact = new ArtifactAgentEvent(
            AgentProtocolVersion.Current,
            AgentEventTypes.Artifact,
            RunId,
            1,
            Timestamp,
            new ArtifactPayload("report", "../outside.txt", "invalid"));

        var metricResult = AgentEventValidator.Validate(invalidMetric, clock);
        var artifactResult = AgentEventValidator.Validate(invalidArtifact, clock);

        Assert.Multiple(() =>
        {
            var metricCodes = metricResult.Errors.Select(error => error.Code).ToArray();
            var artifactCodes = artifactResult.Errors.Select(error => error.Code).ToArray();
            Assert.That(metricCodes, Does.Contain("protocol.unsupportedMajor"));
            Assert.That(metricCodes, Does.Contain("protocol.invalidRunId"));
            Assert.That(metricCodes, Does.Contain("protocol.invalidSequence"));
            Assert.That(metricCodes, Does.Contain("protocol.futureTimestamp"));
            Assert.That(metricCodes, Does.Contain("protocol.invalidMetricName"));
            Assert.That(metricCodes, Does.Contain("protocol.invalidMetricValue"));
            Assert.That(artifactCodes, Does.Contain("protocol.invalidArtifactPath"));
            Assert.That(artifactCodes, Does.Contain("protocol.invalidContentType"));
        });
    }

    private static JsonElement JsonElementFrom(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
