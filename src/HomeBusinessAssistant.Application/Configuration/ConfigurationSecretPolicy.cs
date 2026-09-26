using System.Text.Json;
using HomeBusinessAssistant.Application.Secrets;

namespace HomeBusinessAssistant.Application.Configuration;

/// <summary>Prevents likely secret values from being embedded directly in persisted configuration.</summary>
public static class ConfigurationSecretPolicy
{
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

    /// <summary>Returns safe path-only errors for invalid references or likely inline secret values.</summary>
    public static IReadOnlyList<ConfigurationValidationError> Validate(JsonElement configuration)
    {
        var errors = new List<ConfigurationValidationError>();
        Visit(configuration, "$", propertyName: null, errors);
        return errors;
    }

    private static void Visit(
        JsonElement value,
        string path,
        string? propertyName,
        List<ConfigurationValidationError> errors)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    Visit(property.Value, $"{path}.{property.Name}", property.Name, errors);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (JsonElement item in value.EnumerateArray())
                {
                    Visit(item, $"{path}[{index}]", propertyName, errors);
                    index++;
                }

                break;
            case JsonValueKind.String:
                ValidateString(value.GetString() ?? string.Empty, path, propertyName, errors);
                break;
        }
    }

    private static void ValidateString(
        string value,
        string path,
        string? propertyName,
        List<ConfigurationValidationError> errors)
    {
        if (value.StartsWith("secret://", StringComparison.Ordinal))
        {
            if (!SecretReference.TryParse(value, out _))
            {
                errors.Add(new("configuration.invalidSecretReference", path, "The secret reference is invalid."));
            }

            return;
        }

        string normalizedName = propertyName?.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant() ?? string.Empty;
        if (SensitivePropertyFragments.Any(fragment => normalizedName.Contains(fragment, StringComparison.Ordinal)))
        {
            errors.Add(new("configuration.inlineSecretRejected", path, "Sensitive configuration values must use an opaque secret reference."));
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && !string.IsNullOrEmpty(uri.UserInfo))
        {
            errors.Add(new("configuration.uriCredentialsRejected", path, "Credentials must not be embedded in a URI."));
        }
    }
}
