using System.Text;
using System.Xml;
using System.Xml.Linq;
using HomeBusinessAssistant.Application.SystemIntegration;
using HomeBusinessAssistant.Application.Wake;
using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Windows.SystemIntegration;

namespace HomeBusinessAssistant.Windows.Wake;

/// <summary>Bounded settings for the built-in <c>schtasks.exe</c> bridge.</summary>
public sealed record SchtasksWakeTaskSchedulerBridgeOptions(
    string DataDirectory,
    string SchtasksExecutable,
    TimeSpan CommandTimeout,
    int MaximumOutputCharacters)
{
    /// <summary>Creates conservative defaults under the validated application data directory.</summary>
    public static SchtasksWakeTaskSchedulerBridgeOptions CreateDefault(string dataDirectory)
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return new(
            dataDirectory,
            Path.Combine(windows, "System32", "schtasks.exe"),
            TimeSpan.FromSeconds(30),
            65_536);
    }
}

/// <summary>Registers and verifies one Task Scheduler task through argument-list <c>schtasks.exe</c> calls.</summary>
public sealed class SchtasksWakeTaskSchedulerBridge(
    IProcessExecutor processes,
    SchtasksWakeTaskSchedulerBridgeOptions options,
    Action? ensureApplicationFolder = null) : IWakeTaskSchedulerBridge
{
    private const string FingerprintPrefix = "Fingerprint=";
    private const string OccurrencePrefix = "OccurrenceId=";
    private const string DuePrefix = "DueAtUtc=";

    /// <inheritdoc />
    public async Task<WakeTaskState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        ProcessExecutionResult result = await ExecuteAsync(
            ["/Query", "/TN", ManagedWakeTask.Name, "/XML"],
            cancellationToken).ConfigureAwait(false);
        if (result.TimedOut)
        {
            throw Failure("wake.task-query-timeout", WakeTaskErrorCategory.CommandFailed, "Windows Task Scheduler did not answer the managed-task query.", result.ExitCode);
        }

        if (result.ExitCode != 0)
        {
            if (IsTaskNotFound(result))
            {
                return WakeTaskState.Absent;
            }

            throw CommandFailure("wake.task-query-failed", "The managed wake task could not be queried.", result);
        }

        try
        {
            return ParseState(result.StandardOutput);
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException or FormatException)
        {
            var error = new WakeTaskError(
                "wake.task-invalid-xml",
                WakeTaskErrorCategory.InvalidTask,
                "The registered wake task is not a valid managed task.");
            return new(true, false, null, null, null, error);
        }
    }

    /// <inheritdoc />
    public async Task ReconcileAsync(WakeTaskRequest? nextWake, CancellationToken cancellationToken = default)
    {
        if (nextWake is null)
        {
            await RemoveAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        WakeTaskState current = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        string fingerprint = nextWake.GetFingerprint();
        if (current.Exists
            && current.IsManaged
            && current.OccurrenceId == nextWake.OccurrenceId
            && string.Equals(current.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return;
        }
        if (current.Exists && !current.IsManaged)
        {
            throw Failure(
                "wake.task-ownership-conflict",
                WakeTaskErrorCategory.InvalidTask,
                "A task with the reserved wake-task name exists but is not managed by Home Business Assistant.");
        }

        (ensureApplicationFolder ?? TaskSchedulerFolderManager.EnsureApplicationFolder)();

        string tempDirectory = GetTemporaryDirectory();
        Directory.CreateDirectory(tempDirectory);
        string xmlPath = Path.Combine(tempDirectory, $"next-wake-{Guid.NewGuid():N}.xml");
        try
        {
            string xml = WakeTaskXmlGenerator.GenerateXml(nextWake);
            await File.WriteAllTextAsync(xmlPath, xml, new UnicodeEncoding(bigEndian: false, byteOrderMark: true), cancellationToken).ConfigureAwait(false);
            ProcessExecutionResult result = await ExecuteAsync(
                ["/Create", "/TN", ManagedWakeTask.Name, "/XML", xmlPath, "/F"],
                cancellationToken).ConfigureAwait(false);
            if (result.TimedOut || result.ExitCode != 0)
            {
                throw CommandFailure("wake.task-registration-failed", "The managed wake task could not be registered.", result);
            }

            WakeTaskState verified = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            if (!verified.Exists
                || !verified.IsManaged
                || verified.OccurrenceId != nextWake.OccurrenceId
                || !string.Equals(verified.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw Failure(
                    "wake.task-verification-failed",
                    WakeTaskErrorCategory.InvalidTask,
                    "Windows Task Scheduler did not preserve the expected managed task settings.");
            }
        }
        finally
        {
            TryDelete(xmlPath);
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(CancellationToken cancellationToken = default)
    {
        WakeTaskState current = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (!current.Exists) return;
        if (!current.IsManaged)
        {
            throw Failure(
                "wake.task-ownership-conflict",
                WakeTaskErrorCategory.InvalidTask,
                "The reserved wake task is not managed by Home Business Assistant and was not removed.");
        }

        ProcessExecutionResult result = await ExecuteAsync(
            ["/Delete", "/TN", ManagedWakeTask.Name, "/F"],
            cancellationToken).ConfigureAwait(false);
        if (result.TimedOut)
        {
            throw Failure("wake.task-delete-timeout", WakeTaskErrorCategory.CommandFailed, "Windows Task Scheduler did not answer the managed-task removal request.", result.ExitCode);
        }

        if (result.ExitCode != 0 && !IsTaskNotFound(result))
        {
            throw CommandFailure("wake.task-delete-failed", "The managed wake task could not be removed.", result);
        }
    }

    private Task<ProcessExecutionResult> ExecuteAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ValidateOptions();
        return processes.ExecuteAsync(new(
            Path.GetFullPath(options.SchtasksExecutable),
            arguments,
            options.CommandTimeout,
            options.MaximumOutputCharacters), cancellationToken);
    }

    private static WakeTaskState ParseState(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = 1_048_576,
            XmlResolver = null,
        };
        using var text = new StringReader(xml);
        using XmlReader reader = XmlReader.Create(text, settings);
        XDocument document = XDocument.Load(reader, LoadOptions.None);
        XNamespace ns = WakeTaskXmlGenerator.TaskNamespace;
        XElement root = document.Root ?? throw new InvalidOperationException("The task XML has no root.");
        string? uri = root.Element(ns + "RegistrationInfo")?.Element(ns + "URI")?.Value;
        string? description = root.Element(ns + "RegistrationInfo")?.Element(ns + "Description")?.Value;
        bool managed = string.Equals(uri, ManagedWakeTask.Name, StringComparison.Ordinal)
            && description?.Contains("Home Business Assistant managed wake task.", StringComparison.Ordinal) == true;
        if (!managed)
        {
            return new(true, false, null, null, null, new(
                "wake.task-not-managed",
                WakeTaskErrorCategory.InvalidTask,
                "A task with the managed name exists but was not created by Home Business Assistant."));
        }

        string fingerprint = ReadDescriptionValue(description!, FingerprintPrefix);
        OccurrenceId occurrenceId = OccurrenceId.Parse(ReadDescriptionValue(description!, OccurrencePrefix));
        DateTimeOffset due = DateTimeOffset.Parse(
            ReadDescriptionValue(description!, DuePrefix),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
        XElement? settingsElement = root.Element(ns + "Settings");
        XElement? action = root.Element(ns + "Actions")?.Element(ns + "Exec");
        string? command = action?.Element(ns + "Command")?.Value;
        string? arguments = action?.Element(ns + "Arguments")?.Value;
        bool semanticShape = root.Element(ns + "Triggers")?.Elements(ns + "TimeTrigger").Count() == 1
            && settingsElement?.Element(ns + "WakeToRun")?.Value == "true"
            && settingsElement?.Element(ns + "MultipleInstancesPolicy")?.Value == "IgnoreNew"
            && string.Equals(Path.GetFileName(command), "HomeBusinessAssistant.Runner.exe", StringComparison.OrdinalIgnoreCase)
            && arguments?.StartsWith($"execute --occurrence-id {occurrenceId} ", StringComparison.Ordinal) == true;
        return semanticShape
            ? new(true, true, occurrenceId, due, fingerprint, null)
            : new(true, false, occurrenceId, due, fingerprint, new(
                "wake.task-settings-mismatch",
                WakeTaskErrorCategory.InvalidTask,
                "The registered wake task settings do not match the managed policy."));
    }

    private static string ReadDescriptionValue(string description, string prefix)
    {
        string? line = description.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .SingleOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return line is null || line.Length == prefix.Length
            ? throw new InvalidOperationException("A managed description marker is missing.")
            : line[prefix.Length..];
    }

    private static bool IsTaskNotFound(ProcessExecutionResult result)
    {
        string combined = string.Concat(result.StandardOutput, "\n", result.StandardError);
        return combined.Contains("cannot find", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("0x80070002", StringComparison.OrdinalIgnoreCase);
    }

    private static WakeTaskBridgeException CommandFailure(
        string code,
        string message,
        ProcessExecutionResult result)
    {
        string combined = string.Concat(result.StandardOutput, "\n", result.StandardError);
        bool permission = combined.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("0x80070005", StringComparison.OrdinalIgnoreCase);
        return Failure(
            code,
            permission ? WakeTaskErrorCategory.PermissionDenied : WakeTaskErrorCategory.CommandFailed,
            permission ? $"{message} Run the application with permission to manage the current user's task." : message,
            result.ExitCode);
    }

    private static WakeTaskBridgeException Failure(
        string code,
        WakeTaskErrorCategory category,
        string message,
        int? exitCode = null) => new(new(code, category, message, exitCode));

    private string GetTemporaryDirectory()
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.DataDirectory));
        string path = Path.GetFullPath(Path.Combine(root, "temp", "wake-tasks"));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure("wake.temp-path-invalid", WakeTaskErrorCategory.InvalidRequest, "The wake-task temporary directory is invalid.");
        }

        return path;
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(options.DataDirectory)
            || string.IsNullOrWhiteSpace(options.SchtasksExecutable)
            || options.CommandTimeout < TimeSpan.FromSeconds(1)
            || options.CommandTimeout > TimeSpan.FromMinutes(5)
            || options.MaximumOutputCharacters is < 1 or > 1_048_576)
        {
            throw new ArgumentException("The schtasks bridge options are invalid.", nameof(options));
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The confined random file will be eligible for later stale-temp cleanup.
        }
    }
}
