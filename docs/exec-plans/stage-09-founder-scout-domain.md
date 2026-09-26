# Stage 09 ExecPlan — Founder Scout Domain, Persistence, and Agent Shell

## Purpose and user-visible outcome

Stage 09 turns Founder Scout from a protocol demonstration into an independently executable, durable agent bounded context. A user can import bounded local fixtures through the central Runner, rerun the same capture without duplicating candidates or snapshots, inspect real domain counts, run database/configuration diagnostics, and produce placeholder JSON and Markdown reports. Live browser access, parsing/scoring, AI evaluation, candidate UI, and invitation sending remain absent.

## Current repository state

- The verified Stage 08 baseline builds with 0 warnings/errors and all 175 inherited tests pass.
- `FounderScout.Domain` contains only an assembly marker.
- `FounderScout.Application` contains a schema 1.0 baseline configuration/default provider and the platform configuration-validator adapter. It has no domain repository or import/report use cases.
- `FounderScout.Infrastructure` contains only an assembly marker and has no EF Core dependency, database context, migration, or repository.
- `FounderScout.Agent` supports only the standalone `protocol-demo` command.
- The checked-in Founder Scout manifest exposes only `run`; central manifest seeding is already idempotent and preserves user-managed enabled/configuration state.
- `assistant.db` remains the central platform database. No `founders.db` exists and the project-state handoff records its migration as `None`.
- The Runner supplies exact configuration and occurrence JSON through the private schema 1.0 execution-input file and supplies a root-confined agent data directory and per-run artifact directory.

## Scope and non-goals

### In scope

- Pure Founder Scout domain enums, records, reason codes, and explicit state-transition services.
- Separate `founders.db` EF Core context, migration, SQLite pragma initialization, append-only action enforcement, and indexes.
- Focused repository interfaces and EF implementations for browser accounts, discovery, candidates, identities, snapshots, screening, evaluations, invitation drafts/queue, actions, reports, and analysis claims.
- Atomic strong-identity resolution, alias conflict detection, snapshot idempotency, legal candidate transitions, and bounded analysis claims.
- Versioned fixture capture envelopes, safe local JSON/JSONL/text import, durable raw snapshot artifacts, deterministic hashes, and idempotent persistence.
- Real `run`, `import`, `diagnose`, and `report` behavior; explicit deferred semantics for `discover`, `analyze`, and `authenticate`; retained standalone `protocol-demo`.
- Manifest command/capability update, real temporary-SQLite tests, and a real Runner-to-FounderScout smoke.

### Non-goals

- Playwright, live Startup School access, browser authentication, HTML parsing, or source-site navigation (Stage 10).
- Normalization/screening pipeline and production work processing (Stage 11).
- AI provider calls, scoring arithmetic, invitation generation, or evaluation caching behavior (Stage 12).
- Candidate/invitation/report management UI (Stage 13).
- Automatic invitation or message sending in any stage.

## Design and data flow

```text
assistant.db occurrence/run
        |
        | Runner private input + run/correlation IDs
        v
FounderScout.exe import
        |
        +--> validate fixture and source path
        +--> resolve/create candidate by strong alias
        +--> atomically write raw capture under Founder Scout data root
        +--> short founders.db transaction:
             snapshot row -> current snapshot -> analysis-pending state -> actions
        +--> JSONL metrics/summary to Runner

FounderScout.exe report
        +--> bounded founders.db aggregate query
        +--> durable Founder Scout report copy + hash metadata
        +--> per-run JSON/Markdown artifacts for Runner ingestion
```

`assistant.db` and `founders.db` never share foreign keys or a transaction. Founder Scout persists domain state first and then emits Runner protocol events. Domain action rows carry the central run ID/correlation value as data only. Imports are idempotent by strong identity plus `(CandidateId, SourceProfileKey, ContentHash)`, so a central finalization failure can be retried safely.

The Runner-assigned `--data-directory` is the effective filesystem authority for one agent execution. The typed `dataDirectory` setting remains validated configuration metadata, while all database/snapshot/report paths are root-confined beneath the Runner-assigned directory. This prevents a mutable agent configuration revision from broadening filesystem access.

Raw fixture artifacts are committed with an atomic temporary-file move before the database transaction can set the candidate to pending analysis. If the later transaction fails, an unreferenced content-addressed file may remain and is safe to reuse or remove during future retention work; the inverse (queued analysis without raw content) is prevented.

## Milestones

1. Add domain models/state machines and expand typed configuration without breaking the Stage 08 editor.
2. Add the separate EF model, initializer, first migration, focused repository implementation, and persistence tests.
3. Add fixture import, report, and diagnostic application services with bounded filesystem behavior.
4. Replace the process placeholder with the real SDK command shell and update the manifest.
5. Add process/Runner smoke coverage, validate migration drift and all repository quality gates, then update handoff documentation.

## Detailed steps

- Add domain files under `agents/FounderScout/FounderScout.Domain/` for state enums, business records, transition errors/results, and transition maps.
- Extend `FounderScoutConfiguration.cs` with priority formula weights and a typed founder-persona reference/content. Validate paths, counts, thresholds, weight totals, safe secret references, bounded persona values, and prohibited sensitive-property shapes. Update the Stage 08 Razor form mapping so an ordinary edit preserves all schema 1.0 fields.
- Add application persistence records/interfaces and import/report service contracts under `FounderScout.Application` without exposing EF entities.
- Add EF Core SQLite/Design package references to `FounderScout.Infrastructure`; implement internal persistence entities/configurations, `FounderScoutDbContext`, a short-lived context factory, bootstrap lock/pragmas, and focused repository adapters.
- Generate an initial Founder Scout migration in the Infrastructure assembly. Test table/index/foreign-key/pragma behavior against fresh temporary files and confirm there are no pending model changes.
- Implement import parsing and artifact storage with conservative extensions/count/size/depth/path/reparse checks, source-key/URL normalization, SHA-256 identities, and secret/session/protected-field rejection.
- Implement report generation from persisted counts and rankings, durable report-export metadata, and Runner-staged JSON/Markdown artifacts.
- Implement SDK session composition in `FounderScout.Agent`, including exact normal command parsing, optional direct `import --input` extraction, private execution-input validation, stable exit codes, JSONL-only stdout, and bounded stderr.
- Update the checked-in manifest to expose the real normal commands and retain central idempotent seeding behavior.
- Add domain, persistence, import, command, and actual Runner integration tests using only synthetic fixtures and temporary directories.

## Progress

- [x] 2026-08-30: Read repository guidance, project handoff, execution-plan rules, architecture, ADR-0001/0002/0006, configuration examples, acceptance matrix, protocol contract, Stage 09 prompt, and relevant implementation/tests.
- [x] 2026-08-30: Reconciled Stage 08 claims against direct inspection; inherited Release build passed with 0 warnings/errors and all 175 tests passed.
- [x] 2026-08-30: Added immutable domain records, explicit candidate/account/snapshot/evaluation/invitation transitions, priority/persona configuration, validation, and Stage 08 editor round-tripping.
- [x] 2026-08-30: Added isolated EF Core SQLite persistence and migration `20260830200319_InitialFounderScoutPersistence`, focused repository adapters, append-only enforcement, identity/snapshot constraints, and expiring analysis claims.
- [x] 2026-08-30: Added bounded JSON/JSONL/text import, root-confined atomic raw artifacts, actual domain metrics, durable report metadata, and JSON/Markdown report files.
- [x] 2026-08-30: Replaced the protocol-only agent with real `run`, `import`, `diagnose`, and `report` commands plus explicit Stage 10/11 deferred commands; manifest version is 1.1.0.
- [x] 2026-08-30: Added real temporary-SQLite, security, concurrency, CLI, and Runner/child-process smoke coverage. The Stage 09 project has 24 passing tests.
- [x] 2026-08-30: Required restore, Release build, all 195 tests, format verification, both migration-drift checks, and the required Runner smoke passed. Updated architecture, ADR, configuration/protocol/development docs, README, and canonical project state.

## Decisions

- Keep EF entities internal to Infrastructure; immutable domain/application records cross the boundary.
- Store enum values as stable strings and timestamps as UTC `DateTimeOffset` strings for readable migrations and explicit compatibility.
- Enforce strong alias ownership with a filtered unique index. Weak fingerprint aliases are intentionally non-unique until an explicit manual merge, preventing false-positive deduplication.
- Keep CandidateAction append-only at the context boundary. Legal state changes and their action rows share one short local transaction where the repository exposes a transition operation.
- Use database-conditional updates for analysis claims so competing local processes cannot both claim the same pending candidate.
- Keep raw fixture content out of normalized candidate columns. The Candidate schema contains operational text/status/score fields only and no protected demographic/photo fields.
- Treat `protocol-demo` as a standalone diagnostic command rather than a Runner-supported manifest command, consistent with protocol 1.0.
- Compose the Founder Scout validator/default provider in Runner as well as Host. This allows a fresh Runner-owned database to execute the manifest without relying on the interactive Host having started first, while `SeedIfMissingAsync` preserves existing revisions.

## Validation

Required final commands:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
dotnet tool run dotnet-ef migrations has-pending-model-changes --project agents/FounderScout/FounderScout.Infrastructure --startup-project agents/FounderScout/FounderScout.Infrastructure --configuration Release --no-build
```

Actual results on 2026-08-30:

- `dotnet restore` — passed; 25 projects considered, 2 restored and 23 current.
- `dotnet build HomeBusinessAssistant.sln -c Release --no-restore` — passed with 0 warnings and 0 errors.
- The first full test attempt exposed one inherited assertion that still expected the Stage 08 Founder Scout manifest command array `['run']`. The manifest was intentionally expanded; the assertion was updated to the exact Stage 09 list. A later added composed-`run` test correctly exercised import plus reporting but initially searched for duplicate counts in the terminal report summary rather than the emitted `snapshots.duplicate` metric; that test-only expectation was corrected. The final full run superseded both failures.
- Final `dotnet test HomeBusinessAssistant.sln -c Release --no-build` — passed: 195 tests, 0 failed, 0 skipped across 10 projects. Founder Scout contributed 24 tests.
- `dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore` — passed with no output.
- Founder Scout and central `dotnet-ef migrations has-pending-model-changes` checks — both reported no changes since their last migrations.
- Required Runner smoke — passed in `FounderScoutRunnerIntegrationTests`: first four-envelope import created 3 candidates and 3 snapshots with 1 in-file duplicate; replay created 0 candidates/snapshots and reported 4 duplicate snapshots; `diagnose` completed healthy; `report` produced 2 centrally ingested artifacts; counts remained 3 candidates, 3 snapshots, and 3 pending-analysis candidates; a Founder Scout action carried the same central run ID whose protocol events were persisted in `assistant.db`.

## Recovery and rollback

- A failed fresh migration leaves only the caller-selected Founder Scout data directory; initialization never falls back to `EnsureCreated`. Correct the migration and rerun `Migrate` on the temporary/test database.
- Import writes content-addressed artifacts before committing snapshot state. A failure after the file move but before the database commit can leave an unreferenced file; rerunning the same import safely reuses the path. Retention cleanup is deferred and must never delete referenced content.
- Strong-alias or snapshot uniqueness races are resolved by database constraints and reread/idempotent results. An ambiguous weak fingerprint creates a separate candidate rather than merging automatically.
- Claim expiry makes interrupted analysis work eligible again. A stale worker ID cannot finalize a later worker's claim.
- Central Runner finalization and local Founder Scout commits are intentionally not atomic. Retry safety comes from local identities/hashes and append semantics, not compensating deletion.

## Remaining risks and follow-up

- Stage 10 must populate account/session state through ordinary headed Playwright workflows and versioned source adapters without weakening enforcement-stop behavior.
- Stage 11 must parse/normalize captures, apply protected-attribute redaction, screen deterministically, and exercise the analysis queue.
- Stage 12 must populate evaluation/category/risk/draft schemas and perform all final score arithmetic in C#.
- Stage 13 must add bounded candidate, invitation queue, and export UI reads/mutations.
- Artifact/report retention deletion, backup/restore, and large-dataset performance proof remain later-stage work.
