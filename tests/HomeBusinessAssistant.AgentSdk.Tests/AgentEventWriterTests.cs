using System.Text;
using HomeBusinessAssistant.AgentSdk.Execution;
using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.AgentSdk.Tests;

internal sealed class AgentEventWriterTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 8, 29, 18, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ConcurrentWritesRemainOneJsonObjectPerLineWithMonotonicSequences()
    {
        using StringWriter output = new();
        using var writer = new AgentEventWriter(output, new TestTimeProvider(Timestamp), AgentRunId.New());

        Task[] writes = Enumerable.Range(0, 50)
            .Select(index => writer.WriteHeartbeatAsync(new HeartbeatPayload("work", $"Heartbeat {index}.")).AsTask())
            .ToArray();
        await Task.WhenAll(writes).ConfigureAwait(false);

        string[] lines = NonEmptyLines(output.ToString());
        AgentEvent[] events = lines.Select(line => AgentEventSerializer.Deserialize(line).Event!).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.EqualTo(50));
            Assert.That(events, Has.All.TypeOf<HeartbeatAgentEvent>());
            Assert.That(events.Select(agentEvent => agentEvent.Sequence), Is.EqualTo(Enumerable.Range(1, 50)));
            Assert.That(events.Select(agentEvent => agentEvent.TimestampUtc), Has.All.EqualTo(Timestamp));
            Assert.That(lines.All(line => line.StartsWith('{')), Is.True);
        });
    }

    [Test]
    public async Task TerminalEventsFlushAndPostCompletionWriteReturnsStructuredError()
    {
        using TrackingTextWriter output = new();
        using var writer = new AgentEventWriter(output, new TestTimeProvider(Timestamp), AgentRunId.New());

        await writer.WriteSummaryAsync(new SummaryPayload(AgentRunStatus.Completed, "Done.")).ConfigureAwait(false);
        await writer.WriteCompletedAsync(new CompletedPayload(AgentExitCode.Success)).ConfigureAwait(false);
        AgentEventWriteException exception = Assert.ThrowsAsync<AgentEventWriteException>(async () =>
            await writer.WriteHeartbeatAsync(new HeartbeatPayload()).ConfigureAwait(false))!;

        Assert.Multiple(() =>
        {
            Assert.That(output.FlushCount, Is.EqualTo(2));
            Assert.That(exception.Errors.Single().Code, Is.EqualTo("writer.streamCompleted"));
            Assert.That(NonEmptyLines(output.ToString()), Has.Length.EqualTo(2));
        });
    }

    [Test]
    public async Task SessionMapsCancellationAndStillWritesCompletedLifecycle()
    {
        using StringWriter output = new();
        using var writer = new AgentEventWriter(output, new TestTimeProvider(Timestamp), AgentRunId.New());
        var session = new AgentExecutionSession(writer);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        int exitCode = await session.ExecuteAsync(
            "run",
            (_, token) => ValueTask.FromCanceled<AgentExecutionResult>(token),
            cancellationToken: cancellation.Token).ConfigureAwait(false);

        AgentEvent[] events = NonEmptyLines(output.ToString())
            .Select(line => AgentEventSerializer.Deserialize(line).Event!)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(AgentExitCode.Cancelled));
            Assert.That(events.Select(agentEvent => agentEvent.Type),
                Is.EqualTo(new[] { AgentEventTypes.Started, AgentEventTypes.Summary, AgentEventTypes.Completed }));
            Assert.That(((SummaryAgentEvent)events[1]).Payload.Status, Is.EqualTo(AgentRunStatus.Cancelled));
            Assert.That(((CompletedAgentEvent)events[2]).Payload.ExitCode, Is.EqualTo(AgentExitCode.Cancelled));
        });
    }

    private static string[] NonEmptyLines(string value) => value
        .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);

    private sealed class TrackingTextWriter : StringWriter
    {
        public int FlushCount { get; private set; }

        public override Encoding Encoding => Encoding.UTF8;

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushCount++;
            return Task.CompletedTask;
        }
    }
}
