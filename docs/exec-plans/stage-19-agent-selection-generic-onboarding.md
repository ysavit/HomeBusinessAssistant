# Stage 19 — Agent Selection and Generic Per-Agent Onboarding

## Purpose and user-visible outcome

Add a restart-safe owner workflow that lists installed agent packages, requires an explicit select-or-defer choice, and configures safe generic-schema agents through the same immutable configuration and protected-secret boundaries used by Agent Settings. Built-in Founder Scout and Wake Remote remain disabled and show a clear specialized-setup-pending handoff for Stages 20 and 21.

The owner verifies this at `/Onboarding/Agents`: installed agents are not preselected, choices survive Host restart, Sample Business Agent can be reviewed/saved without exposing its secret, and unavailable or complex-schema agents fail closed.

## Current repository state

- Stage 18 is validated at central migration `20260901181413_AddOnboardingFoundation` with durable `OnboardingSessions`/`OnboardingChecks`, one active slot, optimistic session revisions, dashboard-only first-run entry, and an active `agent-selection-pending` handoff.
- Stage 17 already supplies `FileAgentConfigurationSchemaCatalog`, the bounded primitive/enum/simple-array/secret-reference schema parser and validator, disabled-first package scanning, immutable configuration revisions, and the Agent Settings generic editor.
- `OnboardingSession` has no per-agent selection children. The Host has no onboarding adapter registry, pending-agent projection, generic onboarding route, or Agent Settings re-entry command.
- Built-in configurations are typed and scanner/bootstrap defaults may already be valid; valid defaults are not proof that the owner reviewed an agent.
- No Stage 19 behavior may enable or execute an agent, create a schedule/occurrence, or dynamically load package assemblies.

## Scope and non-goals

In scope:

- Explicit available/unavailable/pending/selected/configured/ready/deferred semantics.
- Session-scoped durable selections with revision-safe transactional updates and immutable configuration references only.
- An Application-owned UI-neutral onboarding adapter contract, explicit Host registration, safe generic fallback, and typed-adapter-required failure.
- Selection, generic configuration, protected-secret, dashboard/Agents/Settings re-entry, unavailable-agent reconciliation, and audit flows.
- An additive EF migration plus Application, Infrastructure, Host, and rendered workflow tests.

Non-goals:

- Founder Scout or Wake Remote specialized forms/checks.
- Diagnostics, authentication, schedule creation, enablement, execution, or automatic package scanning/file watching.
- Arbitrary schema rendering, reflection/plugin assembly loading, scripts, or package-supplied wizard UI.

## Design and data flow

```text
validated package metadata + immutable current configuration + prior reviews
        -> Application pending/readiness policy
        -> explicit adapter registry (built-in placeholders first, safe generic fallback)
        -> /Onboarding/Agents select/defer
        -> OnboardingAgentSelections (IDs/hashes/versions/status only)
        -> /Onboarding/Agent generic form
        -> authoritative AgentConfigurationService + ISecretStore
        -> selection ReadyForValidation or NeedsAttention
```

`OnboardingAgentSelections` is unique by `(SessionId, AgentId)`. It stores selection/progress/status keys, reviewed manifest and configuration-schema versions, optional starting/saved immutable revision IDs and hashes, safe reason codes/timestamps, and its own optimistic revision. It never stores configuration JSON or secret values.

Package inspection remains file-based, bounded, root-confined, non-executing, and returns a safe projection. Adapter resolution is an explicit in-memory allow list composed by Host; the generic fallback is chosen only when the already parsed schema says `SupportsGenericEditor`. Built-in placeholder adapters direct the owner to existing settings and remain `NeedsAttention` until Stages 20/21 replace them.

Configuration POSTs use the shared generic form codec, then the existing configuration service. Protected secret POSTs are separate and never redisplay values. Selection persistence records only the resulting revision identity. Package disappearance changes the selection to unavailable/removed without deleting history or existing configuration/schedules/runs.

## Milestones

1. Add contracts, pending policy, adapter registry/fallback, shared generic form codec, and focused unit tests.
2. Add selection entity/mapping/repository and additive central migration with real-SQLite concurrency/removal tests.
3. Compose adapters and add selection/generic wizard plus dashboard/Agents/Settings re-entry and audit behavior.
4. Run migration, focused, full, format, drift, Host fixture, and desktop/narrow rendered validation; update documentation and canonical state.

## Detailed steps

- Extend `HomeBusinessAssistant.Application/Onboarding` with selection records/enums, package/adaptor contracts, pending derivation, selection orchestration, and safe secret/configuration operations.
- Extract the generic dictionary-to-JSON conversion from the Razor model into an Application-owned reusable codec; keep authoritative schema validation in `AgentConfigurationService`.
- Extend Infrastructure persistence entities/configuration/context/repository with `OnboardingAgentSelections`; use immediate SQLite transactions and optimistic revisions.
- Add a bounded Infrastructure package inspector reusing existing manifest/schema/path rules without executing agent content.
- Add `/Onboarding/Agents` and `/Onboarding/Agent`, preserve Post/Redirect/Get and antiforgery, and add non-blocking dashboard and management re-entry actions.
- Generate one additive central migration and validate upgrade/fresh drift.
- Update architecture, this plan, and `docs/PROJECT_STATE.md` with actual evidence only.

## Progress

- [x] 2026-09-01: Re-read repository instructions, Stage 18 handoff/architecture/ADRs, Stage 19 prompt, browser/frontend QA skills, and inspected the Stage 17–18 implementation paths.
- [x] 2026-09-01: Application selection/progress/adapter contracts, pending policy, explicit registry, safe generic/pending-specialized adapters, shared form codec, orchestration service, and focused tests implemented.
- [x] 2026-09-01: `OnboardingAgentSelections`, immediate-transaction repository operations, bounded package inspection, migration `20260901190322_AddAgentSelectionGenericOnboarding`, populated-upgrade/current-model tests, and removal reconciliation implemented.
- [x] 2026-09-01: Host composition, `/Onboarding/Agents`, `/Onboarding/Agent`, dashboard/Agents/detail/Settings re-entry, shared generic controls, antiforgery, audit, non-echoing secret actions, responsive styling, and rendered tests implemented.
- [x] 2026-09-01: Required restore/build/test/format/drift, fresh/sample/new-agent/removal, restart, hostile encoding, desktop/narrow, and interactive-fixture validation completed; architecture, ADR-0016, README, ExecPlan, and canonical project state updated.

## Decisions

- A valid scanner-seeded default counts as configured but remains pending until explicitly reviewed; the selection stores the reviewed immutable revision identity.
- Patch-only installed-version changes are informational for the generic adapter. Current validator failure, a changed/incompatible configuration schema, a missing required protected secret, or adapter policy can require review.
- Founder Scout and Wake Remote receive explicit placeholder onboarding adapters in Stage 19. This keeps resolution explicit and provides safe re-entry without pretending their Stage 20/21 prerequisites exist.
- No GET handler reads or mutates secret content. Secret values exist only in the separate POST argument and `ISecretStore` call.

## Validation

Planned exact gates:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/HomeBusinessAssistant.Infrastructure --startup-project src/HomeBusinessAssistant.Infrastructure --configuration Release --no-build
dotnet tool run dotnet-ef migrations has-pending-model-changes --project agents/FounderScout/FounderScout.Infrastructure --startup-project agents/FounderScout/FounderScout.Infrastructure --configuration Release --no-build
```

Actual final results on 2026-09-01:

- `dotnet restore` passed with all 29 projects current.
- `dotnet build HomeBusinessAssistant.sln -c Release --no-restore` passed with 0 warnings and 0 errors.
- `dotnet test HomeBusinessAssistant.sln -c Release --no-build` passed 318 tests with 0 failures; three interactive Host fixtures were intentionally skipped in the normal suite. Project pass totals were Domain 19, EndToEnd 3, AgentSdk 37, Application 39, Windows 28, Sample Business Agent 4, Wake Remote 19, Infrastructure 42, Host 17, Runner 24, and Founder Scout 86. The live provider test remains opt-in.
- Exact `dotnet format ... --verify-no-changes` exited 0. Its first run identified two whitespace-only findings; the corrected final standard gate passed.
- Both central and Founder Scout `has-pending-model-changes` checks reported no model changes. A populated Stage 17 database upgraded through Stage 18 to `20260901190322_AddAgentSelectionGenericOnboarding` without losing its configuration.
- The Stage 19 rendered suite passed 2/2: all three agent packages were available, none was preselected, only Sample Business Agent was selected, the others were durably deferred, generic defaults/typed arrays/configuration/DPAPI secret set/delete handling survived Host restart, no agent was enabled, onboarding created no schedule, one pre-existing schedule survived re-entry without duplication, removal retained history, hostile labels were encoded, antiforgery rejected a tokenless POST, and desktop 1440x1000 plus narrow 390x844 had no overflow, console/page errors, or secret echo. Synthetic screenshots remain outside the repository at `%TEMP%\hba-stage19-qa-desktop.png` and `%TEMP%\hba-stage19-qa-narrow.png`.
- A generated safe generic package scanned into an established database produced `New agent available — Set up` on Dashboard with HTTP 200 and no forced redirect.
- The interactive Stage 19 fixture started/stopped cleanly. The independently selected in-app Browser rejected its loopback URL with `ERR_BLOCKED_BY_CLIENT`; no bypass was attempted. The passing repository Playwright workflow is the documented fallback evidence.

Intermediate evidence retained for diagnosis: the first migration-generation command incorrectly used Runner as startup and failed because EF Design tooling is intentionally private to Infrastructure; rerunning with Infrastructure succeeded. Early compile/analyzer issues, a focused test fixture that referenced an unregistered agent, two representation-specific browser assertions, and the CSP/HTML-pattern errors exposed by the browser test were corrected. Final gate repetitions also exposed inherited timing races: a per-check readiness cancellation could be reported as cancellation instead of timeout, and SQLite online backup could retain a busy source/destination connection across retries. Timeout ownership is now classified explicitly and passed 20 consecutive focused repetitions; busy backup retries now reopen both connections and passed 10 consecutive active-writer repetitions. The final standard, focused, rendered, and drift gates above supersede those failures.

## Recovery and rollback

The migration is additive. Before release, rollback drops only `OnboardingAgentSelections`; configuration revisions, protected-secret files, schedules, runs, and agent definitions remain unchanged. A configuration save that succeeds before a later selection-update conflict remains a valid authoritative revision; reloading re-derives the selection from that revision rather than reverting or duplicating it. Package-removal reconciliation preserves every selection and runtime history row.

## Remaining risks and follow-up

- Stage 20 must replace the Founder Scout placeholder with its specialized browser/provider/account adapter.
- Stage 21 must replace the Wake Remote placeholder with its provider/power-window adapter.
- Stage 22 owns diagnostics, schedule templates, activation review, and explicit enablement.
- Live services, real credentials, Task Scheduler mutation, and hardware wake remain outside Stage 19 validation.
