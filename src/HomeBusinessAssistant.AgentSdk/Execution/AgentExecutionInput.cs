using System.Text.Json;

namespace HomeBusinessAssistant.AgentSdk.Execution;

/// <summary>The immutable configuration and occurrence arguments supplied for one agent execution.</summary>
public sealed record AgentExecutionInput(
    string SchemaVersion,
    JsonElement Configuration,
    JsonElement OccurrenceArguments,
    IReadOnlyDictionary<string, string> ResolvedSecrets)
{
    /// <summary>The execution-input envelope version written by the central Runner.</summary>
    public const string CurrentSchemaVersion = "1.0";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Creates the private execution-input envelope from validated JSON objects.</summary>
    public static string Serialize(string canonicalConfigurationJson, string occurrenceArgumentsJson)
        => Serialize(canonicalConfigurationJson, occurrenceArgumentsJson, null);

    /// <summary>Creates the private execution-input envelope with short-lived resolved secrets.</summary>
    public static string Serialize(
        string canonicalConfigurationJson,
        string occurrenceArgumentsJson,
        IReadOnlyDictionary<string, string>? resolvedSecrets)
    {
        using JsonDocument configuration = ParseObject(canonicalConfigurationJson, "configuration");
        using JsonDocument arguments = ParseObject(occurrenceArgumentsJson, "occurrence arguments");
        return JsonSerializer.Serialize(new
        {
            executionInputSchemaVersion = CurrentSchemaVersion,
            configuration = configuration.RootElement,
            occurrenceArguments = arguments.RootElement,
            resolvedSecrets = resolvedSecrets ?? new Dictionary<string, string>(StringComparer.Ordinal),
        }, SerializerOptions);
    }

    /// <summary>Loads and validates one Runner-owned private execution-input file.</summary>
    public static async ValueTask<AgentExecutionInput> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("A fully qualified execution-input path is required.", nameof(path));
        }

        string fullPath = Path.GetFullPath(path);
        long length = new FileInfo(fullPath).Length;
        if (length is < 2 or > 1_048_576)
        {
            throw new JsonException("The execution-input envelope size is outside supported bounds.");
        }

        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16_384,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            new JsonDocumentOptions { MaxDepth = 64 },
            cancellationToken).ConfigureAwait(false);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("executionInputSchemaVersion", out JsonElement schema)
            || schema.ValueKind != JsonValueKind.String
            || !string.Equals(schema.GetString(), CurrentSchemaVersion, StringComparison.Ordinal)
            || !root.TryGetProperty("configuration", out JsonElement configuration)
            || configuration.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("occurrenceArguments", out JsonElement arguments)
            || arguments.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The execution-input envelope is invalid or unsupported.");
        }

        var resolvedSecrets = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("resolvedSecrets", out JsonElement secrets))
        {
            if (secrets.ValueKind != JsonValueKind.Object) throw new JsonException("The resolved secret map is invalid.");
            foreach (JsonProperty property in secrets.EnumerateObject())
            {
                string? value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                if (resolvedSecrets.Count >= 32
                    || property.Name.Length is < 10 or > 256
                    || !property.Name.StartsWith("secret://", StringComparison.Ordinal)
                    || string.IsNullOrEmpty(value)
                    || value.Length > 65_536)
                    throw new JsonException("The resolved secret map is invalid.");
                resolvedSecrets.Add(property.Name, value);
            }
        }

        return new(CurrentSchemaVersion, configuration.Clone(), arguments.Clone(), resolvedSecrets);
    }

    /// <summary>Gets one resolved value by its opaque secret reference.</summary>
    public string? GetResolvedSecret(string secretReference) =>
        ResolvedSecrets.TryGetValue(secretReference, out string? value) ? value : null;

    private static JsonDocument ParseObject(string json, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            return document;
        }

        document.Dispose();
        throw new JsonException($"The {description} must be a JSON object.");
    }
}
