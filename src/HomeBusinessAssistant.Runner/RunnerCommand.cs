namespace HomeBusinessAssistant.Runner;

/// <summary>Dispatches durable Runner operations and bounded development diagnostics.</summary>
public static class RunnerCommand
{
    /// <summary>Dispatches one Runner command.</summary>
    public static async Task<int> ExecuteAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (arguments.Count == 0 || arguments[0] is "help" or "--help" or "-h")
        {
            return Execute(output);
        }

        if (arguments.Count == 1 && arguments[0] is "version" or "--version")
        {
            HomeBusinessAssistant.AgentSdk.Diagnostics.ProductBuildInfo build =
                HomeBusinessAssistant.AgentSdk.Diagnostics.ProductBuildInfo.Load(typeof(RunnerCommand).Assembly);
            await output.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(build)).ConfigureAwait(false);
            return Infrastructure.Execution.RunnerExitCode.Success;
        }

        if (arguments[0] == "persistence-demo")
        {
            return await PersistenceDemoCommand.ExecuteAsync(
                arguments.Skip(1).ToArray(), output, error, cancellationToken).ConfigureAwait(false);
        }

        if (arguments[0] == "scheduling-demo")
        {
            return await SchedulingDemoCommand.ExecuteAsync(
                arguments.Skip(1).ToArray(), output, error, cancellationToken).ConfigureAwait(false);
        }

        if (arguments[0] == "supervision-demo")
        {
            return await SupervisionDemoCommand.ExecuteAsync(
                arguments.Skip(1).ToArray(), output, error, cancellationToken).ConfigureAwait(false);
        }

        RunnerInvocationParseResult parsed = RunnerInvocationParser.Parse(arguments);
        if (parsed.Invocation is null)
        {
            await error.WriteLineAsync(parsed.Error ?? "The Runner command line is invalid.").ConfigureAwait(false);
            return Infrastructure.Execution.RunnerExitCode.InvalidArguments;
        }

        try
        {
            await using RunnerRuntime runtime = await RunnerRuntime.CreateAsync(
                parsed.Invocation.Bootstrap,
                cancellationToken,
                includeOperations: RequiresOperations(parsed.Invocation.Operation)).ConfigureAwait(false);
            return await runtime.ExecuteAsync(parsed.Invocation, output, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("The Runner operation was cancelled.").ConfigureAwait(false);
            return Infrastructure.Execution.RunnerExitCode.Cancelled;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException or InvalidDataException or UnauthorizedAccessException or FormatException or ArgumentException
            or Microsoft.Data.Sqlite.SqliteException or Application.Wake.WakeTaskBridgeException)
        {
            string detail = exception switch
            {
                ArgumentException => SanitizeArgumentFailure(exception.Message),
                InvalidOperationException when exception.Message.StartsWith("startup.task.", StringComparison.Ordinal) =>
                    SanitizeArgumentFailure(exception.Message),
                _ => "See the bounded structured Runner log for details.",
            };
            await error.WriteLineAsync($"Runner operation failed ({exception.GetType().Name}): {detail}").ConfigureAwait(false);
            return Infrastructure.Execution.RunnerExitCode.InvalidExecutionEnvironment;
        }
    }

    private static string SanitizeArgumentFailure(string message)
    {
        string bounded = new(message
            .Where(character => !char.IsControl(character))
            .Take(256)
            .ToArray());
        return string.IsNullOrWhiteSpace(bounded) ? "A validated argument was rejected." : bounded;
    }

    /// <summary>Writes current-stage help.</summary>
    public static int Execute(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.WriteLine("Home Business Assistant Runner - local operations and durable agent execution");
        output.WriteLine("Commands:");
        output.WriteLine("  execute --occurrence-id <guid> [--reconcile-wake-after true] [bootstrap options]");
        output.WriteLine("  run-agent --agent-id <id> --command <name> [--arguments-json <object>] [bootstrap options]");
        output.WriteLine("  recover [bootstrap options]");
        output.WriteLine("  diagnose [bootstrap options]");
        output.WriteLine("  scan-agents [bootstrap options]");
        output.WriteLine("  reconcile-wake [bootstrap options]");
        output.WriteLine("  power-diagnostics [bootstrap options]");
        output.WriteLine("  prepare-wake-test [--minutes 1..60] [bootstrap options]");
        output.WriteLine("  wake-test-status --occurrence-id <guid> [bootstrap options]");
        output.WriteLine("  remove-wake-task [bootstrap options]");
        output.WriteLine("  migrate [bootstrap options]");
        output.WriteLine("  create-backup [bootstrap options]");
        output.WriteLine("  list-backups [--max 1..1000] [bootstrap options]");
        output.WriteLine("  validate-backup --backup-set <id> [bootstrap options]");
        output.WriteLine("  restore-backup --backup-set <id> --maintenance true --confirm RESTORE [bootstrap options]");
        output.WriteLine("  register-host-startup --host-executable <path> --bootstrap-config <path> [--delay-seconds 0..900] [bootstrap options]");
        output.WriteLine("  host-startup-status [bootstrap options]");
        output.WriteLine("  remove-host-startup [bootstrap options]");
        output.WriteLine("  version");
        output.WriteLine("Bootstrap options: --data-directory, --agent-directory, --manifest-directory, --database-file-name.");
        output.WriteLine("Development checks: persistence-demo, scheduling-demo, supervision-demo.");
        return 0;
    }

    private static bool RequiresOperations(RunnerOperation operation) => operation is
        RunnerOperation.Migrate or RunnerOperation.CreateBackup or RunnerOperation.ListBackups
        or RunnerOperation.ValidateBackup or RunnerOperation.RestoreBackup;
}
