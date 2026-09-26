using System.Text;
using System.Xml;
using System.Xml.Linq;
using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Application.SystemIntegration;
using HomeBusinessAssistant.Windows.SystemIntegration;

namespace HomeBusinessAssistant.Windows.Desktop;

/// <summary>Bounded current-user Task Scheduler adapter for Host startup.</summary>
public sealed class SchtasksHostStartupTaskScheduler(
    IProcessExecutor processes,
    string dataDirectory,
    string? schtasksExecutable = null) : IHostStartupTaskScheduler
{
    private readonly string executable = schtasksExecutable ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "schtasks.exe");

    /// <inheritdoc />
    public async ValueTask<HostStartupTaskState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        ProcessExecutionResult result = await ExecuteAsync(
            ["/Query", "/TN", ManagedHostStartupTask.Name, "/XML"],
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            if (IsNotFound(result)) return HostStartupTaskState.Absent;
            throw CreateFailure("startup.task.query-failed", result);
        }

        try
        {
            return Parse(result.StandardOutput);
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            return new(true, false, null, null, "startup.task.invalid-xml");
        }
    }

    /// <inheritdoc />
    public async ValueTask<HostStartupTaskState> ReconcileAsync(
        HostStartupTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        HostStartupTaskState current = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (current.Exists && current.IsManaged && current.Fingerprint == request.GetFingerprint()) return current;
        if (current.Exists && !current.IsManaged)
        {
            throw new InvalidOperationException("startup.task.ownership-conflict: A task with the reserved Host startup name exists but is not managed by Home Business Assistant.");
        }
        TaskSchedulerFolderManager.EnsureApplicationFolder();
        string temporaryRoot = GetTemporaryRoot();
        Directory.CreateDirectory(temporaryRoot);
        string path = Path.Combine(temporaryRoot, $"host-at-logon-{Guid.NewGuid():N}.xml");
        try
        {
            await File.WriteAllTextAsync(
                path,
                HostStartupTaskXmlGenerator.GenerateXml(request),
                new UnicodeEncoding(false, true),
                cancellationToken).ConfigureAwait(false);
            ProcessExecutionResult result = await ExecuteAsync(
                ["/Create", "/TN", ManagedHostStartupTask.Name, "/XML", path, "/F"],
                cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0 || result.TimedOut) throw CreateFailure("startup.task.registration-failed", result);
            HostStartupTaskState verified = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            if (!verified.Exists || !verified.IsManaged || verified.Fingerprint != request.GetFingerprint())
            {
                throw new InvalidOperationException("The Host startup task did not pass semantic verification.");
            }

            return verified;
        }
        finally
        {
            TryDelete(path);
        }
    }

    /// <inheritdoc />
    public async ValueTask RemoveAsync(CancellationToken cancellationToken = default)
    {
        HostStartupTaskState current = await GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (!current.Exists) return;
        if (!current.IsManaged)
        {
            throw new InvalidOperationException("startup.task.ownership-conflict: The reserved Host startup task is not managed by Home Business Assistant and was not removed.");
        }

        ProcessExecutionResult result = await ExecuteAsync(
            ["/Delete", "/TN", ManagedHostStartupTask.Name, "/F"],
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 && !IsNotFound(result)) throw CreateFailure("startup.task.delete-failed", result);
    }

    private Task<ProcessExecutionResult> ExecuteAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        processes.ExecuteAsync(new(
            Path.GetFullPath(executable),
            arguments,
            TimeSpan.FromSeconds(30),
            65_536), cancellationToken);

    private static HostStartupTaskState Parse(string xml)
    {
        using var text = new StringReader(xml);
        using XmlReader reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = 1_048_576,
            XmlResolver = null,
        });
        XDocument document = XDocument.Load(reader, LoadOptions.None);
        XNamespace ns = HostStartupTaskXmlGenerator.TaskNamespace;
        XElement root = document.Root ?? throw new InvalidOperationException("The task XML has no root.");
        string? uri = root.Element(ns + "RegistrationInfo")?.Element(ns + "URI")?.Value;
        string? description = root.Element(ns + "RegistrationInfo")?.Element(ns + "Description")?.Value;
        XElement? action = root.Element(ns + "Actions")?.Element(ns + "Exec");
        string? hostPath = action?.Element(ns + "Command")?.Value;
        string? arguments = action?.Element(ns + "Arguments")?.Value;
        bool managed = uri == ManagedHostStartupTask.Name
            && description?.Contains("Home Business Assistant managed Host startup task.", StringComparison.Ordinal) == true;
        string? fingerprint = description?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .SingleOrDefault(value => value.StartsWith("Fingerprint=", StringComparison.Ordinal))?["Fingerprint=".Length..];
        bool semantic = managed
            && root.Element(ns + "Triggers")?.Elements(ns + "LogonTrigger").Count() == 1
            && root.Element(ns + "Settings")?.Element(ns + "WakeToRun")?.Value == "false"
            && root.Element(ns + "Settings")?.Element(ns + "MultipleInstancesPolicy")?.Value == "IgnoreNew"
            && Path.GetFileName(hostPath) == "HomeBusinessAssistant.Host.exe"
            && arguments?.StartsWith("--bootstrap-config ", StringComparison.Ordinal) == true
            && !string.IsNullOrWhiteSpace(fingerprint);
        return new(true, semantic, fingerprint, hostPath, semantic ? null : "startup.task.settings-mismatch");
    }

    private string GetTemporaryRoot()
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
        string candidate = Path.GetFullPath(Path.Combine(root, "temp", "startup-task"));
        return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? candidate
            : throw new InvalidOperationException("The startup-task temporary path is invalid.");
    }

    private static bool IsNotFound(ProcessExecutionResult result)
    {
        string combined = result.StandardOutput + "\n" + result.StandardError;
        return combined.Contains("cannot find", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("0x80070002", StringComparison.OrdinalIgnoreCase);
    }

    private static InvalidOperationException CreateFailure(string code, ProcessExecutionResult result)
    {
        string combined = result.StandardOutput + "\n" + result.StandardError;
        string category = combined.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
            ? "permission-denied"
            : "command-failed";
        string detail = new(combined
            .Where(character => !char.IsControl(character))
            .Take(256)
            .ToArray());
        return new InvalidOperationException($"{code}:{category}:{(string.IsNullOrWhiteSpace(detail) ? "no-output" : detail)}");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
