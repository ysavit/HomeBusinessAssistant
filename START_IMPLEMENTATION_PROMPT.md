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
