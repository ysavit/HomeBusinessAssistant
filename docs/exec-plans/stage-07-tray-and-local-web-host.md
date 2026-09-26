# Stage 07 — System Tray and Local Web Host ExecPlan

## Purpose and user-visible outcome

Turn `HomeBusinessAssistant.Host.exe` into the single interactive Windows control-plane process. One primary instance owns a tray icon, loopback Razor Pages dashboard, schedule/wake/recovery/notification loops, manual Runner dispatch, and bounded manual keep-awake sessions. A second launch sends an allow-listed named-pipe command to the primary instance and exits. The user verifies exact loopback health URLs, real persisted dashboard state, tray commands, and clean shutdown without directly launching an agent from Host.

## Current repository state

- Stage 06 is validated: Release build and 155 tests pass, EF has no pending changes, and a real 60-second Runner/Wake Remote smoke passed.
- `HomeBusinessAssistant.Host` is already a `net10.0-windows` Web SDK `WinExe` with Windows Forms enabled, but `Program.cs` only runs Kestrel, `/health` is a generic check, and the root page is the Stage 00 placeholder.
- Application already owns schedule reconciliation, manual occurrence creation, global schedule primitives, wake reconciliation, power ports, and stale-recovery contracts. Infrastructure owns SQLite repositories and recovery. Windows owns Task Scheduler, power, and shell-free process adapters.
- Host currently references Application and Windows only. Stage 07 must add Infrastructure and Wake Remote Application at the Host composition root so it can initialize persistence and seed the built-in Wake Remote configuration before a tray diagnostic occurrence is created.
- Existing persistence can represent all Stage 07 state. Global pause uses a versioned system setting and existing schedule pause fields; dashboard/notification data is a read model over current tables. No migration is planned.

## Scope and non-goals

Implement single-instance mutex/named-pipe activation, local dashboard launching, loopback liveness/readiness, desktop/tray lifetime, manual occurrence plus detached Runner dispatch, global pause/resume, bounded manual keep-awake, periodic orchestration and health tracking, local failure notifications, rolling Serilog files, real dashboard read models, accessible local-only Razor Pages, and graceful cleanup.

Defer detailed agent/configuration/schedule CRUD, candidate pages, Windows Service hosting, LAN binding, `HostAtLogon` installation, notification persistence beyond existing run state, and any in-process agent execution.

## Design and data flow

```text
second Host -> per-user mutex occupied -> CurrentUserOnly named pipe -> OpenDashboard

primary Host (STA message loop)
  -> async database migration/bootstrap and recovery
  -> Kestrel http://127.0.0.1:<port>
  -> sequential BackgroundService loops
       schedule reconcile -> due non-wake occurrence -> Runner.exe execute
       wake reconcile -> one managed NextWake task
       stale recovery
       failure polling -> throttled tray notification
  -> NotifyIcon/TrayController
       manual occurrence -> detached Runner (never agent directly)
       pause/resume persisted schedules
       manual power handle -> timed release
  -> shutdown: loops/Kestrel, power handles, tray icon, IPC, mutex, logs
```

The mutex name and pipe name include a hash of the current Windows user identity and local session. The pipe uses `PipeOptions.CurrentUserOnly`, a bounded UTF-8 one-line protocol, and an enum allow list. The Runner adapter uses `ProcessStartInfo.ArgumentList`, `UseShellExecute=false`, and `CreateNoWindow=true`. A successful process start is detached; durable Runner claims remain the duplicate-execution authority. A failed start transitions a still-ready occurrence to Failed and writes bounded audit data.

Hosted loops await each iteration before delaying, so one loop cannot overlap itself. Each failure is logged/recorded without stopping subsequent iterations. Defaults are 30 seconds for schedule reconciliation, 60 seconds for wake reconciliation, 2 minutes for stale recovery, and 15 seconds for notification polling.

## Milestones

1. Add Application host contracts/use cases and Infrastructure read/bootstrap services while keeping the solution buildable.
2. Add Windows single-instance, local-browser, and Runner process adapters with focused tests.
3. Compose Host runtime, readiness, resilient loops, notifications, keep-awake, and testable tray controller.
4. Add the WinForms tray lifecycle and local server-rendered dashboard/placeholder pages.
5. Complete integration/unit coverage, documentation, smoke checks, and canonical project-state handoff.

## Detailed steps

- Add host status records/query port, Runner launch/dispatch ports and use cases, and global pause state/control under `HomeBusinessAssistant.Application/Desktop`.
- Add the EF-backed status reader and shared fenced/retrying database bootstrap helper under Infrastructure; update Runner to use the shared initializer.
- Add `WindowsSingleInstanceCoordinator`, bounded IPC codec transport, `WindowsLocalDashboardLauncher`, and `WindowsRunnerProcessLauncher` under Windows.
- Add Host bootstrap validation, health state/checks, dashboard composition, four periodic workers, notification hub, manual keep-awake service, tray controller, icon/menu/context, log provider, and desktop startup coordinator.
- Replace the Stage 00 root page with a shared local-only UI shell, dashboard cards sourced from SQLite, antiforgery-protected pause/resume actions, and real empty states plus placeholder sections.
- Add tests for validation, IPC, dispatch/failure, launch argument safety, global pause, keep-awake replacement/release, loop resilience/non-overlap/cancellation, readiness transitions, endpoints, antiforgery, dashboard rendering, tray controller, and shutdown abstractions.
- Update architecture/development/configuration/README/project-state documentation and architecture graph expectations.

## Progress

- [x] 2026-08-30: Read repository instructions, project state, plan rules, architecture, ADR-0003/0004, Stage 07 prompt, existing Host, scheduler, wake, Runner, persistence, and tests.
- [x] 2026-08-30: Verified the Stage 06 Host baseline with 2 focused Host tests passing.
- [x] 2026-08-30: Added Application durable dispatch/global pause/status boundaries, Infrastructure shared bootstrap/readiness/status adapters, and Windows instance/browser/Runner adapters.
- [x] 2026-08-30: Composed the STA desktop lifetime, four resilient loops, tray/controller, manual keep-awake, health/readiness, rolling logs, dashboard, antiforgery, and placeholder navigation.
- [x] 2026-08-30: Added focused and integration coverage, including actual same-user mutex/pipe activation, real ephemeral Kestrel, and a real tray-controller→Runner→Wake Remote process smoke.
- [x] 2026-08-30: Final restore, Release build, 171-test suite, format verification, EF drift check, sensitive-artifact audit, documentation, and canonical handoff passed/completed.

## Decisions

- Keep tray/menu logic behind a controller and notification hub so normal tests do not require UI automation.
- Use the existing central tables and a query-only dashboard reader; Stage 07 adds no notification or dashboard cache table.
- Persist global pause by applying existing audited schedule pause controls and recording which schedules the global action changed, so resume does not clear unrelated pre-existing pauses.
- Keep Kestrel available when Runner is missing but report readiness as unhealthy. This gives the local dashboard an actionable status instead of turning a packaging error into an invisible tray startup failure.
- Use a small Microsoft logging provider over the already referenced Serilog core/file sink, avoiding another package and duplicate ASP.NET logging providers.
- Do not pass post-execution wake reconciliation from ordinary Host-launched Runner processes. The managed wake task already carries that explicit flag, while the Host wake loop owns normal periodic reconciliation; this keeps automated/manual Host diagnostics from mutating the machine-global task as a launch side effect.

## Surprises and discoveries

- Mapping the dashboard projection after an EF `Select` caused an untranslatable ordering expression. Ordering the entity query before projection preserved the bound and translated correctly.
- Razor Pages were not discovered when the test server's entry assembly was the test project. Explicitly adding the Host assembly as an MVC application part made runtime and test discovery identical.
- The first real process smoke completed correctly but its cleanup raced the Runner log flush. Waiting for the detached Runner PID to exit before deleting the temporary root now proves clean ownership.
- An intermediate focused build left 249 reusable `dotnet` build-server processes and returned nonzero without compiler diagnostics. `dotnet build-server shutdown` cleared only the build servers; a serial rebuild and the complete regression suite passed.

## Validation

Required commands:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/HomeBusinessAssistant.Infrastructure --startup-project src/HomeBusinessAssistant.Infrastructure --configuration Release --no-build
```

Final results on Windows `2026-08-30`:

- `dotnet restore`: passed; all 25 projects were up to date.
- Release build: passed with 0 warnings and 0 errors.
- Full suite: 171 passed, 0 failed, 0 skipped across 10 test projects.
- Format verification: passed with no findings after correcting three import-order findings from the first verification run.
- EF model drift: passed; no changes since `20260830155728_AddWindowsWakeAndPowerMetadata`, and Stage 07 added no migration.
- Host stage tests: 10 passed, including real ephemeral `/`, `/health/live`, `/health/ready`, readiness transition, missing-antiforgery rejection, dashboard state, runtime migration/recovery, immediate wake reconciliation after a paused schedule pass, and real tray-controller→Runner→Wake Remote completion/cleanup.
- Windows/Application/Infrastructure focused additions passed inside the full suite: real current-user mutex/pipe activation, exact detached process arguments, durable dispatch failure/queue behavior, preserved pre-paused schedules, safe status projection, keep-awake replacement/release, and loop resilience/non-overlap/cancellation.
- Repository artifact/process audit found no database, WAL, log, secret, screenshot/image, diagnostic HTML, or remaining Host/Runner/WakeRemote process outside ignored build/tool outputs.

The native tray icon, balloon appearance, real second-launch browser focus, and dialog interactions remain an interactive manual smoke and were not claimed as performed in this non-interactive run.

## Recovery and rollback

No migration is planned. A failed Host start releases its temporary resources, stops Kestrel if started, disposes power/tray/pipe/mutex/logging, and leaves durable schedules/occurrences intact. A failed Runner launch marks only the still-ready occurrence terminal and audits a reason code. Global pause records the schedules it changed; resume touches only those IDs. Named-pipe failures affect activation only and never execute arbitrary input.

## Remaining risks and follow-up

Task Scheduler registration and native tray behavior depend on an interactive Windows session. Automated tests use abstractions and never sleep the machine or change permanent power settings. Installer-time `HostAtLogon`, robust upgrade handoff, full configuration/schedule management, and richer notification policy remain later stages.
