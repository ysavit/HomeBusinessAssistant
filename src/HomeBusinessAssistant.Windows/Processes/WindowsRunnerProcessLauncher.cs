using System.ComponentModel;
using System.Diagnostics;
using HomeBusinessAssistant.Application.Desktop;
using HomeBusinessAssistant.Domain.Agents;

namespace HomeBusinessAssistant.Windows.Processes;

/// <summary>Validated paths passed to the detached central Runner process.</summary>
public sealed record RunnerProcessLauncherOptions(
    string ApplicationRoot,
    string RunnerExecutablePath,
    string RunnerWorkingDirectory,
    string DataDirectory,
    string AgentDirectory,
    string ManifestDirectory,
    string DatabaseFileName)
{
    /// <summary>Validates containment and bounded bootstrap file names without changing machine state.</summary>
    public void Validate()
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ApplicationRoot));
        string runner = Path.GetFullPath(RunnerExecutablePath);
        string working = Path.TrimEndingDirectorySeparator(Path.GetFullPath(RunnerWorkingDirectory));
        if (!IsContained(root, runner)
            || !IsContained(root, working)
            || string.IsNullOrWhiteSpace(DatabaseFileName)
            || DatabaseFileName != Path.GetFileName(DatabaseFileName)
            || DatabaseFileName.Length > 128)
        {
            throw new ArgumentException("Runner process launcher paths are invalid.");
        }

        _ = Path.GetFullPath(DataDirectory);
        _ = Path.GetFullPath(AgentDirectory);
        _ = Path.GetFullPath(ManifestDirectory);
    }

    private static bool IsContained(string root, string candidate) =>
        string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase)
        || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Starts <c>HomeBusinessAssistant.Runner.exe execute</c> without a shell or visible console.</summary>
public sealed class WindowsRunnerProcessLauncher : IRunnerProcessLauncher
{
    private readonly RunnerProcessLauncherOptions options;
    private readonly Func<ProcessStartInfo, int?> start;

    /// <summary>Creates the launcher with an injectable process boundary for tests.</summary>
    public WindowsRunnerProcessLauncher(
        RunnerProcessLauncherOptions options,
        Func<ProcessStartInfo, int?>? start = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.options = options;
        this.start = start ?? StartDetached;
    }

    /// <inheritdoc />
    public ValueTask<RunnerProcessLaunchResult> LaunchAsync(
        OccurrenceId occurrenceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(options.RunnerExecutablePath))
        {
            return ValueTask.FromResult(new RunnerProcessLaunchResult(false, null, "runner.path-missing"));
        }

        ProcessStartInfo startInfo = CreateStartInfo(occurrenceId);
        try
        {
            int? processId = start(startInfo);
            return ValueTask.FromResult(processId is > 0
                ? new RunnerProcessLaunchResult(true, processId, "runner.started")
                : new RunnerProcessLaunchResult(false, null, "runner.start-returned-null"));
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return ValueTask.FromResult(new RunnerProcessLaunchResult(false, null, "runner.start-failed"));
        }
    }

    /// <summary>Builds the shell-free, argument-list-based process request.</summary>
    public ProcessStartInfo CreateStartInfo(OccurrenceId occurrenceId)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(options.RunnerExecutablePath),
            WorkingDirectory = Path.GetFullPath(options.RunnerWorkingDirectory),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        startInfo.ArgumentList.Add("execute");
        startInfo.ArgumentList.Add("--occurrence-id");
        startInfo.ArgumentList.Add(occurrenceId.ToString());
        startInfo.ArgumentList.Add("--data-directory");
        startInfo.ArgumentList.Add(Path.GetFullPath(options.DataDirectory));
        startInfo.ArgumentList.Add("--agent-directory");
        startInfo.ArgumentList.Add(Path.GetFullPath(options.AgentDirectory));
        startInfo.ArgumentList.Add("--manifest-directory");
        startInfo.ArgumentList.Add(Path.GetFullPath(options.ManifestDirectory));
        startInfo.ArgumentList.Add("--database-file-name");
        startInfo.ArgumentList.Add(options.DatabaseFileName);
        return startInfo;
    }

    private static int? StartDetached(ProcessStartInfo startInfo)
    {
        using Process? process = Process.Start(startInfo);
        return process?.Id;
    }
}
