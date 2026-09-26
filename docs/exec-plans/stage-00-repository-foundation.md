# Stage 00 ExecPlan — Repository Foundation

## Purpose and user-visible outcome

Deliver a buildable .NET 10 repository foundation for Home Business Assistant. A user can run the three placeholder console executables, start the loopback-only Host, open its Stage 00 page, and receive a healthy response from `/health`. No business features are implemented in this stage.

## Current repository state

- Repository root: `C:\Projects\HomeBusinessAssistant\sources`.
- Git branch: `main`; the supplied prompt pack and Stage 00 implementation remain untracked because no commit was requested.
- Preflight found documentation and prompts only. The completed solution now contains 14 production projects and 10 test projects, plus build controls and Windows CI.
- Windows `win-x64` is verified by `dotnet --info`; installed SDKs are `9.0.306` and `10.0.400`.
- `docs/PROJECT_STATE.md` records the completed implementation and final validation evidence.
- No accepted ADRs exist, and no baseline architecture deviation is currently needed.

## Scope and non-goals

### In scope

- Complete prescribed solution/project tree and dependency graph.
- Repository-wide build, analyzer, package, formatting, ignore, and CI controls.
- Minimal marker types, testable console commands, loopback Host, Razor Page, and health endpoint.
- A meaningful test in every required test project, including dependency-boundary enforcement.
- Restore, Release build, full tests, format verification, CLI smoke tests, and Host health smoke test.
- README, development guide, ExecPlan, and verified project-state handoff.

### Non-goals

- Persistence, migrations, scheduling, process supervision, JSONL protocol contracts, Windows Task Scheduler or power APIs.
- Tray behavior, browser automation, AI integration, Founder Scout domain behavior, or Wake & Remote operational behavior.
- Packaging, publishing, deployment, commits, branches, tags, or pull requests.

## Design and data flow

The project graph follows the Stage 00 prompt exactly:

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

Libraries expose sealed namespace marker types. Console projects expose small command classes so their exit behavior can be tested without spawning processes. The Host exposes a composition method used by `Program` and integration tests; Kestrel is configured through the supplied URL and Stage 00 smoke testing explicitly uses a `127.0.0.1` URL.

No persistent data or secrets are created. The only runtime state during validation is generated build/test output and a temporary Host process, all covered by Git ignore rules.

## Milestones

1. [x] Preflight repository, Git, operating system, SDK, documentation, and applicable ADR state.
2. [x] Scaffold the solution and all production/test projects with exact target frameworks.
3. [x] Apply the permitted project-reference graph and repository engineering controls.
4. [x] Replace template code with marker types, CLI handlers, Host endpoints/page, and meaningful tests.
5. [x] Restore, build, test, format, and repair all Stage 00 failures.
6. [x] Smoke-test all executables and the loopback Host, inspect final status, and complete handoff documentation.

## Detailed steps

1. Pin SDK `10.0.400`; enable central package management, nullable analysis, C# 14, deterministic builds, analyzers, and warnings-as-errors.
2. Generate the seven platform projects, seven agent projects, and ten test projects specified by the prompt. Use `net10.0` for pure projects and `net10.0-windows` for Windows integration/executables and their tests.
3. Add only project references required by the declared graph. Add nUnit, NUnit3TestAdapter, Microsoft.NET.Test.Sdk, Moq, and coverlet collector to tests through central package versions.
4. Implement public marker types, public testable CLI command handlers, and a Host application composition boundary. Add a minimal Razor Page plus `/health`.
5. Add one meaningful smoke/boundary test per test project. Place reflection-based forbidden-reference assertions in the end-to-end boundary suite.
6. Add `.editorconfig`, `.gitignore`, GitHub Actions, repository README, and development guide.
7. Execute the required validation and smoke checks. Update this plan and `docs/PROJECT_STATE.md` only with observed results.

## Progress

- 2026-08-29: Read all mandatory Stage 00 context and inspected the repository.
- 2026-08-29: Verified documentation-only initial state, branch `main`, Windows `win-x64`, and SDK `10.0.400`.
- 2026-08-29: Created the solution, 24 projects, exact project references, repository engineering controls, and Windows CI.
- 2026-08-29: Implemented markers, three testable CLI placeholders, loopback policy, Razor Page, health endpoint, and 13 tests across all ten test projects.
- 2026-08-29: Repaired analyzer, test naming, nullable graph-key, and Host test imports without weakening warnings-as-errors.
- 2026-08-29: Completed restore, exact Release build, tests, format verification, three CLI smokes, Host health/page smoke, Ctrl+C shutdown, and sensitive-artifact audit.
- 2026-08-29: Updated README, development guide, this ExecPlan, and `docs/PROJECT_STATE.md`; Stage 00 is validated.

## Decisions

- Pin the newest installed .NET 10 feature-band SDK (`10.0.400`) with `rollForward: latestPatch` so CI and local builds stay on that feature band while accepting servicing releases.
- Keep Windows-specific platform infrastructure, Host, Runner, agent executables, and matching tests on `net10.0-windows`; keep pure domain/application/SDK layers and their tests on `net10.0`.
- Test CLI output via explicit command classes, avoiding fragile child-process tests at the unit-test layer while retaining end-to-end executable smoke checks.
- Use the ASP.NET Core shared framework for Host behavior and a direct application composition method for integration testing; no redundant ASP.NET package is added.
- Enforce one HTTP origin bound specifically to `127.0.0.1`; environment variables or command-line arguments cannot broaden the Host to LAN interfaces.
- Run the built WinExe assembly through `dotnet <path-to-dll>` for the Stage 00 manual smoke so Ctrl+C remains attached. The normal tray shutdown path is deferred to Stage 07.
- No ADR is required because these choices implement the supplied baseline without changing a durable cross-cutting decision.

## Validation

Required commands:

```powershell
dotnet --info
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Stage-specific checks:

- Run Runner, Founder Scout, and Wake Remote once; capture output and exit code.
- Start Host on a non-conflicting `127.0.0.1` port, call `/health`, record status/body, and terminate gracefully.
- Inspect `git status --short` and the repository for accidental databases, logs, secrets, browser data, reports, screenshots, or published binaries.

Actual results:

- `dotnet --info`: passed; SDK `10.0.400`, runtime `10.0.11`, Windows `win-x64`.
- `dotnet restore`: passed; all 24 projects restored. The sandboxed first attempt could not reach/write normal CLI state and returned no diagnostic output; the required rerun with NuGet access and ignored local caches succeeded.
- `dotnet build HomeBusinessAssistant.sln -c Release --no-restore`: passed; 0 warnings and 0 errors. A sandbox-only parallel-worker attempt returned a nonzero result with 0 warnings/errors; the exact command passed with normal MSBuild process permissions and supersedes that environmental failure.
- `dotnet test HomeBusinessAssistant.sln -c Release --no-build`: passed; 13 passed, 0 failed, 0 skipped across ten test projects.
- `dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore`: passed with exit code 0 and no output/changes.
- Runner smoke: exit 0 and bounded Stage 00/no-occurrence output.
- Founder Scout smoke: exit 0 and bounded Stage 00/deferred-feature output.
- Wake Remote smoke: exit 0 and bounded Stage 00/deferred-feature output.
- Host smoke on `http://127.0.0.1:53181`: `/health` returned HTTP 200 with `Healthy`; `/` returned HTTP 200 with the Stage 00 marker; Ctrl+C logged normal shutdown; no listener remained.
- Final repository audit: no deferred-feature package, generic `Class1`, `Assert.Pass`, TODO placeholder, database, log, browser profile, snapshot, screenshot, report, or secret artifact was found outside ignored caches/build output.

## Recovery and rollback

Stage 00 creates no database, schedule, external account, or machine configuration. A failed scaffold can be repaired file-by-file while preserving prompt-pack documents. Build/test output under `bin/` and `obj/` is disposable and ignored. A failed Host smoke process will be identified by PID and stopped explicitly; no permanent port or service registration occurs.

## Remaining risks and follow-up

- The repository began as an untracked prompt pack, so all source and context files remain untracked until the operator chooses to commit them.
- Host tray lifecycle and single-instance behavior are deferred to Stage 07; the manual Stage 00 shutdown command uses the built DLL through the `dotnet` console host.
- All business contracts and real agent protocol behavior remain deliberately deferred to `prompts/01-agent-sdk-and-core-contracts.md` and later prompts.
