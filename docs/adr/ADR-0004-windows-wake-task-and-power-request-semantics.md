# ADR-0004: Windows wake task and process power-request semantics

- Status: Accepted
- Date: 2026-08-30
- Stage: 05
- Owners: Home Business Assistant

## Context

The durable scheduler now identifies an earliest pending occurrence that requires a machine wake, and Runner supervises exact occurrences. Stage 05 must bridge that durable state to Windows without making Task Scheduler another scheduling authority. It must also keep long-running work from being interrupted by idle sleep, without changing permanent power settings or preventing explicit user power actions.

Task Scheduler XML can encode trigger boundaries with or without an offset. A no-offset value follows the machine's current time zone and daylight-saving rules; an explicit offset or `Z` represents the stated instant regardless of those settings. `SetThreadExecutionState` is scoped to the calling thread, which makes ordinary async acquire/dispose calls unsafe if continuations move threads.

## Decision drivers

- The occurrence's persisted UTC due instant must remain authoritative and DST-unambiguous.
- Only one predictable Windows task may be created, updated, queried, and removed.
- Task actions must invoke Runner for an exact occurrence and never invoke an agent directly.
- No password, secret, arbitrary user command, or unbounded output may enter task XML or diagnostics.
- Nested power consumers must not clear each other's active requests.
- Automated validation must not register real tasks, sleep the machine, or change power plans.

## Decision

Use Task Scheduler 2.x XML and the built-in `schtasks.exe` through `ProcessStartInfo.ArgumentList`. The only managed task is `\HomeBusinessAssistant\NextWake`. It has one `TimeTrigger`, `WakeToRun=true`, `MultipleInstancesPolicy=IgnoreNew`, a bounded execution time, explicit battery policy, and a generated Runner action. The task description includes only product/occurrence/configuration/timing/fingerprint diagnostics and a managed-task warning.

Encode `StartBoundary` as the occurrence's UTC instant with `Z`. Keep the configured schedule-local time and Windows time-zone identifier in the diagnostic description. This avoids overlap ambiguity and preserves the exact occurrence instant after a machine time-zone change. Reconciliation still replaces the task when schedule, configuration, occurrence, clock, or relevant system state changes.

Use the current Windows user's `InteractiveToken` with `LeastPrivilege`. V1 stores no password and assumes that the same user remains signed in; locked and sleeping sessions are in scope, signed-out execution is not. `StartWhenAvailable` is true for RunImmediately/diagnostic work and false for schedule policies that should not automatically catch up.

Persist `RequiresWake`, `KeepSystemAwake`, and `KeepDisplayOn` on each occurrence. Wake-enabled schedules set the first two flags; display wake defaults false. Retries inherit their parent flags. The harmless schedule-less wake test sets wake/system true and display false. These immutable flags make the Runner policy reproducible even if a schedule is later edited.

Implement system/display execution state with `ES_CONTINUOUS | ES_SYSTEM_REQUIRED` and optional `ES_DISPLAY_REQUIRED`. All native calls and reference counts run on one dedicated background thread. Each async handle releases only its own reference; the last release calls `ES_CONTINUOUS`. Do not use away mode. A required acquisition failure stops execution before child launch rather than silently risking sleep.

Run only read-only `powercfg /a`, `/waketimers`, `/lastwake`, and `/devicequery wake_armed` diagnostics. Bound and redact output; parse only conservative high-level facts because command text can be localized.

## Alternatives considered

### Third-party Task Scheduler wrapper

The required V1 surface is one task with stable XML elements and four command operations. A wrapper would expand supply-chain and API surface without solving a demonstrated reliability gap. It was rejected.

### Offset-free local StartBoundary

This would reinterpret an already-persisted UTC occurrence after a machine time-zone change and makes fall-back overlap behavior less explicit. It was rejected in favor of `Z`.

### S4U or password logon

S4U limits access to network resources and DPAPI CurrentUser behavior; password logon requires credential storage. Both conflict with the V1 interactive-user/DPAPI boundary. They were rejected.

### Call SetThreadExecutionState from arbitrary async threads

Because execution state is thread-scoped, acquire and release could affect different threads. A dedicated thread was selected instead.

### Windows away mode or automatic sleep after completion

Away mode is intended for specialized media workloads, and forcing sleep violates user expectations and product scope. The platform only releases its request and lets normal Windows policy resume.

## Consequences

### Positive

- The managed task remains a replaceable OS bridge rather than another scheduler.
- UTC trigger identity is deterministic across DST/time-zone changes.
- No password is stored and Runner remains the sole execution authority.
- Power holds compose correctly across nested in-process callers.
- Failures are typed, bounded, auditable, and suitable for later local health UI.

### Negative / tradeoffs

- A signed-out user prevents an InteractiveToken task from running.
- Wake support still depends on firmware, Windows sleep model, wake timers, power plan, battery state, and permissions.
- `schtasks` and `powercfg` diagnostics may be localized; error/not-found parsing is intentionally narrow.
- A dedicated thread exists for the lifetime of a Runner runtime, although it calls the native API only for explicit power-required occurrences.

## Validation

- Semantic XML tests cover one UTC trigger, WakeToRun, no overlap, timeout, battery/start policy, principal, Runner-only action, escaping, Unicode paths, and DST-overlap instants.
- Fake process tests cover create/update/query/delete, idempotence, task-not-found, permission errors, verification, and temp XML cleanup without registering a real task.
- Real temporary SQLite tests cover earliest replacement, removal, lease contention, settings/audit, structured failures, and wake-test creation.
- Mock native tests cover system/display flags, nested handles, final release, and acquisition failure without calling the real power API.
- Read-only diagnostics are fake-tested for aggregation, bounds, and redaction. A separate opt-in manual script owns real registration and any operator-initiated sleep.

## Follow-up work

- Stage 06 uses occurrence power metadata while implementing the bounded Wake & Remote availability workflow.
- Stage 07 invokes schedule/wake reconciliation from the interactive Host lifecycle.
- Stage 15 creates `HostAtLogon`, installer permissions, and operational diagnostics guidance.
