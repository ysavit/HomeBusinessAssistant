# ADR-0013: Windows folder deployment and SQLite online-backup boundary

- Status: Accepted
- Date: 2026-08-31
- Stage: 15

## Context

The platform now consists of an interactive Razor/WinForms Host, a one-shot Runner, two independently published agents, EF migration assemblies, Playwright support, static web assets, prompts, and manifests. Runtime data includes two WAL-mode SQLite databases, non-portable DPAPI secrets, and sensitive persistent browser profiles. Stage 15 must make this layout installable and recoverable without elevating a normal current-user installation or weakening those boundaries.

## Decision

Publish four untrimmed, non-single-file, self-contained `win-x64` applications. ReadyToRun is disabled. A release-level `build-info.json` and sorted SHA-256 `publish-manifest.json` bind the product version, optional source revision, build UTC, runtime target, and every distributable file. The installer validates the complete staged tree before replacing the `app` directory and keeps mutable bootstrap configuration under `config` and runtime state under `data`.

Install and update remain explicit PowerShell operations over a local bundle. They use argument arrays and file APIs, never `Invoke-Expression`; reject downgrade by default; preserve configuration/data/secrets/browser state; create a pre-migration backup; and retain a rollback app folder until migration and health checks pass. `HostAtLogon` is a separate application-managed current-user interactive logon task. It never wakes the machine, stores no password, and starts the Host with an explicit bootstrap-config path.

Back up `assistant.db` and `founders.db` with SQLite's online backup API through independent source and destination connections. A central database lease serializes creation. Each file is opened and checked for integrity, expected tables, and migration history, then hashed. A set is promoted from a root-confined `.pending-*` directory only after both files validate. Ordinary sets exclude SQLite sidecars, secret files, browser profiles, logs, captured profiles, and diagnostic artifacts.

Restore is an explicit maintenance operation. It requires the Host, agents, and active runs to be absent, validates the set manifest/hashes/product/schema compatibility, creates a pre-restore set, stages both databases, and preserves rollback copies until both replacements and current migrations succeed. Secrets and browser profiles are not changed.

Host secondary-instance commands use a per-user/session identity plus a persistent random 192-bit capability stored below the validated data root to derive an unguessable mutex/pipe suffix. Only the bounded command enum is accepted. This accommodates Windows managed/app-container token differences that made `PipeOptions.CurrentUserOnly` and explicit current-SID ACLs fail cross-process on the target desktop while keeping the transport local and capability-protected. The capability file is excluded from ordinary database backup.

Optional platform-maintenance schedules are not seeded until a versioned executable command contract exists for backup, summary, retention, and diagnostics. The current durable scheduler targets independently executable agent commands; seeding records that cannot execute would be less safe than exposing the implemented Runner/UI operations explicitly.

## Alternatives considered

### Single-file, trimming, or ReadyToRun

These increase packaging risk for Razor content discovery, EF migrations, Playwright native/support files, and separately laid-out agents without a demonstrated V1 benefit. Reliability wins over bundle size.

### Copy live `.db`, `-wal`, and `-shm` files

Coordinating three files correctly while writes continue is error-prone and exposes partially checkpointed state. SQLite's backup API provides a consistent database image and is the supported boundary.

### Include secrets and browser profiles in ordinary backup

DPAPI files are not portable across Windows users or machines, and browser profiles contain sensitive authenticated session state. Both remain excluded; recovery requires secret re-entry and browser reauthentication.

### MSI, Windows Service, or automatic updater

None is required for a per-user V1 and each expands privilege, signing, or supply-chain scope. They remain future release options.

## Consequences

- Release folders are larger than trimmed/single-file output but inspectable and predictable.
- A publish bundle is reproducible when version, source revision, and build UTC inputs are held constant.
- Backups are database-consistent under normal concurrent activity and portable except for deliberately excluded secrets/authentication state.
- Restore is intentionally unavailable as an unattended in-place operation while the interactive Host or agents are running.
- Authenticode signing and installer reputation remain release concerns rather than hidden requirements of the folder deployment.
- Task Scheduler registration can be unavailable even when its Windows service reports Running; scripts and Runner surface a bounded failure instead of elevating or bypassing policy.
- Daily backup, summary, retention, and diagnostic operations are callable but are not automatically scheduled in this version.

## Validation

Stage 15 validates the XML/action paths with spaces, four executable publish layout, manifest hashes, clean temporary install and repair, same-version and 1.0.1 update, downgrade refusal, database backup during active reads/writes, interrupted-set invisibility, hash/schema rejection, controlled restore/rollback, data-preserving uninstall/reinstall, and a synthetic published smoke with no live external account or AI access. Real `HostAtLogon` registration could not be validated on the execution host because both `schtasks` and Task Scheduler COM root access returned path-not-found; the failure path and XML semantics were validated instead.
