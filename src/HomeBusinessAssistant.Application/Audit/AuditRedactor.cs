using System.Buffers;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeBusinessAssistant.Application.Configuration;

namespace HomeBusinessAssistant.Application.Audit;

/// <summary>Produces bounded JSON suitable for durable audit records without known secret material.</summary>
public static partial class AuditRedactor
{
    /// <summary>The largest UTF-8 audit payload accepted by the persistence boundary.</summary>
    public const int MaximumUtf8Bytes = 65_536;

    private static readonly string[] SensitivePropertyFragments =
    [
        "authorization",
        "cookie",
        "credential",
        "password",
        "pin",
        "secret",
        "token",
        "apikey",
        "api_key",
    ];

    /// <summary>Redacts known sensitive properties, secret references, bearer values, and URI user information.</summary>
    public static string Redact(JsonElement source)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteRedacted(writer, source, propertyName: null);
        }

        if (buffer.WrittenCount > MaximumUtf8Bytes)
        {
            return "{\"truncated\":true}";
        }

        using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
        return CanonicalJson.Serialize(document.RootElement);
    }

    /// <summary>Redacts known bearer values, secret references, and URI user information in bounded text.</summary>
    public static string RedactText(string value, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, 1);

        string redacted = RedactString(value);
        return redacted.Length <= maximumLength ? redacted : redacted[..maximumLength];
    }

    private static void WriteRedacted(Utf8JsonWriter writer, JsonElement value, string? propertyName)
    {
        if (IsSensitiveProperty(propertyName))
        {
            writer.WriteStringValue("[REDACTED]");
            return;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    WriteRedacted(writer, property.Value, property.Name);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in value.EnumerateArray())
                {
                    WriteRedacted(writer, item, propertyName: null);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(RedactString(value.GetString() ?? string.Empty));
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static bool IsSensitiveProperty(string? propertyName)
    {
        string normalized = propertyName?.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant() ?? string.Empty;
        return SensitivePropertyFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal));
    }

    private static string RedactString(string value)
    {
        if (value.StartsWith("secret://", StringComparison.Ordinal))
        {
            return "secret://[REDACTED]";
        }

        string redacted = BearerPattern().Replace(value, "$1[REDACTED]");
        return UriCredentialsPattern().Replace(redacted, "$1[REDACTED]@");
    }

    [GeneratedRegex("(?i)\\b(bearer\\s+)[A-Za-z0-9._~+\\-/]+=*")]
    private static partial Regex BearerPattern();

    [GeneratedRegex("(?i)(https?://)[^/\\s:@]+:[^@\\s/]+@")]
    private static partial Regex UriCredentialsPattern();
}
