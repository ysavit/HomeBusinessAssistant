using HomeBusinessAssistant.Domain.Agents;
using HomeBusinessAssistant.Infrastructure.Execution;

namespace HomeBusinessAssistant.Runner;

internal enum RunnerOperation
{
    Execute,
    RunAgent,
    Recover,
    Diagnose,
    ScanAgents,
    ReconcileWake,
    PowerDiagnostics,
    PrepareWakeTest,
    WakeTestStatus,
    RemoveWakeTask,
    Migrate,
    CreateBackup,
    ListBackups,
    ValidateBackup,
    RestoreBackup,
    RegisterHostStartup,
    HostStartupStatus,
    RemoveHostStartup,
}

internal sealed record RunnerInvocation(
    RunnerOperation Operation,
    RunnerBootstrapSettings Bootstrap,
    OccurrenceId? OccurrenceId,
    AgentId? AgentId,
    string? CommandName,
    string ArgumentsJson,
    bool ReconcileWakeAfterExecution,
    int WakeTestMinutes,
    string? BackupSetId = null,
    string? ConfirmationToken = null,
    bool MaintenanceMode = false,
    string? HostExecutablePath = null,
    string? BootstrapConfigurationPath = null,
    int StartupDelaySeconds = 15,
    int MaximumResults = 20);

internal sealed record RunnerInvocationParseResult(RunnerInvocation? Invocation, string? Error);

internal static class RunnerInvocationParser
{
    private static readonly HashSet<string> BootstrapOptions = new(StringComparer.Ordinal)
    {
        "--data-directory", "--agent-directory", "--manifest-directory", "--database-file-name",
    };

    public static RunnerInvocationParseResult Parse(IReadOnlyList<string> arguments)
    {
        if (!TryGetOperation(arguments[0], out RunnerOperation operation))
        {
            return Error("Unknown Runner command.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < arguments.Count; index += 2)
        {
            string option = arguments[index];
            if (!option.StartsWith("--", StringComparison.Ordinal)
                || index + 1 >= arguments.Count
                || !values.TryAdd(option, arguments[index + 1]))
            {
                return Error($"Invalid or repeated option '{option}'.");
            }
        }

        HashSet<string> allowed = new(BootstrapOptions, StringComparer.Ordinal);
        if (operation == RunnerOperation.Execute)
        {
            allowed.Add("--occurrence-id");
            allowed.Add("--reconcile-wake-after");
        }
        else if (operation == RunnerOperation.RunAgent)
        {
            allowed.UnionWith(["--agent-id", "--command", "--arguments-json", "--trigger"]);
        }
        else if (operation == RunnerOperation.PrepareWakeTest)
        {
            allowed.Add("--minutes");
        }
        else if (operation == RunnerOperation.WakeTestStatus)
        {
            allowed.Add("--occurrence-id");
        }
        else if (operation is RunnerOperation.ValidateBackup or RunnerOperation.RestoreBackup)
        {
            allowed.Add("--backup-set");
            if (operation == RunnerOperation.RestoreBackup)
            {
                allowed.Add("--confirm");
                allowed.Add("--maintenance");
            }
        }
        else if (operation == RunnerOperation.ListBackups)
        {
            allowed.Add("--max");
        }
        else if (operation == RunnerOperation.RegisterHostStartup)
        {
            allowed.Add("--host-executable");
            allowed.Add("--bootstrap-config");
            allowed.Add("--delay-seconds");
        }

        string? unexpected = values.Keys.FirstOrDefault(key => !allowed.Contains(key));
        if (unexpected is not null)
        {
            return Error($"Option '{unexpected}' is not valid for this command.");
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string applicationRoot = Path.Combine(localAppData, "HomeBusinessAssistant");
        var bootstrap = new RunnerBootstrapSettings(
            Get(values, "--data-directory") ?? Path.Combine(applicationRoot, "data"),
            Get(values, "--agent-directory") ?? Path.Combine(applicationRoot, "app", "agents"),
            Get(values, "--manifest-directory") ?? Path.Combine(AppContext.BaseDirectory, "manifests"),
            Get(values, "--database-file-name") ?? "assistant.db");

        if (operation == RunnerOperation.Execute)
        {
            if (!Guid.TryParse(Get(values, "--occurrence-id"), out Guid value) || value == Guid.Empty)
            {
                return Error("execute requires a non-empty --occurrence-id GUID.");
            }

            string? reconcile = Get(values, "--reconcile-wake-after");
            if (reconcile is not null && reconcile != "true")
            {
                return Error("--reconcile-wake-after accepts only 'true'.");
            }

            return Success(new(operation, bootstrap, OccurrenceId.FromGuid(value), null, null, "{}", reconcile == "true", 3));
        }

        if (operation == RunnerOperation.RunAgent)
        {
            if (!AgentId.TryParse(Get(values, "--agent-id"), out AgentId agentId))
            {
                return Error("run-agent requires a valid --agent-id.");
            }

            string? command = Get(values, "--command");
            if (string.IsNullOrWhiteSpace(command) || command.Length > 64)
            {
                return Error("run-agent requires a valid --command.");
            }

            string? trigger = Get(values, "--trigger");
            if (trigger is not null && !string.Equals(trigger, nameof(TriggerType.CommandLine), StringComparison.Ordinal))
            {
                return Error("run-agent supports only --trigger CommandLine.");
            }

            return Success(new(operation, bootstrap, null, agentId, command, Get(values, "--arguments-json") ?? "{}", false, 3));
        }

        if (operation == RunnerOperation.PrepareWakeTest)
        {
            string? minutesText = Get(values, "--minutes");
            int minutes = minutesText is null ? 3 : int.TryParse(minutesText, out int value) ? value : -1;
            if (minutes is < 1 or > 60)
            {
                return Error("prepare-wake-test requires --minutes between 1 and 60.");
            }

            return Success(new(operation, bootstrap, null, null, null, "{}", false, minutes));
        }

        if (operation == RunnerOperation.WakeTestStatus)
        {
            if (!Guid.TryParse(Get(values, "--occurrence-id"), out Guid value) || value == Guid.Empty)
            {
                return Error("wake-test-status requires a non-empty --occurrence-id GUID.");
            }

            return Success(new(operation, bootstrap, OccurrenceId.FromGuid(value), null, null, "{}", false, 3));
        }

        if (operation is RunnerOperation.ValidateBackup or RunnerOperation.RestoreBackup)
        {
            string? backupSetId = Get(values, "--backup-set");
            if (string.IsNullOrWhiteSpace(backupSetId))
            {
                return Error($"{arguments[0]} requires --backup-set.");
            }

            if (operation == RunnerOperation.RestoreBackup)
            {
                string? maintenance = Get(values, "--maintenance");
                if (maintenance != "true") return Error("restore-backup requires --maintenance true.");
                string? confirmation = Get(values, "--confirm");
                if (confirmation != "RESTORE") return Error("restore-backup requires --confirm RESTORE.");
                return Success(new(operation, bootstrap, null, null, null, "{}", false, 3, backupSetId, confirmation, true));
            }

            return Success(new(operation, bootstrap, null, null, null, "{}", false, 3, BackupSetId: backupSetId));
        }

        if (operation == RunnerOperation.ListBackups)
        {
            string? text = Get(values, "--max");
            int maximum = text is null ? 20 : int.TryParse(text, out int value) ? value : -1;
            if (maximum is < 1 or > 1000) return Error("list-backups requires --max between 1 and 1000.");
            return Success(new(operation, bootstrap, null, null, null, "{}", false, 3, MaximumResults: maximum));
        }

        if (operation == RunnerOperation.RegisterHostStartup)
        {
            string? host = Get(values, "--host-executable");
            string? config = Get(values, "--bootstrap-config");
            string? delayText = Get(values, "--delay-seconds");
            int delay = delayText is null ? 15 : int.TryParse(delayText, out int value) ? value : -1;
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(config) || delay is < 0 or > 900)
            {
                return Error("register-host-startup requires host/config paths and a delay from 0 to 900 seconds.");
            }

            return Success(new(
                operation,
                bootstrap,
                null,
                null,
                null,
                "{}",
                false,
                3,
                HostExecutablePath: Path.GetFullPath(host),
                BootstrapConfigurationPath: Path.GetFullPath(config),
                StartupDelaySeconds: delay));
        }

        return Success(new(operation, bootstrap, null, null, null, "{}", false, 3));
    }

    private static bool TryGetOperation(string value, out RunnerOperation operation)
    {
        operation = value switch
        {
            "execute" => RunnerOperation.Execute,
            "run-agent" => RunnerOperation.RunAgent,
            "recover" => RunnerOperation.Recover,
            "diagnose" => RunnerOperation.Diagnose,
            "scan-agents" => RunnerOperation.ScanAgents,
            "reconcile-wake" => RunnerOperation.ReconcileWake,
            "power-diagnostics" => RunnerOperation.PowerDiagnostics,
            "prepare-wake-test" => RunnerOperation.PrepareWakeTest,
            "wake-test-status" => RunnerOperation.WakeTestStatus,
            "remove-wake-task" => RunnerOperation.RemoveWakeTask,
            "migrate" => RunnerOperation.Migrate,
            "create-backup" => RunnerOperation.CreateBackup,
            "list-backups" => RunnerOperation.ListBackups,
            "validate-backup" => RunnerOperation.ValidateBackup,
            "restore-backup" => RunnerOperation.RestoreBackup,
            "register-host-startup" => RunnerOperation.RegisterHostStartup,
            "host-startup-status" => RunnerOperation.HostStartupStatus,
            "remove-host-startup" => RunnerOperation.RemoveHostStartup,
            _ => default,
        };
        return value is "execute" or "run-agent" or "recover" or "diagnose" or "scan-agents"
            or "reconcile-wake" or "power-diagnostics" or "prepare-wake-test"
            or "wake-test-status" or "remove-wake-task"
            or "migrate" or "create-backup" or "list-backups" or "validate-backup" or "restore-backup"
            or "register-host-startup" or "host-startup-status" or "remove-host-startup";
    }

    private static string? Get(Dictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value) ? value : null;

    private static RunnerInvocationParseResult Error(string error) => new(null, error);
    private static RunnerInvocationParseResult Success(RunnerInvocation invocation) => new(invocation, null);
}
