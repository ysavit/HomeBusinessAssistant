# Stage 18 ExecPlan — Onboarding Foundation and Readiness

## Purpose and user-visible outcome

Add a durable, resumable first-run onboarding lifecycle before any agent is configured, enabled, scheduled, or executed. A fresh owner opening the dashboard is routed to `/Onboarding`, can run bounded platform readiness checks, explicitly test protected secret storage, acknowledge warnings, defer and resume safely, and continue to an honest `agent-selection-pending` handoff for Stage 19. Existing Stage 17 installations are detected conservatively and are never forced through a fake fresh-install flow.

## Current repository state

- Stage 17 is validated with 29 projects and 290 passing tests plus one later focused regression set.
- `assistant.db` has 15 central application tables and current migration `AddOperationalAttentionAndSummaries`; there is no onboarding persistence.
- Host readiness currently covers database connectivity/migrations, writable runtime directories, Runner presence, and the first scheduler pass, but it is an in-memory health projection rather than a durable owner workflow.
- The Windows layer already exposes read-only HostAtLogon/NextWake task state and bounded `powercfg` diagnostics. DPAPI CurrentUser secrets are addressed only through `ISecretStore`.
- The loopback Razor Pages host already enforces current-Windows-owner authorization, exact Host/Origin checks, antiforgery, CSP, and encoded output.
- Built-in configuration defaults use `ChangedBy=system-bootstrap`. User-authored configuration revisions, schedules, and completed/active runs can therefore distinguish an established installation from seed-only state.

## Scope and non-goals

### In scope

- Central `OnboardingSessions` and `OnboardingChecks` tables with migration, constraints, optimistic revisions, latest-check identity, and first-run backfill.
- An Application-owned onboarding state machine, entry decision policy, readiness-check contract, bounded orchestrator, and protected-storage probe.
- Fresh, established-upgrade, deferred, completed, concurrent-tab, and no-valid-agent entry behavior.
- Readiness checks for owner/loopback identity, SQLite safety, Runner/package availability, Task Scheduler ownership/access, power/wake capability, storage/backup state, and an explicit DPAPI round trip with guaranteed cleanup.
- An accessible server-rendered `/Onboarding` flow with warning acknowledgement, defer/resume, recheck, remediation links, and an explicit Stage 19 handoff state.
- Append-only onboarding audit events and durable attention integration for current intervention-worthy readiness failures.
- Focused unit, real-SQLite integration, Host routing/antiforgery/encoding, fake Windows adapter, and desktop/narrow rendered-browser validation.

### Non-goals

- No agent selection or per-agent wizard, provider/browser/API-key setup, configuration revision save, enablement, schedule creation, occurrence creation, Runner launch, task registration/removal, power-setting mutation, remote-access mutation, or live external call.
- No SPA, distributed messaging, MCP surface, CAPTCHA/stealth behavior, or automatic remediation.

## Design and data flow

```text
owner GET /
  -> onboarding entry policy
      -> fresh seed-only install: one durable FirstRun/InProgress session -> /Onboarding
      -> established install: one legacy-completed baseline + optional review invitation -> dashboard
      -> Deferred: dashboard reminder, no redirect loop
      -> Completed/Cancelled: dashboard

POST /Onboarding?handler=RunChecks
  -> bounded readiness orchestrator (max concurrency + per-check/total timeout)
  -> isolated safe results
  -> latest-check upsert + session revision/count update
  -> append-only audit

POST /Onboarding?handler=ProbeSecretStorage
  -> temporary opaque reference -> ISecretStore set/read/delete in finally
  -> no secret value in SQLite, HTML, audit, log, or result details

POST Continue
  -> reject stale revision
  -> reject Blocked/Unknown required checks
  -> require explicit warning acknowledgement
  -> move current step to agent-selection-pending without enabling or running anything
```

`OnboardingSessions.ActiveSlot` is nullable and uniquely filtered. `first-run/add-agents` sessions use the one global active slot only while `InProgress`; defer/terminal transitions clear it. `InitializationKey=first-run` is uniquely filtered so concurrent first dashboard requests cannot create duplicate baseline sessions. `OnboardingChecks.AgentScopeKey` normalizes a platform check to the empty string so the latest identity is uniquely enforced as `(SessionId, Scope, CheckKey, AgentScopeKey)` even on SQLite.

The dashboard owns the automatic entry decision instead of a global middleware redirect. This precisely covers owner dashboard entry while leaving health, static assets, authentication, About, Settings, activation, shutdown, and onboarding routes free from loops. Production composes onboarding; test/empty compositions retain their existing dashboard behavior.

## Milestones

1. [completed] Add the living plan and Application contracts/state/readiness orchestration.
2. [completed] Add EF entities/mappings/repository, migration, initial-install evidence, concurrency constraints, and audit-backed coordinator.
3. [completed] Add platform, storage, Runner/package, Windows task/power, owner identity, and explicit protected-storage checks.
4. [completed] Compose production services, dashboard entry/reminder, operational signal projection, and the `/Onboarding` Razor flow.
5. [completed] Add focused unit/integration/Host/browser coverage and migration/backfill tests.
6. [completed] Run required validation, migration drift, fresh/upgrade/concurrency/fake-Windows/secret-cleanup smokes, rendered browser QA, and update architecture/project state.

## Decisions

- Store onboarding in `assistant.db`; it is platform lifecycle state and must survive Host restart independently of agent databases.
- Keep check details schema-versioned, allow-listed, small, and safe. Raw command output, SID, user name, paths, cookies, keys, browser state, and full exception text are never persisted or rendered.
- Treat warnings as owner decisions and blockers as actual unsafe/impossible prerequisites. Unsupported hardware wake and an absent HostAtLogon task are warnings; an unmanaged reserved task, invalid loopback/owner boundary, unavailable database, unwritable data root, missing Runner, or zero valid agent packages is blocking.
- Run no readiness work on GET. All checks are started by an antiforgery-protected POST; the DPAPI mutation is a separately labeled POST and always attempts cleanup.
- Use current durable operational signals rather than transient-only banners for still-observed blocked checks. The existing detector remains the dedupe/notification authority.
- Do not mark the full onboarding session complete in Stage 18. Readiness completion advances to `agent-selection-pending`; Stage 19 owns selection and later completion semantics.

## Validation

Required commands:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Stage-specific validation:

- Apply the central migration to fresh and populated temporary databases; verify no pending model changes.
- Prove seed-only fresh detection, established backfill, one-active-session concurrency, defer/resume, warning acknowledgement, stale-post rejection, readiness completion, and no agent/schedule/run mutation.
- Prove check exception/timeout isolation, deterministic result ordering, task/power adapters never invoke mutation APIs, and secret-probe deletion on success/read failure/delete failure paths.
- Start a real temporary loopback Host and verify dashboard redirect exclusions, deferred/completed behavior, owner authorization, antiforgery, hostile text encoding, and no secret/path leakage.
- Use the in-app Browser first for the fresh-entry/readiness/defer/resume flow at desktop and narrow widths; record the exact failure and use the repository Playwright fallback only if the in-app Browser cannot access the fixture.

## Recovery and rollback

- The migration is additive. Rolling back application code leaves onboarding rows inert and does not alter configurations, schedules, occurrences, runs, agent databases, secrets, tasks, or power settings.
- Check execution replaces only the latest status for its stable identity. Audit retains the batch history needed for diagnosis.
- Deferred and terminal sessions are retained. No onboarding action deletes user settings or operational history.
- All test and browser fixtures use validated unique temporary roots; cleanup checks the resolved root before recursive deletion.

## Progress

- 2026-09-01: Read `AGENTS.md`, `docs/PROJECT_STATE.md`, `.agent/PLANS.md`, the complete architecture, Stage 18 prompt, and ADRs 0002/0004/0005/0006/0012/0013/0014.
- 2026-09-01: Inspected central persistence/migrations, configuration seed identity, audit/operational attention, Host readiness/composition/routing/auth, Task Scheduler and power contracts/adapters, secret storage, Razor patterns, and real Host/SQLite test fixtures.
- 2026-09-01: Implemented schema 1.0 onboarding contracts/services, durable `OnboardingSessions`/`OnboardingChecks`, migration `20260901181413_AddOnboardingFoundation`, conservative fresh/established detection, optimistic transitions, expiry, audit, and operational attention projection.
- 2026-09-01: Implemented the seven platform readiness cards: owner/loopback, database safety, compatible Runner/packages/no-agent blocker, storage/backup, query-only Task Scheduler, read-only power/wake, and explicit cleanup-guaranteed protected storage.
- 2026-09-01: Implemented dashboard-only entry/reminders and the owner-only `/Onboarding` Razor flow with PRG/antiforgery, warning acknowledgement, defer/resume/optional review cancellation, encoded safe messages, and `agent-selection-pending` handoff.
- 2026-09-01: Added empty and populated Stage 17 migration coverage, concurrent-start/stale/forbidden/expiry/audit/no-mutation tests, failure/timeout/cancellation tests, fake Windows category/non-mutation tests, Host auth/routing/encoding tests, and rendered desktop/narrow Playwright coverage.
- 2026-09-01: Final validation passed: restore; 0-warning/0-error Release build; 310 passing solution tests; clean format; central and Founder Scout no-pending-model-change checks; Stage 18 focused Application 4, Infrastructure 6, Windows 7, Host 2 plus one intentional fixture skip; and one rendered Playwright workflow. The in-app Browser rejected loopback with `ERR_BLOCKED_BY_CLIENT`, so the documented local Playwright fallback was used and passed.

## Validation results

- `dotnet restore HomeBusinessAssistant.sln` — passed for all 29 projects.
- `dotnet build HomeBusinessAssistant.sln -c Release --no-restore` — passed with 0 warnings and 0 errors.
- `dotnet test HomeBusinessAssistant.sln -c Release --no-build` — passed 310, failed 0; two Host interactive fixtures were intentionally skipped and the live provider remained opt-in.
- `dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore` — passed. Its first run found one import-order issue, which was fixed before the passing rerun.
- Both central and Founder Scout `dotnet-ef migrations has-pending-model-changes` — passed with no changes.
- Populated Stage 17 upgrade — passed from `20260831181441_AddOperationalAttentionAndSummaries` to `20260901181413_AddOnboardingFoundation`, preserving the user-authored configuration and producing a completed legacy baseline rather than a forced redirect.
- Rendered UI — in-app Browser was blocked from loopback by `ERR_BLOCKED_BY_CLIENT`; repository Playwright passed 1440x900 and 390x844 with fresh redirect, five steps, seven cards, defer/reminder/session-cookie dismissal/direct resume, no overflow, no console/page errors, and no path leakage.

Intermediate evidence retained: the first strengthened-test build exposed two missing test-helper namespace imports; two focused assertions then exposed an incorrect expected exception type and a missing test content root. Those harness issues were corrected, and the final focused/full build/test runs above supersede them.

## Remaining risks and follow-up

- Task Scheduler and wake capability are machine-dependent; Stage 18 reports safe categories and caveats without claiming that a hardware wake will succeed.
- DPAPI proves the current process/user boundary only. It does not make secrets portable or protect against code already running as the same Windows user.
- Stage 19 must consume `agent-selection-pending`, implement pending-agent semantics, and retain the no-auto-enable/no-auto-schedule/no-auto-run invariants.
- Stages 20–22 still own browser/provider/API-key checks, agent-specific setup, guided diagnostics, schedule creation, explicit enablement, and full onboarding completion.
