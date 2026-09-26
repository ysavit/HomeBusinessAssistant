namespace HomeBusinessAssistant.Application.Scheduling;

/// <summary>The deterministic result of resolving one intended local wall-clock slot.</summary>
public sealed record LocalTimeConversionResult(
    DateTime IntendedLocalDateTime,
    DateTime ResolvedLocalDateTime,
    DateTimeOffset UtcDateTime,
    TimeSpan SelectedOffset,
    bool WasInvalidAdjusted,
    bool WasAmbiguous);

/// <summary>Resolves configured time zones and converts schedule slots.</summary>
public interface IScheduleTimeZoneService
{
    /// <summary>Returns whether the configured time-zone identifier exists on this machine.</summary>
    bool TimeZoneExists(string timeZoneId);

    /// <summary>Resolves a local wall-clock value using the documented DST policy.</summary>
    LocalTimeConversionResult ConvertLocalToUtc(DateTime localDateTime, string timeZoneId);

    /// <summary>Converts a UTC instant to local display time in the configured zone.</summary>
    DateTimeOffset ConvertUtcToLocal(DateTimeOffset utcDateTime, string timeZoneId);
}

/// <summary>Time-zone conversion based on the operating system's installed zone database.</summary>
public sealed class ScheduleTimeZoneService : IScheduleTimeZoneService
{
    private const int MaximumInvalidMinutes = 240;

    /// <inheritdoc />
    public bool TimeZoneExists(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Length > 128)
        {
            return false;
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public LocalTimeConversionResult ConvertLocalToUtc(DateTime localDateTime, string timeZoneId)
    {
        TimeZoneInfo timeZone = Find(timeZoneId);
        DateTime intended = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        DateTime resolved = intended;
        bool adjusted = timeZone.IsInvalidTime(resolved);
        if (adjusted)
        {
            resolved = ResolveFirstValidSecond(timeZone, resolved);
        }

        bool ambiguous = timeZone.IsAmbiguousTime(resolved);
        TimeSpan offset = ambiguous
            ? timeZone.GetAmbiguousTimeOffsets(resolved).Max()
            : timeZone.GetUtcOffset(resolved);
        DateTimeOffset utc = new DateTimeOffset(resolved, offset).ToUniversalTime();
        return new(intended, resolved, utc, offset, adjusted, ambiguous);
    }

    /// <inheritdoc />
    public DateTimeOffset ConvertUtcToLocal(DateTimeOffset utcDateTime, string timeZoneId) =>
        TimeZoneInfo.ConvertTime(utcDateTime.ToUniversalTime(), Find(timeZoneId));

    private static TimeZoneInfo Find(string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ArgumentException("The configured time-zone identifier is unavailable.", nameof(timeZoneId), exception);
        }
    }

    private static DateTime ResolveFirstValidSecond(TimeZoneInfo timeZone, DateTime invalidLocal)
    {
        DateTime cursor = new(
            invalidLocal.Year,
            invalidLocal.Month,
            invalidLocal.Day,
            invalidLocal.Hour,
            invalidLocal.Minute,
            0,
            DateTimeKind.Unspecified);
        for (var minute = 0; minute <= MaximumInvalidMinutes; minute++)
        {
            if (!timeZone.IsInvalidTime(cursor))
            {
                DateTime searchStart = cursor.AddMinutes(-1);
                for (var second = 0; second <= 60; second++)
                {
                    DateTime candidate = searchStart.AddSeconds(second);
                    if (!timeZone.IsInvalidTime(candidate))
                    {
                        return candidate;
                    }
                }

                return cursor;
            }

            cursor = cursor.AddMinutes(1);
        }

        throw new InvalidOperationException("The invalid local-time gap exceeds the supported four-hour safety bound.");
    }
}

/// <summary>Calculates bounded UTC due slots from a typed definition.</summary>
public interface IScheduleCalculator
{
    /// <summary>Calculates slots in the inclusive UTC range with a strict result limit.</summary>
    IReadOnlyList<DateTimeOffset> Calculate(
        ScheduleDefinition definition,
        string timeZoneId,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc,
        int maximumResults);
}

/// <summary>Small deterministic calculator for all V1 schedule shapes.</summary>
public sealed class ScheduleCalculator(IScheduleTimeZoneService timeZoneService) : IScheduleCalculator
{
    /// <inheritdoc />
    public IReadOnlyList<DateTimeOffset> Calculate(
        ScheduleDefinition definition,
        string timeZoneId,
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc,
        int maximumResults)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (maximumResults < 1 || rangeEndUtc < rangeStartUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResults), "The calculation range or result limit is invalid.");
        }

        IReadOnlyList<ScheduleValidationError> errors = ScheduleDefinitionJson.Validate(definition);
        if (errors.Count > 0)
        {
            throw new ScheduleDefinitionValidationException(errors);
        }

        DateTimeOffset start = rangeStartUtc.ToUniversalTime();
        DateTimeOffset end = rangeEndUtc.ToUniversalTime();
        return definition switch
        {
            ManualScheduleDefinition => [],
            OneTimeScheduleDefinition oneTime => Filter([ResolveOneTime(oneTime, timeZoneId)], start, end, maximumResults),
            DailyScheduleDefinition daily => CalculateCalendar(daily.LocalTime, days: null, timeZoneId, start, end, maximumResults),
            WeekdayScheduleDefinition weekday => CalculateCalendar(weekday.LocalTime, weekday.Days, timeZoneId, start, end, maximumResults),
            FixedIntervalScheduleDefinition interval => CalculateFixedInterval(interval, start, end, maximumResults),
            FixedDelayScheduleDefinition delay => CalculateFixedDelayInitial(delay, start, end),
            _ => throw new ArgumentOutOfRangeException(nameof(definition), "The schedule definition type is unsupported."),
        };
    }

    private DateTimeOffset ResolveOneTime(OneTimeScheduleDefinition definition, string timeZoneId) =>
        definition.Semantics == ScheduleTimeSemantics.Utc
            ? definition.UtcDateTime.ToUniversalTime()
            : timeZoneService.ConvertLocalToUtc(definition.LocalDateTime, timeZoneId).UtcDateTime;

    private List<DateTimeOffset> CalculateCalendar(
        TimeOnly localTime,
        IReadOnlyCollection<DayOfWeek>? days,
        string timeZoneId,
        DateTimeOffset start,
        DateTimeOffset end,
        int maximumResults)
    {
        DateOnly firstDate = DateOnly.FromDateTime(timeZoneService.ConvertUtcToLocal(start, timeZoneId).DateTime).AddDays(-1);
        DateOnly lastDate = DateOnly.FromDateTime(timeZoneService.ConvertUtcToLocal(end, timeZoneId).DateTime).AddDays(1);
        var results = new List<DateTimeOffset>(Math.Min(maximumResults, 128));
        for (DateOnly date = firstDate; date <= lastDate && results.Count < maximumResults; date = date.AddDays(1))
        {
            if (days is not null && !days.Contains(date.DayOfWeek))
            {
                continue;
            }

            DateTimeOffset due = timeZoneService.ConvertLocalToUtc(date.ToDateTime(localTime), timeZoneId).UtcDateTime;
            if (due >= start && due <= end && !results.Contains(due))
            {
                results.Add(due);
            }
        }

        results.Sort();
        return results;
    }

    private static List<DateTimeOffset> CalculateFixedInterval(
        FixedIntervalScheduleDefinition definition,
        DateTimeOffset start,
        DateTimeOffset end,
        int maximumResults)
    {
        DateTimeOffset anchor = definition.AnchorUtc.ToUniversalTime();
        long intervalTicks = definition.Interval.Ticks;
        long firstIndex = start <= anchor
            ? 0
            : DivideRoundUp((start - anchor).Ticks, intervalTicks);
        var results = new List<DateTimeOffset>(Math.Min(maximumResults, 128));
        for (long index = firstIndex; results.Count < maximumResults; index++)
        {
            long delta;
            try
            {
                delta = checked(index * intervalTicks);
            }
            catch (OverflowException)
            {
                break;
            }

            DateTimeOffset due;
            try
            {
                due = anchor.AddTicks(delta);
            }
            catch (ArgumentOutOfRangeException)
            {
                break;
            }

            if (due > end)
            {
                break;
            }

            results.Add(due);
        }

        return results;
    }

    private static IReadOnlyList<DateTimeOffset> CalculateFixedDelayInitial(
        FixedDelayScheduleDefinition definition,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        DateTimeOffset due = definition.StartImmediately
            ? start
            : definition.InitialDueAtUtc!.Value.ToUniversalTime();
        return due >= start && due <= end ? [due] : [];
    }

    private static DateTimeOffset[] Filter(
        IEnumerable<DateTimeOffset> candidates,
        DateTimeOffset start,
        DateTimeOffset end,
        int maximumResults) => candidates
            .Select(item => item.ToUniversalTime())
            .Where(item => item >= start && item <= end)
            .Distinct()
            .Order()
            .Take(maximumResults)
            .ToArray();

    private static long DivideRoundUp(long numerator, long denominator) =>
        checked((numerator + denominator - 1) / denominator);
}
