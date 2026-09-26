using System.Text.Json;
using System.Text.RegularExpressions;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Application.Configuration;

/// <summary>The primitive input kinds supported by the safe generic configuration editor.</summary>
public enum GenericConfigurationValueKind
{
    /// <summary>A UTF-8 text value.</summary>
    Text,
    /// <summary>A signed whole number.</summary>
    WholeNumber,
    /// <summary>A finite decimal number.</summary>
    Number,
    /// <summary>A Boolean value.</summary>
    Boolean,
    /// <summary>A simple array whose item type is described separately.</summary>
    Array,
    /// <summary>A constant supplied by the schema and not edited by a user.</summary>
    Constant,
}

/// <summary>Safe server-owned metadata for one generic configuration field.</summary>
public sealed record GenericConfigurationField(
    string Name,
    string Title,
    string? Description,
    GenericConfigurationValueKind Kind,
    GenericConfigurationValueKind? ItemKind,
    bool Required,
    bool IsSecretReference,
    JsonElement? DefaultValue,
    JsonElement? ConstantValue,
    IReadOnlyList<string> AllowedValues,
    decimal? Minimum,
    decimal? Maximum,
    int? MinimumLength,
    int? MaximumLength,
    int? MinimumItems,
    int? MaximumItems,
    string? Pattern);

/// <summary>A parsed, bounded JSON configuration schema.</summary>
public sealed record GenericConfigurationSchema(
    string Title,
    string? Description,
    string SchemaVersion,
    IReadOnlyList<GenericConfigurationField> Fields,
    bool SupportsGenericEditor,
    string? UnsupportedReason)
{
    /// <summary>Builds a default configuration document using constants, declared defaults, and required primitive defaults.</summary>
    public JsonElement CreateDefaultDocument()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (GenericConfigurationField field in Fields)
        {
            if (field.ConstantValue is JsonElement constant)
            {
                values.Add(field.Name, constant.Clone());
            }
            else if (field.DefaultValue is JsonElement defaultValue)
            {
                values.Add(field.Name, defaultValue.Clone());
            }
            else if (field.Required)
            {
                values.Add(field.Name, field.Kind switch
                {
                    GenericConfigurationValueKind.Text => string.Empty,
                    GenericConfigurationValueKind.WholeNumber => 0,
                    GenericConfigurationValueKind.Number => 0m,
                    GenericConfigurationValueKind.Boolean => false,
                    GenericConfigurationValueKind.Array => Array.Empty<object>(),
                    _ => null,
                });
            }
        }

        return JsonSerializer.SerializeToElement(values);
    }
}

/// <summary>The result of parsing one schema file.</summary>
public sealed record GenericConfigurationSchemaParseResult(
    GenericConfigurationSchema? Schema,
    IReadOnlyList<ConfigurationValidationError> Errors)
{
    /// <summary>Gets whether the schema itself is valid.</summary>
    public bool IsValid => Schema is not null && Errors.Count == 0;
}

/// <summary>Finds and parses configuration schemas for installed agents.</summary>
public interface IAgentConfigurationSchemaCatalog
{
    /// <summary>Gets one installed schema, or null when no package schema exists.</summary>
    ValueTask<GenericConfigurationSchema?> GetAsync(
        AgentId agentId,
        CancellationToken cancellationToken = default);
}

/// <summary>Parses the deliberately small JSON Schema subset supported by the generic editor.</summary>
public static class GenericConfigurationSchemaParser
{
    private const int MaximumFields = 64;
    private static readonly HashSet<string> AllowedRootKeywords = new(StringComparer.Ordinal)
    {
        "$schema", "$id", "title", "description", "type", "required", "properties", "additionalProperties",
    };

    /// <summary>Parses a schema without retaining caller-owned JSON storage.</summary>
    public static GenericConfigurationSchemaParseResult Parse(JsonElement root, string expectedSchemaVersion)
    {
        var errors = new List<ConfigurationValidationError>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Invalid("schema.rootMustBeObject", "$", "The configuration schema must be a JSON object.");
        }

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!AllowedRootKeywords.Contains(property.Name))
            {
                errors.Add(new("schema.unsupportedRootKeyword", $"$.{property.Name}", "The schema uses an unsupported root keyword."));
            }
        }

        if (!TryGetString(root, "type", out string? rootType) || rootType != "object")
        {
            errors.Add(new("schema.rootType", "$.type", "The configuration schema root type must be object."));
        }

        if (!root.TryGetProperty("additionalProperties", out JsonElement additional)
            || additional.ValueKind != JsonValueKind.False)
        {
            errors.Add(new("schema.additionalProperties", "$.additionalProperties", "The configuration schema must reject additional properties."));
        }

        string title = TryGetString(root, "title", out string? titleValue) && titleValue!.Length is >= 1 and <= 120
            ? titleValue
            : "Agent configuration";
        string? description = TryGetString(root, "description", out string? descriptionValue)
            && descriptionValue!.Length <= 1_000 ? descriptionValue : null;
        if (!root.TryGetProperty("properties", out JsonElement properties)
            || properties.ValueKind != JsonValueKind.Object
            || properties.GetPropertyCount() > MaximumFields)
        {
            errors.Add(new("schema.invalidProperties", "$.properties", $"The schema must define at most {MaximumFields} properties."));
            return new(null, errors);
        }

        HashSet<string> required = ParseRequired(root, properties, errors);
        var fields = new List<GenericConfigurationField>();
        var unsupported = new List<string>();
        foreach (JsonProperty property in properties.EnumerateObject())
        {
            if (!IsSafePropertyName(property.Name) || property.Value.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new("schema.invalidProperty", $"$.properties.{property.Name}", "The property name or definition is invalid."));
                continue;
            }

            GenericConfigurationField? field = ParseField(property.Name, property.Value, required.Contains(property.Name), errors, unsupported);
            if (field is not null)
            {
                fields.Add(field);
            }
        }

        if (errors.Count > 0)
        {
            return new(null, errors);
        }

        bool supportsGenericEditor = unsupported.Count == 0;
        return new(new(
            title,
            description,
            expectedSchemaVersion,
            fields,
            supportsGenericEditor,
            supportsGenericEditor ? null : $"A typed adapter is required for: {string.Join(", ", unsupported)}."), []);
    }

    private static GenericConfigurationField? ParseField(
        string name,
        JsonElement definition,
        bool required,
        List<ConfigurationValidationError> errors,
        List<string> unsupported)
    {
        string path = $"$.properties.{name}";
        string title = TryGetString(definition, "title", out string? titleValue) && titleValue!.Length is >= 1 and <= 120
            ? titleValue
            : SplitTitle(name);
        string? description = TryGetString(definition, "description", out string? descriptionValue)
            && descriptionValue!.Length <= 1_000 ? descriptionValue : null;

        if (definition.TryGetProperty("const", out JsonElement constant))
        {
            if (!IsPrimitive(constant))
            {
                unsupported.Add(name);
                return null;
            }

            return new(name, title, description, GenericConfigurationValueKind.Constant, null, required, false,
                null, constant.Clone(), [], null, null, null, null, null, null, null);
        }

        if (!TryGetString(definition, "type", out string? type))
        {
            unsupported.Add(name);
            return null;
        }

        GenericConfigurationValueKind kind;
        GenericConfigurationValueKind? itemKind = null;
        switch (type)
        {
            case "string": kind = GenericConfigurationValueKind.Text; break;
            case "integer": kind = GenericConfigurationValueKind.WholeNumber; break;
            case "number": kind = GenericConfigurationValueKind.Number; break;
            case "boolean": kind = GenericConfigurationValueKind.Boolean; break;
            case "array":
                kind = GenericConfigurationValueKind.Array;
                if (!definition.TryGetProperty("items", out JsonElement items)
                    || items.ValueKind != JsonValueKind.Object
                    || !TryGetString(items, "type", out string? itemType)
                    || !TryMapArrayItem(itemType!, out GenericConfigurationValueKind mapped))
                {
                    unsupported.Add(name);
                    return null;
                }

                itemKind = mapped;
                break;
            default:
                unsupported.Add(name);
                return null;
        }

        bool secretReference = TryGetString(definition, "format", out string? format)
            && string.Equals(format, "hba-secret-reference", StringComparison.Ordinal);
        if (secretReference && kind != GenericConfigurationValueKind.Text)
        {
            errors.Add(new("schema.invalidSecretReference", $"{path}.format", "A secret reference field must be a string."));
        }

        JsonElement? defaultValue = definition.TryGetProperty("default", out JsonElement declaredDefault)
            ? declaredDefault.Clone() : null;
        IReadOnlyList<string> allowedValues = ParseEnum(definition, path, errors);
        decimal? minimum = ReadDecimal(definition, "minimum", path, errors);
        decimal? maximum = ReadDecimal(definition, "maximum", path, errors);
        int? minimumLength = ReadBound(definition, "minLength", path, 0, 65_536, errors);
        int? maximumLength = ReadBound(definition, "maxLength", path, 0, 65_536, errors);
        int? minimumItems = ReadBound(definition, "minItems", path, 0, 256, errors);
        int? maximumItems = ReadBound(definition, "maxItems", path, 0, 256, errors);
        string? pattern = TryGetString(definition, "pattern", out string? patternValue) ? patternValue : null;
        if (pattern is { Length: > 256 })
        {
            errors.Add(new("schema.invalidPattern", $"{path}.pattern", "The pattern is too long."));
        }
        else if (pattern is not null)
        {
            try
            {
                _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            }
            catch (ArgumentException)
            {
                errors.Add(new("schema.invalidPattern", $"{path}.pattern", "The pattern is invalid."));
            }
        }

        return new(name, title, description, kind, itemKind, required, secretReference, defaultValue,
            null, allowedValues, minimum, maximum, minimumLength, maximumLength, minimumItems, maximumItems, pattern);
    }

    private static HashSet<string> ParseRequired(
        JsonElement root,
        JsonElement properties,
        List<ConfigurationValidationError> errors)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("required", out JsonElement value)) return required;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > MaximumFields)
        {
            errors.Add(new("schema.invalidRequired", "$.required", "The required property list is invalid."));
            return required;
        }

        foreach (JsonElement item in value.EnumerateArray())
        {
            string? name = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (name is null || !properties.TryGetProperty(name, out _) || !required.Add(name))
            {
                errors.Add(new("schema.invalidRequired", "$.required", "The required property list is invalid."));
            }
        }

        return required;
    }

    private static List<string> ParseEnum(JsonElement definition, string path, List<ConfigurationValidationError> errors)
    {
        if (!definition.TryGetProperty("enum", out JsonElement values)) return [];
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is < 1 or > 64)
        {
            errors.Add(new("schema.invalidEnum", $"{path}.enum", "The allowed-value list is invalid."));
            return [];
        }

        var result = new List<string>();
        foreach (JsonElement item in values.EnumerateArray())
        {
            string? value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (string.IsNullOrEmpty(value) || value.Length > 256)
            {
                errors.Add(new("schema.invalidEnum", $"{path}.enum", "Allowed values must be bounded non-empty strings."));
            }
            else if (!result.Contains(value, StringComparer.Ordinal))
            {
                result.Add(value);
            }
            else
            {
                errors.Add(new("schema.invalidEnum", $"{path}.enum", "The allowed-value list contains duplicates."));
            }
        }
        return result;
    }

    private static decimal? ReadDecimal(JsonElement value, string name, string path, List<ConfigurationValidationError> errors)
    {
        if (!value.TryGetProperty(name, out JsonElement property)) return null;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out decimal number)) return number;
        errors.Add(new("schema.invalidNumericBound", $"{path}.{name}", "The numeric bound is invalid."));
        return null;
    }

    private static int? ReadBound(JsonElement value, string name, string path, int minimum, int maximum, List<ConfigurationValidationError> errors)
    {
        if (!value.TryGetProperty(name, out JsonElement property)) return null;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int number) && number >= minimum && number <= maximum) return number;
        errors.Add(new("schema.invalidBound", $"{path}.{name}", "The declared bound is invalid."));
        return null;
    }

    private static bool TryMapArrayItem(string value, out GenericConfigurationValueKind kind)
    {
        kind = value switch
        {
            "string" => GenericConfigurationValueKind.Text,
            "integer" => GenericConfigurationValueKind.WholeNumber,
            "number" => GenericConfigurationValueKind.Number,
            _ => default,
        };
        return value is "string" or "integer" or "number";
    }

    private static bool TryGetString(JsonElement value, string name, out string? result)
    {
        result = value.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() : null;
        return result is not null;
    }

    private static bool IsPrimitive(JsonElement value) => value.ValueKind is
        JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;

    private static bool IsSafePropertyName(string value) => value.Length is >= 1 and <= 64
        && (char.IsLetter(value[0]) || value[0] == '_')
        && value.All(character => char.IsLetterOrDigit(character) || character is '_' or '-');

    private static string SplitTitle(string value) => Regex.Replace(value, "(?<!^)([A-Z])", " $1", RegexOptions.CultureInvariant);

    private static GenericConfigurationSchemaParseResult Invalid(string code, string path, string message) =>
        new(null, [new(code, path, message)]);
}

/// <summary>Authoritatively validates configurations for agents using the generic schema path.</summary>
public sealed class GenericAgentConfigurationValidator(
    IAgentConfigurationSchemaCatalog catalog,
    IReadOnlySet<AgentId> typedAdapterAgents) : IAgentConfigurationValidator
{
    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ConfigurationValidationError>> ValidateAsync(
        AgentId agentId,
        string schemaVersion,
        JsonElement configuration,
        CancellationToken cancellationToken = default)
    {
        if (typedAdapterAgents.Contains(agentId)) return [];
        GenericConfigurationSchema? schema = await catalog.GetAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (schema is null)
        {
            return [new("configuration.schemaMissing", "$", "The installed agent configuration schema is unavailable.")];
        }

        if (!schema.SupportsGenericEditor)
        {
            return [new("configuration.customAdapterRequired", "$", "This configuration schema requires a typed platform adapter.")];
        }

        if (!string.Equals(schema.SchemaVersion, schemaVersion, StringComparison.Ordinal))
        {
            return [new("configuration.schemaVersionMismatch", "$.schemaVersion", "The configuration schema version does not match the installed agent manifest.")];
        }

        // The baseline validator owns the root-type error. Avoid enumerating a
        // non-object here so validation remains composable and side-effect free.
        if (configuration.ValueKind != JsonValueKind.Object) return [];

        var errors = new List<ConfigurationValidationError>();
        var known = schema.Fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);
        foreach (JsonProperty property in configuration.EnumerateObject())
        {
            if (!known.Contains(property.Name)) errors.Add(new("configuration.additionalProperty", $"$.{property.Name}", "The property is not declared by the installed schema."));
        }

        foreach (GenericConfigurationField field in schema.Fields)
        {
            bool exists = configuration.TryGetProperty(field.Name, out JsonElement value);
            if (!exists)
            {
                if (field.Required) errors.Add(new("configuration.required", $"$.{field.Name}", "A required value is missing."));
                continue;
            }

            ValidateField(field, value, errors);
        }

        return errors;
    }

    private static void ValidateField(GenericConfigurationField field, JsonElement value, List<ConfigurationValidationError> errors)
    {
        string path = $"$.{field.Name}";
        if (field.ConstantValue is JsonElement constant)
        {
            if (JsonElement.DeepEquals(constant, value)) return;
            errors.Add(new("configuration.constant", path, "The value must match the installed schema constant."));
            return;
        }

        bool typeValid = field.Kind switch
        {
            GenericConfigurationValueKind.Text => value.ValueKind == JsonValueKind.String,
            GenericConfigurationValueKind.WholeNumber => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            GenericConfigurationValueKind.Number => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            GenericConfigurationValueKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            GenericConfigurationValueKind.Array => value.ValueKind == JsonValueKind.Array,
            _ => false,
        };
        if (!typeValid)
        {
            errors.Add(new("configuration.type", path, "The value has the wrong type."));
            return;
        }

        if (field.Kind == GenericConfigurationValueKind.Text)
        {
            string text = value.GetString()!;
            if (field.MinimumLength is int minimumLength && text.Length < minimumLength) errors.Add(new("configuration.minLength", path, "The value is too short."));
            if (field.MaximumLength is int maximumLength && text.Length > maximumLength) errors.Add(new("configuration.maxLength", path, "The value is too long."));
            if (field.AllowedValues.Count > 0 && !field.AllowedValues.Contains(text, StringComparer.Ordinal)) errors.Add(new("configuration.enum", path, "The value is not allowed."));
            if (field.Pattern is not null && !Regex.IsMatch(text, field.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) errors.Add(new("configuration.pattern", path, "The value does not match the required format."));
            if (field.IsSecretReference && !Secrets.SecretReference.TryParse(text, out _)) errors.Add(new("configuration.secretReference", path, "The value must be an opaque secret reference."));
        }
        else if (field.Kind is GenericConfigurationValueKind.WholeNumber or GenericConfigurationValueKind.Number)
        {
            decimal number = value.GetDecimal();
            if (field.Minimum is decimal minimum && number < minimum) errors.Add(new("configuration.minimum", path, "The value is below the allowed minimum."));
            if (field.Maximum is decimal maximum && number > maximum) errors.Add(new("configuration.maximum", path, "The value exceeds the allowed maximum."));
        }
        else if (field.Kind == GenericConfigurationValueKind.Array)
        {
            int count = value.GetArrayLength();
            if (field.MinimumItems is int minimumItems && count < minimumItems) errors.Add(new("configuration.minItems", path, "The array has too few items."));
            if (field.MaximumItems is int maximumItems && count > maximumItems) errors.Add(new("configuration.maxItems", path, "The array has too many items."));
            foreach (JsonElement item in value.EnumerateArray())
            {
                bool valid = field.ItemKind switch
                {
                    GenericConfigurationValueKind.Text => item.ValueKind == JsonValueKind.String,
                    GenericConfigurationValueKind.WholeNumber => item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out _),
                    GenericConfigurationValueKind.Number => item.ValueKind == JsonValueKind.Number && item.TryGetDecimal(out _),
                    _ => false,
                };
                if (!valid)
                {
                    errors.Add(new("configuration.arrayItemType", path, "An array item has the wrong type."));
                    break;
                }
            }
        }
    }
}
