using System.Globalization;
using System.Text.Json;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Application.Scheduling;

/// <summary>Stable initial scheduling defaults for the Windows-first V1.</summary>
public static class SchedulingDefaults
{
    /// <summary>The initial Windows time-zone identifier used for new schedules.</summary>
    public const string DefaultWindowsTimeZoneId = "Central Standard Time";
}

/// <summary>A bounded, versioned retry policy captured with a schedule.</summary>
public sealed record RetryPolicyDefinition(
    int MaximumRetries,
    TimeSpan InitialDelay,
    TimeSpan MaximumDelay)
{
    /// <summary>Gets the policy that disables retries.</summary>
    public static RetryPolicyDefinition None { get; } = new(0, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));
}

/// <summary>Serializes and validates retry-policy JSON.</summary>
public static class RetryPolicyJson
{
    /// <summary>Serializes one validated retry policy into canonical JSON.</summary>
    public static string Serialize(RetryPolicyDefinition policy)
    {
        IReadOnlyList<ScheduleValidationError> errors = Validate(policy);
        if (errors.Count > 0)
        {
            throw new ScheduleDefinitionValidationException(errors);
        }

        return CanonicalJson.Serialize(JsonSerializer.SerializeToElement(new
        {
            version = "1.0",
            maximumRetries = policy.MaximumRetries,
            initialDelay = policy.InitialDelay.ToString("c", CultureInfo.InvariantCulture),
            maximumDelay = policy.MaximumDelay.ToString("c", CultureInfo.InvariantCulture),
        }));
    }

    /// <summary>Parses a persisted retry policy and returns path-aware errors.</summary>
    public static (RetryPolicyDefinition? Policy, IReadOnlyList<ScheduleValidationError> Errors) Parse(string json)
    {
        var errors = new List<ScheduleValidationError>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, [new("retryPolicy.invalidObject", "$.retryPolicy", "The retry policy must be an object.")]);
            }

            if (!root.TryGetProperty("version", out JsonElement version)
                || version.ValueKind != JsonValueKind.String
                || version.GetString() != "1.0")
            {
                errors.Add(new("retryPolicy.unsupportedVersion", "$.retryPolicy.version", "The retry-policy version is not supported."));
            }

            int retries = root.TryGetProperty("maximumRetries", out JsonElement retryElement) && retryElement.TryGetInt32(out int retryValue)
                ? retryValue
                : -1;
            TimeSpan initial = ReadDuration(root, "initialDelay", "$.retryPolicy.initialDelay", errors);
            TimeSpan maximum = ReadDuration(root, "maximumDelay", "$.retryPolicy.maximumDelay", errors);
            var policy = new RetryPolicyDefinition(retries, initial, maximum);
            errors.AddRange(Validate(policy));
            return (errors.Count == 0 ? policy : null, errors);
        }
        catch (JsonException)
        {
            return (null, [new("retryPolicy.invalidJson", "$.retryPolicy", "The retry policy contains invalid JSON.")]);
        }
    }

    /// <summary>Validates retry bounds.</summary>
    public static IReadOnlyList<ScheduleValidationError> Validate(RetryPolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var errors = new List<ScheduleValidationError>();
        if (policy.MaximumRetries is < 0 or > 10)
        {
            errors.Add(new("retryPolicy.maximumRetriesOutOfRange", "$.retryPolicy.maximumRetries", "Maximum retries must be between zero and ten."));
        }

        if (policy.InitialDelay < TimeSpan.FromSeconds(1) || policy.InitialDelay > TimeSpan.FromDays(1))
        {
            errors.Add(new("retryPolicy.initialDelayOutOfRange", "$.retryPolicy.initialDelay", "Initial retry delay must be between one second and one day."));
        }

        if (policy.MaximumDelay < policy.InitialDelay || policy.MaximumDelay > TimeSpan.FromDays(7))
        {
            errors.Add(new("retryPolicy.maximumDelayOutOfRange", "$.retryPolicy.maximumDelay", "Maximum retry delay must be at least the initial delay and no more than seven days."));
        }

        return errors;
    }

    private static TimeSpan ReadDuration(JsonElement root, string propertyName, string path, List<ScheduleValidationError> errors)
    {
        if (root.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && TimeSpan.TryParseExact(value.GetString(), "c", CultureInfo.InvariantCulture, out TimeSpan result))
        {
            return result;
        }

        errors.Add(new("retryPolicy.invalidDuration", path, "The retry delay is invalid."));
        return default;
    }
}

/// <summary>Bounded scheduler defaults independent of any host loop.</summary>
public sealed record SchedulingOptions(
    TimeSpan CalendarPlanningHorizon,
    TimeSpan MisfireLookback,
    int MaximumOccurrencesPerSchedule,
    TimeSpan ReconciliationLeaseDuration)
{
    /// <summary>Gets the V1 scheduler defaults.</summary>
    public static SchedulingOptions Default { get; } = new(
        TimeSpan.FromDays(7),
        TimeSpan.FromDays(7),
        100,
        TimeSpan.FromSeconds(30));

    /// <summary>Validates the bounds before scheduling performs side effects.</summary>
    public void Validate()
    {
        if (CalendarPlanningHorizon <= TimeSpan.Zero
            || CalendarPlanningHorizon > TimeSpan.FromDays(31)
            || MisfireLookback < TimeSpan.Zero
            || MisfireLookback > TimeSpan.FromDays(31)
            || MaximumOccurrencesPerSchedule is < 1 or > 1_000
            || ReconciliationLeaseDuration < TimeSpan.FromSeconds(5)
            || ReconciliationLeaseDuration > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(SchedulingOptions), "Scheduler options are outside supported bounds.");
        }
    }
}

/// <summary>Centralizes legal scheduler-owned occurrence transitions.</summary>
public static class OccurrenceStateMachine
{
    /// <summary>Returns whether the specified transition is legal.</summary>
    public static bool CanTransition(OccurrenceStatus from, OccurrenceStatus to) => (from, to) switch
    {
        (OccurrenceStatus.Planned, OccurrenceStatus.Ready or OccurrenceStatus.Skipped or OccurrenceStatus.CancellationRequested) => true,
        (OccurrenceStatus.Ready, OccurrenceStatus.Claimed or OccurrenceStatus.Skipped or OccurrenceStatus.CancellationRequested) => true,
        (OccurrenceStatus.Claimed, OccurrenceStatus.Starting or OccurrenceStatus.CancellationRequested
            or OccurrenceStatus.Failed or OccurrenceStatus.Abandoned) => true,
        (OccurrenceStatus.Starting, OccurrenceStatus.Running or OccurrenceStatus.CancellationRequested
            or OccurrenceStatus.Failed or OccurrenceStatus.TimedOut or OccurrenceStatus.Cancelled or OccurrenceStatus.Abandoned) => true,
        (OccurrenceStatus.Running, OccurrenceStatus.CancellationRequested
            or OccurrenceStatus.Completed or OccurrenceStatus.Failed or OccurrenceStatus.TimedOut
            or OccurrenceStatus.Cancelled or OccurrenceStatus.Abandoned) => true,
        (OccurrenceStatus.CancellationRequested, OccurrenceStatus.Completed or OccurrenceStatus.Failed
            or OccurrenceStatus.TimedOut or OccurrenceStatus.Cancelled or OccurrenceStatus.Abandoned) => true,
        _ => false,
    };

    /// <summary>Returns whether a state is terminal.</summary>
    public static bool IsTerminal(OccurrenceStatus status) => status is OccurrenceStatus.Skipped
        or OccurrenceStatus.Completed
        or OccurrenceStatus.Failed
        or OccurrenceStatus.TimedOut
        or OccurrenceStatus.Cancelled
        or OccurrenceStatus.Abandoned;

    /// <summary>Returns whether a state represents claimed or executing work.</summary>
    public static bool IsActive(OccurrenceStatus status) => status is OccurrenceStatus.Claimed
        or OccurrenceStatus.Starting
        or OccurrenceStatus.Running
        or OccurrenceStatus.CancellationRequested;
}
