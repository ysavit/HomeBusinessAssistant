# Operations runbook

Home Business Assistant has four process boundaries: the interactive tray/loopback Host, the one-shot Runner, Founder Scout, and Wake Remote. Only Runner launches scheduled agents. Windows Task Scheduler bridges logon and the next wake; it is not the schedule source of truth.

## Routine checks

The tray dashboard is available only on exact IPv4 loopback and requires the Windows account that started the Host. Use:

```powershell
Invoke-RestMethod http://127.0.0.1:5180/health/live
Invoke-RestMethod http://127.0.0.1:5180/health/ready
```

The About page reports product/build identity, database migration identity, and installed manifest versions. The Operations and Settings pages show bounded health signals, backup state, recent events, disk use, and attention items. Diagnostics export is redacted and excludes secrets, cookies, browser profile paths, raw exception contents, and full founder profile data.

## Command-line operations

Run commands from the installed `app` directory. Add the install's bootstrap arguments when using a non-default data directory.

```powershell
.\HomeBusinessAssistant.Runner.exe version
.\HomeBusinessAssistant.Runner.exe diagnose
.\HomeBusinessAssistant.Runner.exe migrate
.\HomeBusinessAssistant.Runner.exe recover
.\HomeBusinessAssistant.Runner.exe reconcile-wake
.\HomeBusinessAssistant.Runner.exe power-diagnostics
.\HomeBusinessAssistant.Runner.exe create-backup
.\HomeBusinessAssistant.Runner.exe list-backups --max 20
```

`migrate` initializes both SQLite databases and reports their exact migration IDs. `recover` abandons stale claims/runs conservatively. `reconcile-wake` updates or removes only the application-managed `\HomeBusinessAssistant\NextWake` task.

## Start and stop

The Host normally starts from `\HomeBusinessAssistant\HostAtLogon`. Launching Host again signals the existing per-user/session instance. For controlled maintenance:

```powershell
.\HomeBusinessAssistant.Host.exe --bootstrap-config ..\config\appsettings.json --shutdown
```

Wait for active runs to finish before repair, upgrade, restore, or uninstall. `diagnose` reports the active-run count. `-Force` script options can terminate owned processes but should be reserved for reviewed recovery.

## Logs, reports, and retention

Runtime data lives beneath the configured data directory. Structured platform logs, run logs, artifacts, Founder Scout snapshots/reports, backups, and browser-profile state have distinct subdirectories and retention boundaries. Large diagnostics are files with SQLite metadata. Do not email or commit the data directory.

The Host continuously derives deterministic operational summaries and notification signals from persisted state. Daily backup, cleanup, and diagnostics can be invoked through Runner/UI today. Optional durable platform-maintenance schedule seeding is not enabled automatically in V1 because the published process contract contains only Founder Scout and Wake Remote agents; creating an unexecutable schedule would be unsafe. Operators should use the Settings backup action and documented Runner commands until a versioned maintenance-agent command contract is added.

## Incident checklist

1. Do not delete SQLite `-wal` or `-shm` files while a process is running.
2. Stop dispatch from the UI and let active runs finish.
3. Export redacted diagnostics and record the displayed build/migration versions.
4. Run `diagnose`, then `repair.ps1` if files/tasks/migrations are suspect.
5. Validate a backup before any restore.
6. Use restore only from exclusive maintenance mode and retain the generated pre-restore set.

See [backup-restore.md](backup-restore.md), [security-privacy.md](security-privacy.md), and [troubleshooting.md](troubleshooting.md).
