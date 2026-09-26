using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HomeBusinessAssistant.Application.Configuration;

/// <summary>Creates deterministic compact JSON and SHA-256 identities.</summary>
public static class CanonicalJson
{
    private const int MaximumDepth = 64;

    /// <summary>Canonicalizes a JSON value by recursively sorting object properties ordinally.</summary>
    public static string Serialize(JsonElement value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, MaxDepth = MaximumDepth }))
        {
            WriteElement(writer, value, 0);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Returns lower-case SHA-256 hex for canonical UTF-8 JSON.</summary>
    public static string ComputeHash(string canonicalJson)
    {
        ArgumentNullException.ThrowIfNull(canonicalJson);
        byte[] bytes = Encoding.UTF8.GetBytes(canonicalJson);
        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement value, int depth)
    {
        if (depth > MaximumDepth)
        {
            throw new JsonException("JSON exceeds the supported nesting depth.");
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteElement(writer, property.Value, depth + 1);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in value.EnumerateArray())
                {
                    WriteElement(writer, item, depth + 1);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                WriteNumber(writer, value);
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, JsonElement value)
    {
        string raw = value.GetRawText();
        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal decimalValue))
        {
            string normalized = decimalValue == 0
                ? "0"
                : decimalValue.ToString("G29", CultureInfo.InvariantCulture);
            writer.WriteRawValue(normalized, skipInputValidation: false);
            return;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue)
            && double.IsFinite(doubleValue))
        {
            writer.WriteNumberValue(doubleValue);
            return;
        }

        throw new JsonException("JSON contains a number outside the supported finite range.");
    }
}
