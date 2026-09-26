# ADR-0017: Founder Scout onboarding and disabled authentication

- Status: Accepted
- Date: 2026-09-01
- Stage: 20
- Owners: Home Business Assistant

## Context

Founder Scout needs a resumable specialized setup for its dedicated browser account, source limits, sensitive local persona, optional Responses provider, and protected key. Manual headed authentication must happen before a first-run account or the platform agent is enabled, but the existing manual-run boundary and Runner reject disabled agents. Temporarily enabling the agent would make unrelated commands runnable and would confuse setup with activation.

Readiness evidence must remain durable without copying the typed configuration, key, browser state, persona, provider bodies, or full profile path into onboarding persistence. Provider checks can incur cost and must not become implicit retries or candidate evaluations.

## Decision drivers

- Authentication must still use the central durable occurrence and Runner supervision boundary.
- The exception for a disabled agent must be immutable, explicit, and false by default.
- Account creation is disabled-first; the browser profile exists only when an explicit authentication action opens it.
- Live source consent, browser readiness, authentication, and optional provider readiness must be truthful, durable, and privacy-safe.
- Stage 20 must not create schedules, start discovery/analysis, enable the agent, or add invitation sending.

## Decision

Replace the Founder Scout placeholder with a code-owned `FounderScoutOnboardingAdapter` and a specialized application service. It reads and saves `FounderScoutConfiguration` through `IAgentConfigurationService`, updates existing records with typed `with` mappings so optional settings survive, uses focused Founder Scout account/segment repositories, and stores only immutable revision identities plus safe agent-scoped `OnboardingCheck` results.

Add immutable `AllowDisabledAgent` to `ScheduleOccurrence`, default false. The specialized onboarding service is the sole Stage 20 creator that sets it true, and only on an unscheduled `ManualUi` Founder Scout `authenticate` occurrence with a bounded account ID. Runner additionally checks that exact unscheduled/manual/authenticate shape before honoring the flag; a flagged discovery or other command still fails closed. Normal manual runs and every existing occurrence remain unchanged. The account stays disabled through launch and becomes enabled/healthy only after the agent observes successful authentication. Safe-stop failures preserve disabled state and only a bounded status/reason.

Browser runtime checks compare the application-pinned Playwright package revision and launch only the already installed headless runtime; they never download on GET. The UI provides the reviewed manual repair command instead of an automatic installer.

Deep-analysis provider readiness is a separately confirmed one-shot network action. Infrastructure uses the existing official OpenAI .NET Responses SDK for both OpenAI and Azure OpenAI v1, zero retries, a short timeout, `store=false`, and a minimal strict `{ ok: boolean }` request containing no founder/profile/persona data. Results classify credential, forbidden, deployment, quota/rate-limit, network/TLS/timeout, malformed/rejected, and success. Persistence contains only provider kind, safe endpoint host/hash, safe model identifier, latency bucket, result code, and timestamps. Capture/screen-only mode requires neither key nor provider check.

## Alternatives considered

### Temporarily enable Founder Scout

This widens the runnable command surface and makes first-run setup indistinguishable from explicit activation. An occurrence-local immutable permission was chosen.

### Launch Founder Scout directly from Host

Direct launch bypasses durable claim, configuration pinning, timeout, process-tree supervision, logs, events, and recovery. Runner-only dispatch was retained.

### Reuse the full evaluation pipeline as a provider check

It would require candidate/persona content, produce domain output, consume more tokens, and complicate safe persistence. A minimal dedicated official-SDK request was chosen.

### Persist a wizard JSON document

That would create a second configuration authority and risk retaining persona/key/browser data. Existing immutable configuration, focused domain records, selection state, and safe checks were reused.

## Consequences

- Headed authentication can be safely supervised before activation without opening other disabled-agent runs.
- A disabled account can retain challenge/auth/access status while remaining non-runnable.
- The central database gains one additive Boolean occurrence column; `founders.db` needs no migration.
- Explicit provider tests may cost a small amount and are never retried automatically.
- Stage 22 remains responsible for the first diagnostic, schedule review/creation, and enablement.

## Validation

- Specialized adapter/step and official-SDK provider route/classification/payload tests.
- Real SQLite occurrence permission round-trip/default-false and migration/drift tests.
- Existing Founder Scout browser enforcement fixtures plus disabled-first account lifecycle coverage.
- Antiforgery and desktop/narrow rendered wizard validation without secret/persona/browser-state leakage.

## Follow-up work

- Stage 21 implements Wake & Remote specialized onboarding.
- Stage 22 consumes `ReadyForValidation` for explicit diagnostics, schedule templates, activation, and onboarding completion.
