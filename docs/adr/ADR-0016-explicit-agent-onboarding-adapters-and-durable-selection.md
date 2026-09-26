# ADR-0016: Explicit agent-onboarding adapters and durable selection

- Status: Accepted
- Date: 2026-09-01
- Stage: 19
- Owners: Home Business Assistant

## Context

Stage 18 ends at a durable `agent-selection-pending` step. The platform needs to present installed agents without treating installation as owner consent, remember select/defer choices across Host restarts, and reuse Stage 17's safe generic configuration editor. It must also leave room for Founder Scout and Wake Remote to receive specialized guided flows without loading code or UI definitions from agent packages.

Package presence, scanner-seeded configuration, and manifest capabilities are insufficient evidence that an agent was reviewed or authorized. Configuration JSON and protected values already have authoritative storage boundaries and must not be copied into onboarding state.

## Decision drivers

- Selection and per-agent progress must be restart-safe, idempotent, stale-write safe, and auditable.
- A package must never supply executable onboarding UI or dynamically loaded in-process code.
- Generic rendering must remain restricted to the already validated primitive/enum/simple-array/opaque-secret-reference schema subset.
- Existing configuration, schedules, runs, and enablement must survive re-entry and package disappearance.
- Selection must not enable, schedule, diagnose, or execute an agent.

## Decision

Store one `OnboardingAgentSelections` row per `(SessionId, AgentId)` in `assistant.db`. Each row records the explicit selection state, progress, adapter/step identifiers, reviewed package/schema versions, starting and saved immutable configuration revision identities/hashes, safe reason codes, timestamps, and an optimistic revision. It never stores configuration JSON or secret material.

Application owns `IAgentOnboardingAdapter` and the pending-agent policy. Host explicitly composes one adapter per specialized agent plus a platform generic fallback. Resolution checks the explicit code-owned map first and uses the generic adapter only when the Infrastructure-loaded schema has already passed the Stage 17 parser with `SupportsGenericEditor`. A complex or missing schema without an explicit adapter fails closed as `Custom setup adapter required`. No assembly discovery, reflection over package binaries, scripts, or package-defined Razor is permitted.

Infrastructure performs bounded, root-confined, non-executing inspection of the registered package's folder, manifest, executable, identity/version, and configuration schema. The pending projection combines that result with the authoritative current configuration and the latest durable review. Scanner-authored defaults can be valid but remain pending until explicit owner review. A patch-only package change is informational; schema incompatibility, current validation failure, required protected-secret failure, or adapter policy can require review.

Selection saves cover every currently available supported agent: checked agents become `Selected`; unchecked agents become `Deferred`. Nothing is preselected from installation state. Deferring all agents defers the session and retains a dashboard reminder. If a selected package disappears, reconciliation records `Unavailable` or `Removed` while retaining the selection and all runtime/configuration history.

The generic wizard uses the shared platform form codec and the existing authoritative `IAgentConfigurationService`. Successful saves store only the resulting revision identity in onboarding. Protected values use separate antiforgery-protected POSTs to `ISecretStore`; values are never returned in page models, TempData, audit data, or onboarding persistence. Existing settings and schedules are not reset when Agent Settings opens a reconfiguration session.

Founder Scout and Wake Remote use explicit pending specialized adapters in Stage 19. They direct owners to existing Agent Settings and remain pending until Stages 20 and 21 replace those adapters.

## Alternatives considered

### Preselect every installed or enabled agent

Installation and prior enablement are not current onboarding consent, particularly after upgrades or package scanning. Explicit unchecked-by-default selection was chosen.

### Discover onboarding implementations from agent assemblies

Loading package code into Host would violate process isolation and turn metadata discovery into code execution. Explicit composition and a bounded generic fallback were chosen.

### Copy configuration and secret values into onboarding rows

This would create competing authorities, weaken immutable revision history, and expose protected material. Onboarding stores only opaque revision identities and safe secret-existence status.

### Render every JSON Schema feature generically

Conditional, nested, union, and dictionary schemas require agent-specific semantics. Unsupported schemas fail closed and require a code-owned typed adapter.

## Consequences

- Owner choices and progress survive restart and concurrent tabs without enabling or executing anything.
- Newly scanned agents can produce a non-blocking pending banner on established installations.
- Adding a specialized flow requires a code change and explicit Host registration.
- The central database gains one additive table and focused foreign keys/indexes.
- Stage 22 remains the sole owner of diagnostics, schedule creation, activation review, and enablement.

## Validation

- Real SQLite selection persistence, idempotency, stale revision, upgrade, and package-removal tests.
- Explicit adapter-resolution and safe generic/complex-schema tests.
- Shared codec defaults, constants, typed arrays, invalid input, validation, and hostile-label encoding tests.
- Real Host/Playwright workflow with Founder Scout, Wake Remote, and Sample Business Agent packages; sample-only selection; configuration/secret save; restart/resume; no enablement/schedule mutation; removal reconciliation; desktop/narrow layout; antiforgery; and zero console/page errors or secret echo.
- Established-database scan of a new safe generic agent with a dashboard banner and no forced redirect.

## Follow-up work

- Stage 20 replaces the Founder Scout placeholder with its specialized account/browser/provider adapter.
- Stage 21 replaces the Wake Remote placeholder with its provider/power-window adapter.
- Stage 22 performs explicit diagnostics, schedule review/creation, activation, and onboarding completion.
