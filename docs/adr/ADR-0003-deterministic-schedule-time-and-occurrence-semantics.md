# ADR-0003: Deterministic schedule time and occurrence semantics

- Status: Accepted
- Date: 2026-08-30
- Stage: 03
- Owners: Home Business Assistant

## Context

Stage 03 turns stored schedule rows into durable work. The baseline requires Windows time-zone identifiers, deterministic DST behavior, unique scheduled occurrence identity, completion-based cooldowns, explicit misfire/concurrency policy, and a central runner claim. It does not fully specify how a local time in a DST gap or overlap maps to UTC, whether scheduler-only states should share run status, how missed slots are coalesced, or how fixed-delay successors select configuration revisions.

These choices affect durable identity and cannot change casually after user schedules and occurrence history exist.

## Decision drivers

- One intended local calendar slot must yield at most one durable UTC occurrence.
- Scheduler state must remain meaningful before any process or run exists.
- Reconciliation must be bounded, restart-safe, and safe under competing local processes.
- Founder Scout cooldown must begin after terminal completion, not scheduled start.
- Original scheduled due metadata must survive misfire handling.
- Configuration revisions must be reproducible per occurrence without freezing all schedules unintentionally.

## Decision

Version 1.0 schedule JSON uses an explicit lower-case `type` discriminator plus `version`. The discriminator must match the separately persisted `ScheduleKind`. Calendar values are converted through the configured Windows time-zone ID before persistence.

For a spring-forward invalid local time, resolution advances to the first valid local second after the gap. For a fall-back ambiguous local time, resolution chooses the larger of the two UTC offsets, which produces the earlier UTC instant. Exactly one occurrence is created for that intended local slot.

Occurrences use a separate `OccurrenceStatus` state machine: `Planned`, `Ready`, `Claimed`, `Starting`, `Running`, `Skipped`, `CancellationRequested`, and terminal run-result states. `AgentRunStatus` remains process-run state. Machine-readable terminal reason codes and redacted human messages are separate columns.

Reconciliation holds the existing fenced SQLite lease, plans at most the configured per-schedule limit, and preserves `(ScheduleId, DueAtUtc)` as the scheduled identity. A new recurring schedule begins no earlier than its `CreatedAtUtc`; a one-time schedule may intentionally point into the past and enter misfire handling. `RunImmediately` keeps the original due time, makes only the latest missed slot Ready, and skips older missed slots as coalesced. `Skip` and `RunNextScheduled` both terminally skip the missed row with different reason codes.

Fixed-delay definitions produce only an initial occurrence during normal planning. Each terminal completion hook creates one successor at `CompletedAtUtc + Delay`. All terminal outcomes schedule the next cooldown by default; disabling or indefinitely pausing the schedule suppresses it. Retry occurrences have no schedule identity, link to their parent, and never advance fixed-delay cadence.

Schedules normally capture the agent's current immutable configuration revision when an occurrence is created. An optional pinned revision is explicit, validated as belonging to the same agent, and used instead.

## Alternatives considered

### Reuse `AgentRunStatus` for occurrences

This would force scheduler-only states such as Planned and Skipped into process semantics and make a durable occurrence appear to be a run before a process exists. It was rejected.

### Create both UTC instants during a fall-back overlap

This can surprise users with duplicate execution for one intended local wall-clock slot and conflicts with the unique intended-slot behavior. It was rejected.

### Shift invalid local times by exactly one hour

Historical time-zone gaps are not universally one hour. Advancing to the first valid second follows the installed time-zone rules instead of assuming a gap size.

### Calculate fixed delay from scheduled due or run start

Long runs would shorten or eliminate the requested cooldown. Completion-based calculation matches the Founder Scout batch-plus-cooldown requirement.

### Pin every schedule to the revision active when the schedule is saved

This would prevent normal configuration edits from applying to future occurrences. Current-at-planning is the default; explicit pinning remains available for deliberate reproducibility.

## Consequences

### Positive

- Calendar behavior is deterministic and covered around both DST transitions.
- Scheduler and runner lifecycles remain separately understandable.
- Fixed-delay cooldown is independent of run duration and retry attempts.
- Original due times and explicit terminal reasons provide auditable misfire history.
- Constraints and a fenced lease make competing local reconcilers idempotent.

### Negative / tradeoffs

- A time-zone rules update can change UTC conversion for future, not-yet-planned slots; already persisted occurrences remain unchanged.
- Choosing the earlier overlap instant is a policy choice some users might prefer to configure in a later version.
- Current-at-planning means a seven-day calendar horizon can contain more than one configuration revision after settings change; each row remains explicit and reproducible.
- The completion hook must be invoked after authoritative terminal persistence by Stage 04.

### Risks and mitigations

- Long downtime could create excessive catch-up rows — planning uses a bounded lookback, horizon, and per-reconciliation creation limit.
- Multiple terminal notifications could duplicate cooldown work — the scheduled unique identity and idempotent insert return the existing successor.
- Queue-one work could overlap — scheduler readiness is bounded and Stage 04 must also enforce the agent/process lease before launch.
- Invalid persisted JSON could cause side effects — version, discriminator, definitions, retry policy, command, time zone, manifest capability, and configuration ownership are validated first.

## Compatibility with baseline architecture

The decision implements the supplied custom deterministic scheduler without Quartz/Hangfire, retains SQLite and focused ports, keeps all time logic host-independent and `TimeProvider`-testable, and does not register Windows tasks or launch processes.

## Implementation and migration

- Domain adds `OccurrenceStatus`; Application adds typed definitions, calculator, validation, planner, policy, controls, manual-run, completion, and reconciliation services.
- `AddDurableSchedulingEngine` adds schedule command/arguments, pause, grace, disabled-agent policy, optional pinned revision, manifest scheduling metadata, occurrence terminal reason/message, and a schedule/status/due index.
- Existing `Pending` occurrence strings migrate to `Ready`; existing agent rows are refreshed from built-in manifests during initialization.
- The initial `scheduler.default-time-zone` system setting is `Central Standard Time`.

## Validation

- Definition round trips, invalid paths, one-time/daily/weekday/interval/delay calculations, DST gap/ambiguity, retry bounds, and legal transitions are unit tested.
- Real SQLite tests cover validation, migration/seed metadata, unique identity, planning limits, lease contention, misfire/concurrency policies, pause controls, manual queue-one behavior, revision capture, and fixed-delay successors for every terminal result.
- The self-cleaning scheduling smoke proves a ten-minute Founder Scout cooldown from controlled completion and reports the Wake & Remote weekday occurrence.

## Follow-up work

- Stage 04 must make run lifecycle updates use the occurrence state machine and invoke the fixed-delay hook after terminal commit.
- Stage 05 consumes the earliest wake occurrence and owns Windows Task Scheduler registration.
- Stage 07 supplies the periodic host reconciliation loop.
