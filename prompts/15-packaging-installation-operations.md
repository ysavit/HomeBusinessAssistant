# Stage 15 — Packaging, Installation, Startup, Backup, Restore, and Operations

Paste this entire prompt into Codex from the repository root after Stage 14 passes.

---

You are implementing **Stage 15: Windows Publishing, Installation/Uninstallation/Repair Scripts, At-Logon Startup, Playwright Browser Bootstrap, Database Backup/Restore, and Operator Runbooks**.

Read all repository guidance, current runtime paths, wake bridge, Host/Runner/agents, and this prompt. Create/update `docs/exec-plans/stage-15-packaging-installation-operations.md`.

## Repository-state handoff

Before planning or coding:

1. Read `docs/PROJECT_STATE.md` completely.
2. Inspect the repository and verify the previous stage's claimed state; do not trust the state file blindly.
3. Reconcile any mismatch in the stage ExecPlan before implementation.

Before the final report:

1. Update `docs/PROJECT_STATE.md` with capabilities actually implemented, stage status, material files/projects, migrations/configuration/protocol versions, actual validation results, known limitations, and the exact next prompt.
2. Distinguish `Implemented` from `Validated` and preserve failed checks until a later verified pass.
3. Do not include secrets, cookies, authentication state, tokens, or real founder profile content.

## Goal

Produce a repeatable V1 Windows deployment under the current user's local application data directory. Publish self-contained win-x64 executables, install agents and configuration, create at-logon startup and managed wake task integration, initialize/migrate databases, install Playwright Chromium when requested, create safe backups/restores, and provide repair/uninstall/smoke scripts and runbooks.

Do not require Azure, containers, a Windows Service, an MSI, or administrator privileges for normal per-user installation unless a specific Windows operation proves it is required. Detect and report permission limitations instead of bypassing them.

## 1. Publish layout

Create a reproducible Release publish process for:

```text
HomeBusinessAssistant.Host.exe
HomeBusinessAssistant.Runner.exe
FounderScout.exe
WakeRemote.exe
required assemblies/runtime/native files
agent manifests/config schemas/prompts
local static assets
migration assemblies
Playwright support files/scripts
```

Target:

```text
win-x64
self-contained
single-file only if proven compatible with Razor Pages content, EF migrations,
Playwright, and agent file layout; otherwise use normal self-contained folders
ReadyToRun only if measured benefit and no deployment problems
trim disabled unless comprehensive compatibility testing proves it safe
```

Prefer reliability over aggressive size optimization. Record publish decisions in an ADR.

Expected installed layout:

```text
%LOCALAPPDATA%\HomeBusinessAssistant\
├── app\
│   ├── HomeBusinessAssistant.Host.exe
│   ├── HomeBusinessAssistant.Runner.exe
│   ├── ...
│   └── agents\
│       ├── FounderScout\
│       │   ├── FounderScout.exe
│       │   ├── manifest.json
│       │   ├── configuration.schema.json
│       │   ├── prompts\
│       │   └── ...
│       └── WakeRemote\
├── config\appsettings.json
└── data\
```

Do not publish secrets, browser profiles, real databases, captured profiles, logs, or developer settings.

## 2. Version information

Embed/display:

- semantic product version;
- Git commit hash when available;
- build UTC;
- .NET runtime target;
- agent manifest/version;
- DB migration version.

Host, Runner, agents, audit, diagnostics export, and About page must report consistent versions.

## 3. PowerShell installer

Create `scripts/install.ps1` with parameters such as:

```powershell
-SourceDirectory
-InstallDirectory
-DataDirectory
-Port
-InstallPlaywrightBrowser
-StartAfterInstall
-Force
```

Behavior:

1. require PowerShell version suitable for Windows 11;
2. resolve/validate paths;
3. stop an existing Host gracefully or refuse when active runs exist unless forced with clear confirmation;
4. create timestamped pre-install database/config backup if upgrading;
5. copy to a staging directory;
6. validate executable hashes/manifests;
7. atomically replace/install app files where practical;
8. preserve data, browser profiles, secret store, and user config;
9. write/update bootstrap `appsettings.json` without overwriting mutable DB config;
10. run Runner/Host migration/diagnose command;
11. seed/update built-in manifests;
12. optionally install Playwright Chromium using the published supported script;
13. create/update per-user `HostAtLogon` scheduled task;
14. start Host if requested;
15. run a health smoke check;
16. write install result/log without secrets.

Use argument arrays/quoted PowerShell APIs safely. Do not use `Invoke-Expression`.

## 4. Host-at-logon task

Create managed task:

```text
\HomeBusinessAssistant\HostAtLogon
```

Requirements:

- per-user interactive logon trigger;
- starts `HomeBusinessAssistant.Host.exe` with bootstrap config path;
- no duplicate instances due to Host mutex;
- delayed start optional/configurable;
- task description indicates application-managed;
- update/remove idempotently;
- do not configure WakeToRun for HostAtLogon;
- do not store a plaintext password;
- validate task after creation.

Use the same Task Scheduler XML/process infrastructure where practical.

## 5. Uninstall and repair

Create:

### `scripts/uninstall.ps1`

- stop Host/active runs safely;
- remove HostAtLogon and NextWake managed tasks;
- remove application binaries/config optionally;
- preserve data/secrets/browser profiles by default;
- offer explicit separate `-RemoveData` confirmation path;
- never delete outside validated install/data roots;
- produce a clear summary.

### `scripts/repair.ps1`

- verify app/agent files and hashes/manifests;
- verify/migrate databases;
- verify directory permissions/writability;
- verify/repair HostAtLogon;
- reconcile NextWake;
- check Playwright browser installation;
- check Runner/agent executable paths;
- preserve user data/config;
- produce diagnostics/attention results.

## 6. Database backup

Implement application service and CLI/UI integration for safe SQLite backups of:

```text
assistant.db
founders.db
```

Requirements:

- use SQLite online backup API, `VACUUM INTO`, or another proven SQLite-safe method while DB may be open; document choice;
- create a consistent timestamped backup set with manifest;
- back up central and Founder Scout databases separately but under one backup set ID;
- verify each backup opens and contains expected migration history/tables;
- hash files;
- write temp then promote only after validation;
- do not include browser profiles or secret-store files in ordinary backups;
- optionally include redacted bootstrap/config export and manifests;
- default retention: 7 daily, 4 weekly, configurable;
- serialize backup through a lease;
- audit result and bytes/duration.

Do not simply copy active SQLite files without proving consistency.

## 7. Restore

Implement a guarded restore command, initially CLI and optionally UI link:

```powershell
HomeBusinessAssistant.Runner.exe restore-backup --backup-set <id>
```

Restore rules:

- refuse while Host/agents/runs use the databases unless invoked through controlled maintenance mode;
- verify manifest/hashes/schema compatibility;
- create pre-restore backup;
- restore to staging paths and validate;
- atomically replace DBs where possible;
- run migrations only after opening restored version and confirming compatibility;
- reconcile stale runs/leases/tasks after restore;
- preserve secrets/browser profiles;
- detailed audit and rollback on failure.

Destructive restore requires explicit confirmation/token when invoked interactively. Automated tests use temp directories.

## 8. Scheduled maintenance

Seed optional platform schedules for:

- daily backup;
- daily summary;
- retention cleanup;
- periodic diagnostics/health check.

Use normal durable occurrences/Runner/audit. Do not directly run maintenance from Task Scheduler except through Runner if a wake occurrence requires it.

## 9. Upgrade safety

Installer upgrade flow must:

- reject downgrade by default;
- ensure active agents are stopped or allowed to complete;
- back up before DB migration;
- apply binary update before/with compatible migration strategy;
- recover from failed copy/migration by restoring prior app/DB state when possible;
- preserve config revisions and schedules;
- update manifests without overriding user configurations;
- re-register tasks with new paths;
- show version/health after upgrade.

Document current V1 update model. Automatic internet updates are out of scope.

## 10. Operational documentation

Create/update:

- `docs/installation.md`
- `docs/operations.md`
- `docs/backup-restore.md`
- `docs/wake-remote-setup.md`
- `docs/founder-scout-setup.md`
- `docs/troubleshooting.md`
- `docs/security-privacy.md`

Include:

- install/upgrade/uninstall/repair;
- manual Founder Scout authentication;
- Playwright install;
- schedule strategy: discovery fixed delay, analysis event/recovery, no internal sleep;
- wake test and powercfg diagnostics;
- Chrome Remote Desktop/RDP readiness notes without exposing RDP publicly;
- log/artifact/database paths;
- secret recovery implications;
- backup/restore;
- common attention states;
- how to disable all automation quickly;
- how to collect safe diagnostics.

## 11. Smoke-test script

Create `scripts/smoke-test.ps1` that can validate a published/installed application using synthetic data:

- versions/files/manifests;
- Host starts and health endpoints return ready;
- second-instance behavior;
- DB migrations/pragmas;
- fake agent run through Runner;
- Wake Remote short diagnostic mode;
- Founder Scout fixture import/screen/fake evaluation/report;
- schedule and wake XML generation without requiring real sleep;
- backup + validation;
- diagnostics export;
- no orphan processes;
- returns nonzero on failure.

Do not touch live browser accounts or AI by default.

## 12. Tests

Cover publish/install helpers and scripts where practical:

- clean install to temp path;
- upgrade preserves data/config;
- downgrade rejection;
- task XML/action paths with spaces;
- uninstall preserves data by default;
- remove-data path root safety;
- repair idempotency;
- online backup under active reads/writes;
- backup validation/hash/retention;
- interrupted backup not promoted;
- restore success, compatibility rejection, rollback;
- migration failure recovery using controlled test migration/setup;
- smoke script in CI Windows environment if feasible.

## Constraints

- No MSI/WiX requirement in V1.
- No Windows Service.
- No Azure deployment.
- No automatic internet updater.
- No plaintext scheduled-task passwords.
- No ordinary backup of browser profiles or secret store.

## Done when

- Release publish artifacts are deterministic and installable per user.
- Host starts at logon through managed Task Scheduler task.
- Install/upgrade/repair/uninstall are idempotent and safe.
- SQLite backup/restore is validated and auditable.
- Playwright browser bootstrap is documented/optional.
- Operations/security/troubleshooting docs are usable.
- Published smoke test passes.

## Required manual smoke validation

On a clean temporary install path:

1. publish Release win-x64;
2. install per-user;
3. verify HostAtLogon task and tray/UI health;
4. execute smoke script;
5. create/validate backup;
6. perform a controlled restore with synthetic data;
7. run repair;
8. upgrade same version/no-op then a test version;
9. uninstall preserving data;
10. reinstall and verify preserved data;
11. optionally remove synthetic data with explicit flag.

## Final report

Include publish settings/layout/size, installer behavior, task registration, backup technology, restore safeguards, smoke output, and remaining code-signing/MSI concerns. End with:

```text
Next prompt: prompts/16-hardening-e2e-release.md
```

