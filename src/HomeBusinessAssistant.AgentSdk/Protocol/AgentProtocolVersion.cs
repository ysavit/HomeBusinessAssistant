namespace HomeBusinessAssistant.AgentSdk.Protocol;

/// <summary>A validated two-part version used by manifests and the agent event protocol.</summary>
public readonly record struct AgentProtocolVersion
{
    /// <summary>Creates a bounded non-negative major.minor version.</summary>
    public AgentProtocolVersion(int major, int minor)
    {
        if (major is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(major), "Protocol version parts must be between 0 and 999.");
        }

        if (minor is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(minor), "Protocol version parts must be between 0 and 999.");
        }

        Major = major;
        Minor = minor;
    }

    /// <summary>Gets the major compatibility version.</summary>
    public int Major { get; }

    /// <summary>Gets the additive minor version.</summary>
    public int Minor { get; }

    /// <summary>The current event protocol version.</summary>
    public static AgentProtocolVersion Current { get; } = new(1, 0);

    /// <summary>The current manifest contract version.</summary>
    public static AgentProtocolVersion CurrentManifest { get; } = new(1, 0);

    /// <summary>Parses a non-negative major.minor version.</summary>
    public static AgentProtocolVersion Parse(string value) => TryParse(value, out var result)
        ? result
        : throw new FormatException("Protocol versions must use the major.minor form.");

    /// <summary>Attempts to parse a non-negative major.minor version.</summary>
    public static bool TryParse(string? value, out AgentProtocolVersion result)
    {
        result = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var parts = value.Split('.');
        if (parts.Length != 2
            || !TryParsePart(parts[0], out var major)
            || !TryParsePart(parts[1], out var minor))
        {
            return false;
        }

        result = new AgentProtocolVersion(major, minor);
        return true;
    }

    /// <summary>Returns whether this reader supports the version's major contract.</summary>
    public bool IsSupportedBy(AgentProtocolVersion supported) => Major == supported.Major;

    /// <inheritdoc />
    public override string ToString() => $"{Major}.{Minor}";

    private static bool TryParsePart(string value, out int result)
    {
        result = 0;
        return value.Length > 0
            && value.Length <= 3
            && (value.Length == 1 || value[0] != '0')
            && value.All(character => character is >= '0' and <= '9')
            && int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out result);
    }
}
