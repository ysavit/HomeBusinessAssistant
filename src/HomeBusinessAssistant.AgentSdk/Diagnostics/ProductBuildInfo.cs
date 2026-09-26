using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeBusinessAssistant.AgentSdk.Diagnostics;

/// <summary>Consistent, non-secret build identity shared by every published process.</summary>
public sealed record ProductBuildInfo(
    string ProductVersion,
    string GitCommit,
    string BuildUtc,
    string RuntimeTarget,
    string Framework)
{
    /// <summary>The release metadata file generated beside each published executable.</summary>
    public const string FileName = "build-info.json";

    private const int MaximumBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Loads validated release metadata, or returns an assembly-derived development identity.</summary>
    public static ProductBuildInfo Load(Assembly assembly, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        string directory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        string path = Path.Combine(directory, FileName);
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            if (info.Length is <= 0 or > MaximumBytes)
            {
                throw new InvalidDataException("The build metadata file has an invalid size.");
            }

            ProductBuildInfo? value;
            try
            {
                // PowerShell 5.1 writes UTF-8 JSON with a BOM. Read as text so release
                // metadata produced by the supported Windows packaging shell remains portable.
                value = JsonSerializer.Deserialize<ProductBuildInfo>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("The build metadata file is invalid.", exception);
            }

            value?.Validate();
            return value ?? throw new InvalidDataException("The build metadata file is empty.");
        }

        string informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        string version = informational.Split('+', 2)[0];
        string commit = informational.Contains('+', StringComparison.Ordinal)
            ? informational[(informational.IndexOf('+') + 1)..]
            : "unavailable";
        return new(
            version,
            commit,
            "development",
            System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName ?? "unknown");
    }

    /// <summary>Validates bounded display-safe metadata before it enters diagnostics or audit.</summary>
    public void Validate()
    {
        ValidateValue(ProductVersion, 64, nameof(ProductVersion));
        ValidateValue(GitCommit, 128, nameof(GitCommit));
        ValidateValue(BuildUtc, 64, nameof(BuildUtc));
        ValidateValue(RuntimeTarget, 64, nameof(RuntimeTarget));
        ValidateValue(Framework, 128, nameof(Framework));
    }

    private static void ValidateValue(string value, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || value.Any(char.IsControl))
        {
            throw new InvalidDataException($"Build metadata field '{name}' is invalid.");
        }
    }
}
