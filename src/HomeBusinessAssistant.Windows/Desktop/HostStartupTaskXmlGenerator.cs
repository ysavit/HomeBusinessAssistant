using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using HomeBusinessAssistant.Application.Desktop;

namespace HomeBusinessAssistant.Windows.Desktop;

/// <summary>Deterministically renders the current-user, non-waking Host-at-logon task.</summary>
public static class HostStartupTaskXmlGenerator
{
    /// <summary>The Task Scheduler 2.x XML namespace.</summary>
    public static readonly XNamespace TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>Generates the complete task document.</summary>
    public static XDocument Generate(HostStartupTaskRequest request)
    {
        Validate(request);
        XNamespace ns = TaskNamespace;
        string fingerprint = request.GetFingerprint();
        string description = string.Join('\n',
            "Home Business Assistant managed Host startup task.",
            $"Fingerprint={fingerprint}",
            "Starts the current user's local tray Host at interactive logon.",
            "Managed by Home Business Assistant; do not edit manually.");
        var trigger = new XElement(ns + "LogonTrigger",
            new XAttribute("id", "CurrentUserLogon"),
            new XElement(ns + "Enabled", "true"),
            new XElement(ns + "UserId", request.CurrentUserId));
        if (request.Delay > TimeSpan.Zero)
        {
            trigger.Add(new XElement(ns + "Delay", XmlConvert.ToString(request.Delay)));
        }

        return new XDocument(
            new XDeclaration("1.0", "utf-16", null),
            new XElement(ns + "Task",
                new XAttribute("version", "1.4"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Author", "Home Business Assistant"),
                    new XElement(ns + "Description", description),
                    new XElement(ns + "URI", ManagedHostStartupTask.Name)),
                new XElement(ns + "Triggers", trigger),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal",
                        new XAttribute("id", "CurrentUser"),
                        new XElement(ns + "UserId", request.CurrentUserId),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        new XElement(ns + "RunLevel", "LeastPrivilege"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(ns + "AllowHardTerminate", "true"),
                    new XElement(ns + "StartWhenAvailable", "true"),
                    new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(ns + "WakeToRun", "false"),
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "Hidden", "false"),
                    new XElement(ns + "ExecutionTimeLimit", "PT0S")),
                new XElement(ns + "Actions",
                    new XAttribute("Context", "CurrentUser"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", request.HostExecutablePath),
                        new XElement(ns + "Arguments", $"--bootstrap-config {Quote(request.BootstrapConfigurationPath)}"),
                        new XElement(ns + "WorkingDirectory", request.WorkingDirectory)))));
    }

    /// <summary>Serializes without indentation-dependent semantics.</summary>
    public static string GenerateXml(HostStartupTaskRequest request) => Generate(request).ToString(SaveOptions.DisableFormatting);

    private static string Quote(string value)
    {
        if (value.Any(char.IsControl)) throw new ArgumentException("A Host startup argument contains control characters.", nameof(value));
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static void Validate(HostStartupTaskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Path.IsPathFullyQualified(request.HostExecutablePath)
            || !Path.IsPathFullyQualified(request.WorkingDirectory)
            || !Path.IsPathFullyQualified(request.BootstrapConfigurationPath)
            || !string.Equals(Path.GetFileName(request.HostExecutablePath), "HomeBusinessAssistant.Host.exe", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(request.CurrentUserId)
            || request.CurrentUserId.Length > 256
            || request.CurrentUserId.Any(char.IsControl)
            || request.Delay < TimeSpan.Zero
            || request.Delay > TimeSpan.FromMinutes(15))
        {
            throw new ArgumentException("The Host startup task request is invalid.", nameof(request));
        }

        _ = request.Delay.TotalSeconds.ToString(CultureInfo.InvariantCulture);
    }
}
