using System.Diagnostics;
using System.Text;
using HomeBusinessAssistant.Application.SystemIntegration;

namespace HomeBusinessAssistant.Windows.Processes;

/// <summary>Shell-free Windows process execution with timeout, tree termination, and bounded output.</summary>
public sealed class BoundedProcessExecutor : IProcessExecutor
{
    /// <inheritdoc />
    public async Task<ProcessExecutionResult> ExecuteAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (string argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The local diagnostic process did not start.");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException("The required Windows command is unavailable.", exception);
        }

        Task<string> outputTask = ReadBoundedAsync(
            process.StandardOutput,
            request.MaximumOutputCharacters,
            cancellationToken);
        Task<string> errorTask = ReadBoundedAsync(
            process.StandardError,
            request.MaximumOutputCharacters,
            cancellationToken);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(request.Timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The bounded result still reports timeout; the process may have raced to exit.
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The process may have raced to exit while cancellation was being applied.
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        string output = await outputTask.ConfigureAwait(false);
        string error = await errorTask.ConfigureAwait(false);
        return new(timedOut ? -1 : process.ExitCode, output, error, timedOut);
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var result = new StringBuilder(Math.Min(maximumCharacters, 4_096));
        var buffer = new char[4_096];
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return result.ToString();
            }

            int remaining = maximumCharacters - result.Length;
            if (remaining > 0)
            {
                result.Append(buffer, 0, Math.Min(remaining, read));
            }
        }
    }

    private static void Validate(ProcessExecutionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileName)
            || request.FileName.Length > 32_767
            || request.Arguments.Count > 64
            || request.Arguments.Any(argument => argument is null || argument.Length > 32_767 || argument.Any(char.IsControl))
            || request.Timeout < TimeSpan.FromSeconds(1)
            || request.Timeout > TimeSpan.FromMinutes(5)
            || request.MaximumOutputCharacters is < 1 or > 1_048_576)
        {
            throw new ArgumentException("The bounded process request is invalid.", nameof(request));
        }
    }
}
