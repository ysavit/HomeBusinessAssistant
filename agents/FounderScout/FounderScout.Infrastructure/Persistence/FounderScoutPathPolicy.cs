namespace FounderScout.Infrastructure.Persistence;

/// <summary>Root-confined path construction for sensitive Founder Scout storage.</summary>
public static class FounderScoutPathPolicy
{
    /// <summary>Combines path segments and rejects escape from the supplied root.</summary>
    public static string CombineContained(string root, params string[] segments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(segments);
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string candidate = Path.GetFullPath(Path.Combine([normalizedRoot, .. segments]));
        if (!candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Founder Scout path escapes its configured data root.");
        }

        return candidate;
    }

    /// <summary>Returns a portable relative path only when a full path is contained by the root.</summary>
    public static string GetRelativeContainedPath(string root, string path)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Founder Scout path escapes its configured data root.");
        }

        return Path.GetRelativePath(normalizedRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>Rejects an existing file-system reparse point.</summary>
    public static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Founder Scout storage cannot use a reparse point.");
        }
    }
}
