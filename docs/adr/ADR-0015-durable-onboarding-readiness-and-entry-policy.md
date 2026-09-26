# ADR-0015: Durable onboarding readiness and dashboard entry policy

- Status: Accepted
- Date: 2026-09-01
- Stage: 18
- Owners: Home Business Assistant

## Context

The Stage 17 platform is usable after installation, but its startup health is an in-memory operational projection and agent setup begins on independent management pages. A fresh owner can therefore encounter schedules, provider settings, browser prerequisites, and Windows wake limitations without one resumable workflow. Adding onboarding also creates an upgrade hazard: built-in agent definitions and safe default configuration revisions already exist on every initialized database, so their mere presence cannot distinguish a fresh installation from an established Stage 17 database.

The workflow must remain local, current-owner authenticated, restart-safe, and honest about Windows Task Scheduler and hardware wake limitations. Readiness must not become an alternate configuration, scheduling, or execution authority.

## Decision drivers

- A first-run session and its check results must survive Host restarts and concurrent browser tabs.
- Established installations must not be forced through a newly introduced wizard or lose settings/history.
- Check failures must be isolated, bounded, safe to persist, and distinguish warnings from true blockers.
- Task Scheduler, power, browser, provider, and secret operations require explicit user intent and narrow adapters.
- Stage 18 must stop before agent selection, configuration, enablement, schedule creation, or Runner dispatch.

## Decision

Store `OnboardingSessions` and `OnboardingChecks` in `assistant.db`. Sessions use an optimistic revision and allow-listed lifecycle/step transitions. A filtered unique active slot prevents concurrent active platform onboarding, and a unique first-run initialization key prevents duplicate baseline sessions. Checks store only the latest result for `(SessionId, Scope, CheckKey, AgentScopeKey)`; append-only audit records retain batch history.

Classify an installation as established only when it has evidence of owner or operational use: a configuration revision not authored by `system-bootstrap`, any schedule, or any run. Scanner-seeded definitions and bootstrap defaults alone do not count. The first entry for an established database creates a terminal legacy baseline and shows a non-blocking review invitation. A seed-only installation creates one `FirstRun/InProgress` session.

Apply automatic routing only when the owner requests the dashboard and the fresh first-run session is active. The dashboard PageModel redirects to `/Onboarding`; health, static assets, authentication, About, Settings, activation, shutdown, and onboarding routes are not intercepted. Deferred sessions render a dashboard reminder and do not redirect.

Application owns the readiness contract and orchestrator. Each check declares a stable key/scope, required/explicit policy, and bounded timeout. The runner limits concurrency and total duration, isolates failures, and orders results deterministically. Infrastructure and Windows adapters return safe typed reason codes and schema-versioned allow-listed details. They do not persist raw command output, paths, identities, secret references, or exception text.

Ordinary readiness runs only from an antiforgery-protected POST. The DPAPI round trip is a separately labeled POST through `ISecretStore` and deletes its temporary value in `finally`; no secret is returned to the page or onboarding persistence. Task Scheduler and power checks call query/diagnostic methods only. No readiness handler registers/removes a task, changes power policy, enables an agent, saves agent configuration, creates a schedule/occurrence, or launches Runner.

Warnings require explicit acknowledgement. Blocked or expired/unknown required checks prevent readiness completion. Stage 18 completion advances the still-active session to `agent-selection-pending`; it does not mark full onboarding complete. Stage 19 owns the next transition.

## Alternatives considered

### Store onboarding in browser cookies or Host memory

This loses progress on restart, cannot coordinate tabs, and makes audit/backfill unreliable. It was rejected.

### Redirect every management route until setup completes

This creates loops and can hide About, diagnostics, repair, and health information needed to resolve blockers. Dashboard-only automatic entry plus explicit Settings access was selected.

### Infer freshness from agent/configuration row counts

Bootstrap and scanner state would misclassify every new installation as established. Evidence is restricted to non-bootstrap revisions, schedules, or runs.

### Let checks repair tasks, power policy, providers, or agent state

This mixes observation with high-impact mutation and weakens owner consent. Checks remain observational except for the explicit temporary secret probe.

## Consequences

- First-run state is restart-safe, concurrency-safe, and audit-linked.
- An upgrade remains usable immediately and can opt into the same readiness review.
- Hardware wake remains a warning/caveat rather than a false guarantee.
- The UI needs one more server-rendered route and the central database gains two additive tables.
- Later stages must extend the allow-listed step machine and preserve the no-auto-enable/no-auto-run boundary.

## Validation

- Fresh/established migration and entry-policy tests with real temporary SQLite.
- Concurrent initial-session creation, defer/resume, stale revision, latest-check replacement, warning acknowledgement, and no schedule/run mutation tests.
- Readiness failure/timeout isolation and protected-storage cleanup tests.
- Fake Task Scheduler/power tests asserting query-only behavior and safe detail projection.
- Real loopback Host routing, exclusion, owner authorization, antiforgery, encoding, desktop/narrow layout, and console-error validation.

## Follow-up work

- Stage 19 adds pending-agent discovery, explicit selection, and generic per-agent onboarding contracts.
- Stages 20 and 21 add typed Founder Scout and Wake Remote prerequisites without widening Stage 18 platform checks.
- Stage 22 creates the first diagnostic and schedule only after a reviewed summary and explicit confirmation.

