# ADR-0005: Interactive Host lifecycle and Runner dispatch

- Status: Accepted
- Date: 2026-08-30
- Stage: 07
- Owners: Home Business Assistant

## Context

The platform needs one interactive Windows process to own the system tray, loopback web server, periodic reconciliation, local notifications, and user-requested keep-awake sessions. A user may start the Host more than once, background loops can observe work concurrently with the Windows wake task, and tray commands must not weaken the central Runner boundary.

Windows Forms requires an STA message loop, while ASP.NET Core and the orchestration loops use asynchronous lifetimes. A conventional process-wide mutex alone prevents duplicates but cannot make a second launch useful. Directly awaiting an agent from the tray would couple the interactive process to agent lifetime and bypass durable claim, supervision, and recovery rules.

## Decision drivers

- Exactly one Host may own the tray and loopback port per Windows user/session.
- A second launch should activate the existing instance without accepting arbitrary IPC commands or payloads.
- Host startup must initialize persistence and recover stale execution state before presenting a ready system.
- Tray and background actions must create or select durable occurrences and invoke only the central Runner.
- Loop failures must be isolated, sequential, observable, and cancellable.
- Tray, Kestrel, power requests, IPC, and logs must be released on every shutdown path.

## Decision

Run the Host as an STA Windows Forms application context. The primary instance acquires a `Local` mutex whose name is derived from the current user identity and session. It then listens on a same-scope named pipe created with `PipeOptions.CurrentUserOnly`. The pipe accepts one bounded UTF-8 allow-listed command (`OpenDashboard` or `ShowStatus`) and no arguments. A secondary instance signals `OpenDashboard` with a bounded retry and exits.

Initialize and migrate SQLite through the same cross-process bootstrap fence used by Runner, seed built-in defaults, perform stale-run recovery, then start Kestrel and expose the tray. Readiness is separate from liveness and includes database/migration state, writable runtime directories, first successful schedule reconciliation, and Runner availability.

Run schedule, wake-task, stale-recovery, and failure-notification work as independent sequential `BackgroundService` loops. Each loop awaits its current iteration before its bounded delay, records safe failure state, and continues. `TimeProvider` remains injectable. Persisted claims and leases, not loop timing, remain the duplicate-execution authority.

All tray and Host scheduling actions create or select durable occurrences and start `HomeBusinessAssistant.Runner.exe execute --occurrence-id ...` using `ProcessStartInfo.ArgumentList`, no shell, a hidden window, and validated contained paths. Host detaches after a successful start. It never launches agent executables or parses their protocol streams. A failed Runner start transitions a still-ready occurrence to a terminal failure and audits a bounded reason.

Own manual keep-awake separately from Runner power policy. The Host permits only one bounded one-minute-to-24-hour system-awake request, replaces or extends it explicitly, never requests display-awake, and releases it on timer, user action, failure, or shutdown. Exit requires confirmation when a run or manual hold is active; existing Runner processes remain independently supervised.

Bind the web server only to the exact HTTP `127.0.0.1` origin. Use server-rendered Razor Pages, antiforgery on mutations, restrictive response headers, and safe bounded read models. Only this validated origin may be opened through the Windows shell.

## Alternatives considered

### Mutex only, with the second process exiting silently

This prevents duplicate ownership but makes shortcuts and repeated launches appear broken. A bounded allow-listed named pipe provides useful activation without a general local command channel.

### Launch agents directly from Host

This would duplicate Runner supervision and make Host shutdown affect execution semantics. It was rejected; every action goes through a durable occurrence and Runner claim.

### In-process scheduler timer with overlapping callbacks

Overlapping callbacks complicate database contention and recovery. Sequential hosted loops with independent failure isolation were selected.

### Await Runner completion inside tray handlers

This would couple UI responsiveness and Host lifetime to long-running work. The Host records the durable launch and detaches; persisted state drives subsequent UI and notifications.

### Browser-accessible IPC or non-loopback binding

Neither is required for V1 and both expand the trust boundary. Browser launch and web hosting remain exact-loopback only.

## Consequences

### Positive

- One interactive owner coordinates tray, web, loops, IPC, and manual power state.
- Repeated launches activate the existing dashboard rather than causing a bind error.
- Runner remains the only process supervisor and durable claims still prevent duplicates.
- Individual loop and notification failures do not terminate the desktop process.
- Readiness reports partial startup and packaging failures without hiding the local UI.

### Negative / tradeoffs

- The Host requires an interactive signed-in user and does not yet behave as a Windows Service.
- Named-pipe activation is intentionally small and cannot carry future arbitrary navigation without an explicit protocol revision.
- Detached Runner completion is observed through persisted polling rather than direct process ownership.
- Native tray behavior and hardware wake still require manual Windows verification beyond automated abstraction tests.

## Validation

- Actual two-coordinator tests verify second-instance detection and current-user named-pipe delivery.
- Adapter tests verify loopback-only dashboard opening and shell-free Runner argument construction with paths containing spaces.
- Host tests exercise real ephemeral Kestrel liveness/readiness, dashboard rendering, antiforgery rejection, loop failure recovery, keep-awake replacement/release, and controller state.
- A real integration test creates a tray occurrence, starts the built Runner, executes the built Wake Remote diagnostic, observes terminal SQLite state and a versioned summary, waits for Runner exit, and removes its temporary root.
- The full suite verifies the exact acyclic project graph and inherited scheduler/claim/lease/process/power behavior.

## Follow-up work

- Stage 08 replaces placeholder navigation with agent/configuration/schedule/run management pages.
- Stage 14 expands persisted audit summaries and notification policy.
- Stage 15 installs and manages `HostAtLogon`, publishing, upgrades, and operational recovery.
