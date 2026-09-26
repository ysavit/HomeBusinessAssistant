namespace HomeBusinessAssistant.Domain.Agents;

/// <summary>Identifies an installed agent across process and persistence boundaries.</summary>
public readonly record struct AgentId
{
    /// <summary>The maximum supported identifier length.</summary>
    public const int MaximumLength = 64;

    private AgentId(string value) => Value = value;

    /// <summary>Gets the normalized identifier value.</summary>
    public string Value { get; }

    /// <summary>Parses a valid lower-case agent identifier.</summary>
    public static AgentId Parse(string value) => TryParse(value, out var result)
        ? result
        : throw new FormatException(
            $"Agent identifiers must contain 1 to {MaximumLength} lower-case letters, digits, or hyphens and cannot begin or end with a hyphen.");

    /// <summary>Attempts to parse an agent identifier.</summary>
    public static bool TryParse(string? value, out AgentId result)
    {
        result = default;
        if (string.IsNullOrEmpty(value) || value.Length > MaximumLength || value[0] == '-' || value[^1] == '-')
        {
            return false;
        }

        foreach (var character in value)
        {
            if ((character is >= 'a' and <= 'z') || (character is >= '0' and <= '9') || character == '-')
            {
                continue;
            }

            return false;
        }

        result = new AgentId(value);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}
