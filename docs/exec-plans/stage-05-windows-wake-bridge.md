# Stage 05 — Windows Wake Bridge and Power Integration

## Purpose and user-visible outcome

Stage 05 converts the earliest durable wake-required occurrence into one managed Windows Task Scheduler task, `\HomeBusinessAssistant\NextWake`. The task wakes supported hardware and invokes only the central Runner for that exact occurrence. Runner executions whose durable occurrence metadata requires it hold a system-required Windows power request until finalization and cleanup. Local diagnostics report supported sleep states, wake timers, the last wake, and armed wake devices without changing power policy. A developer can prepare a harmless three-minute wake test, inspect the generated/registered task, optionally sleep the workstation manually, inspect the persisted result, and remove the task.

## Current repository state

Direct inspection on 2026-08-30 agrees with `docs/PROJECT_STATE.md`: Stage 04 is validated. The Release solution builds with zero warnings/errors and all 122 tests pass. Stage 03 already provides deterministic Windows time-zone conversion, durable `Planned`/`Ready` occurrences, `IOccurrenceRepository.GetEarliestWakeAsync`, fenced SQLite leases, system settings, audit, and fixed-delay completion. Stage 04 provides one-shot Runner execution and an `IExecutionLifetimeHook`, currently composed with `NoOpExecutionLifetimeHook`. `HomeBusinessAssistant.Windows` contains only the DPAPI secret adapter; there is no Task Scheduler, power-request, or `powercfg` implementation and no Task Scheduler package.

The persisted occurrence currently derives wake behavior by joining its schedule. That cannot represent a schedule-less diagnostic wake occurrence and does not durably state whether Runner must keep the machine awake. Stage 05 therefore adds explicit, immutable occurrence flags for wake registration, system-awake execution, and display-awake execution. Existing rows migrate to conservative defaults (`false`); newly planned schedule occurrences derive the first two flags from `WakePolicy != Never`, while display wake remains opt-in and false in Stage 05.

## Scope and non-goals

This stage implements:

- focused Application ports and typed health/error/result records for the wake task, power holds, process execution, and diagnostics;
- explicit durable occurrence wake/power metadata and its EF Core migration;
- lease-fenced, idempotent earliest-wake reconciliation with a fingerprinted system setting and change/failure audit;
- deterministic Task Scheduler 2.x XML and `schtasks.exe` create/query/delete through `ProcessStartInfo.ArgumentList`;
- a dedicated-thread, reference-counted `SetThreadExecutionState` implementation;
- bounded/redacted read-only `powercfg` diagnostics;
- Runner commands for wake reconciliation, diagnostics, wake-test preparation/status, and cleanup;
- a harmless Wake Remote `wake-test` protocol command which writes a timestamped artifact and performs no remote or power configuration;
- automated tests with fakes plus an explicitly opt-in manual smoke script.

This stage does not implement the tray/Host loop, `HostAtLogon`, Wake Remote provider/network behavior, automatic suspend/hibernate, permanent power-plan changes, firewall/router/RDP changes, or a UI. Automated tests never register a real task or call a sleep API.

## Design and data flow

```text
schedule planner / wake-test use case
        -> occurrence (RequiresWake, KeepSystemAwake, KeepDisplayOn)
        -> IWakeTaskReconciler [SQLite lease]
             -> earliest pending wake occurrence
             -> validated WakeTaskRequest + stable fingerprint
             -> IWakeTaskSchedulerBridge
                  -> deterministic Task 2.x XML in confined temp file
                  -> schtasks /Create ... /XML ... /F
                  -> schtasks /Query ... /XML verification
             -> SystemSetting + append-only audit

Task Scheduler TimeTrigger (exact UTC StartBoundary with Z, WakeToRun=true)
        -> Runner execute --occurrence-id <id> + generated bootstrap args
        -> atomically claim occurrence
        -> Windows execution lifetime hook
             -> dedicated native-call thread
             -> ES_CONTINUOUS | ES_SYSTEM_REQUIRED [| ES_DISPLAY_REQUIRED]
        -> child agent + durable finalization
        -> release handle; ES_CONTINUOUS when final reference leaves
```

The XML uses a UTC `StartBoundary` with `Z`. Microsoft documents that an explicit offset or `Z` is used regardless of the machine time zone and daylight-saving settings. Schedule-local time and its Windows time-zone ID remain diagnostic description fields. Reconciliation must run again after clock/time-zone relevant changes, but an already generated task still identifies the persisted UTC instant.

The task principal is the current Windows user with `InteractiveToken` and `LeastPrivilege`. V1 therefore assumes that the same user remains signed in; a locked or sleeping interactive session is acceptable, but a signed-out user is not. No password is stored. `IgnoreNew` prevents overlapping bridge task instances; durable Runner claims remain the authoritative duplicate-execution defense. The default permits starting and continuing on battery so a requested wake can do useful work, with both choices explicit in the request/XML and documented as configurable future policy.

Power requests are thread-scoped, so all native calls and reference counts live on one dedicated background thread. Nested callers update the combined display/system flags; one handle cannot clear another active request. The final release calls `ES_CONTINUOUS`, which permits normal Windows idle policy to resume. Acquisition failure is explicit and fails a required Runner execution before child launch. The service does not prevent explicit user sleep actions and uses no away mode.

Task XML and `powercfg` output are bounded. Audit stores codes, occurrence IDs, due times, and fingerprints, never raw command lines, environment values, or arbitrary raw diagnostic text.

## Milestones

1. Add Application contracts, wake/power occurrence metadata, persistence mapping, and migration while keeping the solution buildable.
2. Implement Windows process execution, XML generation/parsing, Task Scheduler bridge, dedicated-thread power service, execution lifetime hook, and diagnostics.
3. Implement Application reconciliation and wake-test use cases, then compose Runner CLI operations and the harmless agent probe.
4. Add unit/integration coverage, ADR/docs/manual smoke workflow, update project state, and run all required validation.

## Detailed steps

- Add `Application/Wake` contracts and services. Validate roots, GUIDs, timeouts, due-time staleness, session/battery policy, and generated-only Runner arguments. Store the registered request in `wake.next-task` system setting schema 1.0.
- Add `Application/Power` contracts and move/evolve the execution-lifetime hook to accept the durable run/occurrence power policy.
- Extend persistence models/entities/mappings and every occurrence creation path. Add `AddWindowsWakeAndPowerMetadata` through EF tooling; verify a fresh database and no pending model changes.
- Add `Windows/Processes`, `Windows/Wake`, and `Windows/Power`. The XML generator uses `System.Xml.Linq`; the bridge creates random XML below `<data>/temp/wake-tasks`, always deletes it, and verifies the managed fingerprint after registration.
- Extend Runner parsing/runtime composition for `reconcile-wake`, `power-diagnostics`, `prepare-wake-test`, `wake-test-status`, and `remove-wake-task`. No command registers a task unless explicitly invoked.
- Add Wake Remote `wake-test` command using the existing SDK lifecycle. It writes a bounded JSON timestamp artifact beneath the Runner-provided artifact directory and emits a valid artifact event.
- Add Application, Infrastructure, Windows, Runner, WakeRemote, and architecture tests. Fakes capture commands/native flags; only temporary SQLite/files are used.
- Add `scripts/manual-wake-smoke.ps1` with an explicit confirmation gate and cleanup, and update architecture/development/README/ADR/project state.

## Progress

- [x] 2026-08-30: Required handoff, architecture, ADR-0002/0003, Stage 03/04 code, Stage 05 prompt, and official Microsoft Task Scheduler/power semantics inspected.
- [x] 2026-08-30: Baseline Release build passed with zero warnings/errors; all 122 existing tests passed.
- [x] 2026-08-30: Application contracts, explicit durable occurrence metadata, persistence mapping/index, and `AddWindowsWakeAndPowerMetadata` migration implemented.
- [x] 2026-08-30: Windows Task Scheduler, process, power-request, and read-only power-diagnostic adapters plus Runner lifetime integration implemented.
- [x] 2026-08-30: Lease-fenced reconciliation, harmless wake-test workflow, Runner CLI, agent artifact, and automated coverage implemented.
- [x] 2026-08-30: ADR, architecture/development/README guidance, opt-in manual script, canonical project state, and complete validation recorded.

## Decisions

- Use built-in Task Scheduler XML and `schtasks.exe`; no third-party scheduler dependency is needed.
- Encode `StartBoundary` as exact UTC with `Z`, not an offset-free local wall-clock value. This preserves the persisted occurrence instant across DST and machine time-zone changes.
- Use `InteractiveToken`/`LeastPrivilege`; no password or S4U credential is stored. V1 requires the user to remain signed in.
- Persist occurrence-level wake/power booleans. This supports a schedule-less diagnostic occurrence and makes Runner power behavior reproducible after schedule edits.
- A wake-enabled schedule sets `RequiresWake=true` and `KeepSystemAwake=true`; `KeepDisplayOn` is false unless a future explicitly validated policy sets it. The wake-test occurrence sets wake/system true and display false.
- Run `SetThreadExecutionState` on a dedicated thread because its state belongs to the calling thread. The process-wide service owns the combined reference count.
- Treat a required power-hold acquisition failure as an execution failure before child launch. Continuing silently could allow the workstation to sleep mid-run.
- Permit wake execution on battery by default but expose both battery settings in the typed request and XML. This favors completing an explicitly requested wake occurrence; hardware, firmware, Windows policy, and battery saver can still limit behavior.

## Validation

Required final commands from the repository root:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/HomeBusinessAssistant.Infrastructure --startup-project src/HomeBusinessAssistant.Infrastructure --configuration Release --no-build
```

Stage-specific automated checks must prove XML semantics/escaping/UTC DST examples, generated Runner arguments, create/update/query/delete and temp cleanup, structured permission/missing-task failures, idempotent/replace/remove/lease behavior, native nested/display/failure behavior, Runner lifetime release on all terminal paths, diagnostic aggregation/redaction, and wake-test occurrence/artifact behavior.

Non-mutating CLI smoke: render/inspect generated XML and capture real read-only `powercfg` diagnostics. Real task registration and workstation sleep remain opt-in manual steps and are not claimed unless actually run.

Final evidence on Windows `10.0.26200`, .NET SDK `10.0.400` / runtime `10.0.11`:

- `dotnet restore` passed; all 25 projects were up to date.
- `dotnet build HomeBusinessAssistant.sln -c Release --no-restore` passed with 0 warnings and 0 errors.
- `dotnet test HomeBusinessAssistant.sln -c Release --no-build` finally passed 136 tests, 0 failed, 0 skipped across all ten test projects. This includes 21 real-process Runner tests, 19 real-SQLite Infrastructure tests, 12 Windows fake-boundary tests, and 3 Wake Remote tests. An intermediate full rerun exposed an existing concurrent Runner bootstrap failure: EF attempted to acquire its SQLite migration lock even when no migration was pending, intermittently surfacing SQLite error 8 while another Runner wrote. Bootstrap now checks pending migrations first, loads manifests before the database transaction, and seeds within a short immediate transaction. The direct and actual CLI competing-executor regression each passed 10 consecutive runs, followed by the clean full-suite rerun.
- `dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore` initially identified mechanical indentation/import ordering in two new files. `dotnet format HomeBusinessAssistant.sln --no-restore` applied those changes. A later restricted-sandbox verify could not connect to Roslyn's named pipe; the exact verify command was rerun with normal process permissions and passed with exit code 0 and no findings. One diagnostic project-only parallel build likewise returned an empty sandbox failure with 0 warnings/errors; `/m:1` and both later exact Release solution builds passed.
- `dotnet tool restore` restored `dotnet-ef` 10.0.11. The first migration-generation attempt used Runner as the startup project and correctly failed because that executable does not reference the private EF Design tooling package; rerunning with Infrastructure as both target and startup generated `20260830155728_AddWindowsWakeAndPowerMetadata`. The final `has-pending-model-changes` command reported no model changes.
- The opt-in PowerShell smoke script parsed as a valid script block. Its real read-only `power-diagnostics` invocation used a unique self-cleaning data root: `/a`, `/lastwake`, and `/devicequery wake_armed` succeeded; `/waketimers` returned a bounded structured administrator-permission error, so the aggregate CLI exit was 70. This verifies safe partial diagnostics but does not verify a wake timer.
- Automated XML/fake-process/native tests proved exact UTC/DST semantics, Unicode/XML escaping, generated Runner arguments, create/update/query/delete, missing-task idempotence, permission errors, failed-registration cleanup, reconciliation idempotence/replacement/removal/lease contention, nested system/display requests, acquisition failure, lifetime release for success/failure/timeout/cancellation, diagnostic redaction, and wake-test persistence/artifact behavior.
- No real Task Scheduler task was created, no native sleep/hibernate call was made, no permanent power setting was changed, and no hardware wake success is claimed. `scripts/manual-wake-smoke.ps1 -ConfirmTaskRegistration` remains the explicit operator workflow.

## Recovery and rollback

Task registration is replace-in-place using `/F`; the old managed task remains if XML generation fails before registration. A failed registration leaves durable scheduling state unchanged and records a failed audit. Reconciliation can be rerun after permissions/path repair. Removal treats a verified not-found response as success. Temporary XML is deleted in `finally`; stale files stay confined to the application temp directory and may be removed safely by later cleanup.

The database migration adds non-null booleans with false defaults, so rollback does not require data transformation beyond dropping those columns. Existing occurrences remain non-wake after upgrade; normal schedule reconciliation creates future occurrences with explicit current policy. A lost power-service thread causes subsequent acquisitions to fail closed. Disposing Runner releases the last native request as far as process lifetime permits; process termination also lets Windows clear the thread-owned execution state.

## Remaining risks and follow-up

- Wake capability depends on device firmware, Windows sleep model, wake timers, power plan, battery state, and the user still being signed in. Stage 05 diagnoses but cannot guarantee it.
- `InteractiveToken` tasks do not run after sign-out. Installer-time startup registration and broader operational guidance arrive in Stage 15.
- Stage 07 must invoke schedule and wake reconciliation after schedule changes, terminal transitions, and periodically in the Host.
- Stage 06 implements network/provider readiness and bounded remote-availability behavior; Stage 05's `wake-test` remains diagnostic only.
- Raw `powercfg` output is localized; parsing is intentionally conservative and the bounded text is local-diagnostic data, not trusted HTML.
