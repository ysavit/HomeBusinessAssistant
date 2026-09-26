namespace FounderScout.Application;

/// <summary>Versioned prompt and scorecard content with stable hashes.</summary>
public sealed record FounderEvaluationPrompt(
    string PromptVersion,
    string SystemPrompt,
    string PromptContentHash,
    string ScorecardVersion,
    string ScorecardContent,
    string ScorecardContentHash);

/// <summary>Loads bounded version-controlled evaluation assets.</summary>
public static class FounderEvaluationPromptCatalog
{
    /// <summary>Current prompt version.</summary>
    public const string CurrentPromptVersion = "founder-evaluation-prompt-1.0";

    /// <summary>Loads and validates the checked-in prompt and scorecard files.</summary>
    public static async ValueTask<FounderEvaluationPrompt> LoadAsync(
        string promptDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(promptDirectory);
        string root = Path.GetFullPath(promptDirectory);
        string promptPath = Path.Combine(root, "founder-evaluation-system-v1.md");
        string scorecardPath = Path.Combine(root, "founder-scorecard-v1.json");
        string prompt = await ReadBoundedAsync(promptPath, 64_000, cancellationToken).ConfigureAwait(false);
        string scorecard = await ReadBoundedAsync(scorecardPath, 32_000, cancellationToken).ConfigureAwait(false);
        if (!prompt.Contains(CurrentPromptVersion, StringComparison.Ordinal)
            || !scorecard.Contains(FounderEvaluationScorecard.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Founder Scout prompt assets have unsupported versions.");
        }
        return new(
            CurrentPromptVersion,
            prompt,
            FounderProfileCanonicalizer.HashUtf8(prompt),
            FounderEvaluationScorecard.Version,
            scorecard,
            FounderProfileCanonicalizer.HashUtf8(scorecard));
    }

    private static async ValueTask<string> ReadBoundedAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        FileInfo info = new(path);
        if (!info.Exists || info.Length is < 2 || info.Length > maximumBytes)
        {
            throw new InvalidDataException("A required Founder Scout prompt asset is missing or outside its size bound.");
        }
        return await File.ReadAllTextAsync(info.FullName, cancellationToken).ConfigureAwait(false);
    }
}
