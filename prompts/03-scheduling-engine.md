# Stage 03 — Durable Scheduling Engine

Paste this entire prompt into Codex from the repository root after Stage 02 passes.

---

You are implementing **Stage 03: Durable Scheduling Engine and Occurrence Planning**.

Read the repository instructions, architecture, persistence implementation, prior ExecPlans, and this prompt. Create/update `docs/exec-plans/stage-03-scheduling-engine.md`.

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

Implement the platform-owned schedule model, deterministic next-occurrence calculation, durable occurrence generation, misfire handling, fixed-delay completion behavior, pause/enable controls, manual-run occurrence creation, due-work queries, and reconciliation service. All time logic must be testable through `TimeProvider`.

This stage does not launch processes and does not register Windows tasks.

## 1. Typed schedule definitions

Implement validated, serializable schedule definitions for:

- `ManualScheduleDefinition`
- `OneTimeScheduleDefinition`
- `DailyScheduleDefinition`
- `WeekdayScheduleDefinition`
- `FixedIntervalScheduleDefinition`
- `FixedDelayScheduleDefinition`

Definitions:

### One time

- one local or UTC date/time with explicit time zone semantics;
- fires at most once.

### Daily

- local `TimeOnly`;
- every day in configured time zone.

### Selected weekdays

- non-empty set of `DayOfWeek` plus local `TimeOnly`.

### Fixed interval

- interval between scheduled due times, independent of run duration;
- minimum and maximum interval bounds;
- anchor UTC time.

### Fixed delay after completion

- delay is calculated from the prior terminal completion timestamp;
- optional initial due time/start-now behavior;
- no next occurrence while a previous occurrence is pending/running unless concurrency policy explicitly permits it;
- intended for Founder Scout discovery batch + cooldown.

Use a stable JSON discriminator and validate persisted schedule JSON against `ScheduleType`.

## 2. Time-zone and DST policy

The application is Windows-first. Persist the configured Windows time-zone ID, defaulting to `Central Standard Time` for the initial machine configuration.

Implement and document deterministic DST behavior:

- **Invalid local time during spring-forward:** move forward to the first valid instant after the gap, or choose another explicit policy and document it. Do not silently throw during normal reconciliation.
- **Ambiguous local time during fall-back:** create one occurrence using a documented offset choice. Never create duplicate occurrences for the same intended local schedule slot.
- Convert to UTC before persistence.
- UI display conversion is future work but expose a reusable application service.

Add tests using time zones that actually have DST and dates around both transitions.

## 3. Schedule validation

Implement `IAgentScheduleValidator` covering:

- agent exists and is enabled or can be scheduled while disabled only if explicitly allowed by policy;
- command is supported by manifest;
- time zone exists;
- timeout bounds;
- schedule-definition validity;
- retry-policy bounds;
- wake policy compatibility;
- fixed interval/delay minimum bounds;
- no schedule name collision per agent if names are intended unique;
- configuration revision exists and belongs to the same agent.

Return field/path-aware errors suitable for future Razor Pages.

## 4. Occurrence planner

Implement `IScheduleOccurrencePlanner` that:

- calculates occurrences within a bounded planning horizon;
- creates missing occurrences idempotently;
- never modifies a terminal occurrence to represent a different due time;
- uses unique constraints and handles concurrent reconcilers;
- records the configuration revision active when each occurrence is created;
- supports a configurable future horizon for calendar schedules;
- creates only the next necessary occurrence for fixed-delay schedules;
- avoids unbounded catch-up creation after long downtime.

Suggested defaults:

```text
calendar planning horizon: 7 days
maximum occurrences created per schedule per reconciliation: 100
scheduler reconciliation lease: 30 seconds
```

## 5. Misfire behavior

When an occurrence is past due and was not executed:

- `Skip`: mark it skipped/cancelled with a reason and plan the next slot.
- `RunImmediately`: create or transition one runnable occurrence at current UTC time while preserving original scheduled due metadata if needed.
- `RunNextScheduled`: leave the missed occurrence non-runnable/marked skipped and wait for the next scheduled slot.

Define a `MisfireGracePeriod` so a job a few seconds late is not treated as a true misfire. Persist an auditable reason/state transition.

If the existing `AgentRunStatus` lacks a suitable occurrence-only state such as `Skipped`, introduce a separate `OccurrenceStatus` enum rather than distorting run statuses.

## 6. Concurrency and queue-one policy

Implement schedule-level decision logic:

- `Forbid`: do not make a new occurrence runnable while the same agent/schedule has claimed/running work.
- `QueueOne`: retain at most one pending future/due occurrence while one is running; coalesce extras.
- `AllowParallel`: occurrences may be runnable concurrently, though specific agents may still impose a lease.

Do not launch anything. Return decisions and persist states.

## 7. Fixed-delay completion hook

Implement an application service called when an occurrence becomes terminal. For fixed-delay schedules:

```text
next due = terminal completion UTC + configured delay
```

Requirements:

- exactly one next occurrence;
- no next occurrence if schedule is disabled or paused indefinitely;
- failure, timeout, and cancellation behavior is configurable or clearly documented; initial default should still schedule the next run after the cooldown unless the failure indicates configuration/auth/challenge suspension handled by the caller;
- retries are distinct from the normal next fixed-delay occurrence;
- next occurrence records the current configuration revision at planning time, unless a documented schedule setting pins a revision.

## 8. Manual runs

Implement `IManualRunService` that:

- validates agent and command;
- captures current configuration revision;
- creates an immediate occurrence with trigger source (`ManualUi`, `TrayMenu`, or `CommandLine`);
- respects or deliberately bypasses schedule pause based on an explicit parameter and audit record;
- applies concurrency policy;
- returns the occurrence ID.

No process is started in this stage.

## 9. Reconciliation service

Implement a bounded `IScheduleReconciler.ReconcileAsync` use case that:

1. acquires the reconciliation lease;
2. loads enabled schedules;
3. validates and calculates/plans occurrences;
4. applies misfire and concurrency policies;
5. returns a summary:
   - schedules examined;
   - occurrences created;
   - skipped/misfired;
   - errors;
   - earliest pending wake occurrence ID/time;
6. writes audit events for meaningful state changes and validation failures;
7. releases the lease.

The Host background loop arrives in Stage 07; keep this service host-independent.

## 10. State transitions

Centralize legal occurrence transitions and reject invalid transitions. Include at least:

```text
Planned -> Ready -> Claimed -> Starting -> Running
Planned/Ready -> Skipped
Planned/Ready/Claimed/Running -> CancellationRequested
Running -> Completed | Failed | TimedOut | Cancelled | Abandoned
```

Clarify which statuses belong to occurrences versus runs. Store terminal reason codes separately from human messages.

## Tests

Thoroughly test:

- each schedule type;
- invalid definitions;
- time zones and DST gap/ambiguity;
- fixed interval versus fixed delay semantics;
- planning horizon and creation limits;
- idempotent and concurrent reconciliation;
- unique occurrence identity;
- each misfire policy;
- grace period;
- Forbid/QueueOne/AllowParallel behavior;
- pause until a date and indefinite pause;
- disabled schedules;
- fixed-delay next occurrence after each terminal result;
- retries not corrupting fixed-delay cadence;
- manual run creation and trigger source;
- configuration revision capture;
- legal/illegal state transitions;
- lease contention;
- clock advancement using a fake `TimeProvider`.

Use real temporary SQLite where persistence/concurrency matters.

## Constraints

- No `System.Threading.Timer` business logic tied directly to wall clock.
- No Quartz/Hangfire unless an ADR demonstrates that a custom implementation is inferior and the dependency is necessary; the intended V1 is a small deterministic calculator.
- No process launching.
- No Windows Task Scheduler registration.
- No UI.

## Done when

- Schedule definitions are typed, validated, persisted, and versioned.
- Reconciliation creates correct idempotent occurrences.
- Fixed-delay schedules implement “batch finishes, then wait configured cooldown.”
- Misfire and concurrency policies are explicit and tested.
- Manual runs create durable occurrences.
- The service can report the earliest pending wake occurrence without registering it.
- All validation passes.

## Required smoke validation

Create a temporary data root, seed a sample Founder Scout fixed-delay schedule with a 10-minute cooldown and a Wake Remote weekday schedule, reconcile at controlled fake times, transition the Founder Scout occurrence to completed, reconcile again, and print non-sensitive occurrence IDs/due times. Verify the next Founder Scout due time equals completion + 10 minutes.

## Final report

Include DST policy, occurrence state machine, fixed-delay semantics, concurrency/misfire decisions, smoke output, and actual tests. End with:

```text
Next prompt: prompts/04-runner-process-supervision.md
```

