using System.Security.Cryptography;
using HomeBusinessAssistant.AgentSdk.Manifest;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence;

namespace HomeBusinessAssistant.Infrastructure.Execution;

/// <summary>Root-confined agent manifest and executable integrity validation.</summary>
public sealed class ExecutableIntegrityService : IExecutableIntegrityService
{
    private readonly string agentDirectory;
    private readonly string manifestDirectory;

    /// <summary>Creates integrity validation beneath explicit existing roots.</summary>
    public ExecutableIntegrityService(string agentDirectory, string manifestDirectory)
    {
        this.agentDirectory = ValidateRoot(agentDirectory, nameof(agentDirectory));
        this.manifestDirectory = ValidateRoot(manifestDirectory, nameof(manifestDirectory));
    }

    /// <inheritdoc />
    public async ValueTask<ValidatedAgentExecutable> ValidateAsync(
        AgentDefinitionRecord definition,
        string commandName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!IsSafeRelativePath(definition.WorkingDirectoryRelativePath)
            || !IsSafeRelativePath(definition.ExecutableRelativePath))
        {
            throw new InvalidOperationException("The installed agent contains an unsafe relative path.");
        }

        string workingDirectory = StoragePathPolicy.CombineContained(
            agentDirectory,
            NormalizeSegments(definition.WorkingDirectoryRelativePath));
        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException("The installed agent working directory does not exist.");
        }

        StoragePathPolicy.RejectExistingReparsePoints(agentDirectory, workingDirectory);
        string packagedManifestPath = StoragePathPolicy.CombineContained(workingDirectory, "manifest.json");
        string manifestPath = File.Exists(packagedManifestPath)
            ? packagedManifestPath
            : StoragePathPolicy.CombineContained(manifestDirectory, $"{definition.Id.Value}.agent-manifest.json");
        RejectFileReparsePoint(manifestPath, "The agent manifest cannot be a reparse point.");
        AgentManifestLoadResult loaded = await AgentManifestLoader.LoadAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        AgentManifest manifest = loaded.Manifest
            ?? throw new InvalidOperationException("The installed agent manifest is missing or invalid.");
        ValidateManifestIdentity(definition, commandName, manifest);

        string executablePath = StoragePathPolicy.CombineContained(
            workingDirectory,
            NormalizeSegments(definition.ExecutableRelativePath));
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("The installed agent executable does not exist.");
        }

        RejectFileReparsePoint(executablePath, "The installed agent executable cannot be a reparse point.");
        var file = new FileInfo(executablePath);
        string hash = await ComputeSha256Async(executablePath, cancellationToken).ConfigureAwait(false);
        return new(
            manifest,
            manifestPath,
            workingDirectory,
            executablePath,
            hash,
            file.Length,
            file.LastWriteTimeUtc);
    }

    /// <inheritdoc />
    public async ValueTask EnsureUnchangedAsync(
        ValidatedAgentExecutable executable,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);
        if (!File.Exists(executable.ExecutablePath))
        {
            throw new FileNotFoundException("The installed agent executable disappeared before launch.");
        }

        RejectFileReparsePoint(executable.ExecutablePath, "The installed agent executable became a reparse point.");
        var file = new FileInfo(executable.ExecutablePath);
        if (file.Length != executable.ExecutableSizeBytes
            || file.LastWriteTimeUtc != executable.ExecutableLastWriteAtUtc
            || !string.Equals(
                await ComputeSha256Async(executable.ExecutablePath, cancellationToken).ConfigureAwait(false),
                executable.ExecutableSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The installed agent executable changed before launch.");
        }
    }

    private static void ValidateManifestIdentity(
        AgentDefinitionRecord definition,
        string commandName,
        AgentManifest manifest)
    {
        if (manifest.Id != definition.Id
            || manifest.ManifestVersion.ToString() != definition.ManifestVersion
            || manifest.Version.Value != definition.InstalledVersion
            || !string.Equals(manifest.Executable, definition.ExecutableRelativePath, StringComparison.Ordinal)
            || manifest.DefaultConcurrencyPolicy != definition.DefaultConcurrencyPolicy
            || !manifest.SupportedCommands.Contains(commandName, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The on-disk manifest does not match the installed agent definition or command.");
        }
    }

    private static string ValidateRoot(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        string fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException("A configured Runner root directory does not exist.");
        }

        StoragePathPolicy.RejectExistingReparsePoints(fullPath, fullPath);
        return fullPath;
    }

    private static string[] NormalizeSegments(string relativePath) =>
        relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private static bool IsSafeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 512
            || Path.IsPathRooted(value)
            || value.StartsWith('/')
            || value.StartsWith('\\')
            || value.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return value.Split(['/', '\\']).All(segment => segment.Length > 0 && segment is not "." and not "..");
    }

    private static void RejectFileReparsePoint(string path, string message)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async ValueTask<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 65_536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }
}
