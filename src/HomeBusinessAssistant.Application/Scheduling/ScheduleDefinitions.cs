using System.Globalization;
using System.Text.Json;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Application.Scheduling;

/// <summary>Specifies whether a one-time value is an intended local wall-clock slot or a UTC instant.</summary>
public enum ScheduleTimeSemantics
{
    /// <summary>The value is interpreted in the schedule's configured time zone.</summary>
    Local = 0,
    /// <summary>The value is an absolute UTC instant.</summary>
    Utc = 1,
}

/// <summary>Base type for versioned schedule definitions.</summary>
public abstract record ScheduleDefinition
{
    /// <summary>The current stable schedule-definition version.</summary>
    public const string CurrentVersion = "1.0";

    /// <summary>Gets the persisted schedule kind represented by the definition.</summary>
    public abstract ScheduleKind Kind { get; }
}

/// <summary>A definition that creates occurrences only through explicit manual requests.</summary>
public sealed record ManualScheduleDefinition : ScheduleDefinition
{
    /// <inheritdoc />
    public override ScheduleKind Kind => ScheduleKind.Manual;
}

/// <summary>A single intended local wall-clock slot or UTC instant.</summary>
public sealed record OneTimeScheduleDefinition(
    ScheduleTimeSemantics Semantics,
    DateTime LocalDateTime,
    DateTimeOffset UtcDateTime) : ScheduleDefinition
{
    /// <inheritdoc />
    public override ScheduleKind Kind => ScheduleKind.OneTime;

    /// <summary>Creates a one-time local wall-clock definition.</summary>
    public static OneTimeScheduleDefinition AtLocal(DateTime localDateTime) =>
        new(ScheduleTimeSemantics.Local, DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified), default);

    /// <summary>Creates a one-time UTC definition.</summary>
    public static OneTimeScheduleDefinition AtUtc(DateTimeOffset utcDateTime) =>
        new(ScheduleTimeSemantics.Utc, default, utcDateTime.ToUniversalTime());
}

/// <summary>A local wall-clock time every day.</summary>
public sealed record DailyScheduleDefinition(TimeOnly LocalTime) : ScheduleDefinition
{
    /// <inheritdoc />
    public override ScheduleKind Kind => ScheduleKind.Daily;
}

/// <summary>A local wall-clock time on a non-empty set of weekdays.</summary>
public sealed record WeekdayScheduleDefinition(
    IReadOnlyList<DayOfWeek> Days,
    TimeOnly LocalTime) : ScheduleDefinition
{
    /// <inheritdoc />
    public override ScheduleKind Kind => ScheduleKind.SelectedWeekdays;
}

/// <summary>A fixed interval measured between scheduled UTC due times.</summary>
public sealed record FixedIntervalScheduleDefinition(
    TimeSpan Interval,
    DateTimeOffset AnchorUtc) : ScheduleDefinition
{
    /// <inheritdoc />
    public override ScheduleKind Kind => ScheduleKind.FixedInterval;
}

/// <summary>A fixed delay measured from the previous terminal completion time.</summary>
public sealed record FixedDelayScheduleDefinition(
    TimeSpan Delay,
    DateTimeOffset? InitialDueAtUtc,
    bool StartImmediately) : ScheduleDefinition
{
    /// <inheritdoc />
    public override ScheduleKind Kind => ScheduleKind.FixedDelay;
}

/// <summary>A path-aware schedule-definition validation error.</summary>
public sealed record ScheduleValidationError(string Code, string Path, string Message);

/// <summary>The result of parsing and validating persisted schedule JSON.</summary>
public sealed record ScheduleDefinitionParseResult(
    ScheduleDefinition? Definition,
    IReadOnlyList<ScheduleValidationError> Errors)
{
    /// <summary>Gets whether parsing and validation succeeded.</summary>
    public bool IsValid => Definition is not null && Errors.Count == 0;
}

/// <summary>Serializes and validates the stable schedule-definition JSON contract.</summary>
public static class ScheduleDefinitionJson
{
    /// <summary>The minimum supported fixed interval or delay.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(1);

    /// <summary>The maximum supported fixed interval or delay.</summary>
    public static readonly TimeSpan MaximumInterval = TimeSpan.FromDays(365);

    /// <summary>Serializes one validated definition into canonical JSON.</summary>
    public static string Serialize(ScheduleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        IReadOnlyList<ScheduleValidationError> errors = Validate(definition);
        if (errors.Count > 0)
        {
            throw new ScheduleDefinitionValidationException(errors);
        }

        object payload = definition switch
        {
            ManualScheduleDefinition => new { version = ScheduleDefinition.CurrentVersion, type = "manual" },
            OneTimeScheduleDefinition oneTime when oneTime.Semantics == ScheduleTimeSemantics.Local => new
            {
                version = ScheduleDefinition.CurrentVersion,
                type = "one-time",
                semantics = "local",
                localDateTime = oneTime.LocalDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            },
            OneTimeScheduleDefinition oneTime => new
            {
                version = ScheduleDefinition.CurrentVersion,
                type = "one-time",
                semantics = "utc",
                utcDateTime = oneTime.UtcDateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            },
            DailyScheduleDefinition daily => new
            {
                version = ScheduleDefinition.CurrentVersion,
                type = "daily",
                localTime = daily.LocalTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            },
            WeekdayScheduleDefinition weekday => new
            {
                version = ScheduleDefinition.CurrentVersion,
                type = "selected-weekdays",
                days = weekday.Days.Order().Select(day => day.ToString()).ToArray(),
                localTime = weekday.LocalTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            },
            FixedIntervalScheduleDefinition interval => new
            {
                version = ScheduleDefinition.CurrentVersion,
                type = "fixed-interval",
                interval = interval.Interval.ToString("c", CultureInfo.InvariantCulture),
                anchorUtc = interval.AnchorUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            },
            FixedDelayScheduleDefinition delay => new
            {
                version = ScheduleDefinition.CurrentVersion,
                type = "fixed-delay",
                delay = delay.Delay.ToString("c", CultureInfo.InvariantCulture),
                initialDueAtUtc = delay.InitialDueAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                startImmediately = delay.StartImmediately,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(definition), "The schedule definition type is unsupported."),
        };
        return CanonicalJson.Serialize(JsonSerializer.SerializeToElement(payload));
    }

    /// <summary>Parses persisted JSON and requires its discriminator to match the persisted kind.</summary>
    public static ScheduleDefinitionParseResult Parse(string json, ScheduleKind expectedKind)
    {
        var errors = new List<ScheduleValidationError>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Failure("scheduleDefinition.invalidObject", "$", "The schedule definition must be a JSON object.");
            }

            string? version = ReadString(root, "version", "$.version", errors);
            string? type = ReadString(root, "type", "$.type", errors);
            if (version is not null && version != ScheduleDefinition.CurrentVersion)
            {
                errors.Add(new("scheduleDefinition.unsupportedVersion", "$.version", "The schedule-definition version is not supported."));
            }

            ScheduleKind? discriminatedKind = ParseKind(type);
            if (type is not null && discriminatedKind is null)
            {
                errors.Add(new("scheduleDefinition.invalidType", "$.type", "The schedule-definition type is invalid."));
            }
            else if (discriminatedKind.HasValue && discriminatedKind.Value != expectedKind)
            {
                errors.Add(new("scheduleDefinition.typeMismatch", "$.type", "The JSON discriminator does not match the persisted schedule type."));
            }

            ScheduleDefinition? definition = errors.Count == 0
                ? ParseDefinition(root, expectedKind, errors)
                : null;
            if (definition is not null)
            {
                errors.AddRange(Validate(definition));
            }

            return new(definition, errors);
        }
        catch (JsonException)
        {
            return Failure("scheduleDefinition.invalidJson", "$", "The schedule definition contains invalid JSON.");
        }
    }

    /// <summary>Validates an already typed schedule definition.</summary>
    public static IReadOnlyList<ScheduleValidationError> Validate(ScheduleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new List<ScheduleValidationError>();
        switch (definition)
        {
            case OneTimeScheduleDefinition oneTime when !Enum.IsDefined(oneTime.Semantics):
                errors.Add(new("scheduleDefinition.invalidSemantics", "$.semantics", "The one-time semantics value is invalid."));
                break;
            case OneTimeScheduleDefinition oneTime when oneTime.Semantics == ScheduleTimeSemantics.Local
                && oneTime.LocalDateTime == default:
                errors.Add(new("scheduleDefinition.localDateTimeRequired", "$.localDateTime", "A local date and time is required."));
                break;
            case OneTimeScheduleDefinition oneTime when oneTime.Semantics == ScheduleTimeSemantics.Utc
                && oneTime.UtcDateTime == default:
                errors.Add(new("scheduleDefinition.utcDateTimeRequired", "$.utcDateTime", "A UTC date and time is required."));
                break;
            case WeekdayScheduleDefinition weekday:
                if (weekday.Days.Count == 0)
                {
                    errors.Add(new("scheduleDefinition.daysRequired", "$.days", "At least one weekday is required."));
                }
                else if (weekday.Days.Any(day => !Enum.IsDefined(day)) || weekday.Days.Distinct().Count() != weekday.Days.Count)
                {
                    errors.Add(new("scheduleDefinition.invalidDays", "$.days", "Weekdays must be valid and unique."));
                }

                break;
            case FixedIntervalScheduleDefinition interval:
                ValidateInterval(interval.Interval, "$.interval", errors);
                if (interval.AnchorUtc == default)
                {
                    errors.Add(new("scheduleDefinition.anchorRequired", "$.anchorUtc", "A fixed-interval UTC anchor is required."));
                }

                break;
            case FixedDelayScheduleDefinition delay:
                ValidateInterval(delay.Delay, "$.delay", errors);
                if (delay.StartImmediately == delay.InitialDueAtUtc.HasValue)
                {
                    errors.Add(new("scheduleDefinition.invalidInitialDue", "$.initialDueAtUtc", "Choose either start immediately or one initial UTC due time."));
                }

                break;
        }

        return errors;
    }

    private static ScheduleDefinition? ParseDefinition(
        JsonElement root,
        ScheduleKind kind,
        List<ScheduleValidationError> errors) => kind switch
        {
            ScheduleKind.Manual => new ManualScheduleDefinition(),
            ScheduleKind.OneTime => ParseOneTime(root, errors),
            ScheduleKind.Daily => ParseDaily(root, errors),
            ScheduleKind.SelectedWeekdays => ParseWeekday(root, errors),
            ScheduleKind.FixedInterval => ParseInterval(root, errors),
            ScheduleKind.FixedDelay => ParseDelay(root, errors),
            _ => AddUnsupportedKind(errors),
        };

    private static OneTimeScheduleDefinition? ParseOneTime(JsonElement root, List<ScheduleValidationError> errors)
    {
        string? semantics = ReadString(root, "semantics", "$.semantics", errors);
        if (semantics == "local")
        {
            string? value = ReadString(root, "localDateTime", "$.localDateTime", errors);
            if (value is not null && DateTime.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
            {
                return OneTimeScheduleDefinition.AtLocal(parsed);
            }

            if (value is not null)
            {
                errors.Add(new("scheduleDefinition.invalidLocalDateTime", "$.localDateTime", "The local date and time is invalid."));
            }
        }
        else if (semantics == "utc")
        {
            string? value = ReadString(root, "utcDateTime", "$.utcDateTime", errors);
            if (value is not null && DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
                && parsed.Offset == TimeSpan.Zero)
            {
                return OneTimeScheduleDefinition.AtUtc(parsed);
            }

            if (value is not null)
            {
                errors.Add(new("scheduleDefinition.invalidUtcDateTime", "$.utcDateTime", "The UTC date and time is invalid."));
            }
        }
        else if (semantics is not null)
        {
            errors.Add(new("scheduleDefinition.invalidSemantics", "$.semantics", "The one-time semantics value is invalid."));
        }

        return null;
    }

    private static DailyScheduleDefinition? ParseDaily(JsonElement root, List<ScheduleValidationError> errors)
    {
        TimeOnly? time = ReadTime(root, errors);
        return time.HasValue ? new(time.Value) : null;
    }

    private static WeekdayScheduleDefinition? ParseWeekday(JsonElement root, List<ScheduleValidationError> errors)
    {
        var days = new List<DayOfWeek>();
        if (!root.TryGetProperty("days", out JsonElement property) || property.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new("scheduleDefinition.daysRequired", "$.days", "A weekday array is required."));
        }
        else
        {
            var index = 0;
            foreach (JsonElement item in property.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String
                    || !Enum.TryParse(item.GetString(), ignoreCase: false, out DayOfWeek day)
                    || !Enum.IsDefined(day))
                {
                    errors.Add(new("scheduleDefinition.invalidDay", $"$.days[{index}]", "The weekday value is invalid."));
                }
                else
                {
                    days.Add(day);
                }

                index++;
            }
        }

        TimeOnly? time = ReadTime(root, errors);
        return errors.Count == 0 && time.HasValue ? new(days, time.Value) : null;
    }

    private static FixedIntervalScheduleDefinition? ParseInterval(JsonElement root, List<ScheduleValidationError> errors)
    {
        TimeSpan? interval = ReadTimeSpan(root, "interval", "$.interval", errors);
        DateTimeOffset? anchor = ReadUtc(root, "anchorUtc", "$.anchorUtc", errors);
        return interval.HasValue && anchor.HasValue ? new(interval.Value, anchor.Value) : null;
    }

    private static FixedDelayScheduleDefinition? ParseDelay(JsonElement root, List<ScheduleValidationError> errors)
    {
        TimeSpan? delay = ReadTimeSpan(root, "delay", "$.delay", errors);
        DateTimeOffset? initial = null;
        if (root.TryGetProperty("initialDueAtUtc", out JsonElement initialProperty)
            && initialProperty.ValueKind != JsonValueKind.Null)
        {
            initial = ReadUtc(root, "initialDueAtUtc", "$.initialDueAtUtc", errors);
        }

        bool? startImmediately = root.TryGetProperty("startImmediately", out JsonElement startProperty)
            && startProperty.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? startProperty.GetBoolean()
            : null;
        if (!startImmediately.HasValue)
        {
            errors.Add(new("scheduleDefinition.startImmediatelyRequired", "$.startImmediately", "The start-immediately flag is required."));
        }

        return delay.HasValue && startImmediately.HasValue ? new(delay.Value, initial, startImmediately.Value) : null;
    }

    private static TimeOnly? ReadTime(JsonElement root, List<ScheduleValidationError> errors)
    {
        string? value = ReadString(root, "localTime", "$.localTime", errors);
        if (value is not null && TimeOnly.TryParseExact(value, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly parsed))
        {
            return parsed;
        }

        if (value is not null)
        {
            errors.Add(new("scheduleDefinition.invalidLocalTime", "$.localTime", "The local time is invalid."));
        }

        return null;
    }

    private static TimeSpan? ReadTimeSpan(JsonElement root, string propertyName, string path, List<ScheduleValidationError> errors)
    {
        string? value = ReadString(root, propertyName, path, errors);
        if (value is not null && TimeSpan.TryParseExact(value, "c", CultureInfo.InvariantCulture, out TimeSpan parsed))
        {
            return parsed;
        }

        if (value is not null)
        {
            errors.Add(new("scheduleDefinition.invalidDuration", path, "The duration is invalid."));
        }

        return null;
    }

    private static DateTimeOffset? ReadUtc(JsonElement root, string propertyName, string path, List<ScheduleValidationError> errors)
    {
        string? value = ReadString(root, propertyName, path, errors);
        if (value is not null
            && DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            && parsed.Offset == TimeSpan.Zero)
        {
            return parsed;
        }

        if (value is not null)
        {
            errors.Add(new("scheduleDefinition.invalidUtcDateTime", path, "The value must be an explicit UTC date and time."));
        }

        return null;
    }

    private static string? ReadString(JsonElement root, string propertyName, string path, List<ScheduleValidationError> errors)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.String)
        {
            errors.Add(new("scheduleDefinition.required", path, "A string value is required."));
            return null;
        }

        return property.GetString();
    }

    private static void ValidateInterval(TimeSpan value, string path, List<ScheduleValidationError> errors)
    {
        if (value < MinimumInterval || value > MaximumInterval)
        {
            errors.Add(new("scheduleDefinition.intervalOutOfRange", path, "The interval must be between one minute and 365 days."));
        }
    }

    private static ScheduleKind? ParseKind(string? type) => type switch
    {
        "manual" => ScheduleKind.Manual,
        "one-time" => ScheduleKind.OneTime,
        "daily" => ScheduleKind.Daily,
        "selected-weekdays" => ScheduleKind.SelectedWeekdays,
        "fixed-interval" => ScheduleKind.FixedInterval,
        "fixed-delay" => ScheduleKind.FixedDelay,
        _ => null,
    };

    private static ScheduleDefinition? AddUnsupportedKind(List<ScheduleValidationError> errors)
    {
        errors.Add(new("scheduleDefinition.invalidKind", "$.type", "The persisted schedule kind is invalid."));
        return null;
    }

    private static ScheduleDefinitionParseResult Failure(string code, string path, string message) =>
        new(null, [new ScheduleValidationError(code, path, message)]);
}

/// <summary>Thrown when a typed schedule definition is invalid.</summary>
public sealed class ScheduleDefinitionValidationException : Exception
{
    /// <summary>Initializes an exception from safe path-aware errors.</summary>
    public ScheduleDefinitionValidationException(IReadOnlyList<ScheduleValidationError> errors)
        : base("The schedule definition is invalid.") => Errors = errors;

    /// <summary>Gets the safe validation errors.</summary>
    public IReadOnlyList<ScheduleValidationError> Errors { get; }
}
