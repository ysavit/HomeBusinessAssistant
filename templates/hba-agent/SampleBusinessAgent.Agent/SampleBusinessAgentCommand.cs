using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Diagnostics;
using HomeBusinessAssistant.AgentSdk.Execution;
using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Domain.Agents;
using SampleBusinessAgent.Application;
using SampleBusinessAgent.Infrastructure;

namespace SampleBusinessAgent.Agent;

/// <summary>The independently executable sample agent command boundary.</summary>
public static class SampleBusinessAgentCommand
{
    private static readonly AgentId Id = AgentId.Parse("sample-business-agent");

    /// <summary>Executes one standard SDK invocation.</summary>
    public static async ValueTask<int> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (arguments.Count == 1 && arguments[0] == "protocol-demo")
        {
            return await AgentProtocolDemo.RunAsync(Id.Value, standardOutput, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        AgentExecutionContextParseResult parsed = AgentExecutionContextParser.Parse(arguments);
        if (parsed.Context is not AgentExecutionContext context
            || context.AgentId != Id
            || context.CommandName is not ("run" or "diagnose"))
        {
            await standardError.WriteLineAsync("Usage: SampleBusinessAgent run|diagnose <standard Agent SDK options> | protocol-demo").ConfigureAwait(false);
            return AgentExitCode.InvalidArguments;
        }

        using var writer = new AgentEventWriter(standardOutput, timeProvider, context.RunId, context.ProtocolVersion);
        return await new AgentExecutionSession(writer).ExecuteAsync(
            context.CommandName,
            (events, token) => ExecuteOperationAsync(context, events, token),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["agent"] = Id.Value },
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<AgentExecutionResult> ExecuteOperationAsync(
        AgentExecutionContext context,
        IAgentEventWriter events,
        CancellationToken cancellationToken)
    {
        AgentExecutionInput input;
        try
        {
            input = await AgentExecutionInput.LoadAsync(context.ConfigurationFilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            await events.WriteErrorAsync(new("sample.executionInput.invalid", "The Runner execution input is invalid.", false), cancellationToken).ConfigureAwait(false);
            return new(AgentExitCode.InvalidConfiguration, AgentRunStatus.Failed, "Sample Business Agent rejected its execution input.");
        }

        SampleBusinessAgentConfigurationParseResult parsed = SampleBusinessAgentConfiguration.Parse(input.Configuration);
        if (parsed.Configuration is not SampleBusinessAgentConfiguration configuration)
        {
            await events.WriteErrorAsync(new(
                "sample.configuration.invalid",
                "The sample configuration failed validation.",
                false,
                JsonSerializer.SerializeToElement(new { parsed.ErrorCodes })), cancellationToken).ConfigureAwait(false);
            return new(AgentExitCode.InvalidConfiguration, AgentRunStatus.Failed, "Sample Business Agent configuration is invalid.");
        }

        if (context.CommandName == "diagnose")
        {
            await events.WriteMetricAsync(new("sample.configuration.valid", TextValue: "true"), cancellationToken).ConfigureAwait(false);
            return new(AgentExitCode.Success, AgentRunStatus.Completed, "Sample Business Agent diagnostics completed.");
        }

        await events.WriteHeartbeatAsync(new("inspection", "Inspecting the assigned sample data folder."), cancellationToken).ConfigureAwait(false);
        await events.WriteProgressAsync(new(0, 1, 0, "inspection", "Starting bounded folder report."), cancellationToken).ConfigureAwait(false);
        SampleFolderReport report = await LocalFolderReportService.CreateAsync(
            configuration,
            context.DataDirectory,
            context.ArtifactDirectory,
            cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new("sample.matching-files", report.MatchingFiles, Unit: "files"), cancellationToken).ConfigureAwait(false);
        await events.WriteArtifactAsync(new(
            "report",
            report.ArtifactRelativePath,
            "application/json",
            "Bounded local sample folder report"), cancellationToken).ConfigureAwait(false);
        await events.WriteProgressAsync(new(1, 1, 100, "completed", "Folder report completed."), cancellationToken).ConfigureAwait(false);
        if (configuration.RequireAtLeastOneFile && report.MatchingFiles == 0)
        {
            await events.WriteWarningAsync(new("sample.noMatchingFiles", "No matching sample files were present; operator attention is required."), cancellationToken).ConfigureAwait(false);
            return new(AgentExitCode.PermanentFailure, AgentRunStatus.Failed, "Sample report completed but no matching files were found.");
        }

        return new(
            AgentExitCode.Success,
            AgentRunStatus.Completed,
            $"Sample report captured {report.MatchingFiles} matching file(s).",
            JsonSerializer.SerializeToElement(new { report.MatchingFiles, report.Truncated }));
    }
}
