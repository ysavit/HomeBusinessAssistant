using System.Globalization;
using System.Text.Json;

namespace HomeBusinessAssistant.Application.Configuration;

/// <summary>One platform-owned form conversion error for a generic configuration field.</summary>
public sealed record GenericConfigurationFormError(string FieldName, string Message);

/// <summary>Result of converting bounded string form values into a typed configuration object.</summary>
public sealed record GenericConfigurationFormResult(
    JsonElement? Configuration,
    IReadOnlyList<GenericConfigurationFormError> Errors)
{
    /// <summary>Gets whether a typed object was produced.</summary>
    public bool IsValid => Configuration.HasValue && Errors.Count == 0;
}

/// <summary>
/// Shared conversion used by Agent Settings and onboarding. Authoritative validation still belongs to
/// <see cref="GenericAgentConfigurationValidator"/> and the immutable configuration service.
/// </summary>
public static class GenericConfigurationFormCodec
{
    /// <summary>Formats the current document into display-safe primitive values.</summary>
    public static IReadOnlyDictionary<string, string> ReadValues(
        GenericConfigurationSchema schema,
        JsonElement configuration)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (configuration.ValueKind != JsonValueKind.Object)
        {
            return values;
        }

        foreach (GenericConfigurationField field in schema.Fields)
        {
            if (!configuration.TryGetProperty(field.Name, out JsonElement value))
            {
                continue;
            }

            values[field.Name] = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Array => string.Join(Environment.NewLine, value.EnumerateArray().Select(FormatPrimitive)),
                _ => value.GetRawText(),
            };
        }

        return values;
    }

    /// <summary>Builds a typed JSON object from platform-owned primitive form controls.</summary>
    public static GenericConfigurationFormResult Build(
        GenericConfigurationSchema schema,
        IReadOnlyDictionary<string, string> submittedValues)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(submittedValues);
        if (!schema.SupportsGenericEditor)
        {
            return new(null, [new(string.Empty, "This schema requires a typed configuration adapter.")]);
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var errors = new List<GenericConfigurationFormError>();
        foreach (GenericConfigurationField field in schema.Fields)
        {
            if (field.ConstantValue is JsonElement constant)
            {
                values.Add(field.Name, constant.Clone());
                continue;
            }

            submittedValues.TryGetValue(field.Name, out string? text);
            text ??= string.Empty;
            if (!field.Required && string.IsNullOrEmpty(text))
            {
                continue;
            }

            object? value = field.Kind switch
            {
                GenericConfigurationValueKind.Text => text,
                GenericConfigurationValueKind.WholeNumber when long.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long whole) => whole,
                GenericConfigurationValueKind.Number when decimal.TryParse(
                    text,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out decimal number) => number,
                GenericConfigurationValueKind.Boolean when bool.TryParse(text, out bool boolean) => boolean,
                GenericConfigurationValueKind.Array => ParseArray(field, text),
                _ => null,
            };
            if (value is null)
            {
                errors.Add(new(field.Name, field.Kind == GenericConfigurationValueKind.Array
                    ? "Enter one value per line using the item type required by the installed schema."
                    : "Enter a value using the type required by the installed schema."));
            }
            else
            {
                values.Add(field.Name, value);
            }
        }

        return errors.Count == 0
            ? new(JsonSerializer.SerializeToElement(values), [])
            : new(null, errors);
    }

    private static object? ParseArray(GenericConfigurationField field, string text)
    {
        string[] lines = text.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try
        {
            return field.ItemKind switch
            {
                GenericConfigurationValueKind.Text => lines,
                GenericConfigurationValueKind.WholeNumber => lines.Select(value => long.Parse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture)).ToArray(),
                GenericConfigurationValueKind.Number => lines.Select(value => decimal.Parse(
                    value,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture)).ToArray(),
                _ => null,
            };
        }
        catch (FormatException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static string FormatPrimitive(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString() ?? string.Empty
        : value.GetRawText();
}
