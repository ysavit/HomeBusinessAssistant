# Agent Manifest, Invocation, and JSONL Protocol 1.0

This document is the implementation contract between `HomeBusinessAssistant.Runner` and independently executable agents. The current manifest and event protocol version is `1.0`.

## Process boundary

An agent is an ordinary console executable. The runner supplies one command and a complete execution context, consumes JSON Lines from standard output, and captures standard error separately.

- Standard output is reserved exclusively for protocol records encoded as UTF-8. Each non-empty line is exactly one compact JSON object.
- Standard error is for bounded human-readable diagnostics. It is not parsed as protocol data.
- Do not write banners, progress prose, stack traces, prompts, secrets, configuration contents, cookies, authorization headers, remote-access PINs, or browser authentication state to standard output.
- A `completed` event is the last structured event. The process exit code, runner timeout/cancellation state, and protocol validity remain authoritative even if the agent reports success.

## Manifest 1.0

Unknown properties are allowed for forward compatibility. Every listed property is required and validated before execution.

```json
{
  "manifestVersion": "1.0",
  "id": "founder-scout",
  "displayName": "Founder Scout",
  "description": "Captures, evaluates, and drafts introductions for startup founder profiles without sending invitations.",
  "version": "1.5.0",
  "executable": "FounderScout.exe",
  "supportedCommands": ["run", "discover", "analyze", "authenticate", "import", "report", "diagnose", "record-fixture"],
  "capabilities": ["fixture-import", "profile-capture", "playwright-browser-discovery", "persistent-browser-profiles", "manual-browser-authentication", "browser-diagnostics", "durable-discovery-checkpoints", "candidate-persistence", "normalized-profile-processing", "protected-attribute-redaction", "deterministic-fast-screening", "candidate-deduplication", "durable-processing-leases", "structured-ai-evaluation", "deterministic-candidate-scoring", "evidence-grounding-validation", "human-review-invitation-drafts", "immutable-draft-revisions", "manual-invitation-queue", "manual-outcome-tracking", "candidate-results-ui", "safe-multiformat-reporting", "raw-artifact-retention", "domain-diagnostics"],
  "defaultTimeoutSeconds": 3600,
  "defaultConcurrencyPolicy": "Forbid",
  "supportsScheduling": true,
  "supportsManualRun": true,
  "requiresInteractiveUserSession": true,
  "configurationSchemaVersion": "1.0"
}
```

The distributable JSON Schema is `src/HomeBusinessAssistant.AgentSdk/agent-manifest.schema.json` and is copied to SDK build and publish output.

Manifest constraints:

- `id` is 1–64 lower-case ASCII letters, digits, or hyphens and cannot begin or end with a hyphen.
- `version` is a Semantic Versioning 2.0.0 value no longer than 128 characters.
- `executable` is a file name or relative path no longer than 512 characters. Rooted paths, drive-qualified paths, empty segments, and `.` or `..` segments are rejected.
- Commands and capabilities use 1–64 ASCII letters, digits, `.`, `_`, or `-`; they must begin and end with a letter or digit. Collections are case-sensitive, unique, and contain at most 64 entries. At least one command is required.
- `defaultTimeoutSeconds` is from 1 through 86,400.
- `defaultConcurrencyPolicy` is exactly `Forbid`, `QueueOne`, or `AllowParallel`.
- Current manifest readers require exactly manifest version `1.0`. Configuration schema versions use `major.minor` and begin at `1.0`.

The loader returns safe structured errors with a stable `code`, JSON `path`, and message. It does not include manifest content in an error.

Founder Scout manifest 1.5.0 implements `analyze --phase screen|deep|all --max <N>` and `report --type all|top-candidates|invitation-queue --top <N>`. A Runner occurrence supplies the same values in occurrence arguments. The screen phase emits bounded `profile-processing` progress, processing/reason-code metrics, a `founder-scout-screening` checkpoint, and a deterministic summary containing `noAiCalls: true`. The deep phase emits `deep-analysis` progress, the `analysis.*`, `candidates.*`, and `invitations.*` metrics, a `founder-scout-deep-analysis` checkpoint, and a bounded summary with top candidate IDs/names/scores plus `invitationsSent: 0`. Reports emit one artifact event per encoded HTML/Markdown/CSV/JSON output, a schema/filter hash summary, and never include raw profile/browser/session material. `all` composes screen then deep, while `run` composes acquisition/import when supplied, analysis, and report without waiting for discovery cooldown. Permanent provider configuration/authentication errors return an attention-required summary with `scheduleAction: Pause`; secret values and provider headers never appear in protocol payloads.

## Command-line contract

Normal runner-launched commands use this form:

```text
Agent.exe <command>
  --run-id <non-empty canonical-guid>
  --occurrence-id <non-empty canonical-guid>
  --agent-id <agent-id>
  --config-file <path>
  --data-directory <path>
  --artifact-directory <path>
  --protocol-version <major.minor>
```

All options are required exactly once and are case-sensitive. Values use a separate argument; `--name=value` is not part of protocol 1.0. Unknown options, missing values, invalid identifiers, and unsupported protocol major versions return exit code 2. File and directory paths are normalized with `Path.GetFullPath` but need not exist during parsing. The host/runner applies allowed-root and access policy later.

The Runner-owned `--config-file` is a private execution-input envelope. Version 1.0 contains the exact immutable configuration revision and immutable occurrence arguments without putting either document in the process command line:

```json
{
  "executionInputSchemaVersion": "1.0",
  "configuration": {},
  "occurrenceArguments": {}
}
```

Both values must be JSON objects. The Runner writes the envelope atomically beneath its root-confined temporary directory, applies a current-user ACL when Windows permits it, zeroes its write buffer, and deletes the file after finalization. Agents must reject missing/unsupported envelope versions before business side effects. The persisted configuration and occurrence argument limits remain the authoritative input bounds.

`protocol-demo` is a diagnostic-only standalone command used by Stage 01 tests and operator smoke checks. It does not accept or simulate a runner execution context and performs no agent business work.

## Event envelope

```json
{"protocolVersion":"1.0","type":"progress","runId":"11111111-1111-1111-1111-111111111111","sequence":3,"timestampUtc":"2026-08-29T18:00:00.0000000Z","payload":{}}
```

| Field | Contract |
|---|---|
| `protocolVersion` | `major.minor`; protocol 1.x readers accept the same major |
| `type` | Stable lower-case discriminator |
| `runId` | Non-empty canonical GUID matching the invocation |
| `sequence` | Starts at 1 and increases monotonically within a run |
| `timestampUtc` | Non-default ISO 8601 UTC timestamp; live validation allows at most five minutes of future clock skew |
| `payload` | Type-specific JSON object |

The maximum event line is 262,144 UTF-8 bytes. Embedded CR/LF characters are escaped by JSON; a reader rejects a supplied record containing a physical newline. The SDK writer serializes compact JSON and assigns sequence and UTC timestamp values while holding one asynchronous write gate.

## Event types and payloads

### `started`

Required `command`; optional bounded string map `metadata`.

```json
{"protocolVersion":"1.0","type":"started","runId":"11111111-1111-1111-1111-111111111111","sequence":1,"timestampUtc":"2026-08-29T18:00:00.0000000Z","payload":{"command":"run","metadata":{"agent-id":"founder-scout"}}}
```

### `heartbeat`

Optional `phase` and single-line `message`.

```json
{"protocolVersion":"1.0","type":"heartbeat","runId":"11111111-1111-1111-1111-111111111111","sequence":2,"timestampUtc":"2026-08-29T18:00:01.0000000Z","payload":{"phase":"capture","message":"Capture is active."}}
```

### `progress`

`current` and `total` appear together, and/or `percentage` is from 0 through 100. Optional `phase` and `message` describe the work.

```json
{"protocolVersion":"1.0","type":"progress","runId":"11111111-1111-1111-1111-111111111111","sequence":3,"timestampUtc":"2026-08-29T18:00:03.0000000Z","payload":{"current":5,"total":20,"percentage":25,"phase":"analysis","message":"Analyzing candidate 5 of 20."}}
```

### `metric`

Required conservative `name`; exactly one of finite `numericValue` or bounded `textValue`; optional `unit` and bounded string map `tags`.

```json
{"protocolVersion":"1.0","type":"metric","runId":"11111111-1111-1111-1111-111111111111","sequence":4,"timestampUtc":"2026-08-29T18:00:04.0000000Z","payload":{"name":"profiles.analyzed","numericValue":5,"unit":"profiles"}}
```

### `checkpoint`

Required conservative `key` and JSON-object `state`. State must be minimal, resumable metadata and must not contain raw secrets.

```json
{"protocolVersion":"1.0","type":"checkpoint","runId":"11111111-1111-1111-1111-111111111111","sequence":5,"timestampUtc":"2026-08-29T18:00:05.0000000Z","payload":{"key":"capture-page","state":{"page":2}}}
```

### `artifact`

Required conservative `kind`, safe `relativePath`, media `contentType`, and optional `description`. The path is relative to the invocation artifact directory and cannot traverse or be rooted.

```json
{"protocolVersion":"1.0","type":"artifact","runId":"11111111-1111-1111-1111-111111111111","sequence":6,"timestampUtc":"2026-08-29T18:00:06.0000000Z","payload":{"kind":"report","relativePath":"reports/top-candidates.html","contentType":"text/html","description":"Candidate report."}}
```

### `warning`

Required conservative `code`, single-line `message`, and optional structured `data`.

```json
{"protocolVersion":"1.0","type":"warning","runId":"11111111-1111-1111-1111-111111111111","sequence":7,"timestampUtc":"2026-08-29T18:00:07.0000000Z","payload":{"code":"capture.partial","message":"One profile was incomplete."}}
```

### `error`

Required conservative `code`, single-line `message`, Boolean `transient`, and optional structured `data`.

```json
{"protocolVersion":"1.0","type":"error","runId":"11111111-1111-1111-1111-111111111111","sequence":8,"timestampUtc":"2026-08-29T18:00:08.0000000Z","payload":{"code":"network.timeout","message":"The request timed out.","transient":true}}
```

### `summary`

Required terminal `status`, single-line human `text`, and optional typed structured `data`. Terminal status names are `Completed`, `Failed`, `TimedOut`, `Cancelled`, or `Abandoned`.

```json
{"protocolVersion":"1.0","type":"summary","runId":"11111111-1111-1111-1111-111111111111","sequence":9,"timestampUtc":"2026-08-29T18:00:09.0000000Z","payload":{"status":"Completed","text":"20 profiles analyzed; 4 shortlisted.","data":{"profilesAnalyzed":20,"shortlisted":4}}}
```

### `completed`

Required stable `exitCode`. This is the final protocol event; no later event is valid.

```json
{"protocolVersion":"1.0","type":"completed","runId":"11111111-1111-1111-1111-111111111111","sequence":10,"timestampUtc":"2026-08-29T18:00:10.0000000Z","payload":{"exitCode":0}}
```

Every normal run emits one `started`, exactly one terminal `summary`, and exactly one final `completed`. The writer flushes `summary` and `completed`. Writer calls made after `completed` throw `AgentEventWriteException` with structured error code `writer.streamCompleted`.

## Exit codes

| Code | SDK name | Meaning |
|---:|---|---|
| 0 | `Success` | Completed successfully |
| 2 | `InvalidArguments` | Invocation contract was invalid |
| 3 | `InvalidConfiguration` | Configuration failed validation |
| 10 | `AuthenticationRequired` | Manual authentication is required |
| 11 | `Throttled` | A remote service reported throttling |
| 12 | `ChallengeDetected` | An access challenge or CAPTCHA was detected |
| 20 | `Cancelled` | Cancellation was requested |
| 30 | `TransientFailure` | A bounded retry may be appropriate |
| 40 | `PermanentFailure` | An expected non-retryable failure occurred |
| 70 | `UnhandledFailure` | An unexpected exception reached the agent boundary |

No other exit code is assigned in protocol 1.0. A retry is always a new explicit attempt owned by the platform; an agent does not silently retry by changing its exit code.

## Compatibility and unknown events

- Major version changes may break envelope or payload compatibility. An invocation or event with a different major is rejected.
- Minor versions may add optional properties or new event discriminators. Readers ignore unknown properties.
- An unknown event discriminator becomes `UnknownAgentEvent`; its raw `payload` is preserved. The reader continues processing rather than activating arbitrary CLR types.
- Existing discriminator meanings, required payload fields, enum names, and exit-code meanings do not change within major version 1.
- Domain enums have explicit numeric values for future database stability, but protocol JSON uses the documented string names and rejects numeric enum JSON.

## Security and validation limits

- Event lines are limited to 262,144 UTF-8 bytes; messages are single-line and at most 4,096 characters.
- Metadata/tag maps contain at most 32 entries; keys use conservative names and values are at most 256 characters without control characters.
- Text metric values are at most 1,024 characters.
- Artifact and executable paths are relative and traversal-free. Treat the runner-provided directory as the only allowed storage root.
- Timestamps are UTC, non-default, and cannot be more than five minutes ahead of the injected clock. Historical UTC events remain readable.
- Emit only redacted structured data. Never include configuration-file contents, credentials, secret values/references where disclosure matters, cookies, authorization headers, browser session data, remote PINs, or full sensitive exceptions.
- Malformed lines are not agent prose. The Runner preserves them as bounded diagnostics and fails the stream at the fifth ordinary violation. Identity, artifact-containment, and terminal-lifecycle violations fail immediately; valid unknown 1.x events remain diagnostics without consuming that budget.
- Artifact payloads may include an optional UTC `deleteAfterUtc`. The Runner validates and persists it with the copied artifact so agent-owned diagnostic/report retention is not lost. Its absence remains valid for backward-compatible protocol 1.0 producers.

## Implementing another agent

1. Reference `HomeBusinessAssistant.AgentSdk`; do not copy its identifiers, policies, exit codes, or payload classes.
2. Create and validate a manifest against `agent-manifest.schema.json`; use a safe relative executable and list only supported commands/capabilities.
3. Parse normal runner invocations with `AgentExecutionContextParser` before side effects.
4. Construct one `AgentEventWriter` with standard output, the injected clock, and the invocation run ID.
5. Use `AgentExecutionSession` for the minimal started/summary/completed lifecycle. Keep business orchestration in the agent's Application layer.
6. Emit progress, metrics, checkpoints, artifacts, warnings, and errors through `IAgentEventWriter`; send human diagnostics to standard error.
7. Return one defined `AgentExitCode` and ensure the completed payload agrees with it.
8. Test every emitted line with `AgentEventSerializer.Deserialize` and `AgentEventValidator`, including concurrent writes, cancellation, and post-completion behavior.
