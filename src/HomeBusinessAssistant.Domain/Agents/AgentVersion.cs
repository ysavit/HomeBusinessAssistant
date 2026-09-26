namespace HomeBusinessAssistant.Domain.Agents;

/// <summary>Represents a bounded Semantic Versioning 2.0.0 agent version.</summary>
public readonly record struct AgentVersion
{
    /// <summary>The maximum supported serialized version length.</summary>
    public const int MaximumLength = 128;

    private AgentVersion(string value) => Value = value;

    /// <summary>Gets the semantic-version text.</summary>
    public string Value { get; }

    /// <summary>Parses a Semantic Versioning 2.0.0 value.</summary>
    public static AgentVersion Parse(string value) => TryParse(value, out var result)
        ? result
        : throw new FormatException("Agent versions must be valid Semantic Versioning 2.0.0 values.");

    /// <summary>Attempts to parse a Semantic Versioning 2.0.0 value.</summary>
    public static bool TryParse(string? value, out AgentVersion result)
    {
        result = default;
        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength)
        {
            return false;
        }

        var buildSeparator = value.IndexOf('+', StringComparison.Ordinal);
        if (buildSeparator != value.LastIndexOf('+'))
        {
            return false;
        }

        var versionAndPrerelease = buildSeparator < 0 ? value : value[..buildSeparator];
        var build = buildSeparator < 0 ? null : value[(buildSeparator + 1)..];
        if (build is not null && !IsIdentifierList(build, enforceNumericLeadingZeroRule: false))
        {
            return false;
        }

        var prereleaseSeparator = versionAndPrerelease.IndexOf('-', StringComparison.Ordinal);
        var core = prereleaseSeparator < 0 ? versionAndPrerelease : versionAndPrerelease[..prereleaseSeparator];
        var prerelease = prereleaseSeparator < 0 ? null : versionAndPrerelease[(prereleaseSeparator + 1)..];
        var coreParts = core.Split('.');

        if (coreParts.Length != 3 || coreParts.Any(part => !IsCoreNumber(part)))
        {
            return false;
        }

        if (prerelease is not null && !IsIdentifierList(prerelease, enforceNumericLeadingZeroRule: true))
        {
            return false;
        }

        result = new AgentVersion(value);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;

    private static bool IsCoreNumber(string value) => value.Length > 0
        && (value.Length == 1 || value[0] != '0')
        && value.All(character => character is >= '0' and <= '9');

    private static bool IsIdentifierList(string value, bool enforceNumericLeadingZeroRule)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var identifier in value.Split('.'))
        {
            if (identifier.Length == 0 || identifier.Any(character =>
                    !((character is >= 'a' and <= 'z')
                      || (character is >= 'A' and <= 'Z')
                      || (character is >= '0' and <= '9')
                      || character == '-')))
            {
                return false;
            }

            if (enforceNumericLeadingZeroRule
                && identifier.Length > 1
                && identifier[0] == '0'
                && identifier.All(character => character is >= '0' and <= '9'))
            {
                return false;
            }
        }

        return true;
    }
}
