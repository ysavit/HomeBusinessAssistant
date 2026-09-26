using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.AgentSdk.Protocol;

internal static class AgentJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 64,
        };

        options.Converters.Add(new AgentIdJsonConverter());
        options.Converters.Add(new AgentVersionJsonConverter());
        options.Converters.Add(new AgentRunIdJsonConverter());
        options.Converters.Add(new OccurrenceIdJsonConverter());
        options.Converters.Add(new AgentProtocolVersionJsonConverter());
        options.Converters.Add(new UtcDateTimeOffsetJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter<AgentRunStatus>(allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<ConcurrencyPolicy>(allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<TriggerType>(allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<WakePolicy>(allowIntegerValues: false));
        options.Converters.Add(new JsonStringEnumConverter<MisfirePolicy>(allowIntegerValues: false));
        return options;
    }

    private sealed class AgentIdJsonConverter : JsonConverter<AgentId>
    {
        public override AgentId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && AgentId.TryParse(reader.GetString(), out var value)
                ? value
                : throw new JsonException("The agent identifier is invalid.");

        public override void Write(Utf8JsonWriter writer, AgentId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }

    private sealed class AgentVersionJsonConverter : JsonConverter<AgentVersion>
    {
        public override AgentVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && AgentVersion.TryParse(reader.GetString(), out var value)
                ? value
                : throw new JsonException("The agent version is invalid.");

        public override void Write(Utf8JsonWriter writer, AgentVersion value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }

    private sealed class AgentRunIdJsonConverter : JsonConverter<AgentRunId>
    {
        public override AgentRunId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && AgentRunId.TryParse(reader.GetString(), out var value)
                ? value
                : throw new JsonException("The run identifier is invalid.");

        public override void Write(Utf8JsonWriter writer, AgentRunId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    private sealed class OccurrenceIdJsonConverter : JsonConverter<OccurrenceId>
    {
        public override OccurrenceId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && OccurrenceId.TryParse(reader.GetString(), out var value)
                ? value
                : throw new JsonException("The occurrence identifier is invalid.");

        public override void Write(Utf8JsonWriter writer, OccurrenceId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    private sealed class AgentProtocolVersionJsonConverter : JsonConverter<AgentProtocolVersion>
    {
        public override AgentProtocolVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && AgentProtocolVersion.TryParse(reader.GetString(), out var value)
                ? value
                : throw new JsonException("The protocol version is invalid.");

        public override void Write(Utf8JsonWriter writer, AgentProtocolVersion value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    private sealed class UtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String
                && DateTimeOffset.TryParse(
                    reader.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var value))
            {
                return value;
            }

            throw new JsonException("The timestamp is invalid.");
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
    }
}
