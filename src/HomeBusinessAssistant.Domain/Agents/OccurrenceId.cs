namespace HomeBusinessAssistant.Domain.Agents;

/// <summary>Identifies one durable schedule or manual occurrence.</summary>
public readonly record struct OccurrenceId
{
    private OccurrenceId(Guid value) => Value = value;

    /// <summary>Gets the underlying non-empty GUID.</summary>
    public Guid Value { get; }

    /// <summary>Creates a new occurrence identifier.</summary>
    public static OccurrenceId New() => new(Guid.NewGuid());

    /// <summary>Creates an identifier from a non-empty GUID.</summary>
    public static OccurrenceId FromGuid(Guid value) => value != Guid.Empty
        ? new OccurrenceId(value)
        : throw new ArgumentException("Occurrence identifiers cannot be empty.", nameof(value));

    /// <summary>Parses a canonical GUID string.</summary>
    public static OccurrenceId Parse(string value) => TryParse(value, out var result)
        ? result
        : throw new FormatException("Occurrence identifiers must be non-empty canonical GUID values.");

    /// <summary>Attempts to parse a canonical GUID string.</summary>
    public static bool TryParse(string? value, out OccurrenceId result)
    {
        result = default;
        if (!Guid.TryParseExact(value, "D", out var parsed) || parsed == Guid.Empty)
        {
            return false;
        }

        result = new OccurrenceId(parsed);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}
