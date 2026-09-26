using System.Text.Json;
using System.Text.Json.Serialization;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Domain.Agents;

namespace WakeRemote.Application;

/// <summary>The supported V1 remote-access provider types.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RemoteProviderKind>))]
public enum RemoteProviderKind
{
    /// <summary>Chrome Remote Desktop host health.</summary>
    ChromeRemoteDesktop,
    /// <summary>Windows Remote Desktop host health.</summary>
    WindowsRdp,
    /// <summary>Explicit non-production provider used by automated/local diagnostics.</summary>
    DiagnosticFake,
}

/// <summary>Optional conservative DNS readiness check.</summary>
public sealed record DnsReadinessProbeSettings(bool Enabled, string? HostName);

/// <summary>Optional conservative TCP readiness check.</summary>
public sealed record TcpReadinessProbeSettings(bool Enabled, string? Host, int Port);

/// <summary>Read-only provider health settings. No credentials are accepted.</summary>
public sealed record RemoteProviderSettings(
    RemoteProviderKind Kind,
    IReadOnlyList<string> ServiceNames,
    IReadOnlyList<string> ProcessNames,
    bool CheckLocalListener,
    string ListenerHost,
    int ListenerPort,
    bool DiagnosticReady);

/// <summary>Versioned Wake &amp; Remote agent configuration.</summary>
public sealed record WakeRemoteConfiguration(
    string SchemaVersion,
    string TimeZoneId,
    int NetworkReadyTimeoutSeconds,
    int NetworkProbeIntervalSeconds,
    int WindowHeartbeatIntervalSeconds,
    int MaximumWakeStalenessSeconds,
    bool KeepDisplayOn,
    bool ReleaseToNormalPowerPolicyAfterWindow,
    bool ForceSleepAfterWindow,
    RemoteProviderSettings RemoteProvider,
    DnsReadinessProbeSettings DnsProbe,
    TcpReadinessProbeSettings TcpProbe)
{
    /// <summary>The supported configuration schema.</summary>
    public const string CurrentSchemaVersion = "1.0";

    /// <summary>Hard V1 maximum availability-window duration.</summary>
    public static readonly TimeSpan MaximumWindowDuration = TimeSpan.FromHours(24);
}

/// <summary>Immutable arguments captured on one availability-window occurrence.</summary>
public sealed record WakeRemoteOccurrenceArguments(
    string WindowInstanceId,
    DateTimeOffset ScheduledWakeAtUtc,
    DateTimeOffset AvailableUntilUtc,
    bool RemoteProviderRequired,
    bool AllowImmediateMisfire = false);

/// <summary>Safe path-aware validation result for configuration or occurrence input.</summary>
public sealed record WakeRemoteValidationError(string Code, string Path, string Message);

/// <summary>Strict typed parsing and validation for Wake &amp; Remote execution input.</summary>
public static class WakeRemoteInput
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Parses and validates a typed configuration document.</summary>
    public static (WakeRemoteConfiguration? Value, IReadOnlyList<WakeRemoteValidationError> Errors) ParseConfiguration(JsonElement element)
    {
        if (ContainsSensitiveProperty(element))
        {
            return (null, [new(
                "wakeRemote.configuration.plaintextCredentialForbidden",
                "$",
                "Remote credentials and authentication material are not accepted in Wake Remote configuration.")]);
        }

        WakeRemoteConfiguration? value;
        try
        {
            value = element.Deserialize<WakeRemoteConfiguration>(Options);
        }
        catch (JsonException)
        {
            return (null, [new("wakeRemote.configuration.invalidJson", "$", "The Wake Remote configuration shape is invalid.")]);
        }

        return value is null
            ? (null, [new("wakeRemote.configuration.required", "$", "A Wake Remote configuration is required.")])
            : (value, ValidateConfiguration(value, element));
    }

    /// <summary>Parses and validates immutable occurrence arguments.</summary>
    public static (WakeRemoteOccurrenceArguments? Value, IReadOnlyList<WakeRemoteValidationError> Errors) ParseOccurrence(
        JsonElement element,
        WakeRemoteConfiguration configuration,
        DateTimeOffset nowUtc)
    {
        WakeRemoteOccurrenceArguments? value;
        try
        {
            value = element.Deserialize<WakeRemoteOccurrenceArguments>(Options);
        }
        catch (JsonException)
        {
            return (null, [new("wakeRemote.occurrence.invalidJson", "$", "The availability-window arguments are invalid.")]);
        }

        if (value is null)
        {
            return (null, [new("wakeRemote.occurrence.required", "$", "Availability-window arguments are required.")]);
        }

        var errors = new List<WakeRemoteValidationError>();
        if (string.IsNullOrWhiteSpace(value.WindowInstanceId)
            || value.WindowInstanceId.Length > 128
            || value.WindowInstanceId.Any(char.IsControl))
        {
            errors.Add(new("wakeRemote.occurrence.invalidWindowId", "$.windowInstanceId", "A bounded window instance identifier is required."));
        }

        DateTimeOffset scheduledUtc = value.ScheduledWakeAtUtc.ToUniversalTime();
        DateTimeOffset untilUtc = value.AvailableUntilUtc.ToUniversalTime();
        if (untilUtc <= scheduledUtc)
        {
            errors.Add(new("wakeRemote.occurrence.invalidWindow", "$.availableUntilUtc", "The availability deadline must be after the scheduled wake time."));
        }
        else if (untilUtc - scheduledUtc > WakeRemoteConfiguration.MaximumWindowDuration)
        {
            errors.Add(new("wakeRemote.occurrence.windowTooLong", "$.availableUntilUtc", "The availability window cannot exceed 24 hours."));
        }

        if (!value.AllowImmediateMisfire
            && nowUtc.ToUniversalTime() - scheduledUtc > TimeSpan.FromSeconds(configuration.MaximumWakeStalenessSeconds))
        {
            errors.Add(new("wakeRemote.occurrence.tooStale", "$.scheduledWakeAtUtc", "The scheduled wake is outside the configured staleness allowance."));
        }

        return (errors.Count == 0 ? value with
        {
            ScheduledWakeAtUtc = scheduledUtc,
            AvailableUntilUtc = untilUtc,
        } : null, errors);
    }

    /// <summary>Serializes occurrence arguments using the stable camel-case contract.</summary>
    public static string SerializeOccurrence(WakeRemoteOccurrenceArguments value) =>
        JsonSerializer.Serialize(value, Options);

    private static List<WakeRemoteValidationError> ValidateConfiguration(
        WakeRemoteConfiguration value,
        JsonElement raw)
    {
        var errors = new List<WakeRemoteValidationError>();
        if (value.SchemaVersion != WakeRemoteConfiguration.CurrentSchemaVersion)
        {
            errors.Add(new("wakeRemote.configuration.unsupportedSchema", "$.schemaVersion", "The Wake Remote configuration schema is unsupported."));
        }

        if (string.IsNullOrWhiteSpace(value.TimeZoneId)
            || value.TimeZoneId.Length > 128
            || !TryFindTimeZone(value.TimeZoneId))
        {
            errors.Add(new("wakeRemote.configuration.invalidTimeZone", "$.timeZoneId", "The configured time zone is unavailable."));
        }

        if (value.NetworkReadyTimeoutSeconds is < 1 or > 900)
        {
            errors.Add(new("wakeRemote.configuration.networkTimeoutOutOfRange", "$.networkReadyTimeoutSeconds", "Network timeout must be between 1 and 900 seconds."));
        }

        if (value.NetworkProbeIntervalSeconds is < 1 or > 60
            || value.NetworkProbeIntervalSeconds > value.NetworkReadyTimeoutSeconds)
        {
            errors.Add(new("wakeRemote.configuration.networkIntervalOutOfRange", "$.networkProbeIntervalSeconds", "Network probe interval must be between 1 and 60 seconds and not exceed the timeout."));
        }

        if (value.WindowHeartbeatIntervalSeconds is < 1 or > 300)
        {
            errors.Add(new("wakeRemote.configuration.heartbeatOutOfRange", "$.windowHeartbeatIntervalSeconds", "Window heartbeat interval must be between 1 and 300 seconds."));
        }

        if (value.MaximumWakeStalenessSeconds is < 0 or > 86_400)
        {
            errors.Add(new("wakeRemote.configuration.stalenessOutOfRange", "$.maximumWakeStalenessSeconds", "Wake staleness must be between zero and 86400 seconds."));
        }

        if (!value.ReleaseToNormalPowerPolicyAfterWindow)
        {
            errors.Add(new("wakeRemote.configuration.releaseRequired", "$.releaseToNormalPowerPolicyAfterWindow", "V1 must release to normal Windows power policy after the window."));
        }

        if (value.ForceSleepAfterWindow)
        {
            errors.Add(new("wakeRemote.configuration.forceSleepUnsupported", "$.forceSleepAfterWindow", "Forced sleep is unsupported in V1."));
        }

        ValidateProvider(value.RemoteProvider, errors);
        ValidateOptionalProbes(value, errors);
        return errors;
    }

    private static void ValidateProvider(RemoteProviderSettings? provider, List<WakeRemoteValidationError> errors)
    {
        if (provider is null || !Enum.IsDefined(provider.Kind))
        {
            errors.Add(new("wakeRemote.configuration.invalidProvider", "$.remoteProvider.kind", "A supported remote provider is required."));
            return;
        }

        if (provider.ServiceNames is null || provider.ProcessNames is null
            || provider.ServiceNames.Count + provider.ProcessNames.Count > 16
            || provider.ServiceNames.Concat(provider.ProcessNames).Any(name => !IsSafeLocalName(name)))
        {
            errors.Add(new("wakeRemote.configuration.invalidProviderNames", "$.remoteProvider", "Provider service/process names must be bounded local names."));
            return;
        }

        if (provider.Kind != RemoteProviderKind.DiagnosticFake
            && (provider.ServiceNames.Count + provider.ProcessNames.Count == 0))
        {
            errors.Add(new("wakeRemote.configuration.providerNotConfigured", "$.remoteProvider", "At least one service or process name is required."));
        }

        if (provider.CheckLocalListener
            && (!IsLoopbackHost(provider.ListenerHost) || provider.ListenerPort is < 1 or > 65_535))
        {
            errors.Add(new("wakeRemote.configuration.invalidLocalListener", "$.remoteProvider.listenerHost", "Only a valid loopback listener may be checked."));
        }
    }

    private static void ValidateOptionalProbes(WakeRemoteConfiguration value, List<WakeRemoteValidationError> errors)
    {
        if (value.DnsProbe is null
            || (value.DnsProbe.Enabled && !IsSafeHost(value.DnsProbe.HostName)))
        {
            errors.Add(new("wakeRemote.configuration.invalidDnsProbe", "$.dnsProbe", "An enabled DNS probe requires a bounded hostname."));
        }

        if (value.TcpProbe is null
            || (value.TcpProbe.Enabled && (!IsSafeHost(value.TcpProbe.Host) || value.TcpProbe.Port is < 1 or > 65_535)))
        {
            errors.Add(new("wakeRemote.configuration.invalidTcpProbe", "$.tcpProbe", "An enabled TCP probe requires a bounded host and valid port."));
        }
    }

    private static bool ContainsSensitiveProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                string normalized = property.Name.ToLowerInvariant();
                if (normalized.Contains("password", StringComparison.Ordinal)
                    || normalized.Contains("credential", StringComparison.Ordinal)
                    || normalized.Contains("secret", StringComparison.Ordinal)
                    || normalized.Contains("token", StringComparison.Ordinal)
                    || normalized.Contains("cookie", StringComparison.Ordinal)
                    || normalized is "pin" or "apikey" or "authorization")
                {
                    return true;
                }

                if (ContainsSensitiveProperty(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(ContainsSensitiveProperty);
        }

        return false;
    }

    private static bool IsSafeLocalName(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ' ');

    private static bool IsSafeHost(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 253
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or ':');

    private static bool IsLoopbackHost(string? value) =>
        string.Equals(value, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "127.0.0.1", StringComparison.Ordinal)
        || string.Equals(value, "::1", StringComparison.Ordinal);

    private static bool TryFindTimeZone(string value)
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(value);
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
}

/// <summary>Exposes Wake Remote validation through the platform configuration boundary.</summary>
public sealed class WakeRemoteConfigurationValidator : IAgentConfigurationValidator
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ConfigurationValidationError>> ValidateAsync(
        AgentId agentId,
        string schemaVersion,
        JsonElement configuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (agentId != WakeRemoteDefaults.AgentId)
        {
            return ValueTask.FromResult<IReadOnlyList<ConfigurationValidationError>>([]);
        }

        (_, IReadOnlyList<WakeRemoteValidationError> errors) = WakeRemoteInput.ParseConfiguration(configuration);
        if (schemaVersion != WakeRemoteConfiguration.CurrentSchemaVersion)
        {
            errors = errors.Concat([
                new WakeRemoteValidationError("wakeRemote.configuration.schemaMismatch", "$.schemaVersion", "The persisted schema version does not match the document."),
            ]).ToArray();
        }

        return ValueTask.FromResult<IReadOnlyList<ConfigurationValidationError>>(
            errors.Select(error => new ConfigurationValidationError(error.Code, error.Path, error.Message)).ToArray());
    }
}

/// <summary>Built-in safe defaults installed only when Wake Remote has no configuration.</summary>
public sealed class WakeRemoteDefaults : IAgentDefaultConfigurationProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The stable Wake Remote agent identifier.</summary>
    public static AgentId AgentId { get; } = AgentId.Parse("wake-remote");

    /// <inheritdoc />
    public AgentDefaultConfiguration GetDefault()
    {
        string timeZoneId = TimeZoneInfo.Local.Id;
        JsonElement value = JsonSerializer.SerializeToElement(new WakeRemoteConfiguration(
            WakeRemoteConfiguration.CurrentSchemaVersion,
            timeZoneId,
            NetworkReadyTimeoutSeconds: 120,
            NetworkProbeIntervalSeconds: 5,
            WindowHeartbeatIntervalSeconds: 60,
            MaximumWakeStalenessSeconds: 900,
            KeepDisplayOn: false,
            ReleaseToNormalPowerPolicyAfterWindow: true,
            ForceSleepAfterWindow: false,
            new(
                RemoteProviderKind.ChromeRemoteDesktop,
                ServiceNames: ["chromoting"],
                ProcessNames: ["remoting_host"],
                CheckLocalListener: false,
                ListenerHost: "127.0.0.1",
                ListenerPort: 3389,
                DiagnosticReady: false),
            new(Enabled: false, HostName: null),
            new(Enabled: false, Host: null, Port: 0)), SerializerOptions);
        return new(
            AgentId,
            WakeRemoteConfiguration.CurrentSchemaVersion,
            value,
            "Installed safe Wake Remote defaults; remote provider names may require local adjustment.");
    }
}
