# ADR-0009: Founder Scout processing, identity, and hash semantics

- Status: Accepted
- Date: 2026-08-30
- Stage: 11
- Owners: Home Business Assistant

## Context

Founder Scout captures are immutable raw evidence, but deterministic processing must tolerate page-layout noise, avoid duplicate work across local processes, exclude protected attributes from evaluator input, and converge cross-account sightings without destroying ambiguous history. The Stage 09 analysis claim is candidate-scoped and intended for later AI evaluation, so reusing it for snapshot parsing would couple distinct recovery and idempotency boundaries.

## Decision drivers

- Raw evidence must survive parser, source-layout, and AI outages.
- Parsing and artifact I/O must not hold SQLite transactions open.
- Layout-only changes must not trigger another screening or later AI evaluation.
- Protected values must not enter normalized/evaluator JSON, scoring, identity fingerprints, protocol, or audit summaries.
- Strong identities may converge automatically, while ambiguity must remain reviewable and reversible.
- A crash must leave processing work reclaimable without allowing concurrent owners.

## Decision

Captured snapshots begin in `Captured` state and use a dedicated snapshot-scoped processing lease. A worker claims one snapshot atomically, reads and transforms its raw artifact outside a transaction, then commits normalized state, identity evidence, screening, candidate routing, and append-only actions in one short transaction. Expired leases are reclaimable; completion verifies worker ownership.

Three SHA-256 identities remain distinct:

- `RawContentHash` identifies captured visible/structured source content and controls capture idempotency.
- `NormalizedProfileHash` identifies canonical founder-relevant fields and controls relevant profile-change detection.
- `EvaluatorInputHash` identifies the protected-attribute-free payload supplied to screening and later evaluation.

Canonicalization applies Unicode NFC, line-ending/whitespace normalization, stable semantic-set ordering, conservative URL normalization, and stable-property-order JSON. A newly captured snapshot whose normalized and evaluator hashes equal the current valid snapshot is retained as immutable history but marked superseded and does not create another screen decision.

Identity precedence is stable source key, canonical URL, trusted source ID, then deterministic display-name/location/stable-background fingerprint. Strong unambiguous evidence may converge candidates. Weak or multi-owner evidence creates an append-only `CandidateIdentityConflict` and routes to manual review. Merge uses a logical source tombstone: mutable dependents move when safe, append-only historical actions/decisions/conflicts keep their original owner, and a colliding snapshot remains on the tombstone rather than being discarded. Manual split is deferred until provenance-aware clone-and-reassign tooling exists.

Protected fields are absent from `NormalizedFounderProfile`. The deterministic redactor also removes clearly labeled protected passages from unstructured founder-relevant text and persists only reason codes/counts, confidence, and evaluator-safe evidence. Screening is deterministic, versioned, configurable, grounded in retained evidence, and reversible through an append-only manual override.

## Alternatives considered

### Use the raw page hash for analysis idempotency

This would requeue layout-only changes and couple decisions to irrelevant HTML/source formatting. It was rejected.

### Reuse the Stage 09 candidate analysis lease

Parsing and later AI evaluation have different work units, failure modes, and retry lifetimes. Reuse would allow one phase to block or steal the other, so a snapshot processing lease was selected.

### Automatically merge every similar fingerprint

Names, locations, and biography fragments can collide. Destructive fuzzy merge was rejected in favor of confidence-qualified strong signals and explicit conflict records.

### Delete the source candidate after merge

Deletion would break attribution for append-only history and complicate rollback. A logical tombstone was selected.

## Consequences

### Positive

- Capture, processing, and later AI evaluation remain independently recoverable.
- Relevant-change decisions are deterministic and testable without browser or AI access.
- Protected attributes cannot become screening or evaluator inputs through typed fields.
- Ambiguous identity remains visible instead of silently corrupting candidate history.

### Negative / tradeoffs

- Logical tombstones and colliding snapshots require active-candidate filters in queries.
- Parser/redactor/ruleset changes require explicit version policy; normalized equality alone is insufficient for later AI cache decisions.
- Manual split remains unavailable until record provenance can be reassigned safely.

## Implementation and migration

- Migration `20260830231727_AddFounderScoutProcessingPipeline` adds processing/hash/evidence fields, candidate merge linkage, screening evaluator metadata, `CandidateIdentityConflicts`, and processing indexes.
- Legacy faux-normalized captures are reset to `Captured` for deterministic reprocessing; raw artifacts remain the recovery source.
- Configuration schema remains 1.0 with additive typed `processing` settings and safe defaults.
- Founder Scout manifest 1.3.0 advertises normalized processing, redaction, fast screening, deduplication, and durable processing leases.

## Validation

- Unit tests cover canonicalization, all normalized fields, unknown/missing semantics, redaction false positives, grounded screening, hard-filter override, diff behavior, and identity confidence.
- Temporary SQLite tests cover lease contention/expiry, atomic processing, strong merge, weak conflict, parser-health pause, and migration/model drift.
- A real Runner/real agent smoke imports 20 sanitized captures, proves no-work replay and one relevant-change requeue, verifies persisted counts/reason protocol, and searches every evaluator input for the protected fixture marker.

## Follow-up work

- Stage 12 must include snapshot/evaluator plus scorecard, prompt, provider, and evaluator versions in AI evaluation idempotency.
- Stage 13 should expose identity conflicts, merge, screening override, and eventually provenance-aware split controls.
