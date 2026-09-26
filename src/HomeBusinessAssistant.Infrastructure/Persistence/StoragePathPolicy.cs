namespace HomeBusinessAssistant.Infrastructure.Persistence;

internal static class StoragePathPolicy
{
    public static string PrepareDataDirectory(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            throw new ArgumentException("A platform data directory is required.", nameof(dataDirectory));
        }

        string fullPath = Path.GetFullPath(dataDirectory);
        if (File.Exists(fullPath))
        {
            throw new InvalidOperationException("The configured data directory is an existing file.");
        }

        Directory.CreateDirectory(fullPath);
        RejectReparsePoint(fullPath, "The configured data directory cannot be a reparse point.");
        return fullPath;
    }

    public static string GetSafeDatabasePath(string dataDirectory, string databaseFileName)
    {
        if (string.IsNullOrWhiteSpace(databaseFileName)
            || databaseFileName != Path.GetFileName(databaseFileName)
            || databaseFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The database file name must be one safe file name.", nameof(databaseFileName));
        }

        return CombineContained(dataDirectory, databaseFileName);
    }

    public static string CombineContained(string root, params string[] segments)
    {
        string fullRoot = Path.GetFullPath(root);
        string candidate = Path.GetFullPath(Path.Combine([fullRoot, .. segments]));
        string prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The storage path escapes its configured root.");
        }

        return candidate;
    }

    public static void RejectExistingReparsePoints(string root, string targetDirectory)
    {
        string fullRoot = Path.GetFullPath(root);
        string current = Path.GetFullPath(targetDirectory);
        while (current.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(current))
            {
                RejectReparsePoint(current, "Artifact storage cannot traverse a reparse point.");
            }

            if (string.Equals(current, fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = Path.GetDirectoryName(current)
                ?? throw new InvalidOperationException("Could not inspect the storage path hierarchy.");
        }
    }

    private static void RejectReparsePoint(string path, string message)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(message);
        }
    }
}
