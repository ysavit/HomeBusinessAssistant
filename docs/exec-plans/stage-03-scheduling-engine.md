# Stage 03 ExecPlan — Durable Scheduling Engine

## Purpose and user-visible outcome

Deliver deterministic, durable occurrence planning without launching processes or registering Windows tasks. A development smoke command will seed a Founder Scout fixed-delay schedule and a Wake & Remote weekday schedule, reconcile at controlled times, complete the Founder Scout occurrence, and prove that its next due time is exactly ten minutes after completion.

## Current repository state

- Stage 02 is validated: the Release build succeeds with no warnings and all 82 existing tests pass before Stage 03 edits.
- `AgentSchedules` stores an untyped JSON definition and policy fields, while `ScheduleOccurrences` currently reuses `AgentRunStatus` and starts rows in `Pending`.
- `(ScheduleId, DueAtUtc)` is already the unique scheduled-occurrence identity, occurrence claims are conditional SQLite updates, and the fenced lease manager is suitable for reconciliation ownership.
- The schedule record does not yet store a command, arguments, optional pinned revision, explicit indefinite-pause state, or per-schedule misfire grace period.
- Installed-agent rows do not yet retain manifest command/scheduling/manual-run metadata needed by application validation.
- Schedule calculation, DST handling, due-work queries, legal occurrence transitions, manual-run creation, fixed-delay completion, and reconciliation are absent.
- The prompt pack and implementation remain untracked on `main`; no commit was requested.

## Scope and non-goals

### In scope

- Versioned typed definitions for manual, one-time, daily, selected-weekday, fixed-interval, and completion-based fixed-delay schedules.
- Deterministic Windows time-zone conversion with explicit DST gap and ambiguity behavior plus reusable UTC/display conversion.
- Field-aware schedule, retry, manifest-command, wake, configuration-revision, and name-collision validation.
- Additive scheduler schema migration, focused occurrence queries/transitions, idempotent bounded planning, misfire and concurrency policy application, pause/enable controls, manual runs, completion hook, reconciliation lease, audit, and earliest wake reporting.
- Unit tests for calculators, definitions, DST, policies, validation, transitions, and fake time; real temporary SQLite tests for identity, contention, planning, reconciliation, manual runs, fixed delay, and migration behavior.
- A self-cleaning Runner scheduling smoke command.

### Non-goals

- Agent process launching, JSONL ingestion, retries execution, heartbeats, timeout enforcement, or stale-run recovery (Stage 04).
- Windows Task Scheduler registration or power management (Stage 05 and Stage 06).
- Host background loops, tray lifecycle, Razor Pages, or other UI (Stage 07 and Stage 08).
- Quartz, Hangfire, distributed coordination, or wall-clock timers.

## Design and data flow

Scheduling types and orchestration live in Application and depend only on Domain policies and focused ports. Infrastructure extends the existing EF entities and repositories; hosts remain composition roots.

```text
typed definition + schedule row + current/pinned revision
                         |
                         v
ScheduleReconciler -- fenced SQLite lease
  -> validate schedule
  -> bounded calculator / idempotent planner
  -> misfire + concurrency decisions
  -> legal occurrence transitions + audit
  -> earliest wake candidate summary

terminal occurrence -> FixedDelayCompletionService
                    -> completion UTC + delay
                    -> exactly one next scheduled identity
```

Occurrences receive their own persisted `OccurrenceStatus`: `Planned`, `Ready`, `Claimed`, `Starting`, `Running`, `Skipped`, `CancellationRequested`, and the run-result terminal states. The separate `AgentRunStatus` remains authoritative only for actual runs. Terminal reason codes are bounded machine-readable values; redacted human detail is stored separately.

Calendar definitions are expanded only within a seven-day future horizon and a bounded recent lookback, with at most 100 creations per schedule per reconciliation. Fixed-delay planning creates only an initial occurrence; subsequent cadence is driven by a terminal hook. Scheduled identity remains `(ScheduleId, DueAtUtc)`, and concurrent creation returns the winning row.

Invalid spring-forward local times advance to the first valid local second after the gap. Ambiguous fall-back local times choose the larger UTC offset, which is the earlier UTC instant, so one intended local slot yields exactly one occurrence.

## Milestones

1. [x] Read required repository context, prior handoff/plan, architecture, ADRs, complete Stage 03 prompt, and scheduling persistence/test paths.
2. [x] Verify the unmodified Stage 02 Release build and 82-test suite.
3. [x] Add typed schedules, retry/options models, time-zone service, calculator, occurrence state machine, and pure policy tests.
4. [x] Extend application ports/records and EF schema/repositories with the additive scheduling migration.
5. [x] Implement validator, bounded planner, reconciliation, schedule controls, manual runs, and fixed-delay completion.
6. [x] Add real-SQLite integration and contention tests plus the controlled scheduling smoke command.
7. [x] Run migration drift, smoke, restore/build/test/format, artifact audit, and update the verified handoff.

## Detailed steps

- Add Domain `OccurrenceStatus` and transition helpers in Application scheduling.
- Add `Scheduling/` definitions, serializer, validation result types, options, retry policy, time-zone conversion, calculator, concurrency/misfire engine, planner, reconciler, control, manual-run, and completion services.
- Extend persistence records and ports for schedule command/config/pause metadata and occurrence status/reason/query/transition operations.
- Extend installed-agent persistence with supported commands and manifest scheduling/manual-run flags so validation does not read files or cross Infrastructure boundaries.
- Update EF entities, mappings, seed logic, indexes, and run/occurrence status mapping; generate an additive `AddDurableSchedulingEngine` migration.
- Update existing Stage 02 tests/fixtures for the explicit Ready state and versioned definitions; add focused Stage 03 tests.
- Add `scheduling-demo` to Runner using only a temporary data root and controlled `TimeProvider`.
- Reconcile architecture/development documentation and canonical project state with actual results.

## Progress

- 2026-08-29: Read mandatory instructions, state, architecture, ADRs, Stage 02 plan, Stage 03 prompt, manifests, persistence entities/mappings/repositories, run terminal path, and existing relevant tests.
- 2026-08-29: Verified baseline Release build with 0 warnings/errors and all 82 tests passing.
- 2026-08-30: Added six stable versioned definition shapes, retry/options models, explicit occurrence states, deterministic DST conversion, calendar/interval calculation, and validation.
- 2026-08-30: Added bounded idempotent planning, misfire/concurrency decisions, due/wake queries, pause controls, manual runs, fixed-delay completion, and lease-fenced reconciliation with redacted audit.
- 2026-08-30: Added `AddDurableSchedulingEngine`, manifest scheduling metadata, default Central time-zone setting, and accepted ADR-0003.
- 2026-08-30: Added 21 tests across Domain, Application, and Infrastructure for 103 total, plus the self-cleaning `scheduling-demo`.
- 2026-08-30: Final restore, Release build, 103-test suite, format, EF drift, scheduling smoke, persistence regression smoke, and sensitive-artifact audit passed.

## Decisions

- Occurrence and run state are separated now because scheduler-only states such as `Planned`, `Ready`, and `Skipped` must not distort process-run semantics.
- Definition JSON uses a stable `version` plus lower-case `type` discriminator and is checked against the persisted `ScheduleKind` before any side effect.
- A schedule normally captures the current configuration revision at occurrence creation; an optional explicit pinned revision is supported and validated as belonging to the same agent.
- `IsPaused` plus nullable `PausedUntilUtc` distinguishes not paused, paused until a UTC instant, and paused indefinitely.
- Misfire `RunImmediately` keeps the original due timestamp as durable scheduled metadata and makes one missed row Ready; older missed rows are skipped as coalesced.
- Fixed-delay cadence follows every terminal result by default, including failure, timeout, cancellation, abandonment, and scheduler skip. Callers may disable/pause a schedule before invoking the hook for authentication/challenge suspension.

## Validation

Required commands:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Stage-specific checks:

- Apply all migrations to a fresh temporary SQLite database and verify the scheduling columns/indexes.
- Run concurrent reconcilers and occurrence creation against real SQLite and prove one scheduled identity.
- Exercise DST gap/ambiguity, all definition shapes, horizons/limits, each misfire/concurrency policy, pause modes, fixed-delay completion, manual trigger sources, revision capture, transition rejection, and lease contention.
- Run `scheduling-demo` and verify printed Founder Scout next due equals completion plus ten minutes.
- Run EF pending-model-change detection and inspect the repository for accidental databases, WAL files, secrets, logs, or artifacts.

Actual results:

- `dotnet restore`: passed; all 24 projects up to date.
- `dotnet build HomeBusinessAssistant.sln -c Release --no-restore`: passed; 0 warnings and 0 errors.
- `dotnet test HomeBusinessAssistant.sln -c Release --no-build`: passed; 103 passed, 0 failed, 0 skipped across ten test projects.
- `dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore`: passed with exit code 0 and no output or changes.
- `dotnet-ef migrations has-pending-model-changes`: passed; no model changes after `AddDurableSchedulingEngine` (`20260830044746`).
- Real SQLite scheduling integration: 15 Infrastructure tests passed, including fresh migration/seed, unique identity, planning limit, lease contention, all misfire/concurrency policies, pause controls, manual queue-one, fixed-delay terminal outcomes, retry separation, and configuration capture.
- Pure scheduling validation: 24 Application tests passed, including all definition round trips, invalid paths, DST gap/ambiguity, calendar/interval/delay calculation, retry bounds, and legal transitions.
- `scheduling-demo`: passed and self-cleaned. First reconciliation created 2; Founder Scout due `2026-08-29T18:00:00Z`, completed `18:02:00Z`, successor due/expected `18:12:00Z`; Wake & Remote due `2026-08-30T18:00:00Z`; second reconciliation created 0.
- `persistence-demo`: passed after Stage 03 changes; database, configuration, DPAPI secret, lease, artifact, and audit checks remained healthy.
- No database/WAL, secret, log, HTML/image diagnostic, temporary scheduling/persistence demo root, or other runtime artifact remained in the repository or temporary directory after validation.

## Recovery and rollback

The Stage 03 migration is additive. Validation uses unique temporary roots that are deleted after disposal. If a migration or smoke fails, retain the failure evidence, correct the model/migration, and rerun against a fresh temporary root; do not down-migrate user data. Reconciliation mutations are short and idempotent, and an expired/released lease can be safely reacquired with a higher fencing token.

## Remaining risks and follow-up

- Stage 04 must connect process lifecycle updates to the occurrence transition API and invoke the fixed-delay completion hook after the run transaction commits.
- Stage 05 consumes the earliest wake candidate but owns Windows Task Scheduler registration.
- Stage 07 supplies the periodic host loop; this stage deliberately exposes only host-independent use cases.
- A future schedule-schema version may expose the fall-back offset choice; V1 intentionally fixes it to the earlier UTC instant.
