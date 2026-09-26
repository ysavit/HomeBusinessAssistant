using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using HomeBusinessAssistant.Application.Wake;

namespace HomeBusinessAssistant.Windows.Wake;

/// <summary>Deterministically renders the one supported Task Scheduler 2.x XML definition.</summary>
public sealed class WakeTaskXmlGenerator
{
    /// <summary>The Task Scheduler 2.x XML namespace.</summary>
    public static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>Generates one safe managed-task document.</summary>
    public static XDocument Generate(WakeTaskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        XNamespace ns = TaskNamespace;
        string fingerprint = request.GetFingerprint();
        string description = string.Join('\n',
            "Home Business Assistant managed wake task.",
            $"OccurrenceId={request.OccurrenceId}",
            $"ConfigurationRevisionId={request.ConfigurationRevisionId:D}",
            $"DueAtUtc={request.DueAtUtc.ToUniversalTime():O}",
            $"DueLocal={request.DueLocal:O}",
            $"TimeZoneId={request.TimeZoneId}",
            $"Fingerprint={fingerprint}",
            "Managed by Home Business Assistant; do not edit manually.");
        string arguments = BuildArguments(request);
        return new XDocument(
            new XDeclaration("1.0", "utf-16", null),
            new XElement(ns + "Task",
                new XAttribute("version", "1.4"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Author", "Home Business Assistant"),
                    new XElement(ns + "Description", description),
                    new XElement(ns + "URI", ManagedWakeTask.Name)),
                new XElement(ns + "Triggers",
                    new XElement(ns + "TimeTrigger",
                        new XAttribute("id", "NextWake"),
                        new XElement(ns + "StartBoundary", request.DueAtUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
                        new XElement(ns + "Enabled", "true"))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal",
                        new XAttribute("id", "CurrentUser"),
                        new XElement(ns + "UserId", request.CurrentUserId),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        new XElement(ns + "RunLevel", "LeastPrivilege"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", XmlConvert.ToString(!request.AllowStartOnBatteries)),
                    new XElement(ns + "StopIfGoingOnBatteries", XmlConvert.ToString(request.StopIfGoingOnBatteries)),
                    new XElement(ns + "AllowHardTerminate", "true"),
                    new XElement(ns + "StartWhenAvailable", XmlConvert.ToString(request.StartWhenAvailable)),
                    new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(ns + "WakeToRun", "true"),
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "Hidden", "false"),
                    new XElement(ns + "ExecutionTimeLimit", XmlConvert.ToString(request.ExecutionTimeout))),
                new XElement(ns + "Actions",
                    new XAttribute("Context", "CurrentUser"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", request.RunnerExecutablePath),
                        new XElement(ns + "Arguments", arguments),
                        new XElement(ns + "WorkingDirectory", request.RunnerWorkingDirectory)))));
    }

    /// <summary>Serializes without indentation-sensitive semantics.</summary>
    public static string GenerateXml(WakeTaskRequest request) => Generate(request).ToString(SaveOptions.DisableFormatting);

    private static string BuildArguments(WakeTaskRequest request) => string.Join(' ',
        "execute",
        "--occurrence-id",
        request.OccurrenceId.ToString(),
        "--reconcile-wake-after",
        "true",
        "--data-directory",
        Quote(request.Bootstrap.DataDirectory),
        "--agent-directory",
        Quote(request.Bootstrap.AgentDirectory),
        "--manifest-directory",
        Quote(request.Bootstrap.ManifestDirectory),
        "--database-file-name",
        Quote(request.Bootstrap.DatabaseFileName));

    private static string Quote(string value)
    {
        if (value.Any(char.IsControl))
        {
            throw new ArgumentException("A managed Runner argument contains control characters.", nameof(value));
        }

        var result = new System.Text.StringBuilder(value.Length + 2);
        result.Append('"');
        var slashCount = 0;
        foreach (char character in value)
        {
            if (character == '\\')
            {
                slashCount++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', slashCount * 2 + 1);
                result.Append('"');
                slashCount = 0;
                continue;
            }

            result.Append('\\', slashCount);
            slashCount = 0;
            result.Append(character);
        }

        result.Append('\\', slashCount * 2);
        result.Append('"');
        return result.ToString();
    }

    private static void Validate(WakeTaskRequest request)
    {
        if (request.ConfigurationRevisionId == Guid.Empty
            || request.CorrelationId == Guid.Empty
            || request.DueAtUtc.Offset != TimeSpan.Zero
            || request.ExecutionTimeout < TimeSpan.FromSeconds(1)
            || request.ExecutionTimeout > TimeSpan.FromHours(25)
            || request.UserSessionPolicy != WakeTaskUserSessionPolicy.CurrentInteractiveUser
            || string.IsNullOrWhiteSpace(request.CurrentUserId)
            || request.CurrentUserId.Length > 256
            || request.CurrentUserId.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(request.TimeZoneId)
            || request.TimeZoneId.Length > 128
            || !Path.IsPathFullyQualified(request.RunnerExecutablePath)
            || !Path.IsPathFullyQualified(request.RunnerWorkingDirectory)
            || !Path.IsPathFullyQualified(request.Bootstrap.DataDirectory)
            || !Path.IsPathFullyQualified(request.Bootstrap.AgentDirectory)
            || !Path.IsPathFullyQualified(request.Bootstrap.ManifestDirectory)
            || request.Bootstrap.DatabaseFileName != Path.GetFileName(request.Bootstrap.DatabaseFileName))
        {
            throw new ArgumentException("The managed wake-task request is invalid.", nameof(request));
        }
    }
}
