namespace HomeBusinessAssistant.Host;

/// <summary>
/// Enforces the V1 security boundary that the management host listens only on IPv4 loopback.
/// </summary>
public static class LoopbackUrlPolicy
{
    /// <summary>
    /// Validates a single HTTP URL that binds specifically to <c>127.0.0.1</c>.
    /// </summary>
    /// <param name="value">The configured URL.</param>
    /// <returns>The trimmed URL when valid.</returns>
    /// <exception cref="ArgumentException">The URL is missing, malformed, or not loopback-only.</exception>
    public static string Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A loopback URL is required.", nameof(value));
        }

        string candidate = value.Trim();
        bool isValid = Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)
            && uri.Scheme == Uri.UriSchemeHttp
            && uri.Host == "127.0.0.1"
            && uri.AbsolutePath == "/"
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment)
            && string.IsNullOrEmpty(uri.UserInfo);

        if (!isValid)
        {
            throw new ArgumentException(
                "The Host URL must be a single HTTP origin bound to 127.0.0.1.",
                nameof(value));
        }

        return candidate;
    }
}
