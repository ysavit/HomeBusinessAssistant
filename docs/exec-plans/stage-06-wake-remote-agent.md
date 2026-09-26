# Stage 06 — Wake & Remote Agent ExecPlan

## Purpose and user-visible outcome

Deliver `WakeRemote.exe` as an independently executable, Runner-supervised agent. A durable wake occurrence holds a bounded Windows power request, waits for local network readiness, checks the configured Chrome Remote Desktop or Windows RDP provider without changing its state, remains available until the immutable window deadline, emits protocol 1.0 telemetry and a deterministic summary, and releases power resources on every exit path. A user can verify the increment with the Runner-backed 60-second diagnostic-provider smoke workflow.

## Current repository state

- Stages 01–05 provide protocol 1.0, SQLite configuration/schedules/occurrences, durable scheduling, Runner supervision, the single `NextWake` Task Scheduler bridge, and reference-counted Windows execution-state requests.
- `WakeRemote.Application` and `WakeRemote.Infrastructure` contain only assembly markers. `WakeRemote.Agent` supports `protocol-demo` and the Stage 05 `wake-test` diagnostic.
- The Runner passes the immutable configuration revision in a private temporary file. Occurrence arguments are durable but are not yet supplied across the child-process boundary; Stage 06 must add a versioned, private execution-input envelope while keeping the standard Agent SDK command line stable.
- Direct verification found a Stage 05 mismatch under full-suite concurrency: lease acquisition classified every EF update failure as a lease collision, and database initialization rewrote unchanged manifest/audit state on every Runner startup. The repair now distinguishes SQLite uniqueness from transient write contention, retries bounded transient acquisition failures, and makes built-in initialization a true no-op. Focused build and persistence/competing-runner tests pass; the final Stage 06 validation will re-run the whole solution.
- No Stage 06 database migration is currently required: configuration revisions, schedules, occurrence arguments, power flags, metrics, events, and artifacts already have durable storage.

## Scope and non-goals

Implement typed versioned configuration and occurrence payload validation; testable network/provider probes; Chrome Remote Desktop, RDP, and explicitly labeled diagnostic provider checks; keep-awake/window orchestration; `run`, `diagnose`, and `check-remote`; deterministic diagnostics artifacts and summary; configuration seeding; availability-window schedule creation; Runner input integration; and automated/real-process smoke coverage.

Do not implement forced sleep, Wake-on-LAN, firewall/router/RDP enablement, remote credentials, provider login, a tray/UI, live provider dependencies in CI, or multiple Windows wake tasks.

## Design and data flow

```text
availability-window use case
  -> immutable configuration revision + Required-wake schedule
  -> occurrence (window arguments + keep-awake flags)
  -> NextWake calls Runner
  -> Runner writes private execution-input envelope
  -> WakeRemote validates configuration + occurrence
  -> agent power handle -> network probe -> provider probe -> deadline wait
  -> JSONL metrics/events/summary + diagnostic artifact
  -> finally release power handle
  -> Runner finalizes and reconciles NextWake
```

`WakeRemote.Application` owns pure configuration, payload, orchestration, validation, and window-schedule mapping. It depends inward on platform Application contracts for power and persistence/scheduling ports, but contains no Windows APIs. `WakeRemote.Infrastructure` implements network, service/process, TCP, and read-only power diagnostic adapters. `WakeRemote.Agent` is the protocol/composition boundary.

The execution-input file uses an explicit envelope (`executionInputSchemaVersion`, `configuration`, `occurrenceArguments`) so occurrence payloads do not become command-line data. The Runner writes it with the same current-user ACL, zeroes temporary bytes, and deletes it after the run. The test agent adopted the envelope in the same increment, and the SDK provides the shared bounded reader for subsequent agents.

Provider checks expose only typed state and bounded reason codes to summaries. Service/process names may be configured for local inspection, but raw lists never enter deterministic summaries. Diagnostic fake-provider mode is opt-in and always labeled `DiagnosticFake` in metrics, artifacts, and summaries.

Window expiration is not extended: a deadline already at/past the agent start returns a non-success `WindowExpired` result before power acquisition. Cancellation and failures pass through a `finally` that disposes the agent power handle. Runner's outer request and agent's nested request remain independently reference-counted.

## Milestones

1. Stabilize and revalidate the inherited persistence/Runner concurrency boundary.
2. Add execution-input transport, typed Wake Remote configuration/payloads, validation, and defaults.
3. Add testable workflow orchestration and protocol mapping.
4. Add conservative Windows network/provider/power diagnostics implementations and agent commands.
5. Add default configuration seeding and availability-window schedule use case.
6. Add unit, integration, and real-process smoke tests; complete repository validation and handoff docs.

## Detailed steps

- Extend Runner temporary input generation to include occurrence arguments for Wake Remote without logging them or adding shell arguments.
- Add `WakeRemoteConfiguration`, provider/probe settings, JSON parser/validator, `WakeRemoteOccurrenceArguments`, a default document, typed probe results, summary/result records, delay abstraction, and workflow services.
- Implement network readiness from `NetworkInterface`, optional DNS, and optional TCP; implement provider service/process checks through bounded shell-free local commands and process APIs.
- Compose `WindowsPowerRequestService` and read-only `PowercfgDiagnosticsService` in the agent. Preserve `wake-test` and protocol-demo behavior.
- Add a platform configuration-validator router if needed so Stage 08 receives Wake Remote errors, seed a default only when absent, and implement a use case that saves daily/weekday Required-wake schedules with occurrence argument templates and bounded timeout.
- Update manifest commands/capabilities and architecture/protocol documentation only where the implemented contract changes.
- Test all prompt-mandated timing, cancellation, required/optional provider, cleanup, summary privacy, JSONL, scheduling, wake reconciliation, Runner execution, and 60-second smoke paths.

## Progress

- [x] 2026-08-30: Read repository guidance, project state, plans, architecture/ADR/protocol, Stage 06 prompt, and existing implementation/tests.
- [x] 2026-08-30: Reproduced and repaired inherited Runner/bootstrap contention; focused Release build and tests pass.
- [x] 2026-08-30: Added bounded execution-input transport plus typed configuration/payload contracts and safe default provider.
- [x] 2026-08-30: Added testable workflow, local network probes, Chrome/RDP/DiagnosticFake provider checks, and read-only diagnostics.
- [x] 2026-08-30: Added run/diagnose/check-remote composition, Required-wake availability-window mapping, default seeding, audit, and manifest 1.1.0.
- [x] 2026-08-30: Completed 155-test regression, 60-second real Runner/agent smoke, format, EF drift, documentation, and project-state handoff.

## Decisions

- Keep occurrence arguments off the command line and inside a private versioned execution-input file. This preserves the protocol command shape and avoids exposing structured data in process listings.
- Reuse the Runner's outer power request while also acquiring the agent's explicit lifecycle request. The Windows adapter already supports independent nested handles; both processes must clean up their own state.
- Use local service/process/network evidence only. Optional DNS/TCP probes are explicitly configured and disabled by default.
- Model the CI/smoke provider as `DiagnosticFake`, not as Chrome/RDP success, so test evidence cannot be mistaken for real remote availability.
- Seed defaults through a generic platform seeding use case composed with the Wake Remote default provider, rather than coupling central persistence to an agent assembly.

## Validation

Planned commands from repository root:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
dotnet ef migrations has-pending-model-changes --project src/HomeBusinessAssistant.Infrastructure --startup-project src/HomeBusinessAssistant.Runner --configuration Release --no-build
```

Stage smoke: use the real Runner and Wake Remote agent with a temporary SQLite/data root, a 60-second availability window, local network checks, and `DiagnosticFake` provider readiness; inspect persisted wake-delay/network/provider metrics, heartbeat(s), terminal summary, occurrence/run state, power-release telemetry, private-input cleanup, and next-wake selection state.

Actual focused checks so far:

- Initial Release build `/m:1 --no-restore`: passed, 0 warnings/errors.
- Inherited foundation correction: `PersistenceIntegrationTests` 7/7 and `CompetingExecutorsLaunchOccurrenceOnlyOnce` 1/1 passed.
- Wake Remote focused suite: 19/19 passed; the eight-second normal real Runner/child integration passed.
- Required 60-second Runner/real-agent smoke with `HBA_WAKE_REMOTE_SMOKE_SECONDS=60`: passed in 61 seconds. It persisted the wake-delay/network/provider/power-release metrics, at least one heartbeat, `DiagnosticFake` terminal summary, completed state, no private input, and no pending next wake.
- An intermediate full-suite run failed only the exact project-reference expectation after the intentional Runner/Wake Remote Application composition edge was introduced. The acyclic allowed graph was updated and its focused suite passed 3/3.
- Final `dotnet restore`: passed; all projects up to date.
- Final Release build: passed with 0 warnings and 0 errors.
- Final solution tests: 155 passed, 0 failed, 0 skipped across 10 test projects.
- Final format verification: passed with no findings.
- EF Core pending-model check: passed; no model changes since `AddWindowsWakeAndPowerMetadata`.
- Current `WakeRemote.exe protocol-demo`: exit 0 with five valid ordered protocol objects.

## Recovery and rollback

No schema migration was required. Invalid Wake Remote configuration/payloads fail before network/provider/power side effects. Runner temporary inputs and staging artifacts remain run-scoped and are cleaned by existing startup recovery. Failed schedule creation leaves earlier immutable configuration revisions usable and does not alter the single managed wake task until normal reconciliation succeeds. Task registration remains idempotent and removable through the Stage 05 bridge.

## Remaining risks and follow-up

- Windows service/process names vary by installation; user-visible configuration and health presentation arrive in Stage 08.
- Chrome Remote Desktop/RDP availability is a conservative local health signal, not proof of internet reachability or authenticated remote access.
- RDP hosting depends on Windows edition, policy, and firewall state, none of which V1 changes.
- On-demand Wake-on-LAN is deferred beyond Stage 06.
- Stage 07 supplies the interactive tray/loopback host that continuously plans/reconciles schedules and exposes local status.
