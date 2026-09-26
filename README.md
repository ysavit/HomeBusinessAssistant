# Home Business Assistant

Home Business Assistant is a Windows-first, local control plane for scheduling, launching, monitoring, auditing, and summarizing independently executable agents. The first planned agents are Founder Scout and Wake & Remote.

Stages 00–20 establish the repository, versioned agent process contract, durable scheduling and supervision, Windows wake/power integration, the Wake Remote and Founder Scout agents, the owner-authenticated tray/loopback Host and management UI, grounded evaluation/review/reporting, operational summaries/notifications, hardening/E2E evidence, a self-contained per-user Windows release candidate, a reusable safe new-agent extension path, durable first-run platform readiness, restart-safe explicit agent selection with generic per-agent configuration, and specialized Founder Scout setup with browser/account/provider readiness.

## Process model

```text
Windows interactive session
        |
        v
HomeBusinessAssistant.Host.exe
  tray + loopback Razor Pages + resilient orchestration loops
        |
        v
HomeBusinessAssistant.Runner.exe
        |
        +----------------------+
        v                      v
FounderScout.exe          WakeRemote.exe
```

The final V1 process responsibilities are described in [docs/architecture.md](docs/architecture.md). The Host periodically reconciles one `\HomeBusinessAssistant\NextWake` Task Scheduler task and dispatches due/manual occurrences only through the central Runner. Ordinary build/test validation never registers a machine task, enables remote access, or changes permanent power policy.

## Project map

```text
src/
  HomeBusinessAssistant.Domain          platform domain boundary
  HomeBusinessAssistant.Application     platform use-case boundary
  HomeBusinessAssistant.AgentSdk        versioned agent protocol boundary
  HomeBusinessAssistant.Infrastructure  EF Core SQLite, artifacts, and platform adapters
  HomeBusinessAssistant.Windows         Windows integration and DPAPI secret boundary
  HomeBusinessAssistant.Host            single-instance tray, loopback Razor Pages, and orchestration host
  HomeBusinessAssistant.Runner          durable occurrence executor, recovery CLI, and development smokes

agents/
  FounderScout/                          domain, application, infrastructure, and agent process
  WakeRemote/                            application, infrastructure, and agent process
  SampleBusinessAgent/                   disabled-by-default local extension example

templates/
  hba-agent/                             repository-local dotnet new agent template

tests/
  ten nUnit test projects covering the implemented platform and agent boundaries
```

The exact permitted project-reference graph is enforced by `HomeBusinessAssistant.EndToEndTests`.

## Prerequisites

- Windows 11 x64 for all Windows runtime and DPAPI checks.
- .NET SDK 10.0.400 or a compatible servicing release selected by `global.json`.
- PowerShell for the documented command examples and explicit Playwright Chromium installation.

No external services, real credentials, Node toolchain, or JavaScript package manager are required. Stage 10 tests use only a loopback synthetic HTML server, but the pinned Playwright Chromium runtime must be installed explicitly after build as documented below. Tests create and delete temporary SQLite databases, browser profiles, and DPAPI-protected test values.

## Build and test

Run from the repository root:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Install/verify the pinned browser revision after the Release build (ordinary runtime commands never download it):

```powershell
pwsh agents/FounderScout/FounderScout.Agent/bin/Release/net10.0-windows/playwright.ps1 install chromium
pwsh agents/FounderScout/FounderScout.Agent/bin/Release/net10.0-windows/playwright.ps1 install --list
```

Run the executable protocol and Host surfaces:

```powershell
dotnet run --project src/HomeBusinessAssistant.Runner --no-build -c Release
dotnet run --project agents/FounderScout/FounderScout.Agent --no-build -c Release
dotnet run --project agents/WakeRemote/WakeRemote.Agent --no-build -c Release
dotnet src/HomeBusinessAssistant.Host/bin/Release/net10.0-windows/HomeBusinessAssistant.Host.dll --urls http://127.0.0.1:5180
```

Run the Stage 02 self-cleaning persistence smoke:

```powershell
dotnet run --project src/HomeBusinessAssistant.Runner --no-build -c Release -- persistence-demo
```

Run the Stage 03 controlled scheduling smoke:

```powershell
dotnet run --project src/HomeBusinessAssistant.Runner --no-build -c Release -- scheduling-demo
```

Runner process commands accept bootstrap path overrides as documented by `--help`:

```powershell
dotnet run --project src/HomeBusinessAssistant.Runner --no-build -c Release -- execute --occurrence-id <guid>
dotnet run --project src/HomeBusinessAssistant.Runner --no-build -c Release -- run-agent --agent-id <id> --command <command>
dotnet run --project src/HomeBusinessAssistant.Runner --no-build -c Release -- recover
dotnet run --project src/HomeBusinessAssistant.Runner --no-build -c Release -- diagnose
dotnet run --project src/HomeBusinessAssistant.Runner --no-build -c Release -- scan-agents
dotnet run --project src/HomeBusinessAssistant.Runner --no-build -c Release -- reconcile-wake
dotnet run --project src/HomeBusinessAssistant.Runner --no-build -c Release -- power-diagnostics
```

Real wake registration is opt-in. After reviewing it, use `scripts/manual-wake-smoke.ps1 -ConfirmTaskRegistration`; the script prepares a harmless three-minute occurrence, displays the task and wake-timer diagnostics, waits for an optional user-initiated sleep test, reports persisted timing, and removes the managed task.

Then browse to `http://127.0.0.1:5180/`. A genuinely fresh seed-only database routes the owner to `/Onboarding`; run the read-only readiness checks, explicitly test protected storage, review Task Scheduler/power caveats, then continue to `/Onboarding/Agents`. Installed agents are never preselected. Choose a safe available subset or defer all; the Sample Business Agent demonstrates the generic configuration/secret wizard, Founder Scout opens its seven-step specialized wizard, and Wake Remote remains pending Stage 21. Founder Scout can be configured for fixture-only capture/screening without AI, or for a conservative live pilot with explicit consent, protected provider key, no-download browser check, optional one-shot provider check, and Runner-backed headed authentication. The wizard does not enable the agent, create a schedule, start discovery, or send messages. Established installations open the dashboard without a forced wizard, offer optional readiness review, and show a non-blocking setup banner for newly scanned agents. Request liveness at `http://127.0.0.1:5180/health/live` or dependency readiness at `http://127.0.0.1:5180/health/ready`. Re-launching the Host signals the existing per-user/session instance to open the dashboard.

See [docs/development.md](docs/development.md) for repository conventions and validation details.

For deployment and operations, see [installation](docs/installation.md), [operations](docs/operations.md), [backup/restore](docs/backup-restore.md), and [security/privacy](docs/security-privacy.md).

## Current stage

Stage 20 — Founder Scout Guided Onboarding is complete. Verified implementation and validation status are recorded in [docs/PROJECT_STATE.md](docs/PROJECT_STATE.md).

The remaining planned first-run onboarding increments are:

1. `prompts/21-wake-remote-onboarding.md`
2. `prompts/22-onboarding-validation-scheduling-completion.md`

These prompts are planned work, not implemented capability. Start with Stage 21 and update `docs/PROJECT_STATE.md` after each verified stage.

## Runtime data warning

Runtime data is local and sensitive. Databases, browser profiles, authentication state, secrets, logs, snapshots, screenshots, reports, backups, temporary execution inputs, and published binaries must never be committed. The repository `.gitignore` excludes the planned runtime locations, but developers remain responsible for checking changes before every commit.

Do not place real founder profiles, cookies, credentials, API keys, remote-access PINs, or browser session data anywhere in this source tree.
