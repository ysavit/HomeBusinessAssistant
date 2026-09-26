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

