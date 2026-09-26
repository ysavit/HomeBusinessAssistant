namespace HomeBusinessAssistant.AgentSdk.Contracts;

internal static class ContractNameRules
{
    public const int MaximumNameLength = 64;

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumNameLength)
        {
            return false;
        }

        if (!IsAsciiLetterOrDigit(value[0]) || !IsAsciiLetterOrDigit(value[^1]))
        {
            return false;
        }

        return value.All(character => IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
    }

    public static bool IsSafeRelativePath(string? value)
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

        var segments = value.Split(['/', '\\']);
        return segments.All(segment => segment.Length > 0 && segment is not "." and not "..");
    }

    private static bool IsAsciiLetterOrDigit(char value) =>
        value is >= 'a' and <= 'z'
        or >= 'A' and <= 'Z'
        or >= '0' and <= '9';
}
