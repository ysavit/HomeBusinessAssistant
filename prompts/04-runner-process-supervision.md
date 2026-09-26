# Stage 04 — Runner and Agent Process Supervision

Paste this entire prompt into Codex from the repository root after Stage 03 passes.

---

You are implementing **Stage 04: Durable Runner, Agent Process Supervision, Protocol Ingestion, Timeout, Cancellation, and Recovery**.

Read all instructions, architecture, agent protocol, scheduling and persistence code, prior plans, and this prompt. Create/update `docs/exec-plans/stage-04-runner-process-supervision.md`.

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

Implement `HomeBusinessAssistant.Runner.exe` as the single authoritative execution path for scheduled and manual occurrences. It must atomically claim an occurrence, resolve the agent and immutable configuration revision, launch the child process safely, ingest JSONL events, persist heartbeats/events/metrics/artifacts/summary, enforce timeout/cancellation, terminate the process tree, and finalize occurrence/run states idempotently.

Also create a fake test agent used for integration and end-to-end tests.

## 1. Runner CLI

Implement commands such as:

```powershell
HomeBusinessAssistant.Runner.exe execute --occurrence-id <guid>
HomeBusinessAssistant.Runner.exe run-agent --agent-id <id> --command <command> [--trigger CommandLine]
HomeBusinessAssistant.Runner.exe recover
HomeBusinessAssistant.Runner.exe diagnose
```

Requirements:

- use a stable command-line library or a well-tested minimal parser;
- `execute` runs an existing durable occurrence;
- `run-agent` creates a manual occurrence through `IManualRunService`, then executes it;
- `recover` finds stale claimed/starting/running records and applies documented recovery rules;
- return stable runner exit codes;
- output concise human diagnostics because Runner stdout is not parsed as an agent protocol by another process in V1;
- no shell command concatenation.

## 2. Occurrence claim and run initialization

Execution flow:

```text
load occurrence
validate runnable state and due/misfire policy
atomically claim with unique runner instance ID
acquire agent/schedule execution lease when required
load AgentDefinition + manifest
load exact ConfigurationRevision referenced by occurrence
validate executable and working directory under AgentDirectory
hash executable
create AgentRun in Starting state
transition occurrence to Starting
launch child
transition run/occurrence to Running
```

Requirements:

- only one process may claim the occurrence;
- a completed/skipped/cancelled occurrence must not launch;
- configuration revision cannot be silently replaced with the newest revision;
- executable and working paths must remain under configured roots and reject traversal/reparse escapes where practical;
- missing or changed executable creates a clear failure and audit record;
- record runner/agent/manifest versions and executable SHA-256.

## 3. Child process invocation

Launch without shell execution using `ProcessStartInfo.ArgumentList`.

Pass:

```text
<command>
--run-id
--occurrence-id
--agent-id
--config-file
--data-directory
--artifact-directory
--protocol-version 1.0
```

Create a per-run temporary configuration file that contains the immutable configuration JSON with secret references, not plaintext secrets. Restrict it to the current user as practical. Delete it in `finally` and through startup cleanup of stale temp files.

Set environment variables for correlation IDs and application paths, not secret values unless a future adapter explicitly requires it.

Set:

- redirected stdout and stderr;
- UTF-8 encodings;
- no shell;
- no visible child window when appropriate;
- working directory from validated manifest.

## 4. Protocol ingestion

Read stdout and stderr concurrently to avoid deadlocks.

For stdout:

- enforce maximum line length;
- parse valid Agent SDK events;
- validate protocol major version, run ID, sequence monotonicity, event timestamp, paths, names, and sizes;
- map events:
  - `heartbeat` updates heartbeat and optionally stores a bounded event;
  - `progress`, `warning`, `error`, `checkpoint` append run events;
  - `metric` appends/upserts according to clearly documented semantics;
  - `artifact` validates the referenced file exists under the assigned artifact directory, calculates hash/size, and persists metadata;
  - `summary` stores one terminal candidate summary but does not finalize before process exit;
  - `completed` stores agent-reported code for comparison with actual exit code.
- preserve malformed/unknown lines as bounded runner diagnostics, not as trusted events;
- after a configurable number of protocol violations, fail or mark degraded according to a documented policy.

For stderr:

- write bounded structured diagnostic events and rolling file logs;
- avoid unbounded database growth by truncating/aggregating after configured limits;
- apply redaction.

## 5. Heartbeats

Runner writes its own heartbeat to `AgentRun.LastHeartbeatAtUtc` at a configured interval even if the agent emits none. Agent heartbeat events may update progress/phase but are not the only liveness source.

If database heartbeat update temporarily fails:

- retry boundedly;
- keep supervising the process;
- record failure in local logs;
- reconcile final state when possible.

## 6. Timeout and cancellation

- Timeout comes from the occurrence/schedule or manifest default.
- Poll persisted cancellation request at a reasonable interval or implement an equivalent local cancellation signal.
- On cancellation/timeout:
  1. signal cooperative cancellation if the agent protocol/OS design supports it;
  2. wait a short configurable grace period;
  3. call `Process.Kill(entireProcessTree: true)`;
  4. wait for exit;
  5. mark terminal status accurately.
- Ensure stdout/stderr readers finish without hanging.
- Release leases, power-request placeholder, temp config, and other resources in all paths.

Do not depend on the tray process remaining alive.

## 7. Finalization

Authoritative result uses:

- cancellation/timeout state;
- actual process exit code;
- protocol errors;
- presence/validity of summary;
- agent-reported completed code.

Rules:

- exit code 0 plus no fatal protocol issue => Completed;
- nonzero mapped exit code => Failed or a more specific terminal reason/state;
- timeout => TimedOut regardless of later child exit code;
- requested cancellation => Cancelled;
- process disappears or Runner crashes => recovery may mark Abandoned;
- duplicate finalization calls are idempotent;
- call Stage 03 terminal-occurrence hook so fixed-delay schedules can create their next occurrence;
- write final audit event and deterministic fallback summary when the agent supplied none.

## 8. Recovery

Implement startup/CLI recovery:

- find Claimed/Starting/Running runs whose heartbeat is older than configured threshold;
- determine whether recorded PID still exists and plausibly belongs to the same executable/start time where possible;
- do not kill unrelated reused PIDs;
- mark orphaned runs/occurrences Abandoned with reason;
- release expired/stale leases through fencing rules;
- invoke terminal scheduling hook as documented;
- clean stale per-run temp config files;
- audit all recovery decisions.

## 9. Fake test agent

Create a dedicated executable under tests, excluded from production packaging, supporting commands/scenarios:

```text
success
fail --exit-code N
emit-all-events
delayed-heartbeats
hang
ignore-cancellation
malformed-json
out-of-order-sequence
oversized-line
artifact-success
artifact-traversal
stderr-burst
no-summary
```

It must use the real Agent SDK where appropriate. Tests launch it as a real process.

## 10. Runner application services

Keep the CLI thin. Implement use cases/services such as:

- `IOccurrenceExecutor`
- `IAgentProcessLauncher`
- `IAgentProtocolReader`
- `IRunFinalizer`
- `IStaleRunRecoveryService`
- `IExecutableIntegrityService`
- `IRunCancellationMonitor`

Use focused types and DI. Avoid one massive Runner class.

## Tests

Integration tests with temporary SQLite and the fake process must cover:

- successful claim/launch/finalization;
- competing runners, only one launch;
- invalid/non-runnable occurrence;
- exact config revision passed;
- executable hash recorded;
- all event types persisted correctly;
- unknown/malformed/oversized stdout handling;
- monotonic-sequence validation;
- stderr bounds/redaction;
- artifact success, missing file, traversal, tampering;
- nonzero exit;
- no summary fallback;
- timeout and process-tree kill;
- cancellation distinct from timeout;
- runner heartbeat;
- fixed-delay next occurrence after finalization;
- stale-run recovery;
- temp config cleanup;
- lease release on every terminal path;
- paths with spaces and Unicode.

Tests must not leak child processes. Add teardown safeguards.

## Constraints

- Do not add the tray/UI yet.
- Do not register Task Scheduler tasks or use power APIs yet; define a small optional execution-lifetime hook interface if needed for Stage 05.
- Do not implement Founder Scout or Wake Remote business behavior.
- Do not put business logic in `Program.cs`.

## Done when

- Runner can execute a durable occurrence end-to-end through a real child process.
- Protocol events, metrics, summaries, artifacts, stderr, and status are persisted safely.
- Timeout, cancellation, duplicate claim prevention, and recovery work.
- Fixed-delay completion creates the next occurrence.
- Tests leave no orphaned processes/files.
- Standard validation passes.

## Required smoke validation

Using a temporary data root:

1. seed/register the fake agent;
2. create a configuration revision;
3. create a manual occurrence;
4. execute success and artifact scenarios through the actual Runner CLI;
5. query/print persisted run summary and metric counts;
6. execute a short timeout scenario and verify terminal state;
7. confirm no child process remains.

Record actual commands and output.

## Final report

Include runner state flow, actual child-process tests, protocol violation policy, timeout/cancellation behavior, recovery behavior, and known Windows limitations. End with:

```text
Next prompt: prompts/05-windows-wake-bridge.md
```

