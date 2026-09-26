# Stage 01 — Agent SDK and Core Contracts

Paste this entire prompt into Codex from the repository root after Stage 00 passes.

---

You are implementing **Stage 01: Agent SDK and Core Contracts**.

Read `AGENTS.md`, `.agent/PLANS.md`, `docs/architecture.md`, the Stage 00 ExecPlan/result, and this prompt. Create or update `docs/exec-plans/stage-01-agent-sdk-and-core-contracts.md`.

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

Implement the versioned contracts that allow the central Runner to launch independently executable agents and consume structured JSON Lines events. Establish platform identifiers, statuses, manifest contracts, command context, exit codes, event payloads, serialization, validation, and a small agent-side writer. Do not implement process launching, databases, scheduling, UI, or real agents yet.

## Scope

### 1. Platform value objects and enums

In the appropriate Domain/Application projects, implement strongly validated types or records for:

- `AgentId` — lowercase stable ID using letters, digits, and hyphens; bounded length.
- `AgentVersion` / semantic version representation or validated string wrapper.
- `AgentRunId` and `OccurrenceId` wrappers if they improve safety without excessive ceremony.
- `AgentRunStatus`:
  - Pending
  - Claimed
  - Starting
  - Running
  - Completed
  - Failed
  - TimedOut
  - Cancelled
  - Abandoned
- `TriggerType`:
  - ManualUi
  - TrayMenu
  - Schedule
  - WakeSchedule
  - Retry
  - CommandLine
  - Recovery
- `ConcurrencyPolicy`: Forbid, QueueOne, AllowParallel.
- `WakePolicy`: Never, IfSleeping, Required.
- `MisfirePolicy`: Skip, RunImmediately, RunNextScheduled.
- `AgentExitCode` constants with documented meanings, including success, invalid arguments, invalid configuration, authentication required, throttled, challenge detected, cancelled, transient failure, permanent failure, and unhandled failure.

Enums persisted in future stages must have stable explicit numeric values or stable string conversion. Document the decision.

### 2. Agent manifest

In `HomeBusinessAssistant.AgentSdk`, implement immutable manifest models:

```text
ManifestVersion
Id
DisplayName
Description
Version
Executable
SupportedCommands
Capabilities
DefaultTimeoutSeconds
DefaultConcurrencyPolicy
SupportsScheduling
SupportsManualRun
RequiresInteractiveUserSession
ConfigurationSchemaVersion
```

Requirements:

- `AgentManifestValidator` returns structured validation errors.
- Executable is a file name or relative path only; reject rooted paths and traversal.
- Command names and capabilities use conservative character/length validation.
- Timeout has safe bounds.
- Unknown manifest properties may be tolerated for forward compatibility, but required properties must be validated.
- Add `agent-manifest.schema.json` as SDK content and ensure it is copied where needed for tests or distribution.
- Add a loader that reads UTF-8 JSON using `System.Text.Json` and reports path-aware errors without logging secret or file contents.

### 3. Agent command context

Implement a typed `AgentExecutionContext` populated by agent command-line arguments:

```text
RunId
OccurrenceId
AgentId
CommandName
ConfigurationFilePath
DataDirectory
ArtifactDirectory
ProtocolVersion
```

Implement parser/validation independent of any specific CLI package. It must:

- reject missing or duplicate required arguments;
- reject unknown protocol major versions;
- normalize full paths;
- ensure artifact/data paths are supplied but defer allowed-root checks to the host/runner;
- provide user-safe error messages and an appropriate exit code;
- never echo configuration contents.

### 4. JSON Lines protocol

Define protocol version `1.0` and a typed event envelope:

```json
{
  "protocolVersion": "1.0",
  "type": "progress",
  "runId": "...",
  "sequence": 3,
  "timestampUtc": "...",
  "payload": {}
}
```

Implement these event types and payloads:

- `started`: command and optional agent metadata;
- `heartbeat`: optional phase/message;
- `progress`: current, total, percentage when meaningful, phase, message;
- `metric`: name, numeric or text value, unit, tags;
- `checkpoint`: key and JSON-safe state metadata, not arbitrary raw secrets;
- `artifact`: kind, relative path, content type, description;
- `warning`: code, message, structured data;
- `error`: code, message, transient flag, structured data;
- `summary`: status, human text, typed JSON data;
- `completed`: agent-reported exit code.

Requirements:

- Use a discriminator strategy that round-trips predictably with `System.Text.Json`.
- Sequence numbers start at 1 and increase within an agent run.
- Timestamps must be UTC.
- Event type names and payload schemas are stable and documented.
- Unknown future event types can be preserved as an `UnknownAgentEvent` rather than crashing deserialization.
- Bound line/event size through a validator; define a safe default maximum.
- Validate metric names, artifact relative paths, sequence, run ID, and timestamp sanity.

### 5. Agent-side event writer

Implement:

```csharp
public interface IAgentEventWriter
{
    ValueTask WriteStartedAsync(...);
    ValueTask WriteHeartbeatAsync(...);
    ValueTask WriteProgressAsync(...);
    ValueTask WriteMetricAsync(...);
    ValueTask WriteCheckpointAsync(...);
    ValueTask WriteArtifactAsync(...);
    ValueTask WriteWarningAsync(...);
    ValueTask WriteErrorAsync(...);
    ValueTask WriteSummaryAsync(...);
    ValueTask WriteCompletedAsync(...);
}
```

A concrete JSONL writer must:

- use an injected `TextWriter` and `TimeProvider`;
- serialize exactly one compact JSON object plus newline per call;
- synchronize concurrent writes;
- assign monotonic sequence numbers;
- flush terminal events;
- never write ordinary prose to stdout;
- prevent events after terminal completion, while allowing the caller to inspect a structured error.

Provide a minimal `AgentExecutionSession` helper that emits started/completed reliably and supports cancellation, but do not build a broad framework or hide agent business logic.

### 6. Documentation

Create `docs/agent-protocol.md` containing:

- manifest example;
- command-line contract;
- stdout/stderr rules;
- event schemas/examples;
- exit-code table;
- compatibility/versioning policy;
- size and security constraints;
- instructions for implementing a future agent.

Update `docs/architecture.md` only where implementation decisions differ or require clarification. Add an ADR if you choose a non-obvious polymorphic serialization strategy.

## Tests

Add thorough tests for:

- `AgentId`, command, capability, and path validation;
- manifest valid/invalid examples and schema file presence;
- command-line context parsing, duplicates, missing values, bad GUIDs, bad protocol versions;
- every event round-trip;
- unknown event preservation;
- invalid run IDs, sequence values, paths, timestamps, metric names, and oversized lines;
- writer sequence monotonicity under concurrent calls;
- exactly one JSON object per line;
- terminal-event behavior;
- deterministic timestamps using fake `TimeProvider`;
- no accidental human text in stdout output.

Do not use snapshot tests that make harmless JSON property order changes excessively brittle unless property order is part of the documented protocol.

## Constraints

- No EF Core or SQLite yet.
- No process launching.
- No browser, AI, schedule, wake, tray, or web pages.
- Do not add a heavy agent framework.
- Keep SDK independent of ASP.NET Core, EF Core, and Windows APIs.

## Done when

- Manifests and command contexts are typed and validated.
- All required JSONL event types serialize, validate, and round-trip.
- Agent executables can use the writer in a small demo mode without producing non-JSON stdout.
- Protocol documentation is complete enough for a new agent author.
- All repository validation passes.

## Required validation

Run the standard repository commands and also run each agent executable in a temporary `protocol-demo` mode that emits a valid short stream. Pipe stdout to a file and verify every non-empty line deserializes as a protocol event. Keep this demo behind a command intended for diagnostics/tests, not normal business execution.

## Final report

Include the protocol version, event list, exit-code mapping, validation limits, and actual test/demo results. End with:

```text
Next prompt: prompts/02-persistence-configuration-audit.md
```

