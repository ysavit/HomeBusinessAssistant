namespace HomeBusinessAssistant.Infrastructure.Persistence;

/// <summary>Bootstrap-only settings required to open the central platform database.</summary>
public sealed record AssistantDatabaseSettings(
    string DataDirectory,
    string DatabaseFileName = "assistant.db",
    TimeSpan? BusyTimeout = null,
    string? ManifestDirectory = null)
{
    /// <summary>Gets the effective bounded SQLite busy timeout.</summary>
    public TimeSpan EffectiveBusyTimeout => BusyTimeout ?? TimeSpan.FromSeconds(5);
}
