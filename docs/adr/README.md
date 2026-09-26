# Architecture Decision Records

Use ADRs for durable, cross-cutting technical decisions or intentional changes to the baseline in `docs/architecture.md`. Do not create an ADR for routine implementation details, package patch versions, local formatting changes, or decisions already unambiguously mandated by repository guidance.

## Location and naming

```text
docs/adr/ADR-0001-short-decision-title.md
docs/adr/ADR-0002-next-decision.md
```

Accepted records:

- [ADR-0001](ADR-0001-agent-contract-ownership-and-event-discrimination.md) — agent contract ownership and event discrimination.
- [ADR-0002](ADR-0002-local-sqlite-coordination-and-current-user-secret-storage.md) — local SQLite coordination and CurrentUser secret storage.
- [ADR-0003](ADR-0003-deterministic-schedule-time-and-occurrence-semantics.md) — deterministic schedule time, DST, occurrence state, misfire, and fixed-delay semantics.
- [ADR-0004](ADR-0004-windows-wake-task-and-power-request-semantics.md) — one managed Windows wake task and reference-counted process power requests.
- [ADR-0005](ADR-0005-interactive-host-lifecycle-and-runner-dispatch.md) — single-instance interactive Host lifetime, bounded activation IPC, resilient loops, and Runner-only dispatch.
- [ADR-0006](ADR-0006-local-management-ui-boundary.md) — bounded local management reads, orchestrated writes, typed revision-aware configuration, and separate secret actions.
- [ADR-0007](ADR-0007-founder-scout-bounded-context-and-import-order.md) — separate Founder Scout persistence, cross-database correlation, and artifact-before-queue import order.
- [ADR-0008](ADR-0008-founder-scout-browser-session-and-stop-semantics.md) — Playwright ownership, dedicated persistent profiles, explicit installation, versioned source adapters, and safe-stop scheduling semantics.
- [ADR-0009](ADR-0009-founder-scout-processing-identity-and-hash-semantics.md) — separate processing leases, three hash identities, protected evaluator inputs, conservative identity convergence, and logical merge tombstones.
- [ADR-0010](ADR-0010-founder-scout-responses-provider-and-validation-boundary.md) — provider-neutral grounded AI evaluation and protected secret resolution.
- [ADR-0011](ADR-0011-founder-scout-results-review-and-report-boundary.md) — review, queue, outcome, report, and manual-send boundaries.
- [ADR-0012](ADR-0012-durable-local-operational-feedback.md) — durable operational attention, local notification, summaries, retention, and diagnostics.
- [ADR-0013](ADR-0013-windows-folder-deployment-and-online-backup.md) — Windows folder deployment, staged installation, and online backup/guarded restore.
- [ADR-0014](ADR-0014-safe-agent-extension-and-generic-configuration.md) — safe installed-agent discovery, default-disabled registration, limited generic configuration, and explicit package composition.
- [ADR-0015](ADR-0015-durable-onboarding-readiness-and-entry-policy.md) — durable readiness sessions/checks, owner routing, explicit warnings, and protected-storage probing.
- [ADR-0016](ADR-0016-explicit-agent-onboarding-adapters-and-durable-selection.md) — explicit adapter resolution, durable agent selection/progress, and safe generic onboarding.
- [ADR-0017](ADR-0017-founder-scout-onboarding-and-disabled-authentication.md) — specialized Founder Scout setup, occurrence-local disabled authentication, and minimal provider readiness checks.

Use the next sequential four-digit number. Never renumber accepted ADRs.

## Status values

- `Proposed`
- `Accepted`
- `Superseded by ADR-XXXX`
- `Deprecated`
- `Rejected`

## Required workflow

1. Inspect the current implementation and applicable architecture first.
2. Identify the decision and why existing guidance does not fully resolve it.
3. Evaluate realistic alternatives, including operational and security consequences.
4. Create the ADR from `ADR-TEMPLATE.md`.
5. Update `docs/architecture.md` when the accepted decision changes the described architecture.
6. Update `docs/PROJECT_STATE.md` with the decision and link.
7. Add or update tests that enforce the decision when practical.

## Decision threshold

Create an ADR when a choice materially affects one or more of:

- process or trust boundaries;
- project dependency direction;
- persistence/schema strategy;
- agent protocol compatibility;
- scheduling/time semantics;
- Windows wake/power behavior;
- secret handling;
- browser/account-session handling;
- deployment/update/rollback behavior;
- major framework or infrastructure dependency;
- a deliberate deviation from the supplied architecture.

## Source of truth

- `AGENTS.md`: permanent implementation rules.
- `docs/architecture.md`: current target architecture.
- `docs/adr/`: rationale and history of durable decisions.
- `docs/PROJECT_STATE.md`: current verified implementation/handoff state.
- Source, tests, migrations, and runtime evidence: proof of what actually exists.
