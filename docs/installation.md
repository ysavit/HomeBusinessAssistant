# Installation and upgrades

Home Business Assistant V1 is a self-contained, per-user Windows 11 x64 folder deployment. Normal installation does not require an administrator, a Windows Service, Docker, Node.js, or a machine-wide .NET runtime.

## Publish

From the repository root:

```powershell
dotnet restore HomeBusinessAssistant.sln -r win-x64
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 `
  -OutputDirectory .\artifacts\publish\win-x64 `
  -Version 1.0.0 `
  -NoRestore
```

The script creates a deterministic manifest-backed bundle with `app`, `config`, `scripts`, and release `docs` directories. Host, Runner, Founder Scout, and Wake Remote are self-contained `win-x64` executables. Trimming, single-file publishing, and ReadyToRun are disabled because correctness and Playwright/native dependency reliability take priority over package size.

Do not add databases, browser profiles, authentication state, logs, secrets, or developer configuration to the bundle. `publish-manifest.json` records every shipped file's size and SHA-256 hash.

## Clean per-user install

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\artifacts\publish\win-x64\scripts\install.ps1 `
  -SourceDirectory .\artifacts\publish\win-x64 `
  -InstallDirectory "$env:LOCALAPPDATA\HomeBusinessAssistant" `
  -DataDirectory "$env:LOCALAPPDATA\HomeBusinessAssistant\data" `
  -Port 5180 `
  -StartAfterInstall
```

The installer verifies the complete publish manifest before changing the install. It stages and swaps application files, writes bootstrap-only configuration, migrates both databases, seeds built-in manifests without overwriting mutable database configuration, registers the per-user `\HomeBusinessAssistant\HostAtLogon` task, and performs a health check. Its log is written beneath the install root without secrets.

Use `-InstallPlaywrightBrowser` only when this account will run Founder Scout browser discovery. The browser download is explicit and may need network access. Use `-SkipStartupTask` for controlled test environments where Task Scheduler must not be changed.

## Upgrade

Run the same installer against a newer bundle and the same install/data roots. An upgrade:

1. refuses active runs unless the operator explicitly uses `-Force`;
2. creates an online backup of both databases before migration;
3. asks the running Host to stop through its single-instance channel;
4. retains the prior app directory until files, migrations, task registration, and health are valid;
5. rolls back the application and databases when a later step fails;
6. preserves data, configuration revisions, schedules, protected secrets, and browser profiles.

Downgrades are refused by default. `-Force` is an emergency override, not a compatibility guarantee. Installing the same version is safe and idempotent; it still verifies content and protects existing databases.

Automatic internet updates, MSI/MSIX packaging, code signing, and enterprise deployment tooling are outside V1.

## Repair and uninstall

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\HomeBusinessAssistant\scripts\repair.ps1"

powershell -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\HomeBusinessAssistant\scripts\uninstall.ps1"
```

Repair verifies installed hashes, directory writability, migrations, agent paths, HostAtLogon, NextWake reconciliation, Playwright state, and Runner diagnosis. Supply `-SourceDirectory <published-bundle>` to replace damaged application files.

Uninstall removes application/configuration files and managed tasks but preserves the data directory by default. Data deletion is deliberately separate:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1 `
  -RemoveData -ConfirmRemoveData REMOVE-DATA
```

The destructive option accepts only validated Home Business Assistant data locations. Back up first.

## Verify

Open `http://127.0.0.1:5180/` as the same Windows user that started the Host and check the About page. Management pages use Windows Negotiate authentication and authorize only that owner's SID; health endpoints remain anonymous. Check:

- `/health/live` and `/health/ready` return healthy responses;
- the product version, commit, runtime target, both database migrations, and agent/manifest versions are present;
- Task Scheduler contains one application-managed HostAtLogon task;
- a second Host launch opens/signals the existing instance instead of creating another Host.

See [operations.md](operations.md), [backup-restore.md](backup-restore.md), and [troubleshooting.md](troubleshooting.md).
