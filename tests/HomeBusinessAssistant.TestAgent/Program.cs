using System.Diagnostics;
using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Execution;
using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Domain.Agents;

var protocolArguments = args.ToList();
int? directExitCode = null;
int directExitIndex = protocolArguments.IndexOf("--exit-code");
if (directExitIndex >= 0
    && directExitIndex + 1 < protocolArguments.Count
    && int.TryParse(
        protocolArguments[directExitIndex + 1],
        System.Globalization.NumberStyles.Integer,
        System.Globalization.CultureInfo.InvariantCulture,
        out int parsedExitCode))
{
    directExitCode = parsedExitCode;
    protocolArguments.RemoveRange(directExitIndex, 2);
}

AgentExecutionContextParseResult parsed = AgentExecutionContextParser.Parse(protocolArguments);
if (!parsed.IsSuccess)
{
    foreach (AgentCommandLineError error in parsed.Errors)
    {
        await Console.Error.WriteLineAsync($"{error.Code}: {error.Message}").ConfigureAwait(false);
    }

    return AgentExitCode.InvalidArguments;
}

AgentExecutionContext context = parsed.Context!;
if (context.CommandName == "child-hang")
{
    await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
    return AgentExitCode.Success;
}

AgentExecutionInput executionInput = await AgentExecutionInput.LoadAsync(context.ConfigurationFilePath).ConfigureAwait(false);
using var writer = new AgentEventWriter(Console.Out, TimeProvider.System, context.RunId, context.ProtocolVersion);

switch (context.CommandName)
{
    case "malformed-json":
        await writer.WriteStartedAsync(new(context.CommandName)).ConfigureAwait(false);
        for (var index = 0; index < 5; index++)
        {
            await Console.Out.WriteLineAsync("{malformed-json").ConfigureAwait(false);
        }

        await WriteTerminalManuallyAsync(context, 2, AgentRunStatus.Completed, AgentExitCode.Success, "Malformed records emitted.").ConfigureAwait(false);
        return AgentExitCode.Success;
    case "out-of-order-sequence":
        await writer.WriteStartedAsync(new(context.CommandName)).ConfigureAwait(false);
        for (var index = 0; index < 5; index++)
        {
            await Console.Out.WriteLineAsync(AgentEventSerializer.Serialize(new HeartbeatAgentEvent(
                context.ProtocolVersion,
                AgentEventTypes.Heartbeat,
                context.RunId,
                1,
                DateTimeOffset.UtcNow,
                new("duplicate", "duplicate sequence")))).ConfigureAwait(false);
        }

        await WriteTerminalManuallyAsync(context, 2, AgentRunStatus.Completed, AgentExitCode.Success, "Out-of-order records emitted.").ConfigureAwait(false);
        return AgentExitCode.Success;
    case "oversized-line":
        await writer.WriteStartedAsync(new(context.CommandName)).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(new string('x', AgentEventSerializer.MaximumEventSizeBytes + 1)).ConfigureAwait(false);
        await writer.WriteSummaryAsync(new(AgentRunStatus.Completed, "Oversized record was followed by a valid result.")).ConfigureAwait(false);
        await writer.WriteCompletedAsync(new(AgentExitCode.Success)).ConfigureAwait(false);
        return AgentExitCode.Success;
    case "artifact-traversal":
        await writer.WriteStartedAsync(new(context.CommandName)).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(AgentEventSerializer.Serialize(new ArtifactAgentEvent(
            context.ProtocolVersion,
            AgentEventTypes.Artifact,
            context.RunId,
            2,
            DateTimeOffset.UtcNow,
            new("diagnostic", "../outside.txt", "text/plain")))).ConfigureAwait(false);
        await WriteTerminalManuallyAsync(context, 3, AgentRunStatus.Completed, AgentExitCode.Success, "Traversal artifact emitted.").ConfigureAwait(false);
        return AgentExitCode.Success;
    case "artifact-missing":
        await writer.WriteStartedAsync(new(context.CommandName)).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(AgentEventSerializer.Serialize(new ArtifactAgentEvent(
            context.ProtocolVersion,
            AgentEventTypes.Artifact,
            context.RunId,
            2,
            DateTimeOffset.UtcNow,
            new("diagnostic", "missing.txt", "text/plain")))).ConfigureAwait(false);
        await WriteTerminalManuallyAsync(context, 3, AgentRunStatus.Completed, AgentExitCode.Success, "Missing artifact emitted.").ConfigureAwait(false);
        return AgentExitCode.Success;
    case "artifact-tampering":
        string changingPath = Path.Combine(context.ArtifactDirectory, "changing.txt");
        await File.WriteAllTextAsync(changingPath, "initial").ConfigureAwait(false);
        await using (var changingFile = new FileStream(
            changingPath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite))
        {
            await writer.WriteStartedAsync(new(context.CommandName)).ConfigureAwait(false);
            await writer.WriteArtifactAsync(new("diagnostic", "changing.txt", "text/plain")).ConfigureAwait(false);
            await Console.Out.FlushAsync().ConfigureAwait(false);
            for (var index = 0; index < 100; index++)
            {
                changingFile.Position = 0;
                changingFile.WriteByte((byte)('a' + (index % 26)));
                await changingFile.FlushAsync().ConfigureAwait(false);
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        await WriteTerminalManuallyAsync(context, 3, AgentRunStatus.Completed, AgentExitCode.Success, "Changing artifact emitted.").ConfigureAwait(false);
        return AgentExitCode.Success;
    case "no-summary":
        await writer.WriteStartedAsync(new(context.CommandName)).ConfigureAwait(false);
        await writer.WriteCompletedAsync(new(AgentExitCode.Success)).ConfigureAwait(false);
        return AgentExitCode.Success;
    case "unknown-future-event":
        await writer.WriteStartedAsync(new(context.CommandName)).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(AgentEventSerializer.Serialize(new UnknownAgentEvent(
            context.ProtocolVersion,
            "future.fixture",
            context.RunId,
            2,
            DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new { marker = "future" })))).ConfigureAwait(false);
        await WriteTerminalManuallyAsync(context, 3, AgentRunStatus.Completed, AgentExitCode.Success, "Unknown future event emitted.").ConfigureAwait(false);
        return AgentExitCode.Success;
    case "hang":
    case "ignore-cancellation":
        await writer.WriteStartedAsync(new(context.CommandName)).ConfigureAwait(false);
        StartChildHang(context);
        await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
        return AgentExitCode.Success;
    default:
        var session = new AgentExecutionSession(writer);
        return await session.ExecuteAsync(
            context.CommandName,
            (events, cancellationToken) => ExecuteScenarioAsync(
                context,
                executionInput.Configuration,
                directExitCode,
                events,
                cancellationToken)).ConfigureAwait(false);
}

static async ValueTask<AgentExecutionResult> ExecuteScenarioAsync(
    AgentExecutionContext context,
    JsonElement configuration,
    int? directExitCode,
    IAgentEventWriter events,
    CancellationToken cancellationToken)
{
    string marker = configuration.TryGetProperty("marker", out JsonElement markerValue)
        ? markerValue.GetString() ?? "none"
        : "none";
    switch (context.CommandName)
    {
        case "success":
            await events.WriteMetricAsync(new("configuration-marker", TextValue: marker), cancellationToken).ConfigureAwait(false);
            return new(AgentExitCode.Success, AgentRunStatus.Completed, $"Success using configuration marker {marker}.");
        case "fail":
            int exitCode = directExitCode ?? (configuration.TryGetProperty("exitCode", out JsonElement exitCodeValue)
                ? exitCodeValue.GetInt32()
                : AgentExitCode.PermanentFailure);
            await events.WriteErrorAsync(new("test.failure", "The deterministic failure was requested.", exitCode == AgentExitCode.TransientFailure), cancellationToken).ConfigureAwait(false);
            return new(exitCode, AgentRunStatus.Failed, $"Requested failure {exitCode}.");
        case "emit-all-events":
            await events.WriteHeartbeatAsync(new("fixture", "alive"), cancellationToken).ConfigureAwait(false);
            await events.WriteProgressAsync(new(1, 2, 50, "fixture", "halfway"), cancellationToken).ConfigureAwait(false);
            await events.WriteMetricAsync(new("fixture.metric", 42, Unit: "items"), cancellationToken).ConfigureAwait(false);
            await events.WriteCheckpointAsync(new("fixture", JsonSerializer.SerializeToElement(new { marker })), cancellationToken).ConfigureAwait(false);
            await events.WriteWarningAsync(new("fixture.warning", "A deterministic warning."), cancellationToken).ConfigureAwait(false);
            await events.WriteErrorAsync(new("fixture.nonfatal", "A deterministic non-fatal error event.", false), cancellationToken).ConfigureAwait(false);
            return new(AgentExitCode.Success, AgentRunStatus.Completed, "All non-artifact event types emitted.");
        case "delayed-heartbeats":
            for (var index = 0; index < 3; index++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                await events.WriteHeartbeatAsync(new("delay", $"heartbeat-{index + 1}"), cancellationToken).ConfigureAwait(false);
            }

            return new(AgentExitCode.Success, AgentRunStatus.Completed, "Delayed heartbeats completed.");
        case "artifact-success":
            string artifactPath = Path.Combine(context.ArtifactDirectory, "fixture.txt");
            await File.WriteAllTextAsync(artifactPath, $"artifact-{marker}", cancellationToken).ConfigureAwait(false);
            await events.WriteArtifactAsync(new(
                "result",
                "fixture.txt",
                "text/plain",
                "Fixture artifact",
                DateTimeOffset.UtcNow.AddDays(7)), cancellationToken).ConfigureAwait(false);
            return new(AgentExitCode.Success, AgentRunStatus.Completed, "Artifact emitted.");
        case "stderr-burst":
            for (var index = 0; index < 150; index++)
            {
                await Console.Error.WriteLineAsync($"stderr {index} Authorization: Bearer fixture-secret-token").ConfigureAwait(false);
            }

            return new(AgentExitCode.Success, AgentRunStatus.Completed, "Stderr burst emitted.");
        default:
            return new(AgentExitCode.InvalidArguments, AgentRunStatus.Failed, "Unknown test scenario.");
    }
}

static async Task WriteTerminalManuallyAsync(
    AgentExecutionContext context,
    long firstSequence,
    AgentRunStatus status,
    int exitCode,
    string summary)
{
    await Console.Out.WriteLineAsync(AgentEventSerializer.Serialize(new SummaryAgentEvent(
        context.ProtocolVersion,
        AgentEventTypes.Summary,
        context.RunId,
        firstSequence,
        DateTimeOffset.UtcNow,
        new(status, summary)))).ConfigureAwait(false);
    await Console.Out.WriteLineAsync(AgentEventSerializer.Serialize(new CompletedAgentEvent(
        context.ProtocolVersion,
        AgentEventTypes.Completed,
        context.RunId,
        firstSequence + 1,
        DateTimeOffset.UtcNow,
        new(exitCode)))).ConfigureAwait(false);
    await Console.Out.FlushAsync().ConfigureAwait(false);
}

static void StartChildHang(AgentExecutionContext context)
{
    string executable = Environment.ProcessPath
        ?? throw new InvalidOperationException("The test agent process path is unavailable.");
    var start = new ProcessStartInfo(executable)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    start.ArgumentList.Add("child-hang");
    AddOption(start, "--run-id", context.RunId.ToString());
    AddOption(start, "--occurrence-id", context.OccurrenceId.ToString());
    AddOption(start, "--agent-id", context.AgentId.Value);
    AddOption(start, "--config-file", context.ConfigurationFilePath);
    AddOption(start, "--data-directory", context.DataDirectory);
    AddOption(start, "--artifact-directory", context.ArtifactDirectory);
    AddOption(start, "--protocol-version", context.ProtocolVersion.ToString());
    _ = Process.Start(start) ?? throw new InvalidOperationException("The child fixture could not start.");
}

static void AddOption(ProcessStartInfo start, string name, string value)
{
    start.ArgumentList.Add(name);
    start.ArgumentList.Add(value);
}
