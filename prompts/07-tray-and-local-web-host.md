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

