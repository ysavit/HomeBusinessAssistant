using System.Text.Json;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Application.Scheduling;

/// <summary>The path-aware result of validating a complete durable schedule.</summary>
public sealed record AgentScheduleValidationResult(IReadOnlyList<ScheduleValidationError> Errors)
{
    /// <summary>Gets whether the schedule is valid.</summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Validates complete schedule records before they cause side effects.</summary>
public interface IAgentScheduleValidator
{
    /// <summary>Validates one schedule and returns future-UI-compatible errors.</summary>
    ValueTask<AgentScheduleValidationResult> ValidateAsync(
        AgentScheduleRecord schedule,
        CancellationToken cancellationToken = default);
}

/// <summary>Application validator spanning agents, manifests, time zones, configuration, and persistence identity.</summary>
public sealed class AgentScheduleValidator(
    IAgentDefinitionRepository agentDefinitions,
    IAgentConfigurationService configurations,
    IScheduleRepository schedules,
    IScheduleTimeZoneService timeZones) : IAgentScheduleValidator
{
    /// <inheritdoc />
    public async ValueTask<AgentScheduleValidationResult> ValidateAsync(
        AgentScheduleRecord schedule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var errors = new List<ScheduleValidationError>();
        ValidateShape(schedule, errors);

        AgentDefinitionRecord? agent = await agentDefinitions.GetAsync(schedule.AgentId, cancellationToken).ConfigureAwait(false);
        if (agent is null)
        {
            errors.Add(new("schedule.agentNotInstalled", "$.agentId", "The selected agent is not installed."));
        }
        else
        {
            ValidateAgentPolicy(schedule, agent, errors);
        }

        if (!timeZones.TimeZoneExists(schedule.TimeZoneId))
        {
            errors.Add(new("schedule.timeZoneUnavailable", "$.timeZoneId", "The configured time zone is unavailable on this machine."));
        }

        ScheduleDefinitionParseResult definition = ScheduleDefinitionJson.Parse(schedule.DefinitionJson, schedule.Kind);
        errors.AddRange(definition.Errors);
        (_, IReadOnlyList<ScheduleValidationError> retryErrors) = RetryPolicyJson.Parse(schedule.RetryPolicyJson);
        errors.AddRange(retryErrors);

        AgentScheduleRecord? collision = string.IsNullOrWhiteSpace(schedule.Name)
            ? null
            : await schedules.GetByAgentAndNameAsync(schedule.AgentId, schedule.Name, schedule.Id, cancellationToken).ConfigureAwait(false);
        if (collision is not null)
        {
            errors.Add(new("schedule.nameCollision", "$.name", "Another schedule for this agent already uses this name."));
        }

        ConfigurationRevisionRecord? revision = schedule.PinnedConfigurationRevisionId.HasValue
            ? await configurations.GetRevisionAsync(schedule.PinnedConfigurationRevisionId.Value, cancellationToken).ConfigureAwait(false)
            : (await configurations.GetCurrentAsync(schedule.AgentId, cancellationToken).ConfigureAwait(false))?.CurrentRevision;
        if (revision is null)
        {
            errors.Add(new("schedule.configurationRequired", "$.pinnedConfigurationRevisionId", "The agent needs a current or explicitly pinned configuration revision."));
        }
        else if (revision.AgentId != schedule.AgentId)
        {
            errors.Add(new("schedule.configurationAgentMismatch", "$.pinnedConfigurationRevisionId", "The configuration revision belongs to a different agent."));
        }

        return new(errors);
    }

    private static void ValidateShape(AgentScheduleRecord schedule, List<ScheduleValidationError> errors)
    {
        if (schedule.Id == Guid.Empty)
        {
            errors.Add(new("schedule.idRequired", "$.id", "A schedule identifier is required."));
        }

        if (string.IsNullOrWhiteSpace(schedule.Name) || schedule.Name.Length > 100)
        {
            errors.Add(new("schedule.invalidName", "$.name", "The schedule name is required and must be at most 100 characters."));
        }

        if (!AgentCommandPolicy.IsValidCommandName(schedule.CommandName))
        {
            errors.Add(new("schedule.invalidCommand", "$.commandName", "The command name is invalid."));
        }

        try
        {
            using JsonDocument arguments = JsonDocument.Parse(schedule.ArgumentsJson, new JsonDocumentOptions { MaxDepth = 64 });
            if (arguments.RootElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new("schedule.argumentsMustBeObject", "$.arguments", "Schedule arguments must be a JSON object."));
            }
        }
        catch (JsonException)
        {
            errors.Add(new("schedule.invalidArguments", "$.arguments", "Schedule arguments contain invalid JSON."));
        }

        if (schedule.Timeout < TimeSpan.FromSeconds(1) || schedule.Timeout > TimeSpan.FromDays(1))
        {
            errors.Add(new("schedule.timeoutOutOfRange", "$.timeout", "The timeout must be between one second and one day."));
        }

        if (schedule.MisfireGracePeriod < TimeSpan.Zero || schedule.MisfireGracePeriod > TimeSpan.FromHours(1))
        {
            errors.Add(new("schedule.misfireGraceOutOfRange", "$.misfireGracePeriod", "The misfire grace period must be between zero and one hour."));
        }

        if (!schedule.IsPaused && schedule.PausedUntilUtc.HasValue)
        {
            errors.Add(new("schedule.invalidPause", "$.pausedUntilUtc", "A pause-until value requires the schedule to be paused."));
        }

        if (schedule.Kind == ScheduleKind.Manual && schedule.WakePolicy != WakePolicy.Never)
        {
            errors.Add(new("schedule.manualWakeUnsupported", "$.wakePolicy", "Manual-only schedules cannot request an operating-system wake."));
        }
    }

    private static void ValidateAgentPolicy(
        AgentScheduleRecord schedule,
        AgentDefinitionRecord agent,
        List<ScheduleValidationError> errors)
    {
        if (!agent.Enabled && !schedule.AllowDisabledAgent)
        {
            errors.Add(new("schedule.agentDisabled", "$.agentId", "The selected agent is disabled."));
        }

        bool supportedShape = schedule.Kind == ScheduleKind.Manual ? agent.SupportsManualRun : agent.SupportsScheduling;
        if (!supportedShape)
        {
            errors.Add(new("schedule.unsupportedByManifest", "$.kind", "The installed agent manifest does not support this schedule shape."));
        }

        if (!AgentCommandPolicy.SupportsCommand(agent, schedule.CommandName))
        {
            errors.Add(new("schedule.commandUnsupported", "$.commandName", "The installed agent manifest does not support this command."));
        }
    }
}

internal static class AgentCommandPolicy
{
    public static bool SupportsCommand(AgentDefinitionRecord agent, string commandName)
    {
        try
        {
            string[] commands = JsonSerializer.Deserialize<string[]>(agent.SupportedCommandsJson) ?? [];
            return commands.Contains(commandName, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool IsValidCommandName(string commandName) =>
        !string.IsNullOrWhiteSpace(commandName)
        && commandName.Length <= 64
        && char.IsAsciiLetterOrDigit(commandName[0])
        && char.IsAsciiLetterOrDigit(commandName[^1])
        && commandName.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}
