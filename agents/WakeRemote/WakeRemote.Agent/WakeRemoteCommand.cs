using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Diagnostics;
using HomeBusinessAssistant.AgentSdk.Execution;
using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Application.Power;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Windows.Power;
using HomeBusinessAssistant.Windows.Processes;
using WakeRemote.Application;
using WakeRemote.Infrastructure;

namespace WakeRemote.Agent;

/// <summary>Injectable command dependencies used by production composition and deterministic tests.</summary>
public sealed record WakeRemoteCommandServices(
    IPowerRequestService PowerRequests,
    INetworkReadinessProbe NetworkProbe,
    IRemoteAccessProviderProbe ProviderProbe,
    IWakeRemoteDelay Delay,
    IPowerDiagnosticsService PowerDiagnostics);

/// <summary>Provides the Wake &amp; Remote process command boundary.</summary>
public static class WakeRemoteCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Executes the requested command using production Windows adapters.</summary>
    public static async ValueTask<int> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var processExecutor = new BoundedProcessExecutor();
        await using var powerRequests = new WindowsPowerRequestService(
            new WindowsExecutionStateNativeApi(),
            timeProvider);
        var services = new WakeRemoteCommandServices(
            powerRequests,
            new LocalNetworkReadinessProbe(),
            WindowsRemoteAccessProviderProbe.CreateDefault(processExecutor),
            new TimeProviderWakeRemoteDelay(timeProvider),
            PowercfgDiagnosticsService.CreateDefault(processExecutor, timeProvider));
        return await ExecuteAsync(
            arguments,
            standardOutput,
            standardError,
            timeProvider,
            services,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Executes the requested command with explicitly supplied adapters.</summary>
    public static async ValueTask<int> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        TimeProvider timeProvider,
        WakeRemoteCommandServices services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(services);

        if (arguments.Count == 1 && string.Equals(arguments[0], "protocol-demo", StringComparison.Ordinal))
        {
            return await AgentProtocolDemo.RunAsync(
                "wake-remote",
                standardOutput,
                timeProvider,
                cancellationToken).ConfigureAwait(false);
        }

        if (arguments.Count == 1 && arguments[0] is "version" or "--version")
        {
            ProductBuildInfo build = ProductBuildInfo.Load(typeof(WakeRemoteCommand).Assembly);
            await standardOutput.WriteLineAsync(JsonSerializer.Serialize(build, JsonOptions)).ConfigureAwait(false);
            return AgentExitCode.Success;
        }

        AgentExecutionContextParseResult parsed = AgentExecutionContextParser.Parse(arguments);
        if (parsed.Context is not AgentExecutionContext context
            || context.AgentId != WakeRemoteDefaults.AgentId
            || context.CommandName is not ("run" or "diagnose" or "check-remote" or "wake-test"))
        {
            await standardError.WriteLineAsync(
                "Usage: WakeRemote run|diagnose|check-remote|wake-test <standard Agent SDK options> | protocol-demo").ConfigureAwait(false);
            return AgentExitCode.InvalidArguments;
        }

        using var writer = new AgentEventWriter(
            standardOutput,
            timeProvider,
            context.RunId,
            context.ProtocolVersion);
        var session = new AgentExecutionSession(writer);
        return await session.ExecuteAsync(
            context.CommandName,
            (events, token) => ExecuteOperationAsync(context, events, services, timeProvider, token),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["agent"] = "wake-remote",
                ["command"] = context.CommandName,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<AgentExecutionResult> ExecuteOperationAsync(
        AgentExecutionContext context,
        IAgentEventWriter events,
        WakeRemoteCommandServices services,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (context.CommandName == "wake-test")
        {
            return await ExecuteWakeTestAsync(context, events, timeProvider, cancellationToken).ConfigureAwait(false);
        }

        AgentExecutionInput input;
        try
        {
            input = await AgentExecutionInput.LoadAsync(
                context.ConfigurationFilePath,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            await events.WriteErrorAsync(new(
                "wakeRemote.executionInput.invalid",
                "The private execution input is missing, invalid, or unsupported.",
                Transient: false), cancellationToken).ConfigureAwait(false);
            return new(
                AgentExitCode.InvalidConfiguration,
                AgentRunStatus.Failed,
                "Wake Remote rejected the execution input.");
        }

        (WakeRemoteConfiguration? configuration, IReadOnlyList<WakeRemoteValidationError> errors) =
            WakeRemoteInput.ParseConfiguration(input.Configuration);
        if (configuration is null)
        {
            await WriteValidationErrorAsync(events, errors, cancellationToken).ConfigureAwait(false);
            return new(
                AgentExitCode.InvalidConfiguration,
                AgentRunStatus.Failed,
                "Wake Remote configuration validation failed.",
                JsonSerializer.SerializeToElement(new
                {
                    result = "InvalidConfiguration",
                    reasonCodes = errors.Select(error => error.Code).ToArray(),
                }, JsonOptions));
        }

        return context.CommandName switch
        {
            "run" => await ExecuteRunAsync(context, input, configuration, events, services, timeProvider, cancellationToken).ConfigureAwait(false),
            "diagnose" => await ExecuteDiagnoseAsync(context, input, configuration, events, services, timeProvider, cancellationToken).ConfigureAwait(false),
            "check-remote" => await ExecuteRemoteCheckAsync(configuration, events, services, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException("The command dispatch is invalid."),
        };
    }

    private static async ValueTask<AgentExecutionResult> ExecuteRunAsync(
        AgentExecutionContext context,
        AgentExecutionInput input,
        WakeRemoteConfiguration configuration,
        IAgentEventWriter events,
        WakeRemoteCommandServices services,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        (WakeRemoteOccurrenceArguments? occurrence, IReadOnlyList<WakeRemoteValidationError> errors) =
            WakeRemoteInput.ParseOccurrence(input.OccurrenceArguments, configuration, timeProvider.GetUtcNow());
        if (occurrence is null)
        {
            await WriteValidationErrorAsync(events, errors, cancellationToken).ConfigureAwait(false);
            return new(
                AgentExitCode.InvalidArguments,
                AgentRunStatus.Failed,
                "Wake Remote occurrence validation failed.",
                JsonSerializer.SerializeToElement(new
                {
                    result = "InvalidOccurrence",
                    reasonCodes = errors.Select(error => error.Code).ToArray(),
                }, JsonOptions));
        }

        var sink = new AgentSdkWakeRemoteEventSink(events);
        var workflow = new WakeRemoteWorkflow(
            services.PowerRequests,
            services.NetworkProbe,
            services.ProviderProbe,
            services.Delay,
            timeProvider);
        WakeRemoteWorkflowResult result = await workflow.RunAsync(
            configuration,
            occurrence,
            sink,
            cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new(
            "power_handle_released",
            TextValue: "true"), CancellationToken.None).ConfigureAwait(false);
        await events.WriteMetricAsync(new(
            "display_required",
            TextValue: configuration.KeepDisplayOn ? "true" : "false"), CancellationToken.None).ConfigureAwait(false);
        (int exitCode, AgentRunStatus status) = result.Kind switch
        {
            WakeRemoteResultKind.Completed => (AgentExitCode.Success, AgentRunStatus.Completed),
            WakeRemoteResultKind.NetworkTimeout => (AgentExitCode.TransientFailure, AgentRunStatus.Failed),
            WakeRemoteResultKind.WindowExpired => (AgentExitCode.PermanentFailure, AgentRunStatus.Failed),
            WakeRemoteResultKind.RemoteProviderUnavailable => (AgentExitCode.PermanentFailure, AgentRunStatus.Failed),
            _ => (AgentExitCode.UnhandledFailure, AgentRunStatus.Failed),
        };
        string text = result.Kind == WakeRemoteResultKind.Completed
            ? $"Wake Remote kept the machine available through window {context.OccurrenceId}."
            : $"Wake Remote ended with {result.Kind}.";
        return new(exitCode, status, text, JsonSerializer.SerializeToElement(result.Summary, JsonOptions));
    }

    private static async ValueTask<AgentExecutionResult> ExecuteRemoteCheckAsync(
        WakeRemoteConfiguration configuration,
        IAgentEventWriter events,
        WakeRemoteCommandServices services,
        CancellationToken cancellationToken)
    {
        RemoteProviderProbeResult result = await services.ProviderProbe.ProbeAsync(
            configuration.RemoteProvider,
            cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new(
            "remote_provider",
            TextValue: result.Provider.ToString()), cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new(
            "remote.ready",
            TextValue: result.Readiness == RemoteProviderReadiness.Ready ? "true" : "false"), cancellationToken).ConfigureAwait(false);
        bool ready = result.Readiness == RemoteProviderReadiness.Ready;
        return new(
            ready ? AgentExitCode.Success : AgentExitCode.PermanentFailure,
            ready ? AgentRunStatus.Completed : AgentRunStatus.Failed,
            ready ? "The configured remote provider is locally ready." : "The configured remote provider is not locally ready.",
            JsonSerializer.SerializeToElement(new
            {
                remoteProvider = result.Provider.ToString(),
                readiness = result.Readiness.ToString(),
                result.ReasonCode,
                diagnosticProvider = result.Provider == RemoteProviderKind.DiagnosticFake,
            }, JsonOptions));
    }

    private static async ValueTask<AgentExecutionResult> ExecuteDiagnoseAsync(
        AgentExecutionContext context,
        AgentExecutionInput input,
        WakeRemoteConfiguration configuration,
        IAgentEventWriter events,
        WakeRemoteCommandServices services,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(configuration.TimeZoneId);
        NetworkReadinessAttempt network = await services.NetworkProbe.ProbeAsync(configuration, cancellationToken).ConfigureAwait(false);
        RemoteProviderProbeResult remote = await services.ProviderProbe.ProbeAsync(configuration.RemoteProvider, cancellationToken).ConfigureAwait(false);
        PowerDiagnosticsSnapshot? power = null;
        string powerDiagnosticsStatus = "available";
        try
        {
            power = await services.PowerDiagnostics.CaptureAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            powerDiagnosticsStatus = "unavailable";
            await events.WriteWarningAsync(new(
                "wakeRemote.diagnostics.powerUnavailable",
                "Read-only Windows power diagnostics are unavailable."), cancellationToken).ConfigureAwait(false);
        }
        DateTimeOffset? nextWindow = null;
        try
        {
            nextWindow = input.OccurrenceArguments.TryGetProperty("scheduledWakeAtUtc", out JsonElement scheduled)
                && scheduled.TryGetDateTimeOffset(out DateTimeOffset value)
                ? value.ToUniversalTime()
                : null;
        }
        catch (InvalidOperationException)
        {
            // Diagnostics tolerate absent optional occurrence context.
        }

        var artifact = new
        {
            schemaVersion = "1.0",
            currentUtc = nowUtc,
            currentLocal = TimeZoneInfo.ConvertTime(nowUtc, zone),
            configuredTimeZone = configuration.TimeZoneId,
            networkReady = network.Ready,
            networkReasonCode = network.ReasonCode,
            remoteProvider = remote.Provider.ToString(),
            remoteReadiness = remote.Readiness.ToString(),
            remoteReasonCode = remote.ReasonCode,
            diagnosticProvider = remote.Provider == RemoteProviderKind.DiagnosticFake,
            power = new
            {
                status = powerDiagnosticsStatus,
                sleepStatesQuerySucceeded = power?.SleepStatesQuerySucceeded,
                wakeTimersQuerySucceeded = power?.WakeTimersQuerySucceeded,
                hasReportedWakeTimer = power?.HasReportedWakeTimer,
                hasArmedWakeDevice = power?.HasArmedWakeDevice,
            },
            nextConfiguredAvailabilityWindowAtUtc = nextWindow,
        };
        const string fileName = "wake-remote-diagnostics.json";
        Directory.CreateDirectory(context.ArtifactDirectory);
        string path = GetContainedArtifactPath(context.ArtifactDirectory, fileName);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(artifact, JsonOptions), cancellationToken).ConfigureAwait(false);
        await events.WriteArtifactAsync(new(
            "wake-remote-diagnostics",
            fileName,
            "application/json",
            "Read-only local Wake Remote readiness diagnostics."), cancellationToken).ConfigureAwait(false);
        return new(
            AgentExitCode.Success,
            AgentRunStatus.Completed,
            "Wake Remote captured read-only local diagnostics.",
            JsonSerializer.SerializeToElement(artifact, JsonOptions));
    }

    private static async ValueTask<AgentExecutionResult> ExecuteWakeTestAsync(
        AgentExecutionContext context,
        IAgentEventWriter events,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DateTimeOffset observedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
        const string fileName = "wake-test-result.json";
        Directory.CreateDirectory(context.ArtifactDirectory);
        string resultPath = GetContainedArtifactPath(context.ArtifactDirectory, fileName);
        string json = JsonSerializer.Serialize(new
        {
            version = "1.0",
            context.RunId,
            context.OccurrenceId,
            observedAtUtc,
            machineName = Environment.MachineName,
            result = "observed",
        }, JsonOptions);
        await File.WriteAllTextAsync(resultPath, json, cancellationToken).ConfigureAwait(false);
        await events.WriteMetricAsync(new(
            "wake_test_observed_at_utc",
            TextValue: observedAtUtc.ToString("O")), cancellationToken).ConfigureAwait(false);
        await events.WriteArtifactAsync(new(
            "wake-test-result",
            fileName,
            "application/json",
            "Timestamp recorded by the harmless wake diagnostic."), cancellationToken).ConfigureAwait(false);
        return new(
            AgentExitCode.Success,
            AgentRunStatus.Completed,
            "The wake diagnostic recorded the Runner-observed start timestamp.",
            JsonSerializer.SerializeToElement(new { observedAtUtc, result = "observed" }, JsonOptions));
    }

    private static async ValueTask WriteValidationErrorAsync(
        IAgentEventWriter events,
        IReadOnlyList<WakeRemoteValidationError> errors,
        CancellationToken cancellationToken)
    {
        await events.WriteErrorAsync(new(
            errors.Count > 0 ? errors[0].Code : "wakeRemote.validation.failed",
            "Wake Remote input validation failed.",
            Transient: false,
            JsonSerializer.SerializeToElement(new
            {
                errors = errors.Take(20).Select(error => new { error.Code, error.Path }).ToArray(),
            }, JsonOptions)), cancellationToken).ConfigureAwait(false);
    }

    private static string GetContainedArtifactPath(string artifactDirectory, string fileName)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(artifactDirectory));
        string path = Path.GetFullPath(Path.Combine(root, fileName));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Wake Remote artifact path is invalid.");
        }

        return path;
    }

    private sealed class AgentSdkWakeRemoteEventSink(IAgentEventWriter events) : IWakeRemoteEventSink
    {
        public ValueTask HeartbeatAsync(string phase, string message, CancellationToken cancellationToken = default) =>
            events.WriteHeartbeatAsync(new(phase, message), cancellationToken);

        public ValueTask ProgressAsync(string phase, string message, CancellationToken cancellationToken = default) =>
            events.WriteProgressAsync(new(Current: 0, Total: 1, Phase: phase, Message: message), cancellationToken);

        public ValueTask NumericMetricAsync(string name, double value, string? unit = null, CancellationToken cancellationToken = default) =>
            events.WriteMetricAsync(new(name, NumericValue: value, Unit: unit), cancellationToken);

        public ValueTask TextMetricAsync(string name, string value, CancellationToken cancellationToken = default) =>
            events.WriteMetricAsync(new(name, TextValue: value), cancellationToken);

        public ValueTask WarningAsync(string code, string message, CancellationToken cancellationToken = default) =>
            events.WriteWarningAsync(new(code, message), cancellationToken);
    }
}
