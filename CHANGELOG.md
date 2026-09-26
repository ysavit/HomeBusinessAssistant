# Prompt Pack Changelog

## Implementation — Stage 11 — 2026-08-30

- Added versioned deterministic Founder Scout parsing, canonical hashes, protected-attribute redaction, identity resolution/conflicts, relevant-field diffs, and configurable fast screening.
- Added durable snapshot processing leases, atomic screening commits, conservative merge behavior, parser-health stops, manual screening overrides, and the additive Founder Scout processing migration.
- Implemented `analyze --phase screen --max <N>` and composed no-AI processing into `run`, with progress, metrics, reason distributions, and checkpoints.
- Added typed processing configuration, Host editor support, and local fixture/Runner validation for idempotency and relevant profile changes.

## Version 2.0 — 2026-08-29

Added durable Codex stage-handoff context and a detailed bootstrap workflow:

- `START_IMPLEMENTATION_PROMPT.md` — one prompt to begin implementation and complete Stage 00.
- `docs/PROJECT_STATE.md` — canonical verified repository/stage state.
- `docs/adr/README.md` — ADR usage and decision threshold.
- `docs/adr/ADR-TEMPLATE.md` — reusable ADR template.
- Updated `AGENTS.md` to require state-file reading and updates.
- Updated `.agent/PLANS.md` to synchronize ExecPlans with project state.
- Updated every stage prompt to read and update `docs/PROJECT_STATE.md`.
- Expanded Stage 00 with repository-state initialization and environment-aware validation.
- Updated `README.md` and regenerated combined prompt/context documents.
- Added SHA-256 manifest for integrity checking.

## Version 1.0 — 2026-08-29

Initial 18-stage Home Business Assistant / Founder Scout Codex implementation prompt pack.
