using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using HomeBusinessAssistant.AgentSdk.Execution;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Persistence;

namespace HomeBusinessAssistant.Infrastructure.Execution;

/// <summary>Root-confined, atomic, current-user temporary execution storage.</summary>
public sealed class RunTemporaryFileManager : IRunTemporaryFileManager
{
    private readonly string dataDirectory;
    private readonly string configurationRoot;
    private readonly string artifactStagingRoot;
    private readonly string agentDataRoot;

    /// <summary>Creates Runner-owned temporary roots beneath the validated data directory.</summary>
    public RunTemporaryFileManager(string dataDirectory)
    {
        this.dataDirectory = Path.GetFullPath(dataDirectory);
        configurationRoot = PreparePrivateDirectory(StoragePathPolicy.CombineContained(this.dataDirectory, "temp", "config"));
        artifactStagingRoot = PreparePrivateDirectory(StoragePathPolicy.CombineContained(this.dataDirectory, "temp", "agent-artifacts"));
        agentDataRoot = PreparePrivateDirectory(StoragePathPolicy.CombineContained(this.dataDirectory, "agents"));
    }

    /// <inheritdoc />
    public ValueTask<string> WriteExecutionInputAsync(
        AgentRunId runId,
        string canonicalConfigurationJson,
        string occurrenceArgumentsJson,
        CancellationToken cancellationToken = default) =>
        WriteExecutionInputAsync(
            runId,
            canonicalConfigurationJson,
            occurrenceArgumentsJson,
            new Dictionary<string, string>(StringComparer.Ordinal),
            cancellationToken);

    /// <inheritdoc />
    public async ValueTask<string> WriteExecutionInputAsync(
        AgentRunId runId,
        string canonicalConfigurationJson,
        string occurrenceArgumentsJson,
        IReadOnlyDictionary<string, string> resolvedSecrets,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalConfigurationJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(occurrenceArgumentsJson);
        string path = StoragePathPolicy.CombineContained(configurationRoot, $"{runId.Value:D}.json");
        string temporaryPath = StoragePathPolicy.CombineContained(configurationRoot, $".{runId.Value:N}.{Guid.NewGuid():N}.tmp");
        byte[] contents = Encoding.UTF8.GetBytes(AgentExecutionInput.Serialize(
            canonicalConfigurationJson,
            occurrenceArgumentsJson,
            resolvedSecrets));
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16_384,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            TryRestrictFileToCurrentUser(temporaryPath);
            File.Move(temporaryPath, path, overwrite: false);
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden | FileAttributes.NotContentIndexed);
            return path;
        }
        catch
        {
            DeleteFileBestEffort(temporaryPath);
            DeleteFileBestEffort(path);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contents);
        }
    }

    /// <inheritdoc />
    public string CreateArtifactDirectory(AgentRunId runId)
    {
        string path = StoragePathPolicy.CombineContained(artifactStagingRoot, runId.Value.ToString("D"));
        if (Directory.Exists(path))
        {
            throw new InvalidOperationException("The run artifact staging directory already exists.");
        }

        return PreparePrivateDirectory(path);
    }

    /// <inheritdoc />
    public string GetAgentDataDirectory(AgentId agentId) =>
        PreparePrivateDirectory(StoragePathPolicy.CombineContained(agentDataRoot, agentId.Value));

    /// <inheritdoc />
    public void CleanupRun(AgentRunId runId)
    {
        DeleteFileBestEffort(StoragePathPolicy.CombineContained(configurationRoot, $"{runId.Value:D}.json"));
        DeleteDirectoryBestEffort(StoragePathPolicy.CombineContained(artifactStagingRoot, runId.Value.ToString("D")));
    }

    /// <inheritdoc />
    public int CleanupStale(DateTimeOffset olderThanUtc)
    {
        DateTime cutoffUtc = olderThanUtc.UtcDateTime;
        var deleted = 0;
        foreach (string file in Directory.EnumerateFiles(configurationRoot, "*", SearchOption.TopDirectoryOnly))
        {
            if (File.GetLastWriteTimeUtc(file) < cutoffUtc && !IsReparsePoint(file))
            {
                File.Delete(file);
                deleted++;
            }
        }

        foreach (string directory in Directory.EnumerateDirectories(artifactStagingRoot, "*", SearchOption.TopDirectoryOnly))
        {
            if (Directory.GetLastWriteTimeUtc(directory) < cutoffUtc && !IsReparsePoint(directory))
            {
                Directory.Delete(directory, recursive: true);
                deleted++;
            }
        }

        return deleted;
    }

    private static string PreparePrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        TryRestrictDirectoryToCurrentUser(path);
        return path;
    }

    private static void TryRestrictDirectoryToCurrentUser(string path)
    {
        try
        {
            SecurityIdentifier? user = WindowsIdentity.GetCurrent().User;
            if (user is null)
            {
                return;
            }

            var security = new DirectorySecurity();
            security.SetOwner(user);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                user,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(security);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or PlatformNotSupportedException or IdentityNotMappedException)
        {
            // The inherited ACL remains the practical boundary when the host cannot replace it.
        }
    }

    private static void TryRestrictFileToCurrentUser(string path)
    {
        try
        {
            SecurityIdentifier? user = WindowsIdentity.GetCurrent().User;
            if (user is null)
            {
                return;
            }

            var security = new FileSecurity();
            security.SetOwner(user);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                user,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or PlatformNotSupportedException or IdentityNotMappedException)
        {
            // The inherited ACL remains the practical boundary when the host cannot replace it.
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static void DeleteFileBestEffort(string path)
    {
        try
        {
            if (File.Exists(path) && !IsReparsePoint(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Startup cleanup can retry after a process or scanner releases the file.
        }
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path) && !IsReparsePoint(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Startup cleanup can retry after a process or scanner releases the directory.
        }
    }
}
