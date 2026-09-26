using System.Text.Json;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Application.Scheduling;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Domain.Audit;

namespace WakeRemote.Application;

/// <summary>Template stored on a Wake Remote availability schedule before a due time is known.</summary>
public sealed record WakeRemoteWindowTemplate(
    int WindowDurationSeconds,
    bool RemoteProviderRequired,
    bool AllowImmediateMisfire);

/// <summary>Transforms Wake Remote schedule templates into immutable occurrence arguments.</summary>
public sealed class WakeRemoteOccurrenceArgumentsProvider(
    IScheduleOccurrenceArgumentsProvider? fallback = null) : IScheduleOccurrenceArgumentsProvider
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    private readonly IScheduleOccurrenceArgumentsProvider fallbackProvider =
        fallback ?? new PassthroughScheduleOccurrenceArgumentsProvider();

    /// <inheritdoc />
    public string CreateArguments(AgentScheduleRecord schedule, DateTimeOffset dueAtUtc)
    {
        if (schedule.AgentId != WakeRemoteDefaults.AgentId || schedule.CommandName != "run")
        {
            return fallbackProvider.CreateArguments(schedule, dueAtUtc);
        }

        WakeRemoteWindowTemplate template;
        try
        {
            template = JsonSerializer.Deserialize<WakeRemoteWindowTemplate>(schedule.ArgumentsJson, Options)
                ?? throw new InvalidOperationException("The Wake Remote schedule arguments are absent.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The Wake Remote schedule arguments are invalid.", exception);
        }

        if (template.WindowDurationSeconds is < 60 or > 86_400)
        {
            throw new InvalidOperationException("The Wake Remote availability duration is outside supported bounds.");
        }

        DateTimeOffset scheduledUtc = dueAtUtc.ToUniversalTime();
        return WakeRemoteInput.SerializeOccurrence(new(
            $"{schedule.Id:N}:{scheduledUtc:yyyyMMddTHHmmssfffffffZ}",
            scheduledUtc,
            scheduledUtc.AddSeconds(template.WindowDurationSeconds),
            template.RemoteProviderRequired,
            template.AllowImmediateMisfire));
    }
}

/// <summary>Request to create or replace a daily/weekday Wake Remote availability schedule.</summary>
public sealed record SaveWakeRemoteWindowRequest(
    Guid? ScheduleId,
    string Name,
    TimeOnly LocalStartTime,
    IReadOnlyList<DayOfWeek>? Days,
    TimeSpan Duration,
    bool RemoteProviderRequired,
    MisfirePolicy MisfirePolicy,
    TimeSpan MisfireGracePeriod,
    TimeSpan StartupGrace,
    bool IsEnabled,
    string ActorId,
    Guid CorrelationId);

/// <summary>Creates validated Required-wake daily or weekday schedules for Wake Remote.</summary>
public sealed class WakeRemoteAvailabilityWindowService(
    IAgentConfigurationService configurations,
    IScheduleRepository schedules,
    IAgentScheduleValidator validator,
    IAuditWriter auditWriter,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Creates or replaces one typed availability-window schedule.</summary>
    public async ValueTask<AgentScheduleRecord> SaveAsync(
        SaveWakeRemoteWindowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        AgentConfigurationRecord configuration = await configurations.GetCurrentAsync(
            WakeRemoteDefaults.AgentId,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Wake Remote requires a current configuration revision.");
        DateTimeOffset nowUtc = timeProvider.GetUtcNow().ToUniversalTime();
        AgentScheduleRecord? current = request.ScheduleId.HasValue
            ? await schedules.GetAsync(request.ScheduleId.Value, cancellationToken).ConfigureAwait(false)
            : null;
        if (request.ScheduleId.HasValue && current is null)
        {
            throw new InvalidOperationException("The Wake Remote schedule does not exist.");
        }

        if (current is not null && current.AgentId != WakeRemoteDefaults.AgentId)
        {
            throw new InvalidOperationException("The selected schedule belongs to another agent.");
        }

        ScheduleDefinition definition = request.Days is { Count: > 0 }
            ? new WeekdayScheduleDefinition(request.Days, request.LocalStartTime)
            : new DailyScheduleDefinition(request.LocalStartTime);
        var template = new WakeRemoteWindowTemplate(
            checked((int)request.Duration.TotalSeconds),
            request.RemoteProviderRequired,
            request.MisfirePolicy == MisfirePolicy.RunImmediately);
        string arguments = CanonicalJson.Serialize(JsonSerializer.SerializeToElement(template, SerializerOptions));
        using JsonDocument configurationDocument = JsonDocument.Parse(configuration.CurrentRevision.CanonicalConfigurationJson);
        WakeRemoteConfiguration typedConfiguration = WakeRemoteInput.ParseConfiguration(configurationDocument.RootElement).Value
            ?? throw new InvalidOperationException("The current Wake Remote configuration is invalid.");
        var candidate = new AgentScheduleRecord(
            current?.Id ?? Guid.NewGuid(),
            WakeRemoteDefaults.AgentId,
            request.Name,
            "run",
            arguments,
            definition.Kind,
            ScheduleDefinitionJson.Serialize(definition),
            typedConfiguration.TimeZoneId,
            request.MisfirePolicy,
            ConcurrencyPolicy.Forbid,
            request.Duration + request.StartupGrace,
            request.MisfireGracePeriod,
            RetryPolicyJson.Serialize(RetryPolicyDefinition.None),
            WakePolicy.Required,
            PinnedConfigurationRevisionId: null,
            AllowDisabledAgent: false,
            request.IsEnabled,
            IsPaused: false,
            PausedUntilUtc: null,
            current?.CreatedAtUtc ?? nowUtc,
            nowUtc,
            current?.ConcurrencyToken ?? 0);
        AgentScheduleValidationResult validation = await validator.ValidateAsync(candidate, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            throw new AgentScheduleValidationException(validation.Errors);
        }

        AgentScheduleRecord saved = await schedules.SaveAsync(
            candidate,
            current?.ConcurrencyToken,
            cancellationToken).ConfigureAwait(false);
        _ = await auditWriter.WriteAsync(new(
            AuditActorType.User,
            request.ActorId,
            current is null ? "wake-remote.window-created" : "wake-remote.window-updated",
            "schedule",
            saved.Id.ToString("D"),
            AuditOutcome.Succeeded,
            request.CorrelationId,
            RunId: null,
            JsonSerializer.SerializeToElement(new
            {
                durationSeconds = (int)request.Duration.TotalSeconds,
                request.RemoteProviderRequired,
                wakePolicy = saved.WakePolicy.ToString(),
                scheduleKind = saved.Kind.ToString(),
            }, SerializerOptions)), cancellationToken).ConfigureAwait(false);
        return saved;
    }

    private static void Validate(SaveWakeRemoteWindowRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)
            || request.Name.Length > 100
            || request.Duration < TimeSpan.FromMinutes(1)
            || request.Duration > WakeRemoteConfiguration.MaximumWindowDuration
            || request.StartupGrace < TimeSpan.Zero
            || request.StartupGrace > TimeSpan.FromHours(1)
            || request.Duration + request.StartupGrace > TimeSpan.FromDays(1)
            || request.MisfireGracePeriod < TimeSpan.Zero
            || request.MisfireGracePeriod > TimeSpan.FromHours(1)
            || !Enum.IsDefined(request.MisfirePolicy)
            || request.Days is { Count: > 0 } days && (days.Distinct().Count() != days.Count || days.Any(day => !Enum.IsDefined(day)))
            || string.IsNullOrWhiteSpace(request.ActorId)
            || request.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("The Wake Remote availability-window request is invalid.", nameof(request));
        }
    }
}
