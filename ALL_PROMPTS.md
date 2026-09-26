# Home Business Assistant — Combined Codex Implementation Prompts
This original combined pack contains the bootstrap prompt and Stages 00–17. Post-V1 onboarding prompts 18–22 are maintained as authoritative individual files under `prompts/`; start with `prompts/18-first-run-onboarding-foundation-readiness.md`. Repository-owned context remains authoritative.


---

# Bootstrap — Start Implementation

# Codex Bootstrap Prompt — Start Home Business Assistant Implementation

Paste this entire prompt into Codex from the **actual repository root** after copying this context pack into the repository. This prompt starts implementation and completes **Stage 00 only**.

---

You are the primary implementation agent for **Home Business Assistant**, a Windows-first local agent orchestration platform. Begin implementation now. Do not return only a plan or architecture review. Inspect the repository, create the required living plan, implement the complete Stage 00 increment, run every validation available in the environment, update the canonical project state, and report actual results.

## Objective for this session

Implement:

```text
Stage 00 — Repository Foundation
```

The authoritative detailed requirements are in:

```text
prompts/00-repository-foundation.md
```

Do **not** begin Stage 01. Keep the repository buildable and leave an exact handoff for the next Codex session.

## Mandatory reading order

Read these files completely before changing code:

1. `AGENTS.md`
2. `docs/PROJECT_STATE.md`
3. `.agent/PLANS.md`
4. `docs/architecture.md`
5. `docs/configuration-examples.md`
6. `docs/acceptance-test-matrix.md`
7. `docs/adr/README.md`
8. `prompts/00-repository-foundation.md`

Then inspect all existing repository files, Git status, project files, solution files, and documentation. The repository may be empty, partially initialized, or contain correct prior work. Do not assume its state from the prompt pack.

## Source-of-truth precedence

Use this precedence when material conflicts exist:

```text
Security and safety constraints in AGENTS.md
        ↓
Accepted ADRs and current architecture
        ↓
Current stage prompt
        ↓
Verified source code, tests, migrations, and runtime evidence
        ↓
PROJECT_STATE.md and older stage reports
```

This order does not permit deleting working behavior blindly. When code and documentation differ, investigate, preserve safe behavior, record the discrepancy, and reconcile the affected documentation or create an ADR when the decision is cross-cutting.

## Non-negotiable execution rules

- Implement Stage 00, not only an ExecPlan.
- Keep changes strictly within Stage 00 scope.
- Do not implement persistence, scheduling, process supervision, wake APIs, tray behavior, browser automation, AI evaluation, or Founder Scout business logic.
- Use .NET 10 and C# 14. Do not downgrade the target framework because the execution environment is missing the SDK.
- Use clean architecture and the exact project dependency direction specified by Stage 00.
- Use nUnit and Moq for tests.
- Treat warnings as errors for first-party projects.
- Do not add Angular, React, Blazor WebAssembly, Electron, Node, Azure resources, containers, queues, or distributed infrastructure.
- Do not add credentials, secrets, cookies, browser profiles, real founder data, databases, logs, reports, screenshots, or runtime artifacts to source control.
- Do not create Git commits, branches, tags, or pull requests unless explicitly instructed.
- Do not claim a command passed unless you ran it and observed a successful exit.
- Do not erase unrelated work. Preserve valid existing code and reconcile it with the stage requirements.

## Phase 1 — Preflight and verified repository state

Run or inspect, as available:

```powershell
git status --short
dotnet --info
dotnet --list-sdks
```

Determine:

- current operating system;
- repository root;
- whether a solution/project already exists;
- installed compatible .NET 10 SDK;
- whether Windows runtime smoke tests can execute in the current environment;
- uncommitted files that must be preserved.

Update the beginning of `docs/PROJECT_STATE.md` with verified preflight facts. Do not mark Stage 00 complete yet.

If no compatible .NET 10 SDK exists, do not silently target another framework. Continue with safe repository/document scaffolding only when it can be validated, record the blocker precisely, and leave Stage 00 as `Blocked` or `Partially complete`. Prefer actual implementation and validation whenever the SDK is available.

## Phase 2 — Create the living ExecPlan

Create or update:

```text
docs/exec-plans/stage-00-repository-foundation.md
```

Follow `.agent/PLANS.md`. The plan must contain:

- verified current repository state;
- Stage 00 scope and explicit non-goals;
- solution/project dependency diagram;
- milestone checklist;
- exact files/projects to create or preserve;
- validation strategy for the current OS;
- risks and recovery steps;
- progress updates as implementation proceeds;
- decisions and assumptions discovered during work.

Do not stop after writing the plan.

## Phase 3 — Scaffold the solution and projects

Create `HomeBusinessAssistant.sln` and the complete Stage 00 project structure:

```text
src/
├── HomeBusinessAssistant.Domain
├── HomeBusinessAssistant.Application
├── HomeBusinessAssistant.AgentSdk
├── HomeBusinessAssistant.Infrastructure
├── HomeBusinessAssistant.Windows
├── HomeBusinessAssistant.Host
└── HomeBusinessAssistant.Runner

agents/
├── FounderScout/
│   ├── FounderScout.Domain
│   ├── FounderScout.Application
│   ├── FounderScout.Infrastructure
│   └── FounderScout.Agent
└── WakeRemote/
    ├── WakeRemote.Application
    ├── WakeRemote.Infrastructure
    └── WakeRemote.Agent

tests/
├── HomeBusinessAssistant.Domain.Tests
├── HomeBusinessAssistant.Application.Tests
├── HomeBusinessAssistant.AgentSdk.Tests
├── HomeBusinessAssistant.Infrastructure.Tests
├── HomeBusinessAssistant.Windows.Tests
├── HomeBusinessAssistant.Runner.Tests
├── HomeBusinessAssistant.Host.Tests
├── FounderScout.Tests
├── WakeRemote.Tests
└── HomeBusinessAssistant.EndToEndTests
```

Apply the target frameworks and executable settings from the Stage 00 prompt. In particular:

- pure domain/application/SDK projects: `net10.0`;
- Windows-specific infrastructure, host, runner, and agents: `net10.0-windows` where required;
- `HomeBusinessAssistant.Host`: `Microsoft.NET.Sdk.Web`, `WinExe`, Windows Forms enabled;
- agent executables and Runner: console applications;
- test target frameworks match their systems under test.

Support cross-compiling Windows-targeted projects from a non-Windows build host without changing their runtime target. If the repository needs `EnableWindowsTargeting`, configure it explicitly and document why. Do not represent a cross-build as a Windows runtime test.

## Phase 4 — Enforce dependency direction

Implement only the references needed by the prescribed architecture:

```text
HomeBusinessAssistant.Domain
    <- HomeBusinessAssistant.Application
    <- HomeBusinessAssistant.Infrastructure

HomeBusinessAssistant.AgentSdk
    <- HomeBusinessAssistant.Runner
    <- FounderScout.Agent
    <- WakeRemote.Agent

HomeBusinessAssistant.Application
    <- HomeBusinessAssistant.Host
    <- HomeBusinessAssistant.Runner

HomeBusinessAssistant.Windows
    <- HomeBusinessAssistant.Host
    <- HomeBusinessAssistant.Runner
    <- WakeRemote.Infrastructure

FounderScout.Domain
    <- FounderScout.Application
    <- FounderScout.Infrastructure
    <- FounderScout.Agent

WakeRemote.Application
    <- WakeRemote.Infrastructure
    <- WakeRemote.Agent
```

Do not introduce circular references. Libraries must not reference executable projects. Domain projects must remain independent from ASP.NET Core, EF Core, Playwright, Windows Forms, and infrastructure projects.

## Phase 5 — Add repository engineering controls

Create and validate:

- `global.json` pinned to an installed compatible .NET 10 SDK with a deliberate roll-forward policy;
- `Directory.Build.props` for nullable, implicit usings, deterministic builds, C# 14, warnings-as-errors, analyzers, and documentation behavior;
- `Directory.Packages.props` with central package management;
- `.editorconfig` for C# formatting, naming, encoding, newlines, and analyzer severity;
- `.gitignore` covering build/IDE output and all runtime-sensitive artifacts;
- repository `README.md` describing the implemented repository rather than merely copying this prompt pack;
- `docs/development.md` with build/test/format conventions and future migration/publish conventions;
- `docs/exec-plans/` preservation;
- `docs/adr/` preservation and use rules;
- a Windows GitHub Actions workflow for restore, Release build, tests, and format verification.

Add only scaffold/test dependencies required by Stage 00. Do not add EF Core, Playwright, Serilog, AI SDKs, Task Scheduler wrappers, or future-stage packages.

## Phase 6 — Add minimal executable behavior

Create meaningful marker types rather than `Class1` placeholders.

Implement:

```text
HomeBusinessAssistant.Runner
    Display bounded Stage 00 help/placeholder output and exit successfully.

FounderScout.Agent
    Display bounded Stage 00 help/placeholder output and exit successfully.

WakeRemote.Agent
    Display bounded Stage 00 help/placeholder output and exit successfully.

HomeBusinessAssistant.Host
    Start a minimal loopback ASP.NET Core host.
    Expose /health with a healthy status/body.
    Expose one minimal Stage 00 page/endpoint.
    Support graceful cancellation.
    Do not implement the tray icon yet.
```

Structure Host composition so Stage 07 can add tray lifetime without replacing the entire composition root.

## Phase 7 — Tests and architecture enforcement

Create a meaningful smoke test in every test project. Avoid `Assert.Pass()` and trivial tests with no behavior.

Add architecture tests that enforce at least:

- domain projects do not reference infrastructure projects;
- domain projects do not reference ASP.NET Core, EF Core, Playwright, or Windows Forms;
- agent domain projects do not reference their infrastructure or executable projects;
- no circular project references exist in the prescribed graph.

Tests must not access Startup School, live AI providers, real remote services, actual sleep/hibernate, or operator credentials.

## Phase 8 — Validation

Run and record actual command output/exit status:

```powershell
dotnet --info
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Also run:

- the Runner placeholder command;
- Founder Scout placeholder command;
- Wake Remote placeholder command;
- the Host on a non-conflicting loopback port;
- `GET /health`, recording status and response;
- graceful Host shutdown.

### Environment-specific validation

On Windows, run all executable and Host smoke checks directly.

On a non-Windows Codex environment:

- cross-build Windows-targeted projects without changing their target;
- run all portable tests and commands that can execute;
- do not claim Windows-only executables were runtime-tested;
- record exact Windows manual commands in `docs/PROJECT_STATE.md` and the final report;
- leave Stage 00 as `Implemented` but not fully `Validated` when a required Windows-only check remains outstanding.

Fix root causes of build, analyzer, test, and format failures. Do not suppress warnings broadly to force a green result.

## Phase 9 — State, ADR, and documentation reconciliation

Before finishing:

1. Update the ExecPlan progress, decisions, actual validation results, and remaining risks.
2. Update `docs/PROJECT_STATE.md` using only verified facts.
3. Set each Stage 00 status accurately:
   - `Implemented` when code exists;
   - `Validated` only when all required validation passed;
   - `Partially complete` or `Blocked` when applicable.
4. Record exact SDK, OS, solution/project list, build/test/format results, and Host smoke result.
5. Record files/projects materially added.
6. Record known limitations and manual Windows validation still required.
7. Create an ADR only for a meaningful cross-cutting decision or intentional deviation. Do not create ceremonial ADRs that repeat the supplied architecture.
8. Ensure `README.md`, `docs/development.md`, architecture docs, and project state do not contradict the code.
9. Ensure no generated runtime or sensitive files are tracked.

## Completion criteria

Stage 00 is complete only when the repository satisfies the full `prompts/00-repository-foundation.md` definition of done, subject to honestly documented environment limitations.

At minimum, the final repository must have:

- the complete solution/project baseline;
- correct project-reference direction;
- minimal working Host, Runner, Founder Scout, and Wake Remote entry points;
- meaningful tests and architecture enforcement;
- quality/build configuration;
- CI workflow;
- development and ADR documentation;
- updated ExecPlan and `docs/PROJECT_STATE.md`;
- actual validation evidence.

## Required final response

Return a technical implementation report containing:

1. Implemented behavior.
2. Important decisions and assumptions.
3. Exact solution/project/reference graph.
4. Files and projects added or changed.
5. Package additions.
6. Database/configuration changes — expected to be none beyond bootstrap files in Stage 00.
7. Commands executed with actual pass/fail results.
8. Host/CLI smoke-test results.
9. Current `docs/PROJECT_STATE.md` status.
10. Manual validation still required.
11. Known limitations/deferred work.

End exactly with:

```text
Next prompt: prompts/01-agent-sdk-and-core-contracts.md
```

Do not start Stage 01 in this session.


---

# Stage 00 — Repository Foundation

Paste this entire prompt into Codex from the empty or newly created repository root.

---

You are implementing **Stage 00: Repository Foundation** for the Home Business Assistant project.

Read, in order:

1. `AGENTS.md`
2. `docs/PROJECT_STATE.md`
3. `.agent/PLANS.md`
4. `docs/architecture.md`
5. `docs/configuration-examples.md`
6. `docs/acceptance-test-matrix.md`
7. `docs/adr/README.md`
8. this prompt

Use an ExecPlan at `docs/exec-plans/stage-00-repository-foundation.md`. Inspect the repository first. If it is empty, initialize only the application structure described below. If files already exist, preserve correct work and reconcile it with this specification instead of blindly replacing it.

## Initialize and maintain repository state

Before planning or coding:

1. Read `docs/PROJECT_STATE.md` completely.
2. Inspect the repository, Git status, operating system, installed SDKs, existing projects, tests, and documentation.
3. Reconcile `docs/PROJECT_STATE.md` with verified facts before implementation; retain the stage as not complete until validation occurs.
4. Preserve the supplied `docs/adr/README.md` and `docs/adr/ADR-TEMPLATE.md`. Create an ADR only for a meaningful cross-cutting decision or intentional architecture deviation.

Before the final report:

1. Update `docs/PROJECT_STATE.md` with the exact solution/project graph, SDK/OS, implemented baseline, material files, actual restore/build/test/format/host-smoke results, limitations, and next prompt.
2. Distinguish `Implemented` from `Validated`; on a non-Windows environment, record any Windows-only runtime checks as manual and outstanding rather than claiming they passed.
3. Do not include secrets, credentials, runtime authentication data, or real founder profile data.

## Goal

Create a production-quality, buildable .NET 10 solution and repository baseline for a Windows-first local agent orchestration application. This stage must establish project boundaries, build rules, documentation conventions, test infrastructure, and minimal executable entry points. It must not implement business features, persistence, scheduling, wake behavior, browser automation, or AI evaluation yet.

## Required solution structure

Create `HomeBusinessAssistant.sln` with these projects:

```text
src/
├── HomeBusinessAssistant.Domain
├── HomeBusinessAssistant.Application
├── HomeBusinessAssistant.AgentSdk
├── HomeBusinessAssistant.Infrastructure
├── HomeBusinessAssistant.Windows
├── HomeBusinessAssistant.Host
└── HomeBusinessAssistant.Runner

agents/
├── FounderScout/
│   ├── FounderScout.Domain
│   ├── FounderScout.Application
│   ├── FounderScout.Infrastructure
│   └── FounderScout.Agent
└── WakeRemote/
    ├── WakeRemote.Application
    ├── WakeRemote.Infrastructure
    └── WakeRemote.Agent

tests/
├── HomeBusinessAssistant.Domain.Tests
├── HomeBusinessAssistant.Application.Tests
├── HomeBusinessAssistant.AgentSdk.Tests
├── HomeBusinessAssistant.Infrastructure.Tests
├── HomeBusinessAssistant.Windows.Tests
├── HomeBusinessAssistant.Runner.Tests
├── HomeBusinessAssistant.Host.Tests
├── FounderScout.Tests
├── WakeRemote.Tests
└── HomeBusinessAssistant.EndToEndTests
```

### Target frameworks

- Pure domain/application/SDK projects: `net10.0`.
- Windows integration, host, runner, browser infrastructure, and Windows agents: `net10.0-windows`.
- `HomeBusinessAssistant.Host`:
  - use `Microsoft.NET.Sdk.Web`;
  - set `OutputType` to `WinExe`;
  - enable Windows Forms;
  - do not show a console window in normal execution.
- Agent console executables and Runner remain console applications.
- Match test target frameworks to the code under test.

## Project-reference direction

Implement only references needed by the target architecture:

```text
HomeBusinessAssistant.Domain
    <- HomeBusinessAssistant.Application
    <- HomeBusinessAssistant.Infrastructure

HomeBusinessAssistant.AgentSdk
    <- HomeBusinessAssistant.Runner
    <- FounderScout.Agent
    <- WakeRemote.Agent

HomeBusinessAssistant.Application
    <- HomeBusinessAssistant.Host
    <- HomeBusinessAssistant.Runner

HomeBusinessAssistant.Windows
    <- HomeBusinessAssistant.Host
    <- HomeBusinessAssistant.Runner
    <- WakeRemote.Infrastructure

FounderScout.Domain
    <- FounderScout.Application
    <- FounderScout.Infrastructure
    <- FounderScout.Agent

WakeRemote.Application
    <- WakeRemote.Infrastructure
    <- WakeRemote.Agent
```

Do not introduce circular references. Do not reference executable projects from libraries.

## Repository engineering files

Create and configure:

- `global.json` pinning an installed compatible .NET 10 SDK with an appropriate roll-forward policy.
- `Directory.Build.props`:
  - nullable enabled;
  - implicit usings enabled;
  - deterministic builds;
  - latest C# language version supported by .NET 10, expected C# 14;
  - warnings as errors for first-party projects;
  - XML documentation generation where appropriate without creating warning noise for every private member;
  - analyzers enabled.
- `Directory.Packages.props` using central package management.
- `.editorconfig` with consistent C# formatting, naming, newline, encoding, and analyzer severity.
- `.gitignore` covering .NET output, IDE data, SQLite databases and WAL files, logs, browser profiles, screenshots, raw snapshots, secrets, temporary config, reports, and published binaries.
- `README.md` for the actual repository, containing:
  - product summary;
  - process diagram;
  - project map;
  - prerequisites;
  - build/test commands;
  - current stage status;
  - explicit warning that runtime data/browser profiles/secrets are not source-controlled.
- `docs/adr/README.md` and an ADR template.
- `docs/exec-plans/.gitkeep` or equivalent.
- `docs/development.md` with local build, test, format, migration, and future publish conventions.

Preserve the supplied architecture/configuration/acceptance documents.

## Package baseline

Add only packages required for the scaffold and tests:

- nUnit test SDK/packages;
- Moq;
- coverage collector if compatible;
- ASP.NET Core packages already provided by the shared framework should not be redundantly added.

Do not add EF Core, Playwright, Serilog, AI SDKs, Task Scheduler wrappers, or other feature packages before their stage.

## Minimal code

Create minimal, meaningful entry points and marker types so all projects build:

- Each library gets a small namespace marker or assembly marker, not generic `Class1` files.
- `HomeBusinessAssistant.Runner` writes a short help/placeholder message and exits successfully.
- `FounderScout.Agent` and `WakeRemote.Agent` do the same.
- `HomeBusinessAssistant.Host` starts a minimal loopback ASP.NET Core host with:
  - `/health` returning a simple healthy result;
  - one minimal Razor Page or endpoint indicating that Stage 00 is installed;
  - no tray icon yet;
  - graceful cancellation.

Keep the Host code structured so Stage 07 can add tray lifetime without rewriting the whole composition root.

## Tests

Create one real smoke test per test project that verifies a meaningful baseline, such as assembly load, marker availability, or a pure helper. Avoid useless `Assert.Pass()` tests. Host tests may use `WebApplicationFactory` only if it does not distort the future WinExe host design; otherwise test a small application factory abstraction.

Create an architecture test or reflection-based test that verifies at least these forbidden references:

- domain projects do not reference infrastructure;
- domain projects do not reference ASP.NET Core, EF Core, Playwright, or Windows Forms;
- agent domain projects do not reference their infrastructure or executable projects.

## CI

Create a GitHub Actions workflow for Windows that:

```text
checkout
setup .NET 10
restore
build Release
run tests Release
verify dotnet format
```

It must not require secrets or external services.

## Constraints

- Do not implement database entities or migrations.
- Do not implement agent protocol types yet.
- Do not implement scheduler logic.
- Do not implement Windows Task Scheduler, power APIs, system tray, Playwright, AI calls, or Founder Scout business behavior.
- Do not use Angular, React, Blazor WebAssembly, Electron, Node, containers, Azure resources, queues, or distributed infrastructure.
- Avoid placeholder TODOs except when they explicitly identify a future stage and cannot be mistaken for complete functionality.

## Done when

- The expected solution and project structure exists.
- Project references follow the required direction and no cycles exist.
- Host, Runner, Founder Scout, and Wake Remote executables start and exit/serve appropriately.
- The local Host health endpoint works on loopback.
- Repository documentation and quality files are present.
- CI is defined.
- Restore, build, all tests, and format validation pass.

## Required validation

Run and record actual results:

```powershell
dotnet --info
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Start the Host on a non-conflicting local port, call `/health`, capture the HTTP status/body, and terminate it cleanly.


### Validation environment handling

- On Windows, run all executable and Host smoke checks directly.
- On a non-Windows implementation host, keep Windows targets intact, enable supported cross-targeting if required, and run all portable validation.
- Never claim Windows-only runtime checks passed on a non-Windows host. Record exact remaining Windows commands in `docs/PROJECT_STATE.md` and leave the stage `Implemented` rather than fully `Validated` until those checks run.
- Do not downgrade .NET or change Windows target frameworks to make the current environment convenient.

## Final report

Return the standard report required by `AGENTS.md`. Include the exact project-reference graph, SDK version used, actual command results, and any installed-SDK assumption. End with:

```text
Next prompt: prompts/01-agent-sdk-and-core-contracts.md
```


---

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


---

# Stage 02 — Persistence, Configuration, Audit, and Secrets

Paste this entire prompt into Codex from the repository root after Stage 01 passes.

---

You are implementing **Stage 02: Central Persistence, Configuration Revisions, Audit, Artifacts, and Windows-Protected Secrets**.

Read all repository instructions and relevant docs. Create/update `docs/exec-plans/stage-02-persistence-configuration-audit.md` before implementation.

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

Implement the central `assistant.db` persistence layer and application services required by all future scheduling, runner, tray, and UI stages. Provide EF Core migrations, focused repositories, immutable configuration revisions, append-only audit events, artifact metadata, leases, bootstrap database pragmas, path safety, redaction, and a Windows CurrentUser protected secret store.

Do not implement schedule calculation or process execution yet.

## Packages

Add centrally managed stable packages compatible with .NET 10:

- EF Core SQLite;
- EF Core Design for migrations, scoped appropriately;
- Serilog core plus rolling file sink and ASP.NET Core integration only if needed by this stage's composition/logging bootstrap;
- Windows DPAPI support package only if required by the target framework/runtime.

Document package choices and avoid prerelease dependencies unless the repository already intentionally uses them.

## 1. Central database model

Implement `AssistantDbContext` and mappings for the following entities. Use explicit configurations and indexes. Do not expose EF entities directly to the UI/application boundary.

### AgentDefinition

Fields:

- `Id` stable string/AgentId primary key;
- display name, description;
- manifest version;
- installed version;
- executable relative path;
- working-directory relative path;
- capabilities JSON;
- enabled;
- created/updated UTC timestamps.

### AgentConfiguration

- `Id` Guid primary key;
- `AgentId` unique foreign key;
- `CurrentRevisionId`;
- schema version;
- updated UTC timestamp.

### ConfigurationRevision

- Guid primary key;
- agent ID;
- monotonically increasing revision number per agent;
- canonical configuration JSON containing secret references only;
- SHA-256 configuration hash;
- changed-by actor;
- optional change summary;
- created UTC timestamp.

Revisions are immutable after creation. Enforce uniqueness on `(AgentId, RevisionNumber)` and `(AgentId, ConfigurationHash)` as appropriate. Define no-op save behavior explicitly.

### AgentSchedule

Create the storage entity now so Stage 03 can implement behavior:

- ID, agent ID, name;
- schedule type string/enum;
- schedule JSON;
- time-zone ID;
- wake, misfire, and concurrency policy;
- timeout seconds;
- retry policy JSON;
- enabled;
- paused-until UTC;
- created/updated UTC timestamps.

### ScheduleOccurrence

Create full durable storage:

- ID;
- optional schedule ID;
- agent ID;
- command name;
- arguments JSON;
- due UTC;
- status;
- trigger source;
- configuration revision ID;
- attempt number;
- optional parent occurrence ID;
- claimed-by/claimed-at;
- started/completed timestamps;
- optional run ID;
- cancellation requested UTC and reason;
- created UTC.

Create indexes supporting due work, agent/status queries, and uniqueness of scheduled occurrences.

### AgentRun

- ID and occurrence ID;
- agent/runner/manifest versions;
- executable hash;
- configuration revision/hash;
- machine name and process ID;
- status;
- start, heartbeat, completion, duration;
- exit code;
- summary text/JSON;
- error type/message;
- created/updated concurrency fields as needed.

### AgentRunEvent

- ID, run ID, sequence, timestamp, level, event type, message, data JSON.
- Unique `(RunId, Sequence)` where sequence originates from valid agent protocol; reserve a strategy for runner-generated events so no collision occurs.

### AgentRunMetric

- ID, run ID, name, numeric value, text value, unit, tags JSON.

### RunArtifact

- ID, run ID, artifact type, file name, relative path, content type, size, SHA-256, created UTC, delete-after UTC.

### AuditEvent

- ID, timestamp, actor type/ID, action, target type/ID, optional run ID, correlation ID, outcome, redacted data JSON.

### AgentLease

- lease name primary key;
- owner ID;
- acquired/expires UTC;
- fencing token or version value to prevent stale owners from extending/releasing a newer lease.

### SystemSetting

- key, value JSON, schema version, updated UTC, optional concurrency token.

## 2. Database bootstrap

Implement a database initializer that:

1. validates and creates the data directory;
2. opens SQLite;
3. applies migrations;
4. sets/verifies:
   - `journal_mode=WAL`;
   - `foreign_keys=ON`;
   - bounded `busy_timeout`;
   - `synchronous=NORMAL` unless an ADR justifies another value;
5. records a clear startup failure if migration or pragmas fail.

Use migrations, not `EnsureCreated`.

Create the initial migration and verify it against a fresh temporary database.

## 3. Focused application ports and repositories

Create focused interfaces and implementations, not a generic repository:

- `IAgentDefinitionRepository`
- `IAgentConfigurationService`
- `IScheduleRepository`
- `IOccurrenceRepository`
- `IAgentRunRepository`
- `IAuditWriter`
- `IArtifactStore`
- `ILeaseManager`
- `ISystemSettingRepository`

Design methods around use cases and atomic operations. Examples:

- get enabled agents;
- create a configuration revision and promote it atomically;
- create an occurrence if absent;
- atomically claim one occurrence;
- request cancellation;
- append run events/metrics;
- mark run/occurrence terminal;
- acquire/renew/release lease with fencing.

Do not hold a DbContext across external work. Prefer `IDbContextFactory<AssistantDbContext>` for host/runner concurrency.

## 4. Canonical configuration and revisions

Implement:

- JSON canonicalization with stable property ordering and normalized representation;
- SHA-256 hashing over UTF-8 canonical JSON;
- secret-reference validation such as `secret://<namespace>/<name>`;
- options/schema validation hook per agent;
- atomic revision creation and current-revision update;
- immutable history queries;
- a redacted change summary/diff that does not reveal secrets.

If a semantically identical configuration is saved, return the existing revision and record either no audit event or a documented no-op audit event. Do not create unlimited duplicate revisions.

## 5. Secret store

Define `ISecretStore` in Application and implement a Windows CurrentUser protected file-backed store in `HomeBusinessAssistant.Windows`:

```text
SetAsync(reference, secret)
GetAsync(reference)
ExistsAsync(reference)
DeleteAsync(reference)
```

Requirements:

- use DPAPI/CurrentUser protection;
- use an application-specific entropy value derived from stable non-secret application identity;
- map secret references to safe hashed file names;
- store under the configured data root in a dedicated secrets directory;
- write atomically via temp file + replace/move;
- do not expose secret values in exception messages, logs, audit, or tests;
- zero temporary byte buffers where practical;
- validate allowed references and reject traversal;
- provide an in-memory fake for tests;
- do not back up secret files in the ordinary unencrypted database backup stage.

Document the threat model: this protects data at rest from casual file inspection but code running as the same Windows user can retrieve the secrets.

## 6. Artifact store and path safety

Implement a file-system artifact store rooted under the validated application data directory:

- per-agent/per-run directories;
- safe relative paths only;
- atomic writes;
- SHA-256 and size calculation;
- metadata persistence;
- bounded maximum size configurable later;
- collision-safe names;
- deletion by retention service in later stage;
- reject symlink/reparse/path traversal escapes where practical on Windows.

## 7. Audit and redaction

Implement:

- append-only `AuditWriter`;
- correlation IDs;
- redaction utility for known secret property names, URI credentials, authorization headers, cookie fields, and secret references where appropriate;
- audit records for configuration created/changed, secret set/deleted, agent installed/enabled/disabled, and database initialization/recovery events;
- structured JSON data with bounded size.

Do not put full raw exceptions, stack traces, configuration JSON, or secret values in audit records. Logs may contain stack traces but must still redact sensitive content.

## 8. Seed built-in agent definitions

During bootstrap, idempotently load the two supplied manifests or create minimal manifest files from `docs/configuration-examples.md`:

- `founder-scout`
- `wake-remote`

Seeding must not overwrite user-managed current configuration revisions. Record manifest/version changes safely.

## Tests

Use temporary file-system roots and real SQLite databases. Cover at minimum:

- migrations on a fresh database;
- all required indexes/foreign keys;
- WAL/foreign-key/busy-timeout setup;
- atomic configuration revision creation;
- canonical JSON and hash stability across property order;
- duplicate/no-op revision behavior;
- concurrent revision attempts;
- focused repository queries;
- atomic occurrence claim with competing callers;
- lease acquisition, fencing, renewal, expiry, stale release rejection;
- audit append and redaction;
- DPAPI secret round-trip on Windows, plus negative invalid-reference tests;
- artifact path traversal rejection, atomic write, hash/size;
- database bootstrap idempotency;
- agent seeding idempotency.

Do not use EF Core InMemory.

## Constraints

- No schedule calculation or hosted scheduler.
- No child-process launch.
- No UI forms.
- No Playwright or AI.
- No real Windows scheduled task registration.
- No broad generic repository/unit-of-work abstraction.

## Done when

- `assistant.db` can be created and migrated from a clean data directory.
- Built-in agents are present.
- Configuration revisions are immutable, canonical, hashed, auditable, and secret-safe.
- Occurrence claiming and leases are concurrency-safe.
- Audit, artifacts, and DPAPI secret storage work with tests.
- All standard validation passes.

## Required smoke validation

Add a development-only CLI or test harness that:

1. creates a temporary platform data directory;
2. migrates the database;
3. seeds agents;
4. writes and reads a non-sensitive configuration revision;
5. writes/reads/deletes a temporary secret;
6. acquires/releases a lease;
7. writes an audit event and artifact;
8. prints only non-sensitive IDs/statuses.

Run it and include the actual result.

## Final report

Include migration name, central tables/indexes, secret-store threat model, SQLite pragmas, concurrency approach, and command results. End with:

```text
Next prompt: prompts/03-scheduling-engine.md
```


---

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


---

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


---

# Stage 05 — Windows Wake Bridge and Power Integration

Paste this entire prompt into Codex from the repository root after Stage 04 passes.

---

You are implementing **Stage 05: Windows Task Scheduler Wake Bridge, Power Requests, and Power Diagnostics**.

Read `AGENTS.md`, `.agent/PLANS.md`, `docs/architecture.md`, relevant Stage 03/04 code and tests, and this prompt. Create/update `docs/exec-plans/stage-05-windows-wake-bridge.md`.

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

Implement Windows-specific infrastructure that converts the platform's earliest pending wake-required occurrence into one durable Windows Task Scheduler task, invokes the central Runner for that exact occurrence, keeps the system awake while execution requires it, and exposes safe power/wake diagnostics. Keep all Windows dependencies behind application interfaces and test them without actually suspending the machine.

## Architecture decision

For V1, prefer the built-in Windows Task Scheduler XML schema plus `schtasks.exe` invoked with `ProcessStartInfo.ArgumentList`, rather than adding a third-party Task Scheduler wrapper. If inspection/prototyping proves this unreliable for required settings, document an ADR before selecting another implementation.

The platform owns schedules and occurrences. Windows Task Scheduler owns only these bridge tasks:

```text
\HomeBusinessAssistant\NextWake
```

`HostAtLogon` arrives in Stage 15.

## 1. Application interfaces

Define or complete focused ports:

```csharp
public interface IWakeTaskSchedulerBridge
{
    Task<WakeTaskState> GetStateAsync(CancellationToken cancellationToken);
    Task ReconcileAsync(WakeTaskRequest? nextWake, CancellationToken cancellationToken);
    Task RemoveAsync(CancellationToken cancellationToken);
}

public interface IPowerRequestService
{
    ValueTask<IAsyncDisposable> AcquireSystemRequiredAsync(
        string reason,
        bool keepDisplayOn,
        CancellationToken cancellationToken);
}

public interface IPowerDiagnosticsService
{
    Task<PowerDiagnosticsSnapshot> CaptureAsync(CancellationToken cancellationToken);
}
```

Create typed results/errors suitable for UI health display. Do not expose raw command lines containing untrusted values.

## 2. Wake-task request

`WakeTaskRequest` must include:

- occurrence ID;
- due UTC;
- due local time and time-zone display data for diagnostics;
- Runner executable full path;
- application data/bootstrap configuration path if required;
- task execution timeout;
- user/session policy;
- correlation/config revision IDs for task description only.

Validate:

- occurrence exists, is pending/runnable, and requires wake;
- due time is not unreasonably stale;
- Runner path is under the validated application root;
- GUID/timeout values are bounded;
- task arguments are generated by code, not accepted as arbitrary user strings.

## 3. Task XML generation

Create a deterministic XML generator for a Task Scheduler 2.x task. It must configure:

- task URI/name `\HomeBusinessAssistant\NextWake`;
- one time trigger for the exact due time;
- enabled trigger;
- `WakeToRun=true`;
- start when available behavior appropriate to the platform misfire policy;
- no overlapping task instance;
- bounded execution time;
- battery behavior exposed as settings/defaults and documented;
- action:

```text
HomeBusinessAssistant.Runner.exe execute --occurrence-id <guid>
```

- safe working-directory/bootstrap arguments where Task Scheduler supports/needs them;
- current user principal and logon mode compatible with the V1 assumption that the user remains signed in/locked/asleep;
- a task description with product name, occurrence ID, due time, and a warning that the task is managed by the application.

Requirements:

- escape XML correctly;
- use invariant formats required by Task Scheduler;
- never embed secrets;
- unit-test exact semantic elements without brittle full-string snapshots;
- document whether the generated trigger time is local-with-offset or converted as required by Task Scheduler and prove it with tests/examples.

## 4. Task registration/removal

Implement `SchtasksWakeTaskSchedulerBridge` using an injected `IProcessExecutor`.

Registration flow:

1. generate XML into an application temp file;
2. invoke `schtasks.exe` with argument-list entries, not shell string concatenation;
3. use create/update semantics (`/Create`, `/TN`, `/XML`, `/F`) as appropriate;
4. capture bounded stdout/stderr and exit code;
5. query/verify task presence and key settings after registration where practical;
6. delete the temp XML in `finally`;
7. write audit and health results with redaction.

Removal must be idempotent. “Task not found” is a successful final state.

Do not attempt to bypass permissions. Detect permission failure and return an actionable error.

## 5. Wake reconciliation service

Implement application service `IWakeTaskReconciler`:

```text
acquire wake-reconciliation lease
find earliest pending enabled occurrence with WakePolicy != Never
if none: remove managed NextWake task
if one: create WakeTaskRequest and reconcile task
persist SystemSetting with registered occurrence/time/task fingerprint
write audit only when state changes or reconciliation fails
release lease
```

Requirements:

- idempotent repeated calls;
- replace the task when the earliest occurrence changes;
- remove it after occurrence becomes claimed/terminal or no longer requires wake;
- handle clock/config/schedule changes;
- never register an agent executable directly;
- call Runner only;
- tolerate Host and Runner both asking for reconciliation via the lease.

Add a Runner command if useful:

```powershell
HomeBusinessAssistant.Runner.exe reconcile-wake
```

But keep actual logic in an application service.

## 6. Power-request service

Implement a Windows power request using `SetThreadExecutionState` or a documented equivalent.

Requirements:

- system-required flag by default;
- display-required only when explicitly configured;
- continuous request held for the lifetime of an `IAsyncDisposable` handle;
- reference-count or serialized behavior so multiple internal callers do not release each other's active request;
- restore/release on disposal;
- safe cleanup on process exit as far as practical;
- record acquisition/release diagnostics without secret data;
- clearly document that it prevents idle sleep while the process runs but does not override explicit user power actions or unsupported hardware behavior.

Integrate Runner with an execution-lifetime option:

- if an occurrence/agent requires the system to remain awake during execution, acquire before child launch and release after finalization;
- make this behavior explicit in manifest/config/occurrence metadata;
- ensure failure to acquire produces a warning or failure according to policy, not a silent condition.

## 7. Power diagnostics

Implement bounded wrappers for:

```powershell
powercfg /a
powercfg /waketimers
powercfg /lastwake
powercfg /devicequery wake_armed
```

Return a structured `PowerDiagnosticsSnapshot`:

- captured UTC;
- command exit codes;
- bounded/redacted output;
- parsed high-level flags where reliable;
- raw text available only in local diagnostics/artifact, not blindly rendered as HTML.

Do not change permanent power settings.

## 8. Wake test preparation

Implement an application use case and CLI command that can schedule a **test occurrence** a configurable number of minutes in the future, default 3, and register it as the NextWake task. The test occurrence should run a harmless built-in/fake command that writes a timestamped wake-test result and exits.

Do not automatically put the machine to sleep. The user will do that manually in a later UI workflow.

The test must record:

- expected wake time;
- actual Runner start time;
- wake delay;
- task registration state;
- result.

## 9. Tests

Use fake process execution and temporary paths. Cover:

- XML has correct task name, trigger, WakeToRun, no-overlap, timeout, Runner action, and no secrets;
- due-time conversion around DST/time zones;
- XML escaping and paths with spaces/Unicode;
- successful create/update/query/delete;
- idempotent reconciliation;
- earliest occurrence replacement;
- no occurrence removes task;
- lease contention;
- permission/command failures become structured health/audit errors;
- temp XML cleanup;
- task-not-found removal behavior;
- power request acquire/dispose and nested handles through a mockable native API wrapper;
- display flag policy;
- Runner releases request on success, failure, timeout, and cancellation;
- diagnostic command aggregation/redaction;
- wake-test occurrence creation.

Automated tests must not register real tasks by default, call actual sleep/hibernate, or change power plans. Add opt-in manual integration commands/tests clearly gated by environment variable or CLI confirmation.

## Constraints

- No system tray or full UI yet.
- No Wake Remote business agent yet.
- No force-sleep/shutdown behavior.
- No public RDP configuration or firewall/router changes.
- No third-party task scheduler package without ADR.

## Done when

- The platform can deterministically generate and reconcile one `NextWake` task for the earliest wake occurrence.
- The task calls Runner for an exact occurrence and has WakeToRun enabled.
- Runner can hold/release a bounded system-awake request.
- Power diagnostics and a harmless wake-test occurrence exist.
- Failures are auditable and actionable.
- All validation passes.

## Required manual smoke test

Provide a script/commands for an opt-in developer smoke test:

1. create a wake test 3 minutes ahead;
2. query and display the managed task;
3. verify `powercfg /waketimers` shows a relevant timer where supported;
4. optionally lock and put the workstation to sleep manually;
5. verify Runner wrote the actual start timestamp;
6. remove/reconcile the task.

Do not claim actual wake success unless it was performed on the machine.

## Final report

Include task XML approach, user/logon assumption, permissions, power semantics, unit/integration results, and clearly separate automated verification from any manual wake test. End with:

```text
Next prompt: prompts/06-wake-remote-agent.md
```


---

# Stage 06 — Wake & Remote Agent

Paste this entire prompt into Codex from the repository root after Stage 05 passes.

---

You are implementing **Stage 06: Wake & Remote Agent**.

Read repository guidance, the agent protocol, Windows wake/power implementation, schedule/runner code, and this prompt. Create/update `docs/exec-plans/stage-06-wake-remote-agent.md`.

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

Implement `WakeRemote.exe` as a small independently executable agent that runs after Windows wakes the machine, obtains a bounded keep-awake lease, waits for network readiness, verifies a configured remote-access provider, remains alive through a scheduled availability window, emits structured metrics/events/summary, and releases all resources. Integrate its typed configuration and schedule occurrence arguments with the platform.

V1 must not force the machine to sleep, change router/firewall settings, expose RDP to the internet, or manage remote-access credentials.

## 1. Typed configuration

Implement validated versioned configuration in `WakeRemote.Application`:

```text
SchemaVersion
TimeZoneId
NetworkReadyTimeoutSeconds
NetworkProbeIntervalSeconds
KeepDisplayOn
ReleaseToNormalPowerPolicyAfterWindow = true
ForceSleepAfterWindow = false and unsupported in V1
RemoteProvider settings
Optional readiness probe settings
```

Availability windows are configured centrally as schedules, but the agent must accept an immutable occurrence argument payload:

```json
{
  "windowInstanceId": "...",
  "scheduledWakeAtUtc": "...",
  "availableUntilUtc": "...",
  "remoteProviderRequired": true
}
```

Validate:

- `availableUntilUtc > scheduledWakeAtUtc`;
- maximum window duration is bounded;
- occurrence is not too stale unless misfire policy allowed immediate execution;
- probe intervals/timeouts are bounded;
- force sleep remains false in V1;
- provider configuration contains no plaintext credentials.

## 2. Commands

Implement:

```powershell
WakeRemote.exe run <standard Agent SDK args>
WakeRemote.exe diagnose <standard Agent SDK args>
WakeRemote.exe check-remote <standard Agent SDK args>
WakeRemote.exe protocol-demo ...
```

`run` is the normal agent command. All normal stdout must be valid Agent SDK JSONL. Human diagnostics go to stderr.

## 3. Network readiness

Define `INetworkReadinessProbe` returning structured attempts/results.

Initial implementation should combine conservative local checks without depending on a public third-party service:

- `NetworkInterface.GetIsNetworkAvailable()`;
- presence of at least one suitable non-loopback interface in an operational state;
- optional DNS lookup of a configured hostname;
- optional TCP connection to a configured host/port, disabled by default.

Behavior:

```text
start timeout window
probe immediately
if ready -> continue
otherwise emit progress/heartbeat and wait configured interval
stop on ready, timeout, or cancellation
```

Record:

- attempts;
- elapsed milliseconds;
- final readiness;
- last bounded reason code.

Do not log IP configuration beyond what is required for local diagnostics.

## 4. Remote-provider readiness

Create `IRemoteAccessProviderProbe` and implementations/configuration for:

### Chrome Remote Desktop

- provider config may specify expected Windows service names and/or process names because installations can differ;
- inspect configured services/processes safely;
- report `Ready`, `NotReady`, `NotConfigured`, or `Unknown` with reason codes;
- do not attempt login, PIN entry, browser automation, extension installation, or credential retrieval.

### Windows RDP

- optional provider;
- check configured service, normally `TermService`, and optionally local listener health;
- do not enable RDP, alter firewall, or expose ports;
- document Windows edition/host prerequisites as an operational concern.

A composite provider may allow `AnyConfiguredProviderReady` in the future, but keep V1 clear and typed.

If the provider is required and not ready after network readiness, fail the run. If optional, emit a warning and continue the machine availability window.

## 5. Keep-awake lifecycle

`run` must:

1. emit `started`;
2. acquire `IPowerRequestService` handle as early as practical;
3. emit scheduled/actual wake-delay metrics;
4. wait for network;
5. verify remote provider;
6. emit `machine.ready`, `network.ready`, and `remote.ready` metrics/status events;
7. remain active until `availableUntilUtc`, emitting bounded heartbeat/progress at a sensible interval;
8. emit summary and completed;
9. release the power handle in `finally`.

If Runner already holds an execution power request, nested/reference-counted handles must remain correct.

If the availability deadline is already past:

- apply a documented stale-window policy, normally exit with a non-success/skipped semantic instead of holding awake;
- do not extend the window silently.

Cancellation/timeout must release the handle immediately.

## 6. Summary contract

Emit deterministic summary JSON:

```json
{
  "scheduledWakeAtUtc": "...",
  "agentStartedAtUtc": "...",
  "wakeDelaySeconds": 0,
  "networkReady": true,
  "networkReadySeconds": 12,
  "remoteProvider": "ChromeRemoteDesktop",
  "remoteProviderRequired": true,
  "remoteProviderReady": true,
  "machineReadyAtUtc": "...",
  "availableUntilUtc": "...",
  "keepAwakeSeconds": 10800,
  "result": "Completed"
}
```

Use reason codes for partial/failure states. Do not include process lists, credentials, or sensitive network details in summary.

## 7. Platform integration

- Seed a valid default Wake Remote configuration revision based on docs examples, without overwriting user data.
- Create an application use case that transforms configured weekday/daily availability windows into Wake Remote schedule definitions and occurrence argument payloads.
- Set wake policy to `Required` for normal wake windows.
- Set timeout to availability duration plus bounded startup grace.
- Ensure `NextWake` invokes Runner, which invokes Wake Remote.
- After completion, wake-task reconciliation selects the next window.
- Expose configuration validation errors for Stage 08 UI.

Do not create multiple independent Windows wake tasks per window; central `NextWake` remains the bridge.

## 8. Diagnostics command

`diagnose` should emit protocol events/artifact containing:

- current UTC/local time and configured time zone;
- current network readiness result;
- remote-provider readiness;
- power diagnostics summary from the platform adapter when accessible;
- next configured availability window if supplied;
- no state changes.

## 9. Tests

Use fake `TimeProvider`, fake power handle, fake network probe, fake provider probe, and actual JSONL writer. Cover:

- valid/invalid configuration and occurrence arguments;
- immediate network ready;
- network becomes ready after retries;
- network timeout;
- cancellation during network wait;
- required provider ready/not ready/not configured;
- optional provider warning;
- active window wait using fake time;
- deadline already passed;
- maximum duration validation;
- power acquired before readiness and released in every path;
- no display-required flag unless configured;
- deterministic metrics/summary;
- stdout contains only valid JSONL;
- no sensitive service/process detail in summary;
- schedule/window integration and next wake reconciliation;
- Runner execution integration with the real agent process using short fake-time/test modes.

Normal tests must not wait real hours. Abstract delays through `TimeProvider`/testable delay service.

## Constraints

- No tray or full web UI yet.
- No forced sleep.
- No Wake-on-LAN relay.
- No public network configuration.
- No remote credential management.
- No requirement for live Chrome Remote Desktop or RDP in CI.

## Done when

- Wake Remote runs through Runner and Agent SDK protocol.
- It holds the machine awake for a bounded occurrence window.
- Network and provider readiness are observable and summarized.
- Power handle is released in every path.
- Platform schedules/occurrences can produce correct wake-window arguments.
- Tests and validation pass.

## Required smoke validation

Run Wake Remote through Runner with a short 60-second test window and local fake/diagnostic provider mode. Verify:

- wake-delay metric;
- network result;
- provider result;
- heartbeats;
- terminal summary;
- power-handle release;
- next wake reconciliation state.

Clearly label fake provider mode in all output.

## Final report

Include configuration, provider checks, lifecycle, summary schema, power cleanup, actual smoke results, and deferred on-demand Wake-on-LAN. End with:

```text
Next prompt: prompts/07-tray-and-local-web-host.md
```


---

# Stage 07 — System Tray and Local Web Host

Paste this entire prompt into Codex from the repository root after Stage 06 passes.

---

You are implementing **Stage 07: Windows System Tray, Single-Instance Desktop Lifecycle, Local Kestrel Host, and Background Orchestration Loops**.

Read all repository instructions and the existing scheduler/runner/wake implementations. Create/update `docs/exec-plans/stage-07-tray-and-local-web-host.md`.

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

Turn `HomeBusinessAssistant.Host.exe` into the interactive Windows control-plane process. It must run as a system-tray application, host Razor Pages on loopback, enforce single instance, open the dashboard, reconcile schedules and wake tasks, recover stale runs, display basic notifications, and shut down cleanly. Build only a foundational UI in this stage; detailed management pages arrive in Stage 08.

## 1. Desktop/web lifetime composition

`HomeBusinessAssistant.Host` is a Windows `WinExe` using ASP.NET Core plus Windows Forms `NotifyIcon`.

Implement a clean startup sequence:

```text
parse bootstrap args/config
acquire single-instance mutex
if existing instance -> signal it to open dashboard and exit
build DI/WebApplication
validate paths/options
migrate/bootstrap assistant.db
recover stale runs/leases/temp files
start Kestrel on loopback
start hosted orchestration loops
create tray ApplicationContext/NotifyIcon
run Windows message loop
on exit -> stop loops/Kestrel, dispose tray icon, release mutex
```

Avoid blocking the WinForms UI thread with async initialization. Use a clear coordinator and robust exception handling. Fatal startup errors should show one actionable local dialog and write logs, then exit.

## 2. Single-instance signaling

Use a named mutex scoped to the current interactive user. Add a small local IPC mechanism, preferably a named pipe, so a second launch sends commands such as:

```text
OpenDashboard
ShowStatus
```

Requirements:

- authenticate/limit IPC to the same Windows user where practical;
- bounded message size;
- no arbitrary command execution;
- timeout when the first process is unhealthy;
- tests around command parsing and second-instance behavior using abstractions.

## 3. Loopback Kestrel

- Bind only to configured `http://127.0.0.1:<port>`.
- Reject wildcard/remote bind settings in V1 validation.
- Add health endpoints:
  - `/health/live`
  - `/health/ready`
- Ready health includes database access, migrations complete, data directories writable, scheduler state initialized, and runner path present. Do not make external services required for platform readiness.
- Add a root/dashboard Razor Page with basic system status and navigation placeholders.
- Use antiforgery on all state-changing handlers.
- Add secure local defaults: no HTTPS requirement on loopback V1, no CORS, no external static resources required for core UI.

## 4. Tray icon and menu

Implement `TrayApplicationContext` and a menu:

```text
Open Dashboard
System Status
Run Agent
  Founder Scout (placeholder/manual occurrence command if installed)
  Wake & Remote diagnostic
Keep Awake
  30 minutes
  1 hour
  Until...
Release Keep-Awake
Next Scheduled Runs
Pause All Agents
Resume All Agents
View Latest Summary
Exit
```

Requirements:

- double-click opens dashboard;
- use an embedded application icon, not a font dependency;
- menu enabled/checked states reflect persisted state;
- long work is started asynchronously and reports via notification, never blocks menu UI;
- “Run Agent” creates a manual occurrence and starts Runner; it must not launch agent executables directly;
- Keep Awake uses `IPowerRequestService` and tracks a bounded manual session;
- Exit confirms only when runs or keep-awake sessions are active, then performs graceful cleanup;
- tray icon is always disposed to avoid ghost icons.

## 5. Browser opening

Implement `ILocalDashboardLauncher`:

- opens the validated local dashboard URL with the default shell/browser;
- does not accept arbitrary external URLs;
- handles failure through notification/logging;
- second-instance IPC invokes it in the existing process.

## 6. Hosted orchestration loops

Implement small `BackgroundService` loops with injected `TimeProvider`/interval options:

### Schedule reconciliation loop

- periodically calls Stage 03 `IScheduleReconciler`;
- starts due non-wake occurrences through the Runner launcher when eligible;
- does not execute agent code in-process;
- uses leases/claims to tolerate overlap.

### Wake-task reconciliation loop

- calls `IWakeTaskReconciler` after schedule reconciliation and on a bounded interval;
- reacts to persisted change notifications/polling without busy looping.

### Stale-run recovery loop

- runs at startup and periodically;
- calls Runner/application recovery service;
- does not mark healthy long-running Wake Remote sessions abandoned.

### Notification polling loop

- consumes high-priority local notification records or derives notifications from new failed/auth-required/strong-candidate events in later stages;
- Stage 07 may support platform run failures and wake-test result only.

Each loop must:

- catch/log per-iteration failures;
- support cancellation;
- avoid overlapping its own iterations;
- record health/last successful iteration;
- use bounded delays, not tight loops.

## 7. Runner launcher

Implement an application/Windows service that starts `HomeBusinessAssistant.Runner.exe execute --occurrence-id` safely:

- validate Runner path;
- no shell command string;
- detached/no visible console where appropriate;
- do not await long agent completion from UI handlers;
- record launch failure and revert/mark occurrence consistently;
- prevent duplicate launches through occurrence claim/lease logic in Runner.

## 8. Basic web UI

Create a coherent server-rendered shell with:

- accessible navigation;
- responsive layout;
- dark-mode-compatible CSS using local assets only;
- top system health summary;
- cards for installed agents, next due occurrences, active runs, recent failures, and wake task status;
- placeholder links/pages for Agents, Schedules, Runs, Audit, Wake & Remote, Founder Scout, Settings;
- no fake data—show real empty states.

Do not implement complex charts or candidate pages yet.

## 9. Logging and unhandled errors

- Configure Serilog rolling files under data/logs.
- Include correlation IDs where available.
- Capture unhandled WinForms, task, and AppDomain exceptions without revealing secrets.
- Avoid duplicate log providers.
- Show a tray balloon/notification for fatal or attention-required events, with throttling to avoid notification storms.

## 10. Tests

Cover:

- bootstrap bind-url validation;
- single-instance command protocol;
- tray command handlers through testable controller/service, not direct UI automation only;
- manual occurrence + Runner launch path;
- keep-awake session lifecycle and replacement/extension rules;
- background loops call services, survive one iteration failure, and stop on cancellation;
- no overlapping loop iteration;
- health readiness transitions;
- loopback-only endpoint integration;
- antiforgery on state-changing handler;
- dashboard empty/real data rendering;
- graceful shutdown disposes icon and power handles via abstractions;
- second-instance OpenDashboard behavior;
- launch paths with spaces.

UI-specific manual smoke is acceptable in addition to testable service coverage.

## Constraints

- Do not build full configuration/schedule CRUD pages yet.
- Do not build Founder Scout candidate UI.
- Do not expose the web server to LAN/internet.
- Do not convert the Host into a Windows Service.
- Do not execute agents inside the Host process.

## Done when

- One Host instance runs in the system tray and hosts a loopback Razor Pages UI.
- A second Host instance opens the existing dashboard and exits.
- Tray can open dashboard, create manual occurrences, start Runner, pause/resume globally, and hold/release a manual keep-awake request.
- Scheduler, wake reconciliation, and recovery loops run safely.
- Health and basic dashboard reflect real persisted state.
- Graceful shutdown is reliable.
- All validation passes.

## Required manual smoke validation

On Windows:

1. start Host and verify no console window;
2. verify tray icon/menu;
3. open dashboard and health endpoints;
4. launch a second Host and verify it opens the first dashboard then exits;
5. trigger a short fake-agent run through tray and verify Runner/run audit;
6. activate/release a 30-minute keep-awake session, cancelling early for the test;
7. exit and verify tray icon/processes are gone.

Record what was actually verified versus what remains manual.

## Final report

Include host lifecycle diagram, IPC choice, hosted-loop intervals/defaults, manual smoke results, and exact URLs. End with:

```text
Next prompt: prompts/08-agent-management-ui.md
```


---

# Stage 08 — Agent Management Web UI and Orchestration Controls

Paste this entire prompt into Codex from the repository root after Stage 07 passes.

---

You are implementing **Stage 08: Local Management UI for Agents, Configuration, Schedules, Runs, Audit, Wake State, and Manual Execution**.

Read repository instructions, architecture, all existing use cases, and this prompt. Create/update `docs/exec-plans/stage-08-agent-management-ui.md`.

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

Build the usable local control-plane UI inside `HomeBusinessAssistant.Host`: dashboard, agent list/detail, typed configuration revisions, schedule CRUD, manual runs, pause controls, run history/detail, audit viewer, Wake & Remote settings/status, system health, and configuration history. Keep it server-rendered, local-only, accessible, and backed by real application services.

Founder Scout candidate-domain screens remain Stage 13.

## 1. Dashboard

Show real persisted/current values:

```text
Platform health
Host version and uptime
Current local/UTC time and configured time zone
Agents enabled/paused/attention required
Active and recently completed runs
Failures in last 24 hours
Next scheduled occurrences
Next registered wake task
Manual keep-awake status
Latest deterministic daily/run summary
```

Actions:

- Run agent/command now;
- Pause all / resume all;
- Open Wake & Remote status;
- View failed runs;
- Reconcile schedules/wake task;
- Run diagnostics.

Use confirmation for pause-all and power-related actions. Do not use fake KPIs.

## 2. Agent list/detail

Agent list columns:

- display name and stable ID;
- installed/manifest version;
- enabled/paused/status;
- next occurrence;
- active run;
- last result/time;
- attention reason.

Agent detail:

- manifest/capabilities/supported commands;
- executable health/hash/path displayed safely and relatively;
- current configuration revision and history;
- schedules;
- latest runs/metrics/artifacts;
- actions: enable/disable, pause/resume, run command, edit config, view audit.

Changing enable state is audited. Disabling an agent prevents new schedule execution but does not silently kill an active run; offer explicit cancellation separately.

## 3. Typed configuration editor

Implement configuration adapters/validators for both built-in agents:

### Wake & Remote

Fields:

- time zone;
- network timeout/probe interval;
- keep display on;
- remote provider type;
- required/optional;
- configured service/process names;
- optional DNS/TCP probe settings;
- availability windows/schedule link;
- force sleep shown as unsupported/disabled in V1.

### Founder Scout baseline

Even before the domain is complete, support the configuration shape from `docs/configuration-examples.md`:

- discovery enabled and limits;
- cooldown/fixed-delay values;
- analysis batch/concurrency;
- ranking thresholds;
- invitation length/queue sizes;
- AI endpoint/deployment and secret-reference status;
- retention;
- data/browser root display.

Requirements:

- GET displays current revision;
- POST validates antiforgery and typed options;
- field-specific validation errors;
- secret value entry is handled by a separate `ISecretStore` action and never round-tripped into HTML;
- display only whether a secret exists plus last changed time/audit;
- successful save creates/promotes immutable revision;
- include change summary;
- concurrent edit detection using revision number/hash;
- no-op save behavior is clear;
- configuration history and redacted diff page.

## 4. Schedule management

Pages:

- schedule list with agent, type, local expression, time zone, next due, wake, enabled/paused, last result;
- create/edit schedule;
- schedule detail with future occurrences and run history;
- enable/disable;
- pause until date/time or indefinitely;
- resume;
- run now;
- delete only when safe, preferably soft-disable/archive if history exists.

Editor supports:

- Manual;
- One time;
- Daily;
- Selected weekdays;
- Fixed interval;
- Fixed delay after completion;
- command selection from manifest;
- time zone;
- wake/misfire/concurrency policy;
- timeout/retry bounds;
- agent-specific argument JSON through typed form where available, never arbitrary executable args.

Preview the next 5 occurrences before save using the real schedule calculator. Show DST/misfire behavior help text.

After changes, trigger schedule and wake reconciliation.

## 5. Manual run UI

- Select supported agent command.
- Render command-specific typed inputs where implemented.
- Show current configuration revision.
- Show concurrency conflict before submitting.
- Allow explicit “run despite agent pause” only with confirmation and audit; never bypass disabled/security/configuration errors.
- Create manual occurrence and start Runner.
- Redirect to run detail.

No handler launches agent executable directly.

## 6. Run history/detail

Run list filters:

- agent;
- status;
- trigger;
- date range;
- attention/failure;
- active only.

Use server-side pagination and indexes.

Run detail displays:

- IDs/correlation;
- agent, command, versions, config revision/hash;
- trigger/due/start/end/duration/heartbeat;
- status and terminal reason;
- progress timeline;
- metrics;
- summary text/structured data;
- warnings/errors;
- bounded stderr/protocol diagnostics;
- artifacts with safe local download/view actions;
- audit events;
- cancellation action for eligible states;
- retry action creating a new explicit retry occurrence.

Never render raw untrusted HTML from agent output. Encode all text. Artifact download validates path/root and uses content disposition.

## 7. Audit viewer

- paginated/filterable by time, actor, action, target, outcome, run, agent;
- detail with redacted structured JSON;
- configuration changes link to revisions;
- local CSV/JSON export optional if bounded;
- no edit/delete UI.

## 8. Wake & Remote page

Show:

- supported sleep states summary;
- current managed wake task and occurrence;
- next availability window;
- active keep-awake handle/window;
- latest network/provider readiness;
- last wake test/result;
- last wake reason and wake timers diagnostics, encoded safely;
- attention items.

Actions:

- run diagnostics;
- schedule test wake in 3/5/custom minutes;
- reconcile/re-register task;
- keep awake 30/60/custom minutes;
- release manual keep-awake;
- edit configuration/schedules.

Do not put the machine to sleep automatically during a test. Provide exact user instructions.

## 9. System settings/health

Read-only or typed settings:

- data root;
- agent root;
- loopback URL/port (changing may require restart);
- log/retention defaults;
- scheduler loop health;
- wake reconciliation health;
- Runner presence/version;
- database migration/version/pragmas;
- disk space;
- latest backup placeholder for Stage 15.

Add a diagnostics export that packages non-sensitive health/config metadata and recent relevant logs/audit. Explicitly exclude secrets, browser profiles, cookies, raw profile data, and AI prompts unless opted in later.

## 10. UI engineering

- Razor Pages + partials/view components/tag helpers as appropriate.
- Avoid oversized page models; call application services.
- Server-side validation is authoritative.
- Local CSS and minimal JS only.
- Accessible labels, error summaries, focus behavior, keyboard operation, status text not conveyed by color alone.
- Responsive at desktop and narrow Remote Desktop/window sizes.
- Use Post/Redirect/Get for successful mutations.
- Add flash/toast messages stored safely.
- Poll active run status at a bounded interval with a small JSON endpoint or partial refresh; no SignalR required in V1.

## 11. Tests

Use Razor Pages integration tests and application tests for:

- loopback pages and navigation;
- antiforgery on mutations;
- configuration valid/invalid/no-op/concurrent edits;
- secrets not rendered or persisted in config JSON;
- schedule create/edit/preview/DST validation;
- reconciliation invoked after changes;
- manual run creates occurrence and launches Runner abstraction;
- pause/disable semantics;
- run pagination/filter/detail/cancel/retry;
- safe artifact access and traversal rejection;
- output encoding/XSS regression with hostile event text;
- audit filters/detail redaction;
- wake test/reconcile/manual keep-awake actions;
- dashboard values/empty states;
- diagnostics export exclusions;
- accessibility basics in rendered markup.

## Constraints

- No Founder Scout candidate list/detail yet.
- No LAN/internet binding.
- No user account/login system in V1.
- No SPA framework.
- No automatic invitation sending.
- No direct agent execution from UI.

## Done when

- The local UI can configure both agents, store secrets safely, create schedules, run commands, monitor/cancel/retry runs, inspect audit, and manage wake readiness.
- All mutations are validated/audited.
- Run and configuration details are safe and paginated.
- Wake-test workflow is usable.
- Standard validation and manual browser smoke pass.

## Required manual smoke validation

From the tray-opened dashboard:

1. edit/save Wake Remote config and inspect revision history;
2. set a test secret and confirm it is never displayed;
3. create a short Wake Remote schedule and preview occurrences;
4. run diagnostics manually and inspect run events/artifacts;
5. cancel or retry a fake long run;
6. schedule/reconcile a test wake;
7. inspect audit entries;
8. verify UI at a narrow window size.

## Final report

Include page map, configuration/concurrency behavior, security checks, screenshots only if tooling supports them, test results, and deferred Founder Scout pages. End with:

```text
Next prompt: prompts/09-founder-scout-domain.md
```


---

# Stage 09 — Founder Scout Domain, Persistence, and Agent Shell

Paste this entire prompt into Codex from the repository root after Stage 08 passes.

---

You are implementing **Stage 09: Founder Scout Domain Model, Separate SQLite Persistence, State Machines, Repository Layer, and Agent CLI Shell**.

Read all repository guidance, architecture, configuration examples, acceptance matrix, and existing platform/agent SDK code. Create/update `docs/exec-plans/stage-09-founder-scout-domain.md`.

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

Create the Founder Scout bounded context and its separate `founders.db`. Implement candidate/profile/evaluation/invitation/discovery entities, states, focused repositories, migrations, domain state transitions, work claiming, domain metrics contracts, and a working `FounderScout.exe` command shell. Provide fixture/file import and report placeholders so the agent can be executed through Runner before browser or AI stages.

Do not implement live browser automation or AI calls yet.

## 1. Founder Scout database

Create `FounderScoutDbContext` in `FounderScout.Infrastructure`, resolved beneath the configured Founder Scout data directory. Use EF Core migrations, SQLite WAL, foreign keys, busy timeout, and short transactions.

### BrowserAccount

Fields:

- stable ID/string;
- display name;
- browser-profile relative path;
- enabled;
- authentication/session health status;
- optional assigned discovery segment IDs;
- last authenticated/successful/failed run UTC;
- last error reason code/message bounded;
- cooldown/suspended-until UTC where applicable;
- created/updated UTC.

Session statuses:

```text
Unknown
Healthy
ReauthenticationRequired
AccessDenied
Throttled
ChallengeDetected
ParserFailure
Disabled
```

Do not store password, cookies, or session state in SQLite.

### DiscoverySegment

- ID/name;
- enabled/priority;
- configuration JSON for source filters/navigation;
- assigned account ID optional;
- last run UTC;
- viewed/new/duplicate/error counters;
- consecutive low-yield runs;
- paused-until UTC;
- created/updated UTC.

### DiscoveryCheckpoint

- ID;
- account ID;
- segment ID;
- domain run/correlation ID;
- last stable source profile key or continuation metadata;
- bounded JSON state;
- captured UTC;
- status/version.

Checkpoint data must not contain cookies, access tokens, raw passwords, or whole HTML pages.

### Candidate

- Guid ID;
- current source profile key and canonical source URL when available;
- display name;
- normalized location text only where operationally relevant;
- technical/non-technical status;
- commitment status;
- idea commitment status;
- candidate lifecycle status;
- first seen/last seen UTC;
- last activity text/parsed UTC optional;
- current snapshot ID;
- latest evaluation ID;
- founder quality, fit, confidence, activity, risk penalty, invitation priority summary columns for sorting;
- created/updated UTC;
- row/concurrency version where needed.

Do not add age, gender, race, religion, photo, marital status, health, or similar personal fields to the normalized candidate table.

Candidate statuses:

```text
Discovered
Captured
Parsed
FilteredOut
PendingAnalysis
Analyzing
Analyzed
Shortlisted
Monitor
Passed
QueuedForInvite
MessageReviewed
ManuallySent
Accepted
Declined
NoResponse
CallScheduled
PassedAfterCall
TrialProject
Selected
```

Centralize allowed transitions with reason codes and tests.

### ProfileSnapshot

- Guid ID;
- candidate ID;
- source account/segment IDs;
- stable source key/url;
- content hash;
- parser/source adapter version;
- capture UTC;
- raw snapshot relative artifact path and retention/deletion timestamp;
- normalized profile JSON optional until Stage 11;
- extraction completeness/confidence;
- status/error code;
- unique identity preventing duplicate identical snapshots per candidate.

Persist the snapshot record/artifact before later analysis.

### CandidateIdentityAlias

- ID;
- candidate ID;
- alias type (`SourceKey`, `CanonicalUrl`, `Fingerprint`, `ManualMerge`);
- normalized alias value/hash;
- source account;
- confidence;
- created UTC;
- uniqueness/indexes preventing two active candidates from owning the same strong alias.

### ScreeningDecision

- ID;
- candidate/snapshot;
- ruleset version;
- outcome;
- score optional;
- reason codes/evidence JSON;
- created UTC.

### Evaluation

Create schema now; Stage 12 fills behavior:

- ID;
- candidate/snapshot;
- scorecard version;
- evaluator provider/model/deployment version;
- prompt version;
- input hash/cache key;
- founder quality score;
- our fit score;
- confidence;
- activity score;
- base/risk/final/priority values;
- recommendation;
- structured evaluation JSON;
- raw provider response artifact reference optional;
- status/error/retry metadata;
- created UTC.

### EvaluationCategory / EvaluationRisk

Normalized children for filtering/reporting plus the original structured evaluation JSON. Include key, score, maximum, evidence JSON, reason, and penalty fields.

### InvitationDraft

- ID;
- evaluation/candidate;
- short draft;
- detailed draft;
- facts used JSON;
- confidence;
- validation status/errors JSON;
- similarity fingerprint;
- created UTC;
- reviewed UTC/by;
- superseded flag.

### CandidateAction

Append-only domain timeline:

- ID;
- candidate;
- action type;
- occurred UTC;
- actor;
- notes bounded;
- structured data JSON;
- related invitation/evaluation/run IDs.

### ManualInvitationWindow

- ID;
- start/end UTC or user-configured tracking window;
- configured primary queue size;
- configured reserve size;
- sent count;
- notes;
- created/updated UTC.

Do not assume an external service's reset time; this is user-managed tracking.

### ReportExport

- ID;
- report type/format;
- filter/sort configuration hash;
- relative path;
- row count;
- file hash/size;
- created/delete-after UTC.

## 2. Domain repository/use-case interfaces

Create focused APIs:

- `IBrowserAccountRepository`
- `IDiscoverySegmentRepository`
- `IDiscoveryCheckpointRepository`
- `ICandidateRepository`
- `IProfileSnapshotRepository`
- `ICandidateIdentityRepository`
- `IScreeningRepository`
- `IEvaluationRepository`
- `IInvitationDraftRepository`
- `ICandidateActionWriter`
- `IInvitationQueueRepository`
- `IReportExportRepository`
- `IFounderScoutWorkQueue`

Important atomic operations:

- find/create candidate by strong identity;
- attach alias with conflict detection;
- add snapshot if content hash is new;
- update current snapshot atomically;
- claim pending candidate for analysis with lease/claimed-until/worker ID;
- release/retry/finalize analysis claim;
- transition candidate state legally;
- create/supersede invitation draft;
- query ranked/paginated candidates;
- append action.

Avoid a generic repository.

## 3. Founder Scout configuration contracts

Move/implement typed versioned Founder Scout configuration in `FounderScout.Application`, matching `docs/configuration-examples.md`:

- data paths;
- discovery limits/cooldown/stop policies;
- analysis batch/concurrency/retries;
- ranking thresholds/formula weights;
- invitation limits/validation settings;
- AI provider endpoint/deployment/secret reference;
- retention;
- founder persona reference/content.

Add thorough validation. No secret values in configuration.

Provide an adapter implementing the platform agent configuration validator used by Stage 08.

## 4. State machines and reason codes

Implement explicit services for:

- candidate lifecycle transitions;
- account session-health transitions;
- snapshot status;
- evaluation status;
- invitation review/send outcome.

Record append-only CandidateAction entries for meaningful user/domain changes. Reject illegal transitions with typed errors.

## 5. Agent CLI shell

Implement real commands using the Stage 01 Agent SDK:

```powershell
FounderScout.exe run
FounderScout.exe discover
FounderScout.exe analyze
FounderScout.exe authenticate
FounderScout.exe import --input <path>
FounderScout.exe report
FounderScout.exe diagnose
FounderScout.exe protocol-demo
```

In this stage:

- `import` works with local JSON/text fixture files and persists candidates/snapshots;
- `diagnose` validates config, data directory, DB migrations, browser-account records, and report/artifact directories;
- `report` emits a minimal domain-count summary and JSON/Markdown placeholder based on actual DB data;
- `discover`, `analyze`, and `authenticate` emit explicit “not implemented until Stage XX” warning/summary with stable non-success or no-work semantics as appropriate, not a false success;
- `run` composes currently implemented phases and accurately summarizes what ran;
- stdout remains JSONL; stderr is human diagnostics.

Register/update the Founder Scout manifest and command list through platform seeding without overwriting user configuration.

## 6. Fixture import format

Define a versioned capture/import envelope that Stage 10 can also produce:

```json
{
  "captureSchemaVersion": "1.0",
  "source": "fixture",
  "sourceAccountId": "fixture-account",
  "sourceSegmentId": "fixture-segment",
  "sourceProfileKey": "candidate-001",
  "profileUrl": "https://example.invalid/profile/candidate-001",
  "capturedAtUtc": "...",
  "displayName": "Candidate A",
  "rawText": "...",
  "structuredFields": {},
  "sourceAdapterVersion": "fixture-1.0"
}
```

The import service must:

- validate input size/count;
- reject traversal/unsafe source paths;
- normalize URL/key enough for identity;
- save raw content as a bounded artifact under Founder Scout data root;
- calculate hash;
- persist snapshot before any downstream work;
- be idempotent for the same source key/hash;
- emit protocol metrics/events.

## 7. Cross-database correlation

Founder Scout runs are owned centrally in `assistant.db`, while candidates live in `founders.db`.

Use run ID/correlation ID values, not cross-database foreign keys. Do not attempt distributed transactions. Required order:

```text
central Runner starts run
Founder Scout commits domain changes locally
Founder Scout emits events/summary to Runner
Runner commits platform audit/summary
```

Operations must be idempotent so a Runner finalization failure does not require reimporting duplicate profiles.

## 8. Tests

Use real temporary SQLite. Cover:

- migrations/pragmas/indexes;
- candidate/account/evaluation/invitation state transitions;
- illegal transitions;
- identity alias uniqueness/conflict;
- snapshot idempotency by source key/hash;
- separate candidate for weak ambiguous fingerprint until manually resolved;
- analysis work claim concurrency/expiry;
- configuration validation;
- fixture import size/format/security/idempotency;
- raw artifact committed before state queues analysis;
- no protected fields in Candidate schema/DTO;
- CandidateAction append behavior;
- queue size/window tracking;
- paginated ranking query shape with seeded records;
- CLI stdout valid JSONL;
- central run correlation without cross-DB FK;
- fresh migration and upgrade test.

## Constraints

- No Playwright or live browsing.
- No AI provider call.
- No full parser/scoring.
- No candidate UI.
- No automatic invitation sending.
- Do not put Founder Scout domain entities in `assistant.db`.

## Done when

- Founder Scout has a separate migrated database and stable domain model.
- Fixture import creates durable candidates/snapshots idempotently.
- Agent CLI works through Runner and emits correct protocol summaries.
- State machines and work claims are explicit/tested.
- Platform configuration UI can validate/save Founder Scout settings.
- All validation passes.

## Required smoke validation

Through Runner:

1. import a fixture file with at least three candidates and one duplicate;
2. rerun import and verify idempotency;
3. run `diagnose`;
4. run placeholder `report` and inspect artifact/summary;
5. query candidate/snapshot/action counts;
6. verify central AgentRun references the same run ID emitted in Founder Scout actions/events.

## Final report

Include Founder Scout schema, state diagrams, fixture format, CLI commands, cross-database approach, smoke results, and exact migration name. End with:

```text
Next prompt: prompts/10-founder-scout-browser-discovery.md
```


---

# Stage 10 — Founder Scout Browser Discovery

Paste this entire prompt into Codex from the repository root after Stage 09 passes.

---

You are implementing **Stage 10: Playwright Browser Sessions, Manual Authentication, Bounded Profile Discovery, Capture, Checkpointing, and Diagnostics**.

Read repository guidance, Founder Scout domain/configuration, acceptance matrix, and this prompt. Create/update `docs/exec-plans/stage-10-founder-scout-browser-discovery.md`.

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

Implement the Founder Scout browser acquisition phase using Microsoft Playwright for .NET. It must reuse dedicated persistent browser profiles, support manual headed authentication, navigate sequentially through configured founder-search discovery segments, capture profile data and raw diagnostics, persist snapshots before analysis, enforce hard run limits, checkpoint progress, and stop safely on authentication/access/throttling/challenge/parser-health signals.

Do not implement stealth, CAPTCHA solving, fingerprint spoofing, proxy rotation, human-movement simulation, automatic invitation sending, or automatic account failover following an enforcement signal.

## 1. Playwright setup

Add centrally managed stable Playwright package(s) compatible with .NET 10.

Provide documented installation/bootstrap command for Chromium binaries, for example through Playwright's generated PowerShell installer. Do not download browsers silently during every agent run.

Create abstractions:

- `IBrowserRuntimeFactory`
- `IBrowserSessionManager`
- `IProfileDiscoverySource`
- `IProfileCaptureExtractor`
- `IBrowserChallengeDetector`
- `IBrowserDiagnosticCapture`

The source adapter for this project should be versioned as `StartupSchoolSourceAdapter`, but keep generic browser/session infrastructure reusable.

## 2. Dedicated persistent account profiles

For each enabled `BrowserAccount`:

```text
<data>/founder-scout/browser/<account-id>/
```

Requirements:

- resolve only beneath the configured data root;
- one active browser context per account via a domain/platform lease;
- never point automation at the user's ordinary default Chrome profile;
- use Chromium or a configured Chrome channel if installed and validated;
- store no passwords in configuration/DB;
- browser profile folders are excluded from logs, reports, normal backups, fixtures, and source control;
- account health/status updated from observed session outcomes.

## 3. Authenticate command

Implement:

```powershell
FounderScout.exe authenticate --account <id>
```

Behavior:

1. validate account and profile path;
2. launch headed persistent browser context;
3. navigate to configured Startup School Co-Founder Matching entry URL;
4. let the user sign in manually;
5. detect authenticated matching page through configured/semantic signals;
6. allow the user to close/confirm completion;
7. update account status and timestamp;
8. emit JSONL progress/summary;
9. never prompt for or capture a password in the console/app.

If MFA or challenge appears, the user handles it manually in the headed browser. Do not automate challenge completion.

Add a timeout that is generous but bounded and cancellable.

## 4. Source configuration and locator strategy

Create versioned `StartupSchoolSourceOptions` containing:

- entry/discovery URL;
- allowed host names;
- authenticated-page signals;
- login-page signals;
- profile-card/profile-link locator candidates;
- next-page/load-more/infinite-scroll behavior;
- profile-page root/field locator candidates;
- challenge/throttle/access-denied signals;
- navigation timeout;
- settle delay/minimum request spacing;
- source adapter version.

Prefer resilient Playwright locators by accessible role, link text, labels, headings, and stable attributes. Avoid brittle absolute XPath and `nth-child` selectors where alternatives exist.

Because credentials/live page access may not be available to Codex or CI:

- implement the adapter against checked-in sanitized local HTML fixtures and a local fixture server;
- add `record-fixture`/diagnostic capability that saves sanitized page HTML/screenshot/locator report from an authenticated developer session;
- when a live authenticated session is available during implementation, inspect and configure real locators, but do not claim live operation if it was not verified;
- keep locator config/version separate from compiled control flow where practical.

Do not commit captured real profile data.

## 5. Discovery command

Implement:

```powershell
FounderScout.exe discover --account <id> [--segment <id>]
```

Normal flow:

```text
validate config/account/segment and acquire account lease
launch persistent context
navigate to discovery entry
verify authenticated healthy page
restore safe checkpoint/segment position where possible
enumerate visible profile links sequentially
for each profile:
    enforce limits and stop conditions
    navigate/open profile
    verify profile page health
    extract stable source key/url/display fields/raw text/structured fields
    save raw HTML/text/screenshot only according to retention/diagnostic policy
    calculate capture hash
    persist candidate/snapshot before any analysis
    emit metrics/progress/checkpoint
    return to discovery/list or continue through stable URLs
persist final checkpoint and domain run summary
close context and release lease
```

Do not keep a database transaction open during browser navigation.

## 6. Hard run limits

Enforce all configured bounds locally:

- maximum new profiles per run, default 20;
- maximum viewed profile pages per run;
- maximum new profiles per day across runs;
- maximum run duration, default 10 minutes;
- browser concurrency 1;
- stop after configured consecutive already-known unchanged profiles;
- minimum spacing/cooldown between navigation actions as a deterministic load bound;
- cancellation;
- account/segment paused or disabled state.

At completion, the platform fixed-delay schedule can create the next discovery occurrence after configured cooldown, default 10 minutes. The agent must not sleep internally for 10 minutes and must not self-schedule.

Emit distinct no-work/completed/capped summaries.

## 7. Stop conditions and session health

Stop the current account/run immediately on:

- redirect to login or missing authenticated-page signals;
- HTTP/navigation result or page signal indicating access denied;
- throttling/too-many-requests signal;
- challenge/CAPTCHA/verification page;
- persistent unexpected host/navigation outside allow-list;
- parser/extraction health below threshold;
- repeated navigation failure;
- browser profile lock/conflict;
- cancellation.

Behavior:

- update BrowserAccount status and bounded reason;
- capture a diagnostic screenshot, URL metadata, sanitized HTML/locator report when allowed;
- emit warning/error/summary and appropriate Agent exit code;
- do not switch to another account automatically;
- do not retry access denial/throttling/challenge in the same run;
- transient network/browser crashes may receive at most a small bounded retry according to config.

## 8. Profile capture envelope

Populate the Stage 09 capture envelope with as much structured data as can be grounded:

- source adapter/version;
- account/segment;
- source profile key;
- canonical URL;
- captured UTC;
- display name;
- raw visible profile text;
- structured sections keyed by stable semantic labels;
- last-seen text if visible;
- extraction completeness and warnings;
- source page fingerprint.

Do not store profile image bytes. If an `<img>` exists, ignore it.

Persist raw HTML only under configured retention and artifact size limits. Prefer extracted visible text/structured JSON for ordinary snapshots; save full HTML/screenshot mainly for diagnostics or explicit configuration.

## 9. Checkpointing and resume

Checkpoint after each successfully persisted profile:

- account/segment;
- last source key/url;
- discovered link set hash or continuation info;
- profiles viewed/new/duplicate;
- current page/scroll marker when reliable;
- adapter version;
- UTC.

Resume only when:

- checkpoint version matches;
- account and segment match;
- source page still validates;
- continuation metadata is safe.

Otherwise restart discovery conservatively and rely on deduplication. Never persist cookies/tokens in checkpoint JSON.

## 10. Diagnostics

On failure, create bounded per-run artifacts:

```text
screenshot.png
page.html or sanitized-page.html
locator-report.json
browser-error.json
console-summary.json
```

Rules:

- artifact paths stay under assigned run directory;
- redact form values, cookie/storage data, authorization values, and sensitive query parameters;
- cap HTML/console sizes;
- retention default 14 days;
- report artifacts through Agent SDK;
- do not include raw diagnostics in ordinary summary.

Provide `FounderScout.exe diagnose --account <id>` that checks browser binary, profile path, lock, configured URL/host, and optionally page authentication in headed or headless mode without performing discovery.

## 11. Scheduling integration

Seed or provide UI action for a recommended discovery schedule:

```text
Type: FixedDelayAfterCompletion
Command: discover
Batch limit: config-driven, default 20 new
Delay: config-driven, default 10 minutes
Concurrency: Forbid
Wake policy: configurable, default IfSleeping only if the user wants unattended runs
Timeout: config max runtime plus startup/cleanup margin
```

When agent returns authentication-required, access-denied, throttled, challenge-detected, or parser-failure exit codes:

- suspend/pause that account's discovery schedule according to explicit mapping;
- require user review/reauthentication before resuming;
- do not create rapid retries.

Transient browser/network failure may follow bounded retry policy.

## 12. Tests

Create sanitized fixture pages and a local test server to cover:

- authenticated list and profile navigation;
- multiple locator candidates;
- pagination/load more/infinite-scroll abstraction;
- duplicate/known profiles;
- new profile capture committed before downstream status;
- max new/viewed/runtime/daily/consecutive-known limits;
- checkpoint/resume and incompatible checkpoint fallback;
- login redirect/authentication required;
- access denied;
- throttle;
- challenge/CAPTCHA detection;
- unexpected host;
- parser-health failure;
- navigation transient retry bound;
- account lease/profile lock;
- diagnostic redaction/size/path/retention;
- no images stored;
- stdout valid JSONL;
- account status and exit-code mapping;
- fixed-delay next occurrence after completion;
- no automatic account failover after an enforcement signal.

Live Startup School tests must be opt-in and disabled by default. Do not put credentials or real profile content in fixtures/CI artifacts.

## Constraints

- No AI evaluation yet.
- No automatic invitations or messages.
- No stealth/anti-detection packages or behavior.
- No proxy rotation or CAPTCHA solving.
- No multi-browser concurrency.
- No Azure Function/cloud browser deployment.

## Done when

- A user can manually authenticate a dedicated browser account.
- A bounded discovery run captures and persists profile snapshots sequentially.
- Limits, checkpointing, deduplication handoff, diagnostics, and session-health states work.
- Fixed-delay scheduling runs batches without internal sleep.
- Local fixtures fully test page flows and stop conditions.
- Any live verification is reported honestly.

## Required smoke validation

1. Install Playwright Chromium through the documented command.
2. Run discovery against the local fixture server through Runner.
3. Capture at least 20 fixture profiles with duplicates and a stop condition.
4. Verify candidate/snapshot/checkpoint/account status/central run metrics.
5. Run an authentication-required and challenge fixture.
6. Confirm no account failover and no orphaned browser process.
7. Optionally run `authenticate` against a real account manually; clearly state whether live profile discovery was tested.

## Final report

Include Playwright/browser version, profile storage strategy, locator/fixture approach, run limits, stop-state mapping, smoke results, and live-verification status. End with:

```text
Next prompt: prompts/11-founder-scout-processing-pipeline.md
```


---

# Stage 11 — Founder Scout Processing Pipeline

Paste this entire prompt into Codex from the repository root after Stage 10 passes.

---

You are implementing **Stage 11: Versioned Parsing, Normalization, Identity Resolution, Deduplication, Protected-Attribute Redaction, Change Detection, and Fast Screening**.

Read repository guidance, Founder Scout capture/domain code, fixtures, configuration, and this prompt. Create/update `docs/exec-plans/stage-11-founder-scout-processing-pipeline.md`.

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

Transform raw captured profiles into normalized, evidence-preserving candidate snapshots; resolve duplicate identities across accounts/segments; detect profile changes; remove irrelevant/protected attributes from evaluator input; perform deterministic fast screening; and queue deep analysis idempotently. No AI provider call in this stage.

## 1. Normalized profile model

Create a versioned `NormalizedFounderProfile` DTO containing only fields relevant to founder evaluation and operations:

- source profile key and canonical URL;
- display name;
- normalized location/time-zone compatibility text when visible;
- technical/non-technical/unknown status;
- full-time/part-time/unknown commitment;
- committed-to-idea/open-to-ideas/unknown status;
- roles the founder can own: design, sales/marketing, operations, product, domain, fundraising, other;
- introduction/about text;
- career/education/building/leadership background text;
- startup/problem/customer/solution description;
- progress/traction/validation claims exactly as stated;
- co-founder desired skills/role/commitment/equity posture when stated;
- industries/interests;
- last-seen/activity text and parsed timestamp/range only when reliably inferable;
- parser warnings and missing-field list;
- profile completeness score;
- parser/source versions.

Do not include age, gender, race/ethnicity, religion, photo/image data, marital/family status, disability/health data, sexual orientation, or other protected/irrelevant attributes in this DTO.

Raw snapshot retention is separate and may contain visible page text temporarily; evaluator inputs and normalized tables must exclude these attributes.

## 2. Versioned parser

Implement `IFounderProfileParser` with a parser version.

Inputs:

- captured structured fields;
- raw visible text;
- source adapter metadata.

Outputs:

- normalized profile;
- field-level evidence mapping to source sections/text spans or safe snippets;
- completeness;
- warnings/errors;
- parser-health result.

Requirements:

- prefer structured semantic sections from the browser adapter;
- use deterministic text parsing for known labels;
- no LLM parsing in V1 pipeline stage;
- unknown stays unknown;
- do not infer revenue/customers/commitment/equity from vague language;
- preserve exact claim text separately from normalized enums;
- parser failure must not overwrite a previously valid normalized snapshot;
- store parser version and normalized JSON on the snapshot or related entity;
- create fixtures for common profile variations, missing sections, formatting changes, Unicode, and hostile text.

## 3. Canonical normalization and hashes

Implement deterministic canonicalization:

- Unicode normalization;
- normalized line endings/whitespace;
- stable ordering of sets/lists where order is semantically irrelevant;
- normalized URLs (allowed host, fragment removal, conservative query handling);
- lowercased keys where appropriate without altering evidence text;
- canonical JSON with stable property ordering.

Calculate:

```text
RawContentHash
NormalizedProfileHash
EvaluatorInputHash
```

Use SHA-256. Document what each controls.

A profile is unchanged for analysis only when the relevant normalized/evaluator hash and all evaluator version inputs are unchanged.

## 4. Identity resolution and deduplication

Implement `ICandidateIdentityResolver` with ordered evidence:

1. stable source profile key;
2. canonical profile URL;
3. trusted source-specific ID extracted from URL/page;
4. strong deterministic fingerprint using normalized display name + location + stable introduction/background fragments;
5. manual merge/split action.

Rules:

- strong IDs merge automatically;
- weak fingerprints produce a match candidate/conflict record when ambiguous, not an automatic destructive merge;
- cross-account/segment sightings attach aliases and snapshots to one Candidate;
- display-name changes do not create a new candidate when stable ID matches;
- manual merge preserves all snapshots/actions/evaluations and records an audit/action;
- manual split is supported or explicitly deferred with a safe migration strategy;
- identity conflicts are visible for later UI/manual review.

Add a `CandidateIdentityConflict` entity if needed.

## 5. Profile change detection

When a captured snapshot arrives:

```text
resolve candidate
compare normalized hash to current valid snapshot
if identical:
    update last seen/activity/account metrics
    no analysis queued
if changed:
    persist normalized snapshot
    mark current snapshot
    create profile-change action with bounded changed-field summary
    queue screening/analysis
```

Do not compare raw HTML hashes for analysis decisions because layout noise can change. Preserve both raw and normalized hashes.

Implement a safe structured diff for relevant fields, not a full raw-text diff in audit.

## 6. Protected-attribute redaction

Implement `IProfileRedactor` that produces `FounderEvaluationInput` from normalized profile/evidence.

Requirements:

- exclude protected/irrelevant fields and image references;
- remove clearly labeled age/gender/family/religion/health passages from unstructured sections where feasible through deterministic label/pattern rules;
- do not use protected values for scoring, ranking, dedupe, or invitation drafts;
- preserve only operational location/time-zone/relocation information;
- emit redaction reason codes/counts, not removed values;
- test false positives and ensure business terms are not unnecessarily removed;
- allow manual review when redaction confidence is low.

## 7. Deterministic fast screening

Implement a versioned rules engine producing:

```text
Outcome: DeepAnalyze | Monitor | FilterOut | ManualReview
ScreeningScore 0..100 optional
ReasonCodes
Evidence snippets
MissingEvidence
RulesetVersion
```

Initial configurable signals:

Positive/preferred:

- non-technical or complementary founder;
- explicitly seeking technical co-founder/CTO;
- full-time or credible transition plan;
- owns sales, operations, design, domain, product, or distribution;
- clear customer/problem;
- traction/validation/customer access evidence;
- founder-level/equal partnership posture;
- US/time-zone compatibility when configured.

Risk/filter/manual-review signals:

- explicitly seeking only unpaid implementation labor/contract developer;
- indefinite part-time posture without transition;
- expects technical co-founder to own product, engineering, sales, fundraising, and operations;
- idea depends on unavailable external access with no validation;
- no meaningful profile content;
- technical founder seeking only another identical technical role when preference excludes it;
- contradictory or parser-low-confidence content.

Important:

- missing traction is not proof of zero traction;
- missing equity information is unknown, not negative by itself;
- rules produce grounded evidence;
- protected attributes never participate;
- thresholds and hard-filter rules are configuration/versioned;
- a hard filter must have an explicit reason code and be reversible/manual-overridable.

## 8. Analysis queue

Implement processing command behavior:

```powershell
FounderScout.exe analyze --phase screen --max <N>
```

It should:

- claim candidates/snapshots needing parsing/screening;
- parse/normalize/redact/screen;
- persist decisions/actions atomically per candidate;
- transition to `PendingAnalysis`, `FilteredOut`, `Monitor`, or `ManualReview` equivalent state;
- emit progress/metrics/checkpoints;
- avoid duplicate work under concurrent process attempts;
- release/expire claims on failure;
- create an immediate deep-analysis occurrence or leave pending work for Stage 12, according to existing orchestration pattern.

Update `FounderScout.exe run` to compose discovery and currently implemented processing accurately. It must not wait/sleep for cooldown.

## 9. Parser health

Define health thresholds:

- required field groups found;
- expected section labels/roots;
- minimum visible text bounds;
- ratio of recognized sections;
- source adapter/parser version compatibility.

If a run encounters repeated parser-health failures, stop discovery/processing and set account/segment `ParserFailure` rather than generating low-quality evaluations.

## 10. Tests

Cover:

- all normalized fields and unknown handling;
- protected fields absent from DTO/schema/evaluator input;
- Unicode/whitespace/URL canonicalization;
- hash stability and changes only when relevant data changes;
- stable ID, canonical URL, strong fingerprint, weak ambiguity, cross-account duplicates;
- candidate rename with stable ID;
- profile-change structured diff;
- no requeue for normalized unchanged profile despite raw layout noise;
- redaction patterns/false positives/low confidence;
- every screening reason code;
- missing evidence semantics;
- configurable thresholds/ruleset version;
- hard filter override/audit;
- parser-health stop threshold;
- work claim concurrency/idempotency/recovery;
- 20-profile batch protocol metrics;
- no AI calls.

Use sanitized fixture profiles with a broad variety of realistic content, but no real personal profile data.

## Constraints

- No AI provider call or semantic embedding dedupe.
- No protected-attribute scoring.
- No candidate UI beyond existing generic agent/run pages.
- No invitation sending.
- Do not rewrite browser acquisition unless required for a proven capture envelope issue.

## Done when

- Captured profiles become stable normalized snapshots.
- Cross-account duplicates converge safely.
- Unchanged profiles skip analysis.
- Protected attributes are excluded.
- Fast screening is grounded, deterministic, versioned, configurable, and auditable.
- Pending deep-analysis queue is populated idempotently.
- All validation passes.

## Required smoke validation

Using local fixtures through Runner:

1. import/discover 20 profiles containing duplicates, one changed profile, missing fields, and protected labels;
2. run screening batch;
3. verify candidate/snapshot/alias/decision/action counts;
4. rerun and verify no duplicate work;
5. change one relevant field and verify one candidate requeues;
6. prove evaluator input artifacts contain no protected values from the fixture markers;
7. print reason-code distribution and protocol summary.

## Final report

Include normalized model, hash purposes, identity precedence, redaction boundaries, screening rules/version, smoke counts, and deferred AI behavior. End with:

```text
Next prompt: prompts/12-founder-scout-ai-evaluation.md
```


---

# Stage 12 — Founder Scout AI Evaluation, Scoring, and Introduction Drafts

Paste this entire prompt into Codex from the repository root after Stage 11 passes.

---

You are implementing **Stage 12: Structured AI Evaluation, Deterministic Score Calculation, Evidence Validation, Caching, Retry Policy, Personalized Introduction Drafts, and Invitation Priority**.

Read all repository guidance, normalized profile/redaction/screening code, configuration examples, architecture, acceptance matrix, and this prompt. Create/update `docs/exec-plans/stage-12-founder-scout-ai-evaluation.md`.

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

Implement deep candidate evaluation through a provider-neutral AI boundary with an initial configurable OpenAI/Azure OpenAI provider. Require strict structured output, grounded evidence, explicit unknowns, deterministic C# scoring, model/prompt/scorecard versioning, caching, bounded retries, two personalized introduction drafts per deep evaluation, deterministic draft validation, and ranked invitation priority.

The system must never auto-send an invitation.

## 1. Provider architecture

Define:

```csharp
public interface IFounderEvaluationModelClient
{
    Task<ModelEvaluationResponse> EvaluateAsync(
        FounderEvaluationRequest request,
        CancellationToken cancellationToken);
}
```

Keep provider SDK/HTTP details in `FounderScout.Infrastructure`.

Support configuration fields:

- provider: `OpenAI` or `AzureOpenAI`;
- endpoint;
- model/deployment;
- API key secret reference;
- request timeout;
- optional API version only when required by the selected Azure endpoint;
- max output size/tokens;
- reasoning/temperature controls only when supported and useful.

Before selecting a package/API approach:

- inspect current official stable .NET SDK support available in the environment;
- prefer an official OpenAI/Azure SDK that supports the current Responses API and strict JSON schema;
- if the required Azure structured-output behavior is not reliably supported by the SDK, implement a focused `HttpClient` adapter and record an ADR with endpoint/auth/version assumptions;
- do not hardcode a model name or obsolete API version;
- use `HttpClientFactory`, timeout, cancellation, and redacted logging;
- secrets are resolved from `ISecretStore` and never persisted in evaluation/config/logs.

Provide a fake deterministic model client for tests.

## 2. Evaluation request and cache key

Create `FounderEvaluationRequest` containing:

- redacted `FounderEvaluationInput`;
- scorecard version/content hash;
- prompt version/content hash;
- evaluator schema version;
- founder persona version/content hash;
- relevant configuration/threshold versions;
- candidate/snapshot IDs for local correlation only, not necessarily sent to the model;
- requested language/tone;
- maximum invitation lengths.

Calculate a deterministic input/cache SHA-256 from all behavior-affecting non-secret inputs. If a completed valid evaluation with the same input hash exists, reuse it without a provider call and record cache metrics.

Changing normalized input, scorecard, prompt, provider/model policy, persona, or evaluator schema must create a new evaluation.

## 3. Score model

Preserve the existing YC evaluation scorecard as the primary category model:

```text
Founder execution quality           20
Commitment and co-founder posture   15
Traction and validation             15
Market potential                    15
GTM and domain advantage            10
CTO fit                             10
Idea and problem clarity             5
Moat potential                       5
Technical feasibility                5
                                    ---
                                    100
```

Every category response contains:

- key;
- integer score in `0..maximum`;
- evidence snippets grounded in the provided profile input;
- explanation;
- missing evidence;
- confidence `0..1`.

The model must also return explicit fit dimensions, each `0..maximum`, total 100:

```text
Complementary skills/ownership      30
Founder-level/equity posture        25
Technical/domain alignment          20
Timing/location/working compatibility 15
CTO workload realism                10
                                    ---
                                    100
```

Do not treat unstated equity as a confirmed negative; mark unknown and lower confidence.

### Founder quality calculation

Calculate in C#:

- primary `BaseScore` from the 100-point scorecard;
- `FounderQualityScore` from the scorecard excluding the personal `CTO fit` category, normalized from 90 points to 100, or document a better exact formula that preserves comparability;
- `OurFitScore` from fit dimensions;
- explicit risk penalties;
- final recommendation.

Do not trust arithmetic returned by the model.

## 4. Risk penalties

Versioned risk definitions and maximum penalties:

```text
unpaidDeveloperRisk                 20
founderReservesMostEquity           15
overscopedTechnicalBuild            10
noCustomerAccessPath                10
ctoExpectedToOwnEverything          10
indefinitePartTimeCommitment        10
unavailableExternalDependency       10
```

The model may propose only known risk keys and must provide evidence. C# validates and applies configured penalty values/caps. Unknown risk keys are rejected or placed in non-scoring warnings.

Do not apply a penalty solely because evidence is absent. Use missing evidence/confidence.

## 5. Structured response schema

Create a strict typed schema similar to:

```json
{
  "schemaVersion": "1.0",
  "profileSummary": "...",
  "qualityCategories": [
    {
      "key": "founderExecution",
      "score": 16,
      "evidence": ["..."],
      "explanation": "...",
      "missingEvidence": [],
      "confidence": 0.82
    }
  ],
  "fitDimensions": [],
  "risks": [
    {
      "key": "overscopedTechnicalBuild",
      "evidence": ["..."],
      "explanation": "...",
      "confidence": 0.75
    }
  ],
  "positiveSignals": [],
  "redFlags": [],
  "missingEvidence": [],
  "priorityQuestions": [],
  "recommendationRationale": "...",
  "invitation": {
    "shortDraft": "...",
    "detailedDraft": "...",
    "candidateFactsUsed": ["..."],
    "personaStrengthsUsed": ["..."],
    "conversationTopic": "...",
    "confidence": 0.9
  }
}
```

Requirements:

- exact required category/fit keys once each;
- bounded arrays and string lengths;
- no HTML;
- no unsupported fields used for scoring;
- no raw chain-of-thought request/storage;
- explanations are concise assessment rationales and evidence, not hidden reasoning traces;
- reject malformed/out-of-range/incomplete results before persistence as completed.

## 6. Evaluation prompt

Create version-controlled prompt templates under `agents/FounderScout/prompts/` rather than hardcoding one massive string in C#.

System/developer-style rules must state:

- evaluate only supplied profile evidence;
- unknown remains unknown;
- do not browse or invent external facts;
- ignore age, gender, race, religion, image, family, health, and other protected attributes;
- distinguish founder quality from fit for Yuriy;
- identify “technical co-founder” versus “unpaid developer” risk carefully;
- do not overreward idea novelty without customer evidence;
- use exact category keys and bounds;
- return only schema-valid JSON;
- generate candidate-specific, truthful introduction drafts;
- never state that Yuriy has decided to join;
- never mention automated analysis/scoring/scraping;
- no invitation sending instruction.

Include the founder persona from configuration and the scorecard definitions.

Add a prompt fixture test that verifies required policy sections are present without snapshotting every word.

## 7. Evidence validation

Implement deterministic post-validation:

- each quoted/paraphrased evidence item must be findable or meaningfully traceable to normalized input/evidence map;
- category score above zero requires evidence or a narrowly documented exception for purely structural assessments;
- risk penalty requires evidence;
- claimed revenue/customer/partnership numbers must appear in profile input;
- candidate facts used in invitation must be grounded;
- protected-attribute terms/values from redaction markers must not appear;
- unsupported claims move evaluation to `NeedsReview`/failed validation, not completed ranking.

Use conservative normalized substring/token matching plus evidence IDs from the request where possible. Do not require exact punctuation.

## 8. Retry/error policy

Classify:

- transient network/timeout/429/5xx/provider-unavailable;
- authentication/configuration/model-not-found/permanent;
- invalid structured output;
- evidence-validation failure.

Policy:

- bounded retries, default 3 total attempts;
- exponential backoff with jitter for transient provider failures only;
- respect provider retry-after when available;
- at most one repair/regeneration attempt for invalid schema/evidence/draft validation, with a concise validation-error payload;
- no tight retry loop;
- configuration/auth errors pause analysis and surface attention required;
- persist attempt metadata without secrets or full sensitive request headers.

## 9. Deterministic scoring and recommendation

Implement `ICandidateScoreCalculator`.

Calculate and store:

- BaseScore;
- FounderQualityScore;
- OurFitScore;
- risk penalty total;
- adjusted evaluation score(s);
- overall confidence derived from weighted category/fit confidence, completeness, and evidence coverage;
- activity score from parsed last-seen data using configurable buckets;
- InvitationPriority.

Initial priority formula:

```text
OurFitScore * 0.55
+ FounderQualityScore * 0.25
+ OverallConfidence*100 * 0.15
+ ActivityScore * 0.05
- configured invitation-priority penalties
```

Clamp `0..100` and document rounding.

Initial recommendations based on adjusted/base/fit/confidence thresholds:

```text
StrongConnect
ExploratoryCall
Monitor
Pass
ManualReview
```

Low confidence below configured threshold forces `ManualReview` regardless of high numeric score.

## 10. Introduction drafts

Every valid deep evaluation must produce:

- short draft;
- detailed draft;
- grounded candidate facts used;
- complementary persona strengths used;
- one conversation topic/question;
- confidence.

Deterministic `IInvitationDraftValidator` checks:

- candidate-specific grounded fact;
- complementary value statement;
- clear founder-to-founder reason to connect;
- optional question/topic;
- configured maximum length;
- no protected attributes;
- no unsupported revenue/customer/company claims;
- no mention of scoring/automation/scraping;
- no commitment to join/invest/build for free;
- no generic-only text;
- similarity fingerprint compared with recent queued drafts under configurable threshold.

If validation fails, perform at most one targeted regeneration. Otherwise persist draft as `NeedsReview` with errors; do not discard the underlying evaluation.

## 11. Analyze command

Complete:

```powershell
FounderScout.exe analyze --phase deep --max <N>
FounderScout.exe analyze --phase all --max <N>
```

Flow:

```text
claim pending analysis item
load normalized redacted input and versions
check cache
call provider if needed
validate schema/evidence
calculate scores/recommendation
validate/regenerate invitation drafts
persist evaluation/categories/risks/draft/action
transition candidate state
emit metrics/progress/checkpoint
release claim
```

Default batch 20, concurrency 2, configurable. Per-candidate failures do not lose other completed work. Use a bounded channel/semaphore and one short DB scope per operation.

Update `run` to compose discover -> screen -> deep analyze -> report according to command configuration, without sleeping internally.

## 12. Metrics and summary

Emit at minimum:

```text
analysis.claimed
analysis.completed
analysis.cacheHit
analysis.filtered
analysis.manualReview
analysis.failed
analysis.providerRequests
analysis.providerRetries
candidates.strongConnect
candidates.exploratory
candidates.monitor
candidates.pass
invitations.generated
invitations.needsReview
```

Summary includes counts and top candidate IDs/names/scores, bounded to a small number.

## 13. Tests

Use fake model client by default. Cover:

- cache key stability and invalidation by every versioned input;
- strict response schema and exact category keys;
- score bounds and C# arithmetic;
- founder quality normalization;
- fit score;
- risk application only with evidence;
- unknown/missing evidence behavior;
- low-confidence forced manual review;
- activity buckets and priority formula;
- evidence grounding and fabricated numeric claim rejection;
- protected marker exclusion;
- provider transient/permanent failures and retries;
- invalid schema repair limit;
- configuration/auth pause behavior;
- short/detailed draft requirements;
- length, generic, unsupported claim, automation mention, protected attribute, commitment, and similarity validation;
- one regeneration maximum;
- per-candidate batch isolation/concurrency/idempotency;
- prompt required policies;
- secret redaction;
- valid JSONL metrics/summary;
- no auto-send path/API/action exists.

Add opt-in live integration test guarded by required secret reference/environment configuration. It must be skipped by default and use synthetic profiles only.

## Constraints

- Do not browse external web sources during candidate evaluation.
- Do not auto-send invitations.
- Do not expose raw API keys or provider payload headers.
- Do not use protected attributes.
- Do not let model-provided totals override C# calculations.
- Do not make live provider tests part of normal CI.

## Done when

- Deep analysis is structured, grounded, versioned, cached, retry-safe, and deterministic after model output.
- Founder quality, fit, confidence, risk, activity, and invitation priority are separate.
- Every deep evaluation has validated short/detailed introduction drafts or explicit NeedsReview errors.
- Batch analysis works through Runner with correct metrics and no duplicate work.
- Standard validation passes.

## Required smoke validation

Using synthetic fixture profiles and the fake deterministic model:

1. analyze at least 20 candidates across all recommendation states;
2. include one invalid-schema repair, one evidence failure, one cache hit, one provider transient retry, and one invitation validation failure;
3. verify score calculations and ranking values;
4. rerun unchanged inputs and verify cache/no duplicate evaluation behavior;
5. change scorecard or persona version and verify reevaluation;
6. inspect drafts for grounding and no protected markers;
7. optionally run one synthetic live-provider evaluation and clearly label it.

## Final report

Include provider/SDK decision and ADR, prompt/scorecard/schema versions, formulas, evidence validation, retry policy, draft checks, smoke counts, and whether live provider testing occurred. End with:

```text
Next prompt: prompts/13-founder-scout-results-ui.md
```


---

# Stage 13 — Founder Scout Results UI, Shortlist, Manual Invitation Queue, and Reports

Paste this entire prompt into Codex from the repository root after Stage 12 passes.

---

You are implementing **Stage 13: Founder Scout Candidate Dashboard, Detail, Ranking, Profile History, Manual Invitation Workflow, Outcome Tracking, and Exports**.

Read repository guidance, Host UI patterns, Founder Scout domain/evaluation code, acceptance matrix, and this prompt. Create/update `docs/exec-plans/stage-13-founder-scout-results-ui.md`.

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

Expose Founder Scout as a usable local decision-support tool inside the system-tray web UI. Implement candidate ranking/filtering, evidence-rich details, profile/evaluation history, short and detailed introduction draft review/copy, a configurable manual invitation queue and reserve queue, manual outcome tracking, discovery/account/segment status, and consistent Markdown/CSV/JSON/HTML reports.

No invitation may be sent automatically.

## 1. Founder Scout dashboard

Show real values:

```text
Profiles/candidates total
New/updated in selected period
Pending screening/deep analysis
Analyzed
Strong Connect / Exploratory / Monitor / Pass / Manual Review
Invitation drafts ready / needs review
Primary queue and reserve counts
Manually sent/accepted/calls scheduled
Browser account health
Discovery segment yield
Latest discovery/analysis/report runs
Attention items
```

Actions:

- run discovery for selected healthy account/segment;
- run pending analysis;
- regenerate reports;
- open primary queue;
- review account/session attention;
- open latest top-candidate report.

Actions create occurrences and start Runner. Do not execute agent code in web handlers.

## 2. Candidate list

Columns:

- rank/invitation priority;
- founder/display name;
- recommendation/status;
- founder quality;
- our fit;
- confidence;
- activity;
- risk penalty;
- last seen/captured;
- current queue/action state;
- profile changed indicator.

Filters:

- recommendation;
- candidate lifecycle status;
- minimum/maximum score and confidence;
- full-time/technical/idea posture;
- has traction/customer-access evidence;
- specific risk keys;
- new/changed date range;
- activity recency;
- invitation state;
- account/segment;
- needs manual review;
- text search over safe normalized fields.

Requirements:

- stable server-side sort and pagination;
- default sort by InvitationPriority descending, then confidence/activity/name/ID tie breakers;
- indexes and query projection for performance;
- no raw profile text in list query;
- bulk select only for local queue/status operations, never sending.

## 3. Candidate detail

Sections:

### Summary

- recommendation;
- founder quality, fit, confidence, activity, priority;
- concise profile summary;
- current status and latest activity;
- source link opened manually in browser with safe external-link handling.

### Why this candidate ranks

- category score breakdown with maximum;
- fit dimensions;
- evidence under each category;
- positive signals;
- risks/penalties;
- missing evidence;
- recommendation rationale.

### First-call preparation

- priority questions;
- expected CTO workload/risk signals;
- topics to validate;
- notes/actions.

### Introduction draft

- short and detailed drafts;
- facts/persona strengths used;
- validation status/errors;
- copy buttons using local JS;
- mark reviewed;
- manually edit into a new revision while preserving generated original;
- mark ready/manual sent;
- no Send button or network integration.

### Profile/evaluation history

- snapshot timeline;
- safe relevant-field diff;
- parser/source versions;
- evaluation history with scorecard/prompt/model/persona versions;
- why reevaluated;
- previous invitation drafts.

### Candidate timeline

- discovered, updated, analyzed, queued, reviewed, manually sent, accepted/declined/no response, calls/trial/selected;
- bounded notes;
- audit/correlation links.

Encode all untrusted text. Do not render raw captured HTML.

## 4. Manual invitation queue

Provide:

```text
Primary queue: configurable size, default 15
Reserve queue: configurable size, default 15
```

The queue is a local planning tool, not a claim about external reset timing.

Features:

- automatically suggest top eligible candidates by priority;
- user explicitly adds/removes/reorders;
- prevent duplicate active queue entry;
- show draft readiness and source-profile link;
- review checklist;
- copy draft;
- mark manually sent with date/time and optional note;
- track accepted/declined/no response/call scheduled;
- maintain user-managed invitation window dates and sent count;
- rollover/reset creates a new local window without deleting history;
- reserve promotion is manual or user-confirmed, not silent.

Eligibility default:

- valid evaluation;
- confidence above threshold or manually approved;
- invitation draft valid/reviewed;
- not already sent/declined/passed;
- no unresolved identity conflict;
- not stale beyond configured threshold unless manually included.

## 5. Draft editing and versioning

Generated invitation drafts are immutable. A user edit creates an `InvitationDraftRevision` or equivalent:

- source draft ID;
- edited short/detailed text;
- edited by/time;
- validation results;
- active selection;
- no secret or protected content.

Re-run deterministic validation after editing. Similarity is advisory for manual edits but still shown.

Copy action may be client-side and need not store clipboard contents. Record “copied” only if valuable and not noisy; manual-sent is authoritative.

## 6. Account and discovery segment pages

Browser account page:

- status/attention;
- enabled;
- profile path displayed only as safe relative path;
- last auth/success/failure;
- assigned segments;
- cooldown/suspension;
- actions: authenticate (launch headed agent command), diagnose, enable/disable, clear status after successful manual review;
- never display cookies/passwords/session files.

Discovery segment page:

- filters/config summary;
- priority;
- viewed/new/duplicate yield;
- consecutive low-yield runs;
- paused state;
- last/next run;
- account assignment;
- actions: run now, pause/resume, edit safe configuration.

Authentication command launch must be clearly interactive and occur in the current user session.

## 7. Reports and exports

Implement one report query model so all formats use the same selected candidate set and ordering.

Formats:

### `top-candidates.html`

Local standalone or application-rendered HTML with ranking, summary, reasons, risks, questions, and drafts. Encode content and avoid external scripts/assets.

### `top-candidates.md`

Readable Markdown containing:

- generation UTC/local time;
- filters/scorecard versions;
- reviewed counts;
- top candidates;
- why connect/concerns/questions;
- short introduction drafts;
- attention/manual-review list;
- common positive/risk reason distribution.

### `candidates.csv`

Stable UTF-8 CSV with score/status columns and safe one-line summary. Prevent spreadsheet formula injection by escaping values beginning with `=`, `+`, `-`, or `@` where relevant.

### `candidates.json`

Versioned export schema without raw browser/session/secrets/protected attributes.

### `discovery-summary.md`

- accounts/segments/runs;
- visited/new/duplicate/changed;
- analyzed/recommendations;
- top candidates;
- common reasons;
- attention required;
- next scheduled runs.

### `manual-invitation-queue.md`

Primary/reserve candidates, draft, source link, review status, and manual checklist.

Store report artifact metadata/hash/retention. Use atomic temp + replace.

## 8. Report command

Complete:

```powershell
FounderScout.exe report --type all
FounderScout.exe report --type top-candidates --top 30
FounderScout.exe report --type invitation-queue
```

It must emit artifact events and a structured summary through Runner. A successful deep-analysis run can enqueue a report occurrence or generate reports as a final phase based on configuration; avoid concurrent writers through a report lease.

## 9. Profile raw-data retention

Add UI/service to apply configured raw snapshot retention:

- derived normalized/evaluation/action records remain;
- delete eligible raw HTML/text/screenshot artifacts safely;
- mark snapshot raw artifact as deleted/expired;
- never delete artifacts needed by active runs or unresolved parser/identity diagnostics without policy;
- audit counts and failures;
- allow manual delete of one candidate's raw data with confirmation;
- do not delete candidate/evaluation history unless a separate explicit delete workflow is built.

## 10. Outcome feedback

Support manual outcomes:

- invitation sent;
- accepted;
- declined;
- no response;
- call scheduled/completed;
- second call;
- passed after call with reason;
- trial project;
- selected.

Use structured reason codes plus optional notes. Do not automatically change scorecard weights. Provide an export/query that later compares initial score/recommendation with outcomes.

## 11. Tests

Cover:

- candidate ranking formula/order/tie breakers;
- filters, pagination, projections, performance indexes;
- detail evidence/history/diffs;
- hostile profile text encoded/no XSS;
- immutable generated draft + editable revision;
- draft validation after edit;
- queue eligibility, capacity, duplicate prevention, reorder, primary/reserve, window rollover;
- manual send/outcomes and legal transitions/audit;
- no network send route/action exists;
- account/segment attention/actions;
- report format set consistency;
- Markdown/HTML encoding;
- CSV formula-injection protection and Unicode;
- JSON schema version;
- atomic report generation and lease;
- raw retention without breaking derived data;
- artifact path safety;
- 10,000 seeded candidate list query remains paginated and reasonably bounded;
- UI antiforgery and validation.

## Constraints

- No automatic invitation/message sending.
- No LinkedIn/email enrichment or external research in V1.
- No protected attributes in ranking/export.
- No raw HTML rendering.
- No SPA rewrite.

## Done when

- Founder Scout has a complete local review workflow from discovery through manual invitation outcome.
- Candidate evidence, risks, questions, and drafts are easy to review.
- Queue is configurable and manual.
- Reports are consistent, safe, and generated through the agent/Runner.
- Browser session health and discovery yield are visible.
- Retention and outcome tracking work.
- All validation passes.

## Required manual smoke validation

Using synthetic fixture/evaluation data:

1. open Founder Scout dashboard and candidate list;
2. filter/sort/paginate;
3. review one candidate with changed snapshots and multiple evaluations;
4. copy/review/edit a draft;
5. add/reorder primary and reserve queues;
6. mark one manually sent/accepted and one passed;
7. generate all report formats and compare row set/order;
8. test hostile text encoding and CSV safety;
9. run raw-data retention on expired synthetic artifacts;
10. confirm no send action/network call exists.

## Final report

Include page map, ranking/query design, queue rules, report schemas/paths, retention behavior, outcome states, smoke results, and deferred enrichment. End with:

```text
Next prompt: prompts/14-platform-audit-summary-notifications.md
```


---

# Stage 14 — Platform Audit, Summaries, Notifications, Retention, and Health

Paste this entire prompt into Codex from the repository root after Stage 13 passes.

---

You are implementing **Stage 14: Operational Audit Completion, Deterministic Summaries, Attention/Notification Pipeline, Retention, and Platform Health**.

Read all repository guidance, central audit/run data, Host tray/UI, Founder Scout metrics, Wake Remote metrics, and this prompt. Create/update `docs/exec-plans/stage-14-platform-audit-summary-notifications.md`.

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

Complete the platform's operational visibility. Implement deterministic per-run and daily summaries, durable attention items/local notifications, notification deduplication/throttling, tray delivery, improved health/status pages, abandoned-run and schedule attention detection, retention cleanup, log correlation, and safe diagnostics export. Do not add external email/SMS/push delivery in V1.

## 1. Durable attention and notification model

Add central entities/migration as needed:

### AttentionItem

- ID;
- category;
- severity: Info, Warning, Error, Critical;
- title;
- bounded safe message;
- agent ID/run/occurrence/candidate/account reference IDs as strings/Guids;
- deduplication key;
- status: Active, Acknowledged, Resolved, Suppressed;
- first/last observed UTC;
- occurrence count;
- acknowledged/resolved UTC/by;
- data JSON redacted;
- expires UTC optional.

### LocalNotification

- ID;
- attention item ID optional;
- title/message;
- severity;
- created UTC;
- not-before UTC;
- delivered UTC;
- delivery attempts;
- status;
- deduplication/throttle key.

Do not use a database notification row for high-frequency progress. This is for actionable/summary notifications.

## 2. Attention detectors

Implement idempotent detectors for:

- agent run failed/timed out/abandoned;
- repeated agent failure threshold;
- agent authentication required;
- access denied/throttled/challenge detected/parser failure;
- missing/invalid configuration or secret;
- next wake task reconciliation failure;
- wake test failed or late beyond threshold;
- Wake Remote network/provider not ready;
- stale scheduler/reconciliation/host health heartbeat;
- low disk space;
- database migration/backup/retention failure;
- Founder Scout new Strong Connect candidate;
- Founder Scout invitation draft NeedsReview;
- primary queue ready count reaches configured threshold;
- overdue manually tracked follow-up optional.

Each detector must use a stable dedupe key, update the same active item, resolve it when the condition clears, and avoid leaking profile raw text or secrets.

## 3. Run summary service

Implement deterministic `IRunSummaryService` that builds a standard summary even if an agent did not emit one.

Common fields:

- agent/run/trigger/status;
- due/start/end/duration;
- command;
- key metrics;
- warning/error counts;
- artifact links;
- terminal reason;
- attention items created/resolved;
- next scheduled occurrence.

Agent-specific adapters:

### Founder Scout

- viewed/new/duplicate/changed;
- parsed/screened/deep analyzed;
- recommendation counts;
- invitation drafts ready/needs review;
- top bounded candidate list;
- account/segment status;
- next discovery/analysis occurrence.

### Wake Remote

- scheduled/actual start and delay;
- network readiness/time;
- remote provider readiness;
- keep-awake duration;
- availability end;
- next wake.

Persist `SummaryText` and versioned `SummaryJson`. A summary can be regenerated from events/metrics with an explicit version/revision without changing historical raw events.

## 4. Daily summary

Implement `IDailySummaryService` and a durable `DailySummary` entity or report artifact.

For a configured local date/time zone, aggregate:

```text
Agent runs total/success/failed/timeout/cancelled
Wake sessions and readiness
Founder profiles visited/new/analyzed
Strong/exploratory/manual-review candidates
Invitation drafts and manually sent/accepted/calls
Active attention items
Resolved attention items
Next scheduled activity
Backup/retention/health status
```

Output:

- summary JSON;
- readable Markdown/HTML/text artifact;
- one local notification when ready, subject to settings;
- link from dashboard/tray.

Run once daily through the normal schedule/Runner path or as a platform internal occurrence with full audit. Do not require AI to generate operational summaries.

## 5. Tray notification delivery

Implement bounded local delivery through the existing tray app:

- poll/subscribe to pending `LocalNotification` rows;
- show Windows tray balloon/toast mechanism available to the app without adding heavy packaging requirements;
- severity-specific icon/title, but include textual severity;
- click opens a validated local dashboard route for the related item;
- mark delivered only after delivery attempt accepted by the local API;
- bounded retry;
- quiet hours configuration;
- deduplicate repeated items;
- throttle storms globally and per key;
- tray unavailable means notification remains pending or expires according to policy;
- no arbitrary external URL in click targets.

Default notify:

- failed run;
- authentication/challenge attention;
- failed wake test/remote readiness;
- new Strong Connect candidates as a grouped notification, not one per candidate;
- daily summary ready.

## 6. Dashboard and health enhancements

Add:

- active attention list with severity, age, owner/reference, acknowledge/resolve actions;
- system health timeline/status;
- scheduler last success and lag;
- wake reconciler last success/task fingerprint;
- runner/agent executable health;
- DB pragmas/migration status;
- disk space;
- last backup/retention run placeholders or actual when available;
- notification status/quiet hours;
- daily summary history.

Acknowledgment does not resolve the underlying detector condition. Resolution is automatic when condition clears or explicit when the item is user-action-only.

## 7. Correlation and logs

Ensure structured scopes/properties include when known:

```text
CorrelationId
AgentId
OccurrenceId
AgentRunId
CandidateId
BrowserAccountId
DiscoverySegmentId
ConfigurationRevisionId
```

Implement a local log query/download view only if it can be bounded and safe. Prefer diagnostics export over a full log-search engine.

Log policy:

- rolling files;
- configurable level/retention;
- redaction middleware/enricher;
- no cookie/session/API key/raw profile content by default;
- stack traces allowed in local files but encoded/redacted in UI.

## 8. Retention service

Implement scheduled/idempotent cleanup for:

- logs;
- central run diagnostic artifacts;
- Founder Scout error artifacts;
- raw profile artifacts according to Stage 13 rules;
- temporary config/XML files;
- delivered/expired notifications;
- resolved attention items after retention period;
- old report exports;
- stale checkpoints when safe;
- database event rows only under explicit, conservative policy.

Requirements:

- never delete active-run files;
- use allowed-root/path validation;
- audit counts/bytes/failures;
- dry-run mode;
- cancellation;
- partial failure isolation;
- do not delete browser profile directories;
- do not delete secret store;
- do not delete immutable configuration revisions unless a future archival policy is explicitly designed.

## 9. Diagnostics export

Create a ZIP export under artifacts containing only selected safe content:

- application/agent versions;
- bootstrap settings redacted;
- active configuration schema/field names and redacted values;
- schedule summaries;
- latest relevant audit/attention/run summaries;
- power diagnostics;
- migration/DB metadata;
- bounded redacted logs;
- no secrets;
- no cookies/browser profiles;
- no raw founder profiles or screenshots by default;
- no invitation text unless explicitly included through a checkbox/config and clearly labeled.

Generate a manifest with file hashes and exclusions. Enforce size cap.

## 10. Tests

Cover:

- each detector create/update/resolve/dedupe behavior;
- notification grouping/throttling/quiet hours/retry/expiry;
- safe local route validation;
- standard run summary fallback and agent-specific summaries;
- daily aggregation across local date/time-zone boundaries and DST;
- top candidate list bounded and safe;
- no raw profile/secret in summary/notification;
- attention acknowledge versus resolve semantics;
- retention dry run/execute, active-run protection, path safety, partial failures;
- diagnostics ZIP contents, hashes, exclusions, size cap;
- structured correlation properties through representative flows;
- dashboard pages and antiforgery actions;
- large event/attention sets remain paginated.

## Constraints

- No email/SMS/push/cloud notification provider.
- No LLM required for summaries.
- No browser/session content in diagnostics.
- No destructive audit editing.
- No automatic invitation sending.

## Done when

- Important failures and opportunities become deduplicated attention items and local tray notifications.
- Every run has a deterministic summary.
- Daily summaries are generated and visible.
- Retention is safe, auditable, and dry-runnable.
- Diagnostics export is useful without exposing sensitive data.
- Health/dashboard show operational truth.
- All validation passes.

## Required smoke validation

Generate synthetic/fixture conditions for:

1. failed run;
2. authentication-required account;
3. failed wake readiness;
4. five Strong Connect candidates;
5. one draft NeedsReview;
6. low disk warning via fake provider;
7. daily summary;
8. retention dry-run and execution;
9. diagnostics export.

Verify grouped tray notifications, attention lifecycle, summary artifacts, and ZIP exclusions.

## Final report

Include attention categories, dedupe/throttle rules, summary schemas, retention table, diagnostics exclusions, smoke results, and deferred external notifications. End with:

```text
Next prompt: prompts/15-packaging-installation-operations.md
```


---

# Stage 15 — Packaging, Installation, Startup, Backup, Restore, and Operations

Paste this entire prompt into Codex from the repository root after Stage 14 passes.

---

You are implementing **Stage 15: Windows Publishing, Installation/Uninstallation/Repair Scripts, At-Logon Startup, Playwright Browser Bootstrap, Database Backup/Restore, and Operator Runbooks**.

Read all repository guidance, current runtime paths, wake bridge, Host/Runner/agents, and this prompt. Create/update `docs/exec-plans/stage-15-packaging-installation-operations.md`.

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

Produce a repeatable V1 Windows deployment under the current user's local application data directory. Publish self-contained win-x64 executables, install agents and configuration, create at-logon startup and managed wake task integration, initialize/migrate databases, install Playwright Chromium when requested, create safe backups/restores, and provide repair/uninstall/smoke scripts and runbooks.

Do not require Azure, containers, a Windows Service, an MSI, or administrator privileges for normal per-user installation unless a specific Windows operation proves it is required. Detect and report permission limitations instead of bypassing them.

## 1. Publish layout

Create a reproducible Release publish process for:

```text
HomeBusinessAssistant.Host.exe
HomeBusinessAssistant.Runner.exe
FounderScout.exe
WakeRemote.exe
required assemblies/runtime/native files
agent manifests/config schemas/prompts
local static assets
migration assemblies
Playwright support files/scripts
```

Target:

```text
win-x64
self-contained
single-file only if proven compatible with Razor Pages content, EF migrations,
Playwright, and agent file layout; otherwise use normal self-contained folders
ReadyToRun only if measured benefit and no deployment problems
trim disabled unless comprehensive compatibility testing proves it safe
```

Prefer reliability over aggressive size optimization. Record publish decisions in an ADR.

Expected installed layout:

```text
%LOCALAPPDATA%\HomeBusinessAssistant\
├── app\
│   ├── HomeBusinessAssistant.Host.exe
│   ├── HomeBusinessAssistant.Runner.exe
│   ├── ...
│   └── agents\
│       ├── FounderScout\
│       │   ├── FounderScout.exe
│       │   ├── manifest.json
│       │   ├── configuration.schema.json
│       │   ├── prompts\
│       │   └── ...
│       └── WakeRemote\
├── config\appsettings.json
└── data\
```

Do not publish secrets, browser profiles, real databases, captured profiles, logs, or developer settings.

## 2. Version information

Embed/display:

- semantic product version;
- Git commit hash when available;
- build UTC;
- .NET runtime target;
- agent manifest/version;
- DB migration version.

Host, Runner, agents, audit, diagnostics export, and About page must report consistent versions.

## 3. PowerShell installer

Create `scripts/install.ps1` with parameters such as:

```powershell
-SourceDirectory
-InstallDirectory
-DataDirectory
-Port
-InstallPlaywrightBrowser
-StartAfterInstall
-Force
```

Behavior:

1. require PowerShell version suitable for Windows 11;
2. resolve/validate paths;
3. stop an existing Host gracefully or refuse when active runs exist unless forced with clear confirmation;
4. create timestamped pre-install database/config backup if upgrading;
5. copy to a staging directory;
6. validate executable hashes/manifests;
7. atomically replace/install app files where practical;
8. preserve data, browser profiles, secret store, and user config;
9. write/update bootstrap `appsettings.json` without overwriting mutable DB config;
10. run Runner/Host migration/diagnose command;
11. seed/update built-in manifests;
12. optionally install Playwright Chromium using the published supported script;
13. create/update per-user `HostAtLogon` scheduled task;
14. start Host if requested;
15. run a health smoke check;
16. write install result/log without secrets.

Use argument arrays/quoted PowerShell APIs safely. Do not use `Invoke-Expression`.

## 4. Host-at-logon task

Create managed task:

```text
\HomeBusinessAssistant\HostAtLogon
```

Requirements:

- per-user interactive logon trigger;
- starts `HomeBusinessAssistant.Host.exe` with bootstrap config path;
- no duplicate instances due to Host mutex;
- delayed start optional/configurable;
- task description indicates application-managed;
- update/remove idempotently;
- do not configure WakeToRun for HostAtLogon;
- do not store a plaintext password;
- validate task after creation.

Use the same Task Scheduler XML/process infrastructure where practical.

## 5. Uninstall and repair

Create:

### `scripts/uninstall.ps1`

- stop Host/active runs safely;
- remove HostAtLogon and NextWake managed tasks;
- remove application binaries/config optionally;
- preserve data/secrets/browser profiles by default;
- offer explicit separate `-RemoveData` confirmation path;
- never delete outside validated install/data roots;
- produce a clear summary.

### `scripts/repair.ps1`

- verify app/agent files and hashes/manifests;
- verify/migrate databases;
- verify directory permissions/writability;
- verify/repair HostAtLogon;
- reconcile NextWake;
- check Playwright browser installation;
- check Runner/agent executable paths;
- preserve user data/config;
- produce diagnostics/attention results.

## 6. Database backup

Implement application service and CLI/UI integration for safe SQLite backups of:

```text
assistant.db
founders.db
```

Requirements:

- use SQLite online backup API, `VACUUM INTO`, or another proven SQLite-safe method while DB may be open; document choice;
- create a consistent timestamped backup set with manifest;
- back up central and Founder Scout databases separately but under one backup set ID;
- verify each backup opens and contains expected migration history/tables;
- hash files;
- write temp then promote only after validation;
- do not include browser profiles or secret-store files in ordinary backups;
- optionally include redacted bootstrap/config export and manifests;
- default retention: 7 daily, 4 weekly, configurable;
- serialize backup through a lease;
- audit result and bytes/duration.

Do not simply copy active SQLite files without proving consistency.

## 7. Restore

Implement a guarded restore command, initially CLI and optionally UI link:

```powershell
HomeBusinessAssistant.Runner.exe restore-backup --backup-set <id>
```

Restore rules:

- refuse while Host/agents/runs use the databases unless invoked through controlled maintenance mode;
- verify manifest/hashes/schema compatibility;
- create pre-restore backup;
- restore to staging paths and validate;
- atomically replace DBs where possible;
- run migrations only after opening restored version and confirming compatibility;
- reconcile stale runs/leases/tasks after restore;
- preserve secrets/browser profiles;
- detailed audit and rollback on failure.

Destructive restore requires explicit confirmation/token when invoked interactively. Automated tests use temp directories.

## 8. Scheduled maintenance

Seed optional platform schedules for:

- daily backup;
- daily summary;
- retention cleanup;
- periodic diagnostics/health check.

Use normal durable occurrences/Runner/audit. Do not directly run maintenance from Task Scheduler except through Runner if a wake occurrence requires it.

## 9. Upgrade safety

Installer upgrade flow must:

- reject downgrade by default;
- ensure active agents are stopped or allowed to complete;
- back up before DB migration;
- apply binary update before/with compatible migration strategy;
- recover from failed copy/migration by restoring prior app/DB state when possible;
- preserve config revisions and schedules;
- update manifests without overriding user configurations;
- re-register tasks with new paths;
- show version/health after upgrade.

Document current V1 update model. Automatic internet updates are out of scope.

## 10. Operational documentation

Create/update:

- `docs/installation.md`
- `docs/operations.md`
- `docs/backup-restore.md`
- `docs/wake-remote-setup.md`
- `docs/founder-scout-setup.md`
- `docs/troubleshooting.md`
- `docs/security-privacy.md`

Include:

- install/upgrade/uninstall/repair;
- manual Founder Scout authentication;
- Playwright install;
- schedule strategy: discovery fixed delay, analysis event/recovery, no internal sleep;
- wake test and powercfg diagnostics;
- Chrome Remote Desktop/RDP readiness notes without exposing RDP publicly;
- log/artifact/database paths;
- secret recovery implications;
- backup/restore;
- common attention states;
- how to disable all automation quickly;
- how to collect safe diagnostics.

## 11. Smoke-test script

Create `scripts/smoke-test.ps1` that can validate a published/installed application using synthetic data:

- versions/files/manifests;
- Host starts and health endpoints return ready;
- second-instance behavior;
- DB migrations/pragmas;
- fake agent run through Runner;
- Wake Remote short diagnostic mode;
- Founder Scout fixture import/screen/fake evaluation/report;
- schedule and wake XML generation without requiring real sleep;
- backup + validation;
- diagnostics export;
- no orphan processes;
- returns nonzero on failure.

Do not touch live browser accounts or AI by default.

## 12. Tests

Cover publish/install helpers and scripts where practical:

- clean install to temp path;
- upgrade preserves data/config;
- downgrade rejection;
- task XML/action paths with spaces;
- uninstall preserves data by default;
- remove-data path root safety;
- repair idempotency;
- online backup under active reads/writes;
- backup validation/hash/retention;
- interrupted backup not promoted;
- restore success, compatibility rejection, rollback;
- migration failure recovery using controlled test migration/setup;
- smoke script in CI Windows environment if feasible.

## Constraints

- No MSI/WiX requirement in V1.
- No Windows Service.
- No Azure deployment.
- No automatic internet updater.
- No plaintext scheduled-task passwords.
- No ordinary backup of browser profiles or secret store.

## Done when

- Release publish artifacts are deterministic and installable per user.
- Host starts at logon through managed Task Scheduler task.
- Install/upgrade/repair/uninstall are idempotent and safe.
- SQLite backup/restore is validated and auditable.
- Playwright browser bootstrap is documented/optional.
- Operations/security/troubleshooting docs are usable.
- Published smoke test passes.

## Required manual smoke validation

On a clean temporary install path:

1. publish Release win-x64;
2. install per-user;
3. verify HostAtLogon task and tray/UI health;
4. execute smoke script;
5. create/validate backup;
6. perform a controlled restore with synthetic data;
7. run repair;
8. upgrade same version/no-op then a test version;
9. uninstall preserving data;
10. reinstall and verify preserved data;
11. optionally remove synthetic data with explicit flag.

## Final report

Include publish settings/layout/size, installer behavior, task registration, backup technology, restore safeguards, smoke output, and remaining code-signing/MSI concerns. End with:

```text
Next prompt: prompts/16-hardening-e2e-release.md
```


---

# Stage 16 — Hardening, End-to-End Verification, and V1 Release

Paste this entire prompt into Codex from the repository root after Stage 15 passes.

---

You are implementing **Stage 16: Security/Privacy Hardening, Failure Injection, Concurrency and Recovery Verification, Performance Validation, End-to-End Tests, Documentation Reconciliation, and V1 Release Candidate**.

Read every repository instruction, ADR, ExecPlan, architecture/configuration document, the entire `docs/acceptance-test-matrix.md`, and current code/tests. Create/update `docs/exec-plans/stage-16-hardening-e2e-release.md`.

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

Bring the complete system to a releasable V1 state. Close gaps against the acceptance matrix, eliminate placeholder behavior, verify process/database/power cleanup, run end-to-end synthetic workflows, harden local security and privacy boundaries, measure important query/run behavior, validate packaging on a clean path, and produce a release checklist with honest known limitations.

Do not add new product scope unless required to satisfy documented V1 behavior.

## 1. Repository-wide gap analysis

Before coding:

- map every acceptance-matrix item to an existing automated test, manual test, or missing implementation;
- inspect all TODO/FIXME/NotImplemented/placeholder responses;
- inspect warnings/analyzer suppressions;
- inspect migration history and upgrade path;
- inspect UI routes/actions for direct agent launch or unsafe rendering;
- inspect logs/audit/artifacts for secret/raw data leakage;
- inspect browser stop/failover behavior;
- inspect invitation code for any network send path;
- inspect power-request disposal and child-process cleanup;
- inspect schedule/occurrence/run state transitions and idempotency;
- inspect file path normalization/traversal/reparse handling;
- inspect test flakiness and real-time sleeps.

Record findings and priorities in the ExecPlan. Then fix them.

## 2. End-to-end synthetic environment

Build a deterministic E2E harness using:

- temporary install/data roots with spaces and Unicode;
- real SQLite databases/migrations;
- actual Runner and agent child processes;
- fake Test Agent;
- local Startup School-like fixture HTTP server/pages;
- dedicated temporary Playwright profile;
- fake AI provider server/client with scripted valid/invalid/transient responses;
- fake Windows wake/task/power adapters for automated tests;
- fake `TimeProvider` where in-process, and short bounded real durations only where process boundaries require it.

No normal CI test may access real Startup School, a live AI endpoint, remote desktop provider, or sleep the workstation.

## 3. Required end-to-end scenarios

Automate or explicitly document manual-only parts of all acceptance-matrix scenarios. At minimum automate:

### Platform lifecycle

- fresh bootstrap/migrations/seeding;
- Host starts loopback/tray-service composition where UI automation permits;
- second-instance signal;
- schedule creation/reconciliation;
- Runner launch;
- event/audit/summary/notification;
- restart recovery;
- backup/restore;
- retention.

### Founder Scout happy path

```text
fixture discovery of >= 40 profile pages
-> new/duplicate/changed capture
-> parser/normalizer/dedupe/redaction
-> fast screening
-> fake structured AI deep analysis
-> C# scores/risks/confidence/priority
-> short/detailed draft validation
-> candidate UI query models
-> primary/reserve queue
-> all report formats
-> manual sent/outcome action
-> daily summary/notification
```

Verify counts and hashes across central and Founder Scout databases.

### Founder Scout failure paths

- login redirect;
- access denied;
- throttle;
- challenge;
- parser layout change;
- browser crash;
- profile lock;
- AI auth/config error;
- AI transient retries;
- invalid schema/evidence/draft;
- cancellation/timeout;
- unchanged cache hit;
- no automatic account failover;
- no invitation send.

### Wake Remote paths

- generated wake task semantics through fake bridge;
- Runner starts Wake Remote short window;
- network ready/not ready;
- provider required/optional;
- cancellation/timeout;
- nested power handles;
- release in all paths;
- next wake reconciliation.

## 4. Concurrency and crash recovery

Add stress/failure-injection tests for:

- simultaneous schedule reconcilers;
- simultaneous Runner claim attempts;
- Host and Task Scheduler bridge launch race;
- two Founder Scout discovery attempts for one account;
- two analysis workers claiming candidates;
- report/backup/retention lease contention;
- SQLite busy/lock transient handling;
- Runner crash after child launch;
- agent crash after domain commit before summary;
- Host crash during config save/schedule reconciliation;
- interrupted artifact/temp file write;
- stale lease fencing;
- PID reuse-safe recovery;
- backup during active read/write;
- restore rollback.

Use bounded loops and deterministic assertions. Ensure no flaky unbounded timing tests.

## 5. Security and privacy review

Create/update `docs/security-privacy.md` with threat model:

Assets:

- API secrets;
- browser profiles/session state;
- founder raw profiles/evaluations/drafts;
- remote availability configuration;
- audit/logs/backups.

Trust boundaries:

- current Windows user;
- local Host/UI;
- Runner/agent processes;
- browser/external website;
- AI provider;
- file system/backups.

Verify/fix:

- loopback-only binding;
- antiforgery;
- output encoding/no raw HTML;
- argument injection prevention;
- allowed-root path handling, symlinks/reparse points where practical;
- manifest/config/schema validation;
- DPAPI secret handling and zero/log behavior;
- no secret in SQLite/config revisions/audit/logs/diagnostics;
- browser profile excluded from backups/exports;
- raw profile retention;
- no protected attributes in evaluator/ranking/report;
- no auto invitation sending;
- no browser stealth/CAPTCHA/proxy/account failover after enforcement signals;
- diagnostic artifact bounds/redaction;
- CSV formula injection;
- zip-slip prevention for future restore/diagnostic handling;
- package dependency vulnerability/deprecation review using available .NET tooling.

Document residual risk: code running as the same Windows user can access local files/secrets/session profiles.

## 6. Performance and scale checks

Measure and optimize only proven bottlenecks:

- candidate list/filter/sort with 10,000 candidates and multiple evaluations;
- run/event queries with 100,000 events;
- scheduler reconciliation with representative schedules;
- batch analysis queue claim with 20/100 items;
- report generation with top 30 and full CSV;
- startup/migration/readiness;
- backup duration/size;
- memory/process cleanup after repeated short browser/agent runs.

Add indexes/projections where query plans or timings show need. Keep tests tolerant of CI variability; enforce structural bounds and broad performance thresholds, not fragile milliseconds.

## 7. Reliability cleanup

- Remove or resolve all V1 placeholders.
- Ensure every background loop exposes health and cancellation.
- Ensure all process, browser, file, DB, timer, power, tray, and IPC resources are disposed.
- Ensure every terminal run/occurrence path is idempotent.
- Ensure summaries/attention remain available after agent failure.
- Ensure errors are actionable and not swallowed.
- Ensure no infinite retries or tight loops.
- Ensure fixed-delay schedules wait after completion rather than internal sleeping.
- Ensure external error states pause/suspend appropriately and require user action.

## 8. UI/user acceptance review

Verify all pages at typical and narrow remote-desktop window sizes:

- navigation and empty states;
- keyboard/focus/labels/error summaries;
- running/failure/attention states;
- configuration revisions/secrets;
- schedules/preview;
- runs/audit/artifacts;
- Wake status/test instructions;
- Founder candidate/detail/queue/reports;
- destructive confirmations;
- no secret/raw HTML exposure.

Fix obvious usability issues without introducing a frontend framework.

## 9. Documentation reconciliation

Update all docs to actual implementation. Remove stale planned behavior. Include:

- architecture diagram/process boundaries;
- data models and migrations;
- command reference;
- agent protocol;
- schedule semantics and defaults;
- browser authentication/discovery behavior and stop conditions;
- AI provider setup and synthetic/live test distinction;
- scoring formulas/versions;
- invitation draft/manual workflow;
- wake setup/test/limitations;
- install/upgrade/backup/restore/uninstall;
- security/privacy;
- troubleshooting/attention codes;
- release/rollback.

Generate `docs/v1-release-checklist.md`.

## 10. Release build

Create a versioned V1 release candidate artifact:

- clean Release build/test/format;
- self-contained win-x64 publish;
- manifest/checksum file;
- install package directory or ZIP;
- installer/repair/uninstall/smoke scripts;
- release notes;
- known limitations;
- database migration list;
- dependency/license inventory where feasible.

Do not publish real data or secrets.

## 11. Tests and required validation

Run:

```powershell
dotnet --info
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Also run:

- complete E2E synthetic suite;
- architecture tests;
- migration fresh/upgrade/restore tests;
- published install smoke script;
- dependency vulnerability/deprecation checks available in current SDK;
- test filtering/report proving no skipped required tests except documented opt-in live/manual tests.

For each manual-only test, provide exact steps and do not claim success unless executed.

## Constraints

- No new cloud architecture.
- No Windows Service.
- No automatic updater.
- No external notification delivery.
- No automatic invitation sending.
- No anti-detection/evasion browser features.
- No live external tests required for CI/release correctness.

## Done when

- Every acceptance-matrix item has automated evidence or an explicit manual-only reason/procedure.
- Synthetic E2E workflow passes.
- Security/privacy review is documented and critical findings fixed.
- Concurrency/recovery tests pass without leaked processes/files.
- Performance is acceptable for V1 local scale.
- Published clean-install smoke passes.
- V1 release artifact/checksums/release notes/checklist exist.
- Known limitations are honest and bounded.

## Final report

Provide:

1. acceptance coverage totals: automated/manual/deferred;
2. critical fixes made;
3. E2E scenario results;
4. security findings/residual risks;
5. performance measurements;
6. publish/install artifact paths and hashes;
7. manual tests actually executed;
8. known limitations;
9. release/rollback procedure.

End with:

```text
Next optional prompt: prompts/17-new-agent-template.md
```


---

# Stage 17 — New Agent Template and Extension Workflow

Paste this entire prompt into Codex after the V1 release stage when you want a repeatable way to add future agents.

---

You are implementing **Stage 17: Reusable New-Agent Template, Scaffolding Command, Example Agent, and Extension Documentation**.

Read all repository guidance, the final agent protocol, manifest/configuration/run patterns, V1 release code, and this prompt. Create/update `docs/exec-plans/stage-17-new-agent-template.md`.

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

Make future Home Business Assistant agents easy to add without copying Founder Scout internals or modifying central orchestration for every agent. Create a supported template/scaffolder, a small example agent, manifest/config schema conventions, registration/installation hooks, tests, and a developer guide.

The architecture remains external-process agents communicating through the Agent SDK. Do not introduce in-process dynamic plugin loading.

## 1. Agent template

Create either:

- a `dotnet new` template package, preferred; or
- a repository-local scaffolding command/script if a template package is disproportionate.

Invocation example:

```powershell
dotnet new hba-agent \
  --name InvoiceMonitor \
  --agent-id invoice-monitor \
  --display-name "Invoice Monitor"
```

Generated structure:

```text
agents/InvoiceMonitor/
├── InvoiceMonitor.Application/
├── InvoiceMonitor.Infrastructure/
├── InvoiceMonitor.Agent/
├── InvoiceMonitor.Tests/
├── manifest.json
├── configuration.schema.json
├── README.md
└── AGENTS.md optional agent-specific instructions
```

Only generate a Domain project when the agent's model warrants it; document the option.

## 2. Generated agent behavior

The example/template agent must:

- reference `HomeBusinessAssistant.AgentSdk`;
- parse standard execution context;
- support `run`, `diagnose`, and `protocol-demo`;
- load/validate versioned typed configuration;
- emit started/progress/metric/artifact/summary/completed JSONL events;
- write human diagnostics to stderr only;
- honor cancellation;
- use assigned data/artifact directories safely;
- return stable exit codes;
- contain no secrets or fake production integrations.

Generate meaningful tests for protocol, config validation, cancellation, artifact path use, and Runner integration.

## 3. Manifest/config schema workflow

Template provides:

- valid manifest matching final schema;
- supported commands/capabilities;
- default timeout/concurrency/session requirements;
- versioning fields;
- JSON Schema or typed schema metadata for configuration;
- sample config with secret references;
- instructions for implementing a platform UI adapter when generic rendering is insufficient.

Add tooling/test that validates every installed agent manifest/config schema in the repository.

## 4. Agent registration and packaging

Implement a generic installer/registry scan:

- scan only immediate validated directories under configured AgentDirectory;
- require manifest and executable;
- validate paths/hashes/versions;
- upsert `AgentDefinition` safely;
- preserve user configuration/schedules;
- disable/report invalid agents rather than executing them;
- audit install/update/remove state;
- packaging automatically includes opted-in agent projects/manifests through explicit build configuration, not arbitrary directory execution.

Do not auto-run newly installed agents.

## 5. Generic configuration UI

If not already implemented, add a limited JSON-schema-driven form renderer for safe primitive V1 fields:

- string;
- integer/number;
- boolean;
- enum;
- arrays of simple values;
- secret-reference field type;
- descriptions/defaults/min/max/pattern.

Requirements:

- server-side validation remains authoritative;
- no arbitrary HTML/scripts from schema;
- unsupported complex schema shows a clear message and requires a custom typed adapter;
- secrets use `ISecretStore` and are never echoed;
- configuration revision/audit behavior remains unchanged.

Do not replace the strongly typed Founder Scout/Wake UI if it is better.

## 6. Example `SampleBusinessAgent`

Generate and install a harmless example agent, such as a local folder/report watcher, that demonstrates:

- schedule;
- config;
- progress/metrics;
- one artifact;
- summary;
- failure/attention mode;
- cancellation;
- no external credentials.

Mark it disabled by default and exclude from production install unless a sample flag is set.

## 7. Documentation

Create `docs/adding-an-agent.md` covering:

```text
scaffold
project boundaries
manifest
configuration and secrets
commands
protocol events
data/artifacts
scheduling/wake/concurrency
runner behavior
audit/metrics/summary/attention
UI extension
tests
packaging/versioning/upgrade
security checklist
```

Add a concise checklist and a sample end-to-end command sequence.

## 8. Tests

Cover:

- template generation into a temporary folder;
- generated solution/projects build and tests pass;
- generated manifest/config schema validate;
- generated agent runs through real Runner/fake occurrence;
- agent scan accepts valid/rejects invalid/traversal/duplicate IDs;
- upgrade preserves config/schedules;
- generic config renderer encoding/validation/secret behavior;
- sample agent schedule/run/artifact/summary/cancellation;
- production packaging excludes disabled sample by default.

## Constraints

- No in-process assembly loading/plugin execution.
- No arbitrary script agents.
- No auto-run after install.
- No central database tables specific to a future agent's domain.
- No reduction of Runner validation/security.

## Done when

- A new external-process agent can be scaffolded, built, registered, configured, scheduled, run, audited, and packaged with minimal central changes.
- Generated code follows repository standards and has tests.
- Invalid agents cannot be executed.
- Documentation is complete.
- All validation passes.

## Required smoke validation

1. generate a temporary `InvoiceMonitor` or equivalent agent;
2. add it to a temporary solution or build it independently as designed;
3. install/register it under a temp AgentDirectory;
4. configure and schedule it;
5. run through Runner;
6. inspect events/metrics/artifact/summary/audit;
7. uninstall/disable it without harming other agents;
8. run full repository tests.

## Final report

Include scaffolding command, generated tree, generic versus custom UI decision, registration security, smoke results, and the exact documented procedure for the next real agent.
