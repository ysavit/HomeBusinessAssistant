# Stage 04 ExecPlan — Runner and Agent Process Supervision

## Purpose and user-visible outcome

Deliver `HomeBusinessAssistant.Runner.exe` as the single durable execution path for scheduled and manual occurrences. An operator can execute one occurrence, create and execute a manual occurrence, recover stale work, or diagnose bootstrap paths from the Runner CLI. The runner claims work once, validates the exact installed executable and immutable configuration revision, supervises a real child process, safely ingests protocol 1.0, persists operational state, enforces timeout/cancellation, and finalizes the run and its fixed-delay successor.

## Current repository state

- Stage 03 is directly revalidated before edits: the Release build succeeds with zero warnings/errors and all 103 existing tests pass.
- The solution contains 14 production projects and 10 test projects. Runner currently exposes only `persistence-demo` and `scheduling-demo`; it does not launch or supervise agents.
- SQLite already persists installed-agent definitions, immutable configuration revisions, durable occurrences, runs, append-only run events, metrics, artifacts, audit events, and fenced leases.
- `OccurrenceRepository.TryClaimAsync` atomically changes one due `Ready` occurrence to `Claimed`. `AgentRunRepository.CreateAsync` atomically links one run and changes its occurrence to `Starting`. Terminal run/occurrence persistence is transactional but is not yet idempotent and has no running/heartbeat/recovery operations.
- Agent run rows currently require a non-null process ID before a child exists and expose no recorded process start time. A Stage 04 additive migration is required.
- Protocol 1.0 already supplies strict deserialization/validation, ten known event types, forward-compatible unknown events, a 262,144-byte maximum line, stable agent exit codes, and the complete invocation parser.
- `FileSystemArtifactStore` copies supplied streams into platform-owned artifact storage. Stage 04 must add registration of an agent-created file only after root containment and integrity checks.
- The initial built-in agent seed stores working directories beneath a relative `agents/<agent-id>` path. Runner will interpret the configured agent directory as the containing application-agent root and all executable/working paths as relative, contained paths.
- The prompt pack and all implementation remain untracked on `main`; no commit was requested.

## Scope and non-goals

### In scope

- Stable, tested Runner parsing and exit codes for `execute`, `run-agent`, `recover`, `diagnose`, and a self-cleaning supervision smoke.
- Exact-revision occurrence execution, atomic claim, conditional agent execution lease, executable/manifest integrity validation, SHA-256 recording, process launch through `ProcessStartInfo.ArgumentList`, and restrictive temporary configuration lifecycle.
- Concurrent bounded stdout/stderr consumption, protocol lifecycle validation, run event/metric/artifact/summary ingestion, runner and lease heartbeats, persisted cancellation polling, timeout/grace/process-tree termination, and cleanup on every path.
- Idempotent run finalization, deterministic fallback summaries, audit, fixed-delay completion, and bounded retry-occurrence creation when the schedule policy permits it.
- Stale run/occurrence recovery, conservative PID identity checks, expired lease reuse through existing fencing semantics, and stale temp cleanup.
- A test-only executable using the real Agent SDK for success, failure, all-event, heartbeat, hang, malformed, sequence, oversized, artifact, stderr, and no-summary scenarios.
- Real-process integration/end-to-end tests plus required CLI smoke validation.

### Non-goals

- Tray/UI/background host loops, Windows Task Scheduler registration, wake/power APIs, or Windows Service hosting.
- Founder Scout or Wake Remote business workflows.
- Agent-side plaintext-secret resolution or cooperative cancellation transport beyond process lifetime and the protocol 1.0 exit contract.
- Distributed coordination, shell execution, or automatic unbounded retries.

## Design and data flow

Runner remains a thin composition root. Focused execution services live in Infrastructure because they adapt process, filesystem, SDK protocol, SQLite, and Serilog boundaries; application-facing records and persistence ports remain in Application.

```text
Runner CLI
  -> initialize assistant.db + validated bootstrap roots
  -> recover stale temp files
  -> IOccurrenceExecutor
       -> atomic Ready -> Claimed
       -> optional fenced agent lease
       -> exact configuration revision + persisted agent + on-disk manifest
       -> contained executable/working paths + executable SHA-256
       -> AgentRun Starting + occurrence Starting
       -> temp config (secret references only)
       -> child process, redirected UTF-8 stdout/stderr, no shell
       -> AgentRun/occurrence Running with PID/process-start identity
       -> protocol/stderr readers || runner heartbeat/lease renewal || cancellation/timeout monitor
       -> grace, process-tree kill when required, readers drained
       -> authoritative idempotent finalization + audit
       -> fixed-delay completion and bounded explicit retry occurrence
       -> lease/temp/log cleanup
```

Known agent event sequences retain their positive protocol sequence in `AgentRunEvents`; runner diagnostics use atomically allocated negative sequences. Metrics are append-only observations. Unknown minor-version events are bounded, redacted runner diagnostics and do not count as protocol violations. Ordinary malformed/invalid records mark the stream degraded; the configured fifth violation is fatal. Run-ID mismatch, artifact containment/integrity violations, records after `completed`, duplicate terminal records, and completed/actual-exit disagreement are immediately fatal because they cross identity, filesystem, or authoritative lifecycle boundaries.

Timeout is taken from a related schedule or the on-disk manifest default. Cancellation is polled from the durable occurrence. Runner cannot add a new agent-protocol cancellation channel in version 1.0, so timeout/cancellation first wait the configured grace interval and then terminate the complete process tree. The authoritative outcome precedence is timeout, requested cancellation, fatal protocol failure, actual exit code, and protocol/fallback summary.

Recovery queries stale `Starting`/`Running` runs by last runner heartbeat. A recorded PID is treated as the original child only when its executable path and start-time tolerance match; recovery never kills a merely reused PID. A missing or nonmatching process makes the run/occurrence `Abandoned`, invokes the fixed-delay hook, and audits the decision. A matching live process is left untouched and audited for operator diagnosis because a new process cannot safely adopt redirected pipes.

## Milestones

1. [x] Read mandatory instructions/state/plans/architecture/protocol/ADRs/prompt and inspect scheduling, persistence, Runner, SDK, and existing tests.
2. [x] Revalidate the unmodified Stage 03 Release build and 103-test suite.
3. [x] Extend application persistence contracts and SQLite run state for process identity, running transitions, heartbeats, queries, idempotent terminal state, and agent artifact registration.
4. [x] Implement bootstrap/path/integrity/temp-file/logging/process/protocol/cancellation/finalization/retry/recovery services.
5. [x] Replace the placeholder Runner dispatch with thin stable commands and composition.
6. [x] Add the test-only fake agent and real-process tests covering success, contention, protocol, artifacts, stderr, timeout, cancellation, heartbeats, recovery, cleanup, and Unicode/spaced paths; Stage 03 tests continue to cover fixed-delay and lease fencing policies used by finalization.
7. [x] Generate/validate the additive migration, run actual Runner smoke scenarios, full required validation, final artifact/process audits, and update the canonical handoff.

## Detailed steps

- Add execution option/result/service contracts and extend focused persistence records/ports in `HomeBusinessAssistant.Application` without referencing the SDK or executable projects.
- Extend `AgentRunEntity`, mappings/configuration, occurrence/run repositories, and artifact persistence; generate `AddRunnerProcessSupervision` through EF tooling.
- Add `Infrastructure/Execution/` services for bootstrap options, safe path resolution, executable/manifest hash validation, temporary config files, raw process launching, bounded UTF-8 line reading, protocol persistence, Serilog per-run rolling files, cancellation/heartbeat coordination, finalization/retries, and recovery.
- Add Serilog and the rolling file sink using centrally pinned stable versions verified from official NuGet metadata.
- Refactor `RunnerCommand` into a tested parser/dispatcher and `RunnerRuntime` composition, preserving the two earlier development demos.
- Add `tests/HomeBusinessAssistant.TestAgent` as an executable outside production packaging and stage its output/manifests beneath unique temporary agent roots for integration tests.
- Expand Infrastructure/Runner/EndToEnd tests and use real temporary SQLite. Ensure every process test has teardown process-tree termination and safe directory cleanup.
- Reconcile `docs/architecture.md`, `docs/agent-protocol.md` if runner-side policy clarification is needed, `docs/development.md`, `README.md`, this plan, and `docs/PROJECT_STATE.md`.

## Progress

- 2026-08-30: Read all repository-mandated sources, the complete Stage 04 prompt, all Stage 01–03 plans/ADRs, architecture/protocol documents, relevant source paths, migrations, and tests.
- 2026-08-30: Verified 24-project baseline, untracked initial repository state, Release build with zero warnings/errors, and 103 passing tests.
- 2026-08-30: Verified current stable Serilog `4.4.0` and Serilog.Sinks.File `7.0.0` against official NuGet package metadata.
- 2026-08-30: Added process identity/heartbeat persistence, atomic running and idempotent terminal transitions, retry identity, the complete focused Infrastructure execution stack, and thin durable Runner CLI/runtime composition.
- 2026-08-30: Added the real test-agent executable and 19 Runner process tests plus SQLite recovery coverage. Tests exercise exact configuration/hash, all-event/metric persistence, stderr limits/redaction, artifact success/missing/mutation/traversal, protocol limits/lifecycle, contention, nonzero exit, heartbeat, cancellation, timeout/tree kill, temp cleanup, and Unicode paths.
- 2026-08-30: Full parallel runs exposed database initialization and claim races between competing Runner runtimes. Added a bounded cross-process bootstrap file lock plus SQLite busy/locked/read-only claim retries; the contention regression passed five consecutive stress runs and the final solution run.
- 2026-08-30: Actual built Runner smoke persisted three runs (Completed, Completed, TimedOut), one metric, one artifact, scenario exit codes `0/0/21`, and no remaining child. The verified temporary root was deleted.
- 2026-08-30: Final restore, zero-warning Release build, 122 tests, formatting, EF model drift, recovery integration, and CLI smoke all passed. Canonical project state and Stage 04 docs were updated.

## Decisions

- The existing project graph is preserved: process/SDK/filesystem adapters belong in Infrastructure; Runner owns composition and CLI lifetime; Application gains only process-neutral contracts and persistence operations.
- The configured agent directory is a trusted root, while every manifest, working directory, executable, artifact, and temp path beneath it is untrusted until full-path containment and existing reparse-point checks succeed.
- The runner persists its own heartbeat; an agent `heartbeat` remains progress telemetry and cannot be the sole liveness authority.
- Process ID plus executable path plus process-start timestamp is recovery evidence. PID alone is never sufficient to kill or adopt a process.
- A matching live orphan cannot be safely reattached to redirected streams, so recovery reports it and leaves it running. The stale database record remains recoverable after the process disappears.
- Explicit retry attempts are new `Retry` occurrences linked to the failed occurrence, bounded by the persisted schedule policy, and never advance fixed-delay cadence.
- Agent-created artifacts are copied into platform-owned artifact storage only after canonical containment, non-reparse validation, size/hash inspection, and a no-write stability check; the source file is not trusted as durable storage.

## Validation

Required commands:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Stage-specific checks:

- `dotnet-ef migrations has-pending-model-changes` after generating `AddRunnerProcessSupervision`.
- Execute the fake agent through real child processes for success, all events, nonzero exit, fallback summary, malformed/unknown/oversized/sequence input, stderr bounds/redaction, artifact containment/tampering, timeout, and persisted cancellation.
- Start competing executors and prove only one creates/launches a run.
- Prove runner heartbeat, lease release, fixed-delay successor, stale recovery, process-tree termination, temporary configuration cleanup, and path handling with spaces/Unicode.
- Run the Runner supervision smoke in a unique temporary data/agent root; query persisted summaries/metric/artifact counts; verify timeout; confirm no child remains; delete the root.
- Inspect the repository and temp location for leaked database, log, config, secret, artifact, or child-process residue.

Actual results:

- `dotnet restore`: passed; all 25 projects were up to date.
- `dotnet build HomeBusinessAssistant.sln -c Release --no-restore`: passed with 0 warnings and 0 errors.
- Final `dotnet test HomeBusinessAssistant.sln -c Release --no-build`: 122 passed, 0 failed, 0 skipped across 10 test projects.
- `dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore`: passed with exit code 0.
- `dotnet-ef migrations has-pending-model-changes`: no changes since `20260830053751_AddRunnerProcessSupervision`.
- Infrastructure suite: 16 passed including fresh migration and stale recovery. Runner suite: 19 passed using real child processes.
- Actual `HomeBusinessAssistant.Runner.exe supervision-demo`: success/artifact/timeout produced 3 runs, 1 metric, 1 artifact, `Completed/Completed/TimedOut`, exit codes `0/0/21`, and `childProcessRemains=false`; temporary root cleanup returned `CLEANED=True`.

## Recovery and rollback

The schema change is additive/rename-only and will be tested against a fresh migrated database plus the full migration history. Test/smoke roots are unique directories under the system temp path and are deleted only after validating their exact prefix and containment. A failed child test forcibly terminates its recorded process tree in teardown. Temp configuration writes use atomic moves and startup age-based cleanup. Leases retain fencing tokens, so expired/released rows are safely reused without deleting coordination history.

## Remaining risks and follow-up

- Protocol 1.0 has no independent cooperative-cancellation pipe; Stage 04 uses grace then process-tree termination. A future protocol version may add a named-pipe or control-channel signal.
- Windows process start time/path checks are best-effort and can fail under access restrictions; recovery fails safe and never kills on ambiguous identity.
- Stage 05 owns Windows Task Scheduler and wake reconciliation. Stage 07 owns periodic host-side execution/recovery loops and interactive lifecycle.
- Protocol 1.0 exposes no cooperative cancellation channel; current enforcement is grace then process-tree kill. A matching live stale process cannot be safely reattached to redirected pipes and is conservatively left running and audited.
