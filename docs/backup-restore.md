# Backup and restore

Ordinary backup sets contain the central `assistant.db` and Founder Scout `founders.db`. They deliberately exclude DPAPI secret files, browser profiles/authentication state, logs, and diagnostic artifacts.

## Backup technology and consistency

`DatabaseBackupService` serializes backup with the central `operations.database-backup` lease and uses `Microsoft.Data.Sqlite`'s online backup API with independent source and destination connections. It does not copy a live database or its WAL/SHM sidecars.

Both database copies share one backup-set ID. Before promotion, each staged database must open, pass `PRAGMA quick_check`, contain its expected tables and migration history, and match recorded size/SHA-256 metadata. The completed `backup-manifest.json` records product/build identity, migration IDs, reason, and database entries. Interrupted `.pending-*` directories are never listed or restorable.

The default retention policy keeps bounded daily and weekly restore points. Retention never removes the set currently selected for restore or its mandatory pre-restore backup.

## Create and validate

Use the Settings page or Runner:

```powershell
.\HomeBusinessAssistant.Runner.exe create-backup
.\HomeBusinessAssistant.Runner.exe list-backups --max 20
.\HomeBusinessAssistant.Runner.exe validate-backup --backup-set <set-id>
```

Treat a set as usable only when validation returns success. Copy the entire completed set directory when transferring it to offline storage; do not copy individual database files without the manifest.

## Restore safeguards

Restore is intentionally CLI-only in V1. It requires:

- the Host and owned agents to be stopped;
- no active persisted runs;
- an intact completed backup manifest and both verified database hashes;
- a compatible product/schema version;
- explicit maintenance mode and the exact `RESTORE` confirmation token.

```powershell
.\HomeBusinessAssistant.Host.exe --bootstrap-config ..\config\appsettings.json --shutdown
.\HomeBusinessAssistant.Runner.exe restore-backup `
  --backup-set <set-id> `
  --maintenance true `
  --confirm RESTORE
```

The service creates a new pre-restore backup, validates staged copies, replaces both databases with rollback files retained through validation, applies only compatible current migrations, runs stale-run recovery, and reconciles wake state. If either replacement or post-restore initialization fails, both original databases are restored. Secret and browser state are unchanged.

After restore, run:

```powershell
.\HomeBusinessAssistant.Runner.exe diagnose
.\HomeBusinessAssistant.Runner.exe reconcile-wake
```

Then start Host and inspect readiness, recent audit, schedules, and Founder Scout results. Never restore a database from a newer incompatible product using `-Force` or manual file copying.
