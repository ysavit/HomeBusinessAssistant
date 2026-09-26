# Stage 15 ExecPlan — Packaging, Installation, and Operations

## Purpose and user-visible outcome

Deliver a repeatable per-user Windows deployment that publishes the Host, Runner, Founder Scout, and Wake Remote as self-contained `win-x64` folders; installs them below `%LOCALAPPDATA%\HomeBusinessAssistant`; manages current-user Host startup; initializes and repairs the runtime; and creates, validates, retains, and restores consistent backups of both SQLite databases without copying active WAL files. Operators receive version/about information, backup controls, safe diagnostics, smoke automation, and complete recovery runbooks.

## Current repository state

Stage 14 is verified and the repository builds in Release with zero warnings. The Host and Runner already share migration/bootstrap fencing, immutable configuration and audit stores, the Runner owns process supervision, and the Windows project owns a bounded `schtasks.exe` bridge for `NextWake`. The Host already has a Settings/health page and a placeholder explicitly deferring backup to Stage 15. Founder Scout has a separate migrated database at `data/agents/founder-scout/founders.db`. There is no publish assembly script, deployment manifest, Host-at-logon task, build metadata file, online database backup, guarded restore command, repair/uninstall flow, or installed-application smoke script.

The repository state was reconciled against source and a baseline `dotnet build HomeBusinessAssistant.sln -c Release --no-restore` pass on 2026-08-31 (0 warnings, 0 errors).

## Scope and non-goals

In scope:

- reliable untrimmed, non-single-file, self-contained `win-x64` publishing;
- a hash-manifested install bundle and consistent build identity;
- idempotent per-user install, upgrade, repair, and uninstall scripts;
- an application-managed `\HomeBusinessAssistant\HostAtLogon` task with current-user interactive-logon semantics;
- SQLite online backup, verification, retention, guarded restore, rollback, audit, Runner CLI, and Host Settings integration;
- optional Playwright Chromium bootstrap and synthetic smoke validation;
- operator installation, operations, backup/restore, agent setup, troubleshooting, and privacy runbooks.

Out of scope: MSI/WiX, a Windows Service, administrator-only installation, automatic internet updates, code signing infrastructure, cloud deployment, copying DPAPI secret files or browser profiles, live Startup School/AI use, and any automated invitation sending.

## Design and data flow

```text
dotnet publish (4 executables)
        -> staged release tree
        -> build-info.json + SHA-256 publish-manifest.json
        -> install.ps1 validates/stages/swaps app files
        -> config/appsettings.json + migrations + manifest seed
        -> Runner registers HostAtLogon + smoke health

Host Settings / Runner CLI
        -> database-backup lease in assistant.db
        -> SQLite online backup to .pending set
        -> integrity/schema/migration/hash verification
        -> atomic directory promotion + retention + audit

restore-backup --confirm RESTORE --maintenance true
        -> require Host/agent/runs stopped
        -> validate manifest/hash/schema/version
        -> create pre-restore backup
        -> stage and validate both databases
        -> replace with per-file rollback copies
        -> migrate/recover/reconcile/audit
```

Build metadata is a checked-at-runtime `build-info.json` generated once per publish and copied beside every executable. Assembly metadata supplies a safe development fallback. The deployment manifest hashes only release content; data, secrets, profiles, logs, databases, and developer settings never enter the bundle.

The backup service uses the Microsoft SQLite online-backup API with independent source/destination connections. It never copies a live database, WAL, or SHM file. A backup set is not visible until both databases pass `quick_check`, expected-table, migration-history, size, and SHA-256 verification. Restore accepts only completed manifest-backed sets, requires explicit maintenance confirmation, stages both files first, preserves rollback copies until both replacements validate, then initializes current migrations and reconciles recovery/wake state.

## Milestones

1. Record publish/update/backup decisions and add shared build identity.
2. Implement and test online backup/retention and guarded restore.
3. Add Runner maintenance/version/startup-task commands and Host backup/About surfaces.
4. Assemble deterministic publish output and implement installer/repair/uninstall/Playwright/smoke scripts.
5. Execute focused and full automated validation plus a clean temporary publish/install/backup/restore/repair/uninstall smoke.
6. Finish runbooks, architecture, and canonical project-state handoff.

## Detailed steps

- Set repository product/assembly/file versions and add `ProductBuildInfo` loading from a bounded JSON file.
- Add Windows Host-at-logon XML generation and `schtasks.exe` reconciliation with semantic verification, plus Runner commands used by scripts.
- Add Application backup contracts and an Infrastructure implementation using `SqliteConnection.BackupDatabase`, database leases, safe path policy, manifests, retention, and audit.
- Add Runner parsing/runtime handlers for version, migrate/diagnose, backup listing/creation/validation, and restore with explicit confirmation and maintenance checks.
- Add Host Settings backup action/history and an About page containing product/build/runtime/manifest/migration versions.
- Add configuration schemas and publish/install manifests without changing mutable database configuration.
- Add PowerShell scripts with explicit parameters, argument arrays, root checks, staging/rollback, no `Invoke-Expression`, and JSON result logs.
- Add NUnit coverage for build identity, backup consistency/failure/retention/restore, task XML paths with spaces, command parsing, UI handlers, and script safety/contracts.

## Progress

- [x] Reconciled Stage 14 handoff, required guidance, architecture, ADRs, prompt, runtime composition, and a clean baseline build.
- [x] Publish identity/layout implemented and validated as a 1,174-file, 562,668,748-byte self-contained `win-x64` bundle (1,173 payload entries plus its manifest).
- [x] Backup/restore implemented and validated under concurrent writes, retention, interrupted-set, guarded restore, and immediate post-restore backup tests.
- [x] Host-at-logon XML/bridge and install/repair/uninstall scripts implemented; unit and script flows pass, while this machine's Task Scheduler COM/root query returns `0x80070003`, so real registration is reported as an environmental limitation.
- [x] Host Settings backup action, About page, and all seven operational runbooks implemented.
- [x] Required full and published-install smoke validation completed except real HostAtLogon creation; synthetic installation roots were removed after validation.

## Decisions

- Prefer normal self-contained folders: Razor content, Playwright native/support layout, migration discovery, and separately installed agents are more reliable than single-file extraction. Trimming and ReadyToRun remain disabled.
- Use one release-level build metadata document for cross-process consistency and assembly metadata as a development fallback.
- Use SQLite's online backup API, not file copying. Backup sets include databases and redacted metadata only; DPAPI secrets and browser state remain explicitly excluded.
- Reuse the existing shell-free bounded process executor and managed-task conventions for HostAtLogon, but give it a separate XML contract with `WakeToRun=false` and a logon trigger.
- Installation remains per-user and does not request elevation. Permission failures are surfaced with recovery instructions.
- Automatic update discovery/downloading is out of scope; the update model is explicit local bundle replacement with pre-migration backup and rollback.

## Validation

Required commands:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Stage checks published all four executables for `win-x64`, verified the manifest/layout, installed into guarded temporary roots, ran migration/versions/health/Wake/Founder fixture/backup/restore/diagnostics checks, repaired twice, exercised same-version and test-version upgrades, rejected downgrade, uninstalled while preserving data, reinstalled over preserved data, and removed only the exact validated synthetic roots. The custom temporary data root is intentionally outside the uninstall script's approved destructive locations, so final cleanup used one-shell absolute-path/name validation before removal. Real Startup School, AI, sleep, remote access, and invitation delivery were forbidden in the smoke.

Final verified results on 2026-08-31:

- dependency restore passed after granting NuGet metadata access; the restricted-network attempt failed with `NU1900` and was superseded;
- Release build passed with 0 warnings and 0 errors; formatting verification passed;
- 272 tests passed across ten projects, with the intentional interactive Browser fixture skipped;
- both EF models reported no pending changes;
- the final 1.0.0 publish contains 1,173 manifest payload entries plus the manifest itself and is 562,668,748 bytes;
- clean install, packaged smoke, backup validation, controlled restore, immediate post-restore backup, same-version update, two idempotent repairs, 1.0.1 upgrade, downgrade refusal, uninstall-preserve, and reinstall-over-preserved-data all passed;
- packaged smoke returned `status=passed`, Host ready, second-instance passed, Wake Remote diagnostic passed, Founder Scout `import-screen-report` passed, backup valid, diagnostics 2,480 bytes, and no managed wake-task change;
- desktop About and Settings browser QA passed with zero console warnings/errors. The Settings backup click exposed a restored stale lease; the restore path now expires that lease and a regression test plus real immediate backup pass. Browser policy blocked a second mobile pass after the Host restart, so that specific repeat remains manual;
- Task Scheduler service was running, but both `schtasks /Query` and COM root-folder access returned path-not-found on this host. The Runner reports one bounded safe error and the XML/adapter contract is covered by 19 Windows tests.

## Recovery and rollback

The installer leaves data untouched while app files are staged and hash-checked, keeps the previous app folder until migration and health checks pass, and restores it on failure. Upgrades create a completed pre-install backup set before migration. Interrupted backup directories remain `.pending-*` and are never listed as restorable. Restore creates a pre-restore set, uses per-file rollback copies, removes SQLite sidecars only after exclusive maintenance checks, and reinstates both originals if any replacement or validation fails. Uninstall preserves data by default and accepts `-RemoveData` only for an explicitly validated application data root.

## Remaining risks and follow-up

Authenticode signing, MSI packaging, clean-machine SmartScreen behavior, a formal release provenance pipeline, exhaustive migration-failure fault injection, hardware wake, and live browser/provider checks remain Stage 16/release concerns. Task Scheduler behavior still depends on current-user policy and an interactive sign-in. DPAPI secrets remain intentionally non-portable and require re-entry after user/machine loss.

The prompt requested optional durable schedules for backup, summary, retention, and diagnostics. They are not auto-seeded in Stage 15 because the published process contract contains only Founder Scout and Wake Remote; inserting schedules whose command cannot execute would corrupt the durable scheduler's guarantees. Runner/UI operations exist for all four capabilities. A future versioned platform-maintenance command/agent contract is required before those schedules can be safely enabled.
