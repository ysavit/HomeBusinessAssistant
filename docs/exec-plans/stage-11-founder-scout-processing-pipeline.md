# Stage 11 ExecPlan — Founder Scout Processing Pipeline

## Purpose and user-visible outcome

Stage 11 turns durable Founder Scout captures into versioned, evidence-preserving normalized profiles and deterministic screening decisions. `FounderScout analyze --phase screen --max <N>` claims raw snapshots with a database lease, parses and canonicalizes them, resolves derived identities conservatively, produces a protected-attribute-free evaluator input, detects relevant profile changes, and routes each candidate to deep analysis, monitoring, filtering, or manual review without calling an AI provider.

## Verified starting state

- The Stage 10 Release solution builds with 0 warnings and 0 errors.
- Browser discovery and fixture import commit content-addressed capture artifacts and idempotent snapshots to the separate migrated `founders.db`.
- Capture currently stores structured fields as if already normalized and marks snapshots `PendingAnalysis`; `analyze` is deliberately deferred.
- Strong source keys and canonical URLs already converge across accounts. Derived fingerprint conflict persistence, normalized/evaluator hashes, redaction metadata, processing leases, and executable screening do not exist.
- The current Founder Scout migration is `20260830200319_InitialFounderScoutPersistence`; Stage 11 requires an additive migration.

## Scope and non-goals

In scope are the versioned normalized DTO, deterministic parser/canonicalizer/redactor/screening rules, conservative identity resolution and conflict records, structured change summaries, durable per-snapshot processing claims, atomic processing commits, parser-health stops, CLI/run composition, configuration validation, migrations, fixtures, and deterministic tests. AI evaluation, embedding dedupe, invitation generation/sending, and candidate-specific Razor Pages remain out of scope.

## Architecture and data flow

```text
captured snapshot + root-confined artifact
        |
        +--> atomically claim snapshot lease
        +--> read and validate capture envelope (no database transaction)
        +--> parse semantic fields, canonicalize, hash
        +--> resolve derived identity / record ambiguity
        +--> redact to FounderEvaluationInput
        +--> deterministic versioned screen
        +--> short atomic commit:
             snapshot + candidate + screening + actions + aliases/conflicts
        +--> PendingAnalysis | Monitor | FilteredOut | ManualReview
```

`RawContentHash` identifies visible/structured capture content and controls capture idempotency. `NormalizedProfileHash` ignores layout noise and controls profile-change detection. `EvaluatorInputHash` identifies the protected-attribute-free semantic input; combined with parser, redactor, ruleset, scorecard, prompt, and evaluator versions it controls later re-analysis.

## Implementation decisions

- Keep the Stage 10 artifact as the immutable raw record and make newly captured snapshots start as `Captured` with no normalized JSON.
- Add snapshot-scoped processing lease columns so Stage 11 never borrows the Stage 12 candidate analysis lease.
- Prefer structured semantic fields, then deterministic labeled-text extraction. Unknown values remain unknown and exact claims remain evidence text.
- Store field evidence and redaction reason metadata as bounded canonical JSON; never store removed protected values in evaluator input or redaction metadata.
- Treat source key, canonical URL, and trusted source ID as strong. A derived fingerprint is automatically attachable only when it has sufficient stable evidence and one unambiguous owner; ambiguity creates an append-only conflict and routes to manual review.
- Preserve the existing candidate as the aggregate owner. Automatic destructive merge is limited to unambiguous identity ownership; manual merge moves all dependent records in one transaction and appends an action. Manual split is deferred to a future explicit clone-and-reassign migration because guessing provenance would risk evidence loss.
- A normalized-unchanged capture remains immutable history but does not replace the current valid snapshot or append another screening decision.
- Parser-health failure never overwrites a previous valid normalized snapshot. Repeated failures for one account/segment stop the batch and mark the account `ParserFailure` and segment paused.
- Screening hard filters are explicit reason codes and can be reversed through a persisted manual override action; missing traction/equity is missing evidence, not negative evidence.

## Milestones

1. Add processing DTOs/contracts, canonical hashing, parser, redactor, identity policy, change differ, and rules engine.
2. Add processing configuration, domain states/actions, EF schema/migration, snapshot claims, identity conflicts, and atomic commit behavior.
3. Compose the `analyze --phase screen --max` command and make `run` execute the currently implemented acquisition plus processing phases.
4. Add unit, SQLite concurrency/recovery, command/protocol, and 20-profile Runner smoke tests.
5. Run migration/model, restore/build/test/format, smoke, and documentation gates; update the canonical handoff.

## Progress

- [x] 2026-08-30: Read repository guidance, current state, architecture, ADR-0007/0008, Stage 11 prompt, existing capture/domain/persistence/CLI code, and tests.
- [x] 2026-08-30: Verified the inherited Release solution builds with 0 warnings and 0 errors.
- [x] 2026-08-30: Added application/domain processing contracts, versioned normalized/evaluator models, canonical hashes, deterministic parser/redactor/screen, identity policy, and structured diff.
- [x] 2026-08-30: Added snapshot processing leases, atomic persistence, identity conflicts, logical merge tombstones, parser-health pause, manual screen override, and migration `20260830231727_AddFounderScoutProcessingPipeline`.
- [x] 2026-08-30: Implemented `analyze --phase screen --max`, no-sleep `run` composition, progress/metrics/reason/checkpoint output, manifest 1.3.0, and typed Host processing-policy editing.
- [x] 2026-08-30: Added 9 focused processing tests and strengthened real Runner/browser tests; Founder Scout passed 53/53 and the final solution passed 225/225.
- [x] 2026-08-30: Completed migration drift, restore, Release build, test, format, smoke, architecture/ADR, and canonical handoff documentation.

## Validation plan

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
dotnet tool run dotnet-ef migrations has-pending-model-changes --project agents/FounderScout/FounderScout.Infrastructure --startup-project agents/FounderScout/FounderScout.Infrastructure --configuration Release --no-build
```

The required Runner smoke uses only sanitized local fixtures. It imports 20 profiles including duplicate identities, missing fields, protected labels, and one changed profile; runs screening; verifies counts and reason distribution; reruns idempotently; changes one relevant field; and proves exactly one candidate is requeued while evaluator inputs contain none of the protected marker values.

## Validation outcome

- `dotnet restore` — passed; all projects up to date.
- `dotnet build HomeBusinessAssistant.sln -c Release --no-restore` — passed with 0 warnings and 0 errors.
- `dotnet test HomeBusinessAssistant.sln -c Release --no-build` — passed: 225 tests, 0 failed, 0 skipped.
- `dotnet test tests/FounderScout.Tests/FounderScout.Tests.csproj -c Release --no-build` — passed: 53 tests.
- Required Runner smoke — passed: initial 20 captures became 18 active candidates, 19 distinct snapshots, and 1 duplicate; the first screen completed 19; replay completed 0; one relevant change completed 1; final state was 18 candidates, 20 snapshots, 54 aliases, 20 decisions, 139 actions, and 0 conflicts, with no protected marker in evaluator input and `noAiCalls=true`.
- Founder Scout migration drift — passed after `AddFounderScoutProcessingPipeline`.
- Central migration drift — passed after `AddWindowsWakeAndPowerMetadata`; an initial invocation with Runner as startup failed because EF Design is intentionally private, and the documented Infrastructure-startup command superseded it.
- `dotnet format ... --verify-no-changes` initially found whitespace in new sources; `dotnet format ... --no-restore` corrected it and the final verification passed.

## Recovery and rollback

- Expired processing leases are reclaimable; worker ownership is checked on finalization.
- Parsing and artifact reads occur outside transactions. Each snapshot is committed in one short SQLite transaction.
- Failed processing releases the claim with a bounded error code and preserves any prior valid current snapshot.
- Identity ambiguity is additive and reviewable; it never causes a destructive automatic merge.
- The additive migration can be rolled back only after processing is stopped and a database backup is taken; existing raw snapshots remain the recovery source.

## Remaining risks and follow-up

- Source labels may drift; parser-health metrics stop poor-quality batches instead of silently degrading.
- Manual split is deliberately deferred until provenance-aware UI/workflow exists.
- Stage 12 must include all evaluator version inputs when deciding whether a pending candidate requires a new AI evaluation.
