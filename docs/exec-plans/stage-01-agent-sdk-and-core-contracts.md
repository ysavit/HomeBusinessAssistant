# Stage 01 ExecPlan — Agent SDK and Core Contracts

## Purpose and user-visible outcome

Deliver the stable cross-process contract shared by the central runner and independently executable agents. Founder Scout and Wake Remote will each expose a diagnostic `protocol-demo` command whose standard output is a valid, compact JSON Lines 1.0 event stream. This stage establishes contracts only; it does not schedule work, persist state, launch child processes, browse, call AI services, or perform either agent's business behavior.

## Current repository state

- Stage 00 is validated: the Release build succeeds with no warnings and all 13 baseline tests pass.
- The solution contains the expected 24 projects and no database, migration, scheduling, process-supervision, browser, AI, or Windows wake implementation.
- `HomeBusinessAssistant.Domain` currently contains only its assembly marker; `HomeBusinessAssistant.AgentSdk` contains only its assembly marker.
- Founder Scout and Wake Remote currently emit Stage 00 placeholder prose to standard output. Stage 01 will reserve their standard output for protocol JSONL and move usage diagnostics to standard error.
- The supplied prompt pack and implementation remain untracked on `main`; no commit was requested.

## Scope and non-goals

### In scope

- Validated identifiers, semantic agent versions, and stable policy/status enums.
- Stable agent exit-code mapping.
- Immutable, typed manifest contract, schema, validator, and UTF-8 loader with safe structured errors.
- Dependency-free command-line execution-context parser.
- JSONL protocol 1.0 event envelopes, all required payload types, unknown-event preservation, serialization, validation, and size limits.
- Concurrency-safe event writer with monotonic sequence assignment, terminal-state protection, and a minimal execution-session helper.
- Complete protocol documentation and compatibility rules.
- Unit and integration tests for contract validation, parsing, round trips, forward compatibility, writer concurrency, terminal behavior, and diagnostic demo streams.
- `protocol-demo` modes for Founder Scout and Wake Remote plus executable smoke validation.

### Non-goals

- EF Core, SQLite, migrations, configuration persistence, secret storage, audit persistence, or artifacts on disk.
- Scheduling calculations, occurrence claims, retries, process launching, cancellation of child process trees, or recovery.
- Browser automation, AI integration, real Founder Scout discovery/evaluation, Wake Remote work, Task Scheduler, power APIs, tray behavior, or web UI changes.

## Design and data flow

Platform identifiers and persisted-policy enums live in `HomeBusinessAssistant.Domain`. `HomeBusinessAssistant.AgentSdk` references that inward domain contract and owns process-facing manifest, command-line, exit-code, JSONL, writer, and execution-session contracts. Runner and agent executables already reference the SDK.

```text
HomeBusinessAssistant.Domain
    <- HomeBusinessAssistant.AgentSdk
    <- HomeBusinessAssistant.Runner
    <- FounderScout.Agent
    <- WakeRemote.Agent
```

An agent invocation will parse a complete execution context before side effects. During execution, the SDK writer assigns sequence numbers and timestamps while holding a single asynchronous gate, validates the envelope and payload, writes exactly one compact JSON object plus newline, and flushes summary/completed terminal records. Readers select a payload by the stable `type` discriminator; unrecognized types become `UnknownAgentEvent` with the original payload retained.

## Milestones

1. [x] Read mandatory repository state, planning rules, architecture, ADR index, Stage 01 prompt, implementation, and tests.
2. [x] Revalidate the Stage 00 baseline build and tests before editing.
3. [x] Add platform value objects, explicit enums, and stable exit codes.
4. [x] Add manifest model, schema, validator, and safe UTF-8 loader.
5. [x] Add execution-context parsing and protocol version negotiation.
6. [x] Add JSONL events, serializer, validator, writer, and execution-session helper.
7. [x] Add protocol documentation and both diagnostic agent modes.
8. [x] Add comprehensive tests and repair all failures without weakening repository checks.
9. [x] Run required validation, executable JSONL smokes, final audits, and update the canonical handoff.

## Decisions

- Shared identifiers and platform policies are domain concepts. The SDK takes an inward reference on the Domain project rather than duplicating stringly typed identifiers or scheduler policies.
- Enum members receive explicit numeric values for future persistence stability. JSON contract surfaces use their documented stable names and reject unnamed numeric values.
- Protocol version 1.0 uses a manual, allow-listed `type` discriminator. This permits strict known-payload deserialization while preserving unknown payload JSON for forward-compatible readers without enabling runtime type-name deserialization.
- Protocol event size is measured as UTF-8 bytes, not UTF-16 characters. The default maximum line size is 262,144 bytes.
- Event timestamps must be explicit UTC values, non-default, and no more than five minutes ahead of the injected clock. Historical UTC events remain readable.
- Manifest executables and artifact paths are relative contract paths. Rooted paths, empty segments, and `.`/`..` traversal segments are rejected on all platforms.
- Command-line paths are normalized with `Path.GetFullPath` but are not required to exist and are not constrained to a storage root in this stage.
- Writer misuse after `completed` raises an exception carrying a stable structured error; callers can inspect the code without parsing exception text.

## Progress

- 2026-08-29: Read all required Stage 01 context and inspected every current implementation and test file.
- 2026-08-29: Verified the unmodified Stage 00 baseline: Release build passed with 0 warnings/errors and all 13 tests passed.
- 2026-08-29: Implemented validated Domain identifiers/policies and stable SDK exit codes.
- 2026-08-29: Implemented manifest schema/model/validation/loading and dependency-free execution-context parsing.
- 2026-08-29: Implemented all ten JSONL 1.0 events, manual discriminator serialization, unknown-event preservation, validators, concurrent writer, and cancellation-aware execution session.
- 2026-08-29: Replaced agent stdout placeholders with diagnostic-only JSONL demos and stderr-only usage help.
- 2026-08-29: Added comprehensive tests, protocol documentation, ADR-0001, and architecture/configuration reconciliation.
- 2026-08-29: Completed final restore, Release build, 63-test suite, format verification, two isolated external executable smokes, and sensitive-artifact audit.

## Validation

Required commands:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Stage-specific checks:

- Execute the built Founder Scout agent with `protocol-demo`, redirect standard output, and verify every non-empty line deserializes and validates as protocol 1.0.
- Execute the built Wake Remote agent with `protocol-demo`, redirect standard output, and verify every non-empty line deserializes and validates as protocol 1.0.
- Confirm both demo commands write no prose to standard output and return the success exit code.
- Inspect Git status and the repository for accidental databases, logs, secrets, browser state, diagnostic captures, or unrelated changes.

Actual results:

- `dotnet restore`: passed; all 24 projects are up to date.
- `dotnet build HomeBusinessAssistant.sln -c Release --no-restore`: passed; 0 warnings and 0 errors.
- `dotnet test HomeBusinessAssistant.sln -c Release --no-build`: passed; 63 passed, 0 failed, 0 skipped across ten test projects.
- `dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore`: passed with exit code 0 and no output/changes.
- Founder Scout external `protocol-demo`: exit 0, five non-empty JSON objects, empty stderr, sequential event types `started,progress,metric,summary,completed`.
- Wake Remote external `protocol-demo`: exit 0, five non-empty JSON objects, empty stderr, sequential event types `started,progress,metric,summary,completed`.
- Both external demos ran from a verified isolated temporary directory; every line parsed as JSON and satisfied required envelope/type/sequence checks. The temporary directory was deleted afterward.
- Final audit found no database, WAL, log, JSONL, image, or secret artifact outside ignored build/tool caches. All prompt-pack and implementation files remain untracked as documented; no commit was requested or created.

## Recovery and rollback

Stage 01 creates no databases, schedules, browser profiles, secrets, or machine configuration. Contract additions can be repaired file-by-file. Build/test output remains disposable and ignored. Demo streams are redirected to temporary files and removed after validation.

## Remaining risks and follow-up

- These contracts are the compatibility boundary for later persisted runs and child processes, so any change after Stage 01 must follow the documented versioning rules.
- Persistence conversion and database mappings intentionally begin in `prompts/02-persistence-configuration-audit.md`.
- Actual runner supervision begins in Stage 04; Stage 01 validates only in-process writers and standalone diagnostic streams.
