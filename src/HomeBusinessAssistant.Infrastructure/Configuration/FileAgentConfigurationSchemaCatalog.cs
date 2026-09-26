using System.Text.Json;
using HomeBusinessAssistant.AgentSdk.Manifest;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Persistence;

namespace HomeBusinessAssistant.Infrastructure.Configuration;

/// <summary>Loads bounded configuration schemas from installed agent package roots.</summary>
public sealed class FileAgentConfigurationSchemaCatalog : IAgentConfigurationSchemaCatalog
{
    /// <summary>The largest accepted configuration schema file.</summary>
    public const long MaximumSchemaBytes = 262_144;

    private readonly string agentDirectory;

    /// <summary>Creates a catalog confined to one configured agent directory.</summary>
    public FileAgentConfigurationSchemaCatalog(string agentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentDirectory);
        this.agentDirectory = Path.GetFullPath(agentDirectory);
        if (!Directory.Exists(this.agentDirectory))
        {
            throw new DirectoryNotFoundException("The configured agent directory does not exist.");
        }

        StoragePathPolicy.RejectExistingReparsePoints(this.agentDirectory, this.agentDirectory);
    }

    /// <inheritdoc />
    public async ValueTask<GenericConfigurationSchema?> GetAsync(
        AgentId agentId,
        CancellationToken cancellationToken = default)
    {
        string packageRoot = StoragePathPolicy.CombineContained(agentDirectory, agentId.Value);
        if (!Directory.Exists(packageRoot)) return null;
        StoragePathPolicy.RejectExistingReparsePoints(agentDirectory, packageRoot);

        string manifestPath = StoragePathPolicy.CombineContained(packageRoot, "manifest.json");
        string schemaPath = StoragePathPolicy.CombineContained(packageRoot, "configuration.schema.json");
        RejectFileReparsePoint(manifestPath);
        RejectFileReparsePoint(schemaPath);
        if (!File.Exists(manifestPath) || !File.Exists(schemaPath)) return null;

        AgentManifestLoadResult manifestResult = await AgentManifestLoader.LoadAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        AgentManifest manifest = manifestResult.Manifest
            ?? throw new InvalidDataException("The installed agent manifest is invalid.");
        if (manifest.Id != agentId)
        {
            throw new InvalidDataException("The installed manifest identity does not match its package directory.");
        }

        var file = new FileInfo(schemaPath);
        if (file.Length is < 2 or > MaximumSchemaBytes)
        {
            throw new InvalidDataException("The installed configuration schema size is outside supported bounds.");
        }

        await using FileStream stream = new(
            schemaPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16_384,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            new JsonDocumentOptions { MaxDepth = 32 },
            cancellationToken).ConfigureAwait(false);
        GenericConfigurationSchemaParseResult parsed = GenericConfigurationSchemaParser.Parse(
            document.RootElement,
            manifest.ConfigurationSchemaVersion.ToString());
        if (!parsed.IsValid)
        {
            throw new InvalidDataException($"The installed configuration schema is invalid ({parsed.Errors[0].Code}).");
        }

        return parsed.Schema;
    }

    private static void RejectFileReparsePoint(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("An installed agent metadata file cannot be a reparse point.");
        }
    }
}
