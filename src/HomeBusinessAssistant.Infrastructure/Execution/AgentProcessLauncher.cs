using System.Diagnostics;
using System.Text;
using HomeBusinessAssistant.AgentSdk.Protocol;

namespace HomeBusinessAssistant.Infrastructure.Execution;

/// <summary>Starts redirected agent processes through the protocol 1.0 argument contract.</summary>
public sealed class AgentProcessLauncher(TimeProvider timeProvider) : IAgentProcessLauncher
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <inheritdoc />
    public ValueTask<AgentProcessHandle> LaunchAsync(
        AgentProcessStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo
        {
            FileName = request.Executable.ExecutablePath,
            WorkingDirectory = request.Executable.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = StrictUtf8,
            StandardErrorEncoding = StrictUtf8,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        AddArguments(startInfo, request);
        startInfo.Environment["HBA_RUN_ID"] = request.RunId.ToString();
        startInfo.Environment["HBA_OCCURRENCE_ID"] = request.OccurrenceId.ToString();
        startInfo.Environment["HBA_AGENT_ID"] = request.AgentId.Value;
        startInfo.Environment["HBA_DATA_DIRECTORY"] = request.AgentDataDirectory;
        startInfo.Environment["HBA_ARTIFACT_DIRECTORY"] = request.ArtifactDirectory;

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The agent process did not start.");
            }

            DateTimeOffset startedAtUtc;
            try
            {
                startedAtUtc = process.StartTime.ToUniversalTime();
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                startedAtUtc = timeProvider.GetUtcNow().ToUniversalTime();
            }

            return ValueTask.FromResult(new AgentProcessHandle(process, startedAtUtc));
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private static void AddArguments(ProcessStartInfo startInfo, AgentProcessStartRequest request)
    {
        startInfo.ArgumentList.Add(request.CommandName);
        AddOption(startInfo, "--run-id", request.RunId.ToString());
        AddOption(startInfo, "--occurrence-id", request.OccurrenceId.ToString());
        AddOption(startInfo, "--agent-id", request.AgentId.Value);
        AddOption(startInfo, "--config-file", request.ConfigurationFile);
        AddOption(startInfo, "--data-directory", request.AgentDataDirectory);
        AddOption(startInfo, "--artifact-directory", request.ArtifactDirectory);
        AddOption(startInfo, "--protocol-version", AgentProtocolVersion.Current.ToString());
    }

    private static void AddOption(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }
}
