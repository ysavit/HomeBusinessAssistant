# Stage 02 ExecPlan — Persistence, Configuration, Audit, and Secrets

## Purpose and user-visible outcome

Deliver the durable central platform boundary for installed agents, immutable configuration revisions, schedules and occurrences, runs and append-only events, artifacts, audit records, leases, settings, and Windows-protected secrets. A development-only Runner command will prove the full local workflow in a temporary data directory without exposing secret contents.

## Current repository state

- Stage 01 is validated: the Release build succeeds with no warnings and all 63 tests pass.
- The solution has no EF Core package, SQLite database, migration, runtime data bootstrap, secret store, artifact store, or persistence repository.
- Agent identifiers and policy/status enums already live in `HomeBusinessAssistant.Domain`; process manifests and protocol contracts live in `HomeBusinessAssistant.AgentSdk`.
- Runner is still a bounded Stage 00 placeholder except for the two agents' Stage 01 protocol demos.
- The prompt pack and all implementation files remain untracked on `main`; no commit was requested.

## Scope and non-goals

### In scope

- EF Core 10 SQLite central schema, explicit mappings/indexes, and an initial migration.
- Data-directory validation and idempotent database initialization with WAL, foreign keys, bounded busy timeout, and `synchronous=NORMAL`.
- Focused application ports and infrastructure adapters for agent definitions, immutable configuration revisions, schedules, occurrences, runs/events/metrics, audit events, artifacts, leases, and system settings.
- Canonical JSON, SHA-256 configuration identity, configuration validation hooks, secret-reference enforcement, redacted bounded audit payloads, and atomic revision promotion.
- Current-user DPAPI secret storage with opaque references, hashed file names, stable purpose entropy, atomic writes, and an in-memory test fake.
- Root-confined atomic artifact storage with traversal/reparse-point defenses, content hashing, size enforcement, and database metadata.
- Idempotent built-in Founder Scout and Wake Remote manifest seeding without mutable-configuration overwrite.
- Real temporary SQLite integration tests, Windows secret-store tests, and a development persistence smoke command.

### Non-goals

- Schedule calculation, recurrence expansion, misfire processing, or Windows Task Scheduler integration.
- Child-process launch/supervision, JSONL ingestion, runner recovery, or process-tree termination.
- Razor Pages management UI, browser automation, AI calls, agent business behavior, or Founder Scout's separate database.
- Generic repositories, distributed locks, distributed messaging, or remote database support.

## Design and data flow

Pure persistence models, requests, results, validation/redaction helpers, and focused ports live in `HomeBusinessAssistant.Application`. EF entity types remain internal to Infrastructure and are mapped explicitly to application records so EF tracking objects do not cross the boundary. `AssistantDbContext` is created per operation through `IDbContextFactory`.

Configuration saves canonicalize and validate JSON before opening a short serializable transaction. A unique `(AgentId, ConfigurationHash)` index makes semantically identical revisions idempotent, while `(AgentId, RevisionNumber)` and atomic current-revision promotion preserve history. Audit records created by configuration changes participate in the same transaction.

Occurrence claims use one conditional database update. Leases use SQLite upsert/conditional update semantics and monotonically increasing fencing tokens. Neither operation relies on an in-memory mutex.

Secret material never enters SQLite. An opaque `secret://` reference maps to a SHA-256 file name under the data root's `secrets` directory; plaintext bytes are protected with Windows DPAPI `CurrentUser`. Artifact bytes similarly stay on disk, while SQLite stores only bounded metadata, hash, size, type, retention, and relative path.

## Milestones

1. [x] Read all mandatory repository context, the complete Stage 02 prompt, current implementation/tests, and official package metadata.
2. [x] Revalidate the unmodified Stage 01 Release build and 63-test suite.
3. [x] Add the Stage 02 application contracts, domain persistence enums, and pure validation/redaction helpers.
4. [x] Add EF Core entities, explicit mappings/indexes, runtime factory/bootstrap, initial migration, and manifest seeding.
5. [x] Implement focused repositories, atomic configuration revisioning, occurrence claims, leases, audit, settings, and artifact storage.
6. [x] Implement the Windows CurrentUser DPAPI secret store and in-memory test fake.
7. [x] Add real-SQLite, file-system, DPAPI, architecture, and smoke tests.
8. [x] Run the migration/database smoke and the development persistence workflow.
9. [x] Run restore, Release build/test, format verification, final audits, and update the canonical handoff.

## Decisions

- EF entities are internal Infrastructure details. Application ports exchange immutable records and typed domain identifiers.
- The initial migration is named `InitialCentralPersistence` and contains only the central `assistant.db` schema.
- All persisted timestamps are UTC `DateTimeOffset` values converted consistently by EF Core.
- Configuration hashes are lowercase SHA-256 hex over recursively property-sorted, compact UTF-8 JSON. Arrays retain input order because array order can be semantically significant.
- A save matching the current configuration hash is a no-op and produces no duplicate revision or audit event. Re-promoting an older existing hash is an explicit audited change.
- Append-only revision, run-event, and audit rows are protected by `AssistantDbContext` against update or deletion through normal EF operations.
- Runner-authored run events will use negative sequence numbers; agent protocol events retain their positive sequence space. This stage persists the fields but does not ingest process output.
- Filesystem writes use unique temporary files and same-directory atomic moves. A failed artifact metadata insert triggers best-effort file cleanup.
- SQLite remains the single-workstation coordination authority; database constraints and conditional statements, not process-local locking, resolve competing configuration saves, claims, and leases.

## Progress

- 2026-08-29: Read the repository rules, canonical Stage 01 state, planning instructions, architecture, ADRs, configuration/development guidance, complete Stage 02 prompt, current projects, and tests.
- 2026-08-29: Verified stable .NET 10 package versions against official NuGet metadata.
- 2026-08-29: Verified the unmodified baseline: Release build passed with 0 warnings/errors and all 63 tests passed.
- 2026-08-29: Added complete Application persistence/secret contracts, canonical JSON and hashing, schema and secret-reference validation, redaction, and explicit Domain audit/schedule enums.
- 2026-08-29: Added all 12 internal EF entities/mappings, `InitialCentralPersistence`, per-connection pragmas, idempotent built-in manifest seeding, and focused repositories using short-lived contexts.
- 2026-08-29: Added atomic first-write/no-op configuration revisioning, scheduled-occurrence identity/claim/cancellation, run creation/terminal transitions, append-only events/audit, numeric/text metrics, monotonic fenced leases, and optimistic schedule/setting updates.
- 2026-08-29: Added root-confined artifact storage and a compensated DPAPI CurrentUser secret store with hashed references and in-memory fake.
- 2026-08-29: Added real temporary SQLite/filesystem/DPAPI tests and the self-cleaning Runner `persistence-demo`.
- 2026-08-29: Reconciled architecture/configuration/development documentation and accepted ADR-0002.
- 2026-08-29: Final restore, Release build, 82-test suite, format verification, migration drift check, and persistence smoke passed.

## Validation

Required commands:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Stage-specific checks:

- Apply migrations to a new temporary database, inspect every required table/index, and verify the configured SQLite pragmas.
- Run competing configuration saves, occurrence claims, and lease acquisitions against real temporary SQLite databases.
- Run the development persistence workflow in a temporary data directory: migrate/seed, save non-sensitive configuration, round-trip/delete a DPAPI secret, acquire/release a lease, persist an audit record, and write a hashed artifact.
- Confirm the smoke output contains only non-sensitive identifiers/statuses and remove its temporary data directory.
- Inspect Git status and the repository for accidental databases, WAL files, secrets, artifacts, logs, or unrelated changes.

Actual results:

- `dotnet restore`: passed; all 24 projects up to date.
- `dotnet build HomeBusinessAssistant.sln -c Release --no-restore`: passed; 0 warnings and 0 errors.
- `dotnet test HomeBusinessAssistant.sln -c Release --no-build`: passed; 82 passed, 0 failed, 0 skipped across ten test projects.
- `dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore`: passed with exit code 0 and no output/changes.
- `dotnet-ef migrations has-pending-model-changes`: passed; no model changes after `InitialCentralPersistence` (`20260830041353`).
- Fresh temporary database integration: migration, all 12 tables, required unique/supporting indexes, foreign keys, WAL, 5000 ms busy timeout, `synchronous=NORMAL`, seeding, and idempotent restart passed.
- Concurrent integration: six competing first configuration saves produced one revision/audit; eight occurrence claimers produced one winner; eight lease acquirers produced one winner; renew/release/reacquire produced fencing token 2 and rejected the stale token.
- DPAPI integration: CurrentUser round trip/delete, ciphertext not equal to plaintext, audit compensation, and corrupted ciphertext fail-closed behavior passed.
- Artifact/audit/run integration: traversal and size rejection, temp cleanup, SHA-256/size/readback, audit redaction/append-only enforcement, cancellation, unique run events, and terminal run/occurrence transition passed.
- Runner `persistence-demo`: exit 0; `database=ready`, configuration revision ID, secret round-trip passed, lease released, artifact/audit IDs, and `persistenceDemo=passed`; temporary root deleted.

## Recovery and rollback

All test and smoke databases, artifacts, and encrypted secrets are created under unique temporary roots and deleted after validation. No machine-wide settings or Windows credentials are changed. The migration is additive and can be repaired before any production data exists; no down migration will be run against user data during this stage.

## Remaining risks and follow-up

- DPAPI `CurrentUser` deliberately binds encrypted values to the current Windows user profile; copying files to another user or machine is not a recovery strategy.
- SQLite coordinates one workstation well, but long external work must remain outside transactions. Later scheduler and runner stages must keep their database mutations short.
- Scheduling behavior begins in `prompts/03-scheduling-engine.md`; this stage supplies only storage and atomic primitives.
