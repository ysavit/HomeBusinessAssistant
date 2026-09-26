# Stage 20 — Founder Scout Guided Onboarding

## Purpose and user-visible outcome

Replace the Stage 19 Founder Scout placeholder with a durable specialized wizard that configures a conservative browser account, discovery segment, screening/fit policy, and optional production AI provider without enabling the agent or starting discovery. The owner can explicitly launch headed authentication through Runner, inspect safe readiness results, and finish in `ReadyForValidation` for Stage 22.

## Current repository state

- Stage 19 is validated at central migration `20260901190322_AddAgentSelectionGenericOnboarding`; it supplies explicit onboarding adapter resolution, durable per-agent selections, immutable configuration references, and a resumable generic wizard.
- Founder Scout already owns typed configuration validation, focused account/segment repositories, browser safe-stop behavior, Responses API clients, DPAPI-backed secret references, and an independent agent `authenticate` command.
- The Host currently registers a Stage 20 placeholder adapter. Its existing Operations authentication path assumes the agent is enabled, so first-run authentication cannot yet use Runner without a narrowly scoped occurrence permission.
- No Stage 20 behavior may enable the agent, create schedules, start discovery/analysis, or add invitation/message sending.

## Design and data flow

```text
/Onboarding/FounderScout step forms
  -> FounderScoutOnboardingService (typed configuration + strict local policy)
  -> immutable Founder Scout configuration revision
  -> focused BrowserAccount / DiscoverySegment persistence
  -> agent-scoped safe OnboardingChecks

explicit Authenticate
  -> durable manual occurrence (AllowDisabledAgent=true, authenticate only)
  -> central Runner -> FounderScout authenticate -> headed persistent profile
  -> durable occurrence/run + account session state -> polling projection

explicit Test provider
  -> local typed validation + protected-secret lookup
  -> official Responses client, one minimal strict request, no retry
  -> stable safe classification -> agent-scoped safe check
```

The specialized adapter stores only its stable version/step, final immutable revision identity, and safe readiness/check references in onboarding persistence. Consent is an agent-scoped check: fixture-only is `NotApplicable`; live use requires an explicit acknowledgment. The browser profile path is always generated as `browser/<strict-account-id>` and is not created by GET.

## Milestones

1. Add typed onboarding contracts/service/adapter and strict account/segment/configuration mapping.
2. Add browser/provider probes and Runner-backed allow-disabled authentication occurrences.
3. Add the resumable Razor wizard, protected secret mutations, polling status, and safe review.
4. Add the central migration and focused/persistence/rendered tests; run all required gates and update canonical state.

## Progress

- [x] 2026-09-01: Re-read repository guidance, Stage 19 handoff and implementation, Stage 20 prompt, relevant Founder Scout configuration/browser/AI/persistence boundaries, Runner dispatch, ADRs, and browser/frontend testing skills.
- [x] 2026-09-01: Implemented the specialized adapter/service, typed revision saves, disabled-first account and segment mapping, no-download browser-runtime check, protected provider-key flow, one-shot Responses probe, narrowly scoped disabled-agent authentication occurrence, and account authentication state updates.
- [x] 2026-09-01: Replaced the placeholder with the seven-step Razor workflow, safe review/blockers, explicit confirmations, polling projection, no-AI path, Agent Selection routing, and responsive privacy-conscious styling.
- [x] 2026-09-01: Added the central occurrence migration, focused provider/persistence tests, complete synthetic non-live browser flow, architecture/ADR/README updates, and canonical handoff.
- [x] 2026-09-01: Final restore, zero-warning Release build, 328-test solution run, format verification, and both EF drift checks passed.

## Decisions

- A manual occurrence owns an immutable `AllowDisabledAgent` flag. Only the specialized onboarding service creates it, only for Founder Scout `authenticate`, and the default remains false for every other occurrence. This avoids temporarily enabling the platform agent or weakening schedule policy.
- Account records remain disabled until successful headed authentication. The agent promotes a successfully authenticated disabled account through `Unknown` to `Healthy`; failure preserves disabled state and stores only a bounded safe error.
- Capture/screen-only mode persists `Analysis.Enabled=false` and does not require an AI key or provider check. Deep analysis requires a configured protected secret and a successful explicit provider test.
- Provider checks use the existing official OpenAI client family, one minimal schema-constrained request, a short timeout, and no automatic retries. Persisted evidence excludes request/response bodies and credential material.
- Agent-scoped readiness rows are accepted while an onboarding session is in `agent-configuration` as well as the global `readiness` step. This keeps specialized consent/provider/browser/authentication evidence in the existing durable check model without creating a second check store.

## Validation plan

Completed: the repository standard gate and both EF model-drift checks passed. Focused provider checks passed 8/8; affected Playwright discovery/authentication checks passed 4/4; the specialized rendered workflow passed 1/1 across all seven non-live capture/screen steps at 1440×1000 and 390×844 with antiforgery and privacy assertions. The final solution run passed 328 tests with no failures. Existing Runner/browser fixtures cover success/challenge safe stops; live Startup School, a real provider, and a real browser session remain opt-in and are not claimed. The in-app Browser selected the fixture but blocked loopback navigation, so repository Playwright supplied rendered evidence.

## Recovery and rollback

The central migration is additive and removes only the occurrence permission column on rollback. Configuration and onboarding saves are immutable/idempotent; a conflict reloads current state instead of reverting a valid revision. Protected-secret replacement/deletion remains separately audited. No browser profile is deleted by rollback.

## Remaining risks and follow-up

- Stage 21 supplies Wake & Remote specialized onboarding.
- Stage 22 owns the first diagnostic, schedule creation, activation review, and explicit agent enablement.
- Real source/provider behavior and credentials remain operator-controlled opt-in checks.
