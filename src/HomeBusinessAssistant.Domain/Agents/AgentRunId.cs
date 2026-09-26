namespace HomeBusinessAssistant.Domain.Agents;

/// <summary>Identifies one durable agent run.</summary>
public readonly record struct AgentRunId
{
    private AgentRunId(Guid value) => Value = value;

    /// <summary>Gets the underlying non-empty GUID.</summary>
    public Guid Value { get; }

    /// <summary>Creates a new run identifier.</summary>
    public static AgentRunId New() => new(Guid.NewGuid());

    /// <summary>Creates an identifier from a non-empty GUID.</summary>
    public static AgentRunId FromGuid(Guid value) => value != Guid.Empty
        ? new AgentRunId(value)
        : throw new ArgumentException("Agent run identifiers cannot be empty.", nameof(value));

    /// <summary>Parses a canonical GUID string.</summary>
    public static AgentRunId Parse(string value) => TryParse(value, out var result)
        ? result
        : throw new FormatException("Agent run identifiers must be non-empty canonical GUID values.");

    /// <summary>Attempts to parse a canonical GUID string.</summary>
    public static bool TryParse(string? value, out AgentRunId result)
    {
        result = default;
        if (!Guid.TryParseExact(value, "D", out var parsed) || parsed == Guid.Empty)
        {
            return false;
        }

        result = new AgentRunId(parsed);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}
