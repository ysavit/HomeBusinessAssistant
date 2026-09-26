using HomeBusinessAssistant.AgentSdk.Contracts;
using HomeBusinessAssistant.AgentSdk.Protocol;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.AgentSdk.Execution;

/// <summary>Parses the dependency-free command-line contract used to launch agents.</summary>
public static class AgentExecutionContextParser
{
    private const string RunIdOption = "--run-id";
    private const string OccurrenceIdOption = "--occurrence-id";
    private const string AgentIdOption = "--agent-id";
    private const string ConfigurationFileOption = "--config-file";
    private const string DataDirectoryOption = "--data-directory";
    private const string ArtifactDirectoryOption = "--artifact-directory";
    private const string ProtocolVersionOption = "--protocol-version";

    private static readonly string[] RequiredOptions =
    [
        RunIdOption,
        OccurrenceIdOption,
        AgentIdOption,
        ConfigurationFileOption,
        DataDirectoryOption,
        ArtifactDirectoryOption,
        ProtocolVersionOption,
    ];

    /// <summary>Parses a command followed by required `--name value` options.</summary>
    public static AgentExecutionContextParseResult Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var errors = new List<AgentCommandLineError>();

        if (arguments.Count == 0 || arguments[0].StartsWith("--", StringComparison.Ordinal))
        {
            errors.Add(new AgentCommandLineError("arguments.missingCommand", "command", "A command name is required."));
            return new AgentExecutionContextParseResult(null, errors);
        }

        var commandName = arguments[0];
        if (!ContractNameRules.IsValid(commandName))
        {
            errors.Add(new AgentCommandLineError("arguments.invalidCommand", "command", "The command name is invalid."));
        }

        var values = ParseOptions(arguments, errors);
        foreach (var option in RequiredOptions)
        {
            if (!values.ContainsKey(option))
            {
                errors.Add(new AgentCommandLineError("arguments.missingOption", option, $"The {option} option is required."));
            }
        }

        if (errors.Count > 0)
        {
            return new AgentExecutionContextParseResult(null, errors);
        }

        var runId = ParseRunId(values[RunIdOption], errors);
        var occurrenceId = ParseOccurrenceId(values[OccurrenceIdOption], errors);
        var agentId = ParseAgentId(values[AgentIdOption], errors);
        var protocolVersion = ParseProtocolVersion(values[ProtocolVersionOption], errors);
        var configurationFile = NormalizePath(values[ConfigurationFileOption], ConfigurationFileOption, errors);
        var dataDirectory = NormalizePath(values[DataDirectoryOption], DataDirectoryOption, errors);
        var artifactDirectory = NormalizePath(values[ArtifactDirectoryOption], ArtifactDirectoryOption, errors);

        if (errors.Count > 0)
        {
            return new AgentExecutionContextParseResult(null, errors);
        }

        return new AgentExecutionContextParseResult(
            new AgentExecutionContext(
                runId!.Value,
                occurrenceId!.Value,
                agentId!.Value,
                commandName,
                configurationFile!,
                dataDirectory!,
                artifactDirectory!,
                protocolVersion!.Value),
            Array.Empty<AgentCommandLineError>());
    }

    private static Dictionary<string, string> ParseOptions(
        IReadOnlyList<string> arguments,
        List<AgentCommandLineError> errors)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < arguments.Count; index++)
        {
            var option = arguments[index];
            if (!RequiredOptions.Contains(option, StringComparer.Ordinal))
            {
                errors.Add(new AgentCommandLineError("arguments.unknownOption", "option", "An unknown command-line option was supplied."));
                if (index + 1 < arguments.Count && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    index++;
                }

                continue;
            }

            if (!values.TryAdd(option, string.Empty))
            {
                errors.Add(new AgentCommandLineError("arguments.duplicateOption", option, $"The {option} option may be supplied only once."));
            }

            if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                errors.Add(new AgentCommandLineError("arguments.missingValue", option, $"The {option} option requires a value."));
                continue;
            }

            values[option] = arguments[++index];
        }

        return values;
    }

    private static AgentRunId? ParseRunId(string value, List<AgentCommandLineError> errors)
    {
        if (AgentRunId.TryParse(value, out var parsed))
        {
            return parsed;
        }

        errors.Add(new AgentCommandLineError("arguments.invalidRunId", RunIdOption, "The run identifier is invalid."));
        return null;
    }

    private static OccurrenceId? ParseOccurrenceId(string value, List<AgentCommandLineError> errors)
    {
        if (OccurrenceId.TryParse(value, out var parsed))
        {
            return parsed;
        }

        errors.Add(new AgentCommandLineError("arguments.invalidOccurrenceId", OccurrenceIdOption, "The occurrence identifier is invalid."));
        return null;
    }

    private static AgentId? ParseAgentId(string value, List<AgentCommandLineError> errors)
    {
        if (AgentId.TryParse(value, out var parsed))
        {
            return parsed;
        }

        errors.Add(new AgentCommandLineError("arguments.invalidAgentId", AgentIdOption, "The agent identifier is invalid."));
        return null;
    }

    private static AgentProtocolVersion? ParseProtocolVersion(
        string value,
        List<AgentCommandLineError> errors)
    {
        if (!AgentProtocolVersion.TryParse(value, out var parsed))
        {
            errors.Add(new AgentCommandLineError("arguments.invalidProtocolVersion", ProtocolVersionOption, "The protocol version is invalid."));
            return null;
        }

        if (!parsed.IsSupportedBy(AgentProtocolVersion.Current))
        {
            errors.Add(new AgentCommandLineError("arguments.unsupportedProtocolMajor", ProtocolVersionOption, "The protocol major version is not supported."));
            return null;
        }

        return parsed;
    }

    private static string? NormalizePath(
        string value,
        string option,
        List<AgentCommandLineError> errors)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Path is empty.", nameof(value));
            }

            return Path.GetFullPath(value);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors.Add(new AgentCommandLineError("arguments.invalidPath", option, $"The {option} path is invalid."));
            return null;
        }
    }
}
