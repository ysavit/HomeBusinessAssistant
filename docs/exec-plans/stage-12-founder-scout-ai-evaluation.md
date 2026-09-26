# Stage 12 ExecPlan — Founder Scout AI Evaluation

## Purpose and user-visible outcome

Stage 12 turns protected-attribute-free Founder Scout evaluator inputs into versioned, grounded evaluations, deterministic scores, recommendations, and two human-review-only introduction drafts. `FounderScout analyze --phase deep --max <N>` and `--phase all` process the durable Stage 11 queue with bounded concurrency, cache identical behavior-affecting inputs, classify provider failures, and emit stable JSONL metrics. Invitations remain manual; this stage adds no send API or action.

## Verified starting state

- The inherited Stage 11 Release solution builds with 0 warnings and 0 errors.
- Stage 11 stores canonical normalized/evaluator JSON and hashes, evidence/redaction metadata, deterministic screen decisions, and candidate-scoped expiring analysis claims in the separate migrated `founders.db`.
- The existing Stage 09 schema already has `Evaluations`, `EvaluationCategories`, `EvaluationRisks`, and `InvitationDrafts`, plus separate candidate quality/fit/confidence/activity/risk/priority columns. Stage 12 will extend focused queries and create a migration only if the EF model actually changes.
- `analyze` currently accepts only `--phase screen`; `run` stops after processing/reporting and makes no provider call.
- The stable `OpenAI` NuGet package available in the environment is 2.13.0. Current official OpenAI and Microsoft documentation expose Responses API strict JSON Schema in .NET; Azure's v1 route uses the OpenAI SDK endpoint shape and implicit versioning.

## Scope and non-goals

In scope are provider-neutral application contracts, an official-SDK Responses adapter for OpenAI/Azure OpenAI, private Runner secret resolution, strict response validation, behavior-complete cache hashing, grounded evidence checks, deterministic score/recommendation arithmetic, bounded retries/repair, draft validation/similarity, durable persistence, deep/all CLI composition, a diagnostic fake, and normal/opt-in tests. Live browsing, external enrichment, auto-send, queue UI, and Stage 13 result pages are out of scope.

## Architecture and data flow

```text
PendingAnalysis candidate + current processed snapshot + screen input
        |
        +--> atomically claim candidate lease
        +--> load versioned prompt/scorecard/persona/provider policy
        +--> calculate behavior-complete cache hash
        +--> completed valid cache hit? ---- yes --> reuse evaluation/draft
        |                                    no
        +--> provider-neutral model client (strict JSON Schema)
        +--> schema/key/bound validation
        +--> evidence and numeric-claim grounding
        +--> C# quality/fit/confidence/activity/risk/priority calculation
        +--> deterministic draft validation / one targeted repair maximum
        +--> short persistence operations + claim finalization
        +--> StrongConnect | ExploratoryCall | Monitor | Pass | ManualReview
```

## Implementation decisions

- Use the official `OpenAI` .NET SDK Responses client for both providers. OpenAI uses its v1 endpoint; Azure OpenAI uses the configured resource `/openai/v1/` endpoint and deployment in the model field. The optional API-version field is reserved for a future compatibility adapter; the current v1 adapter rejects it to avoid ambiguous versioning. The SDK transport is backed by an `IHttpClientFactory` client with bounded timeout/cancellation.
- Keep `IFounderEvaluationModelClient` in Founder Scout Application. Infrastructure owns SDK types, endpoint/authentication, status classification, response extraction, and payload-size bounds.
- Resolve opaque secret references in Runner through `ISecretStore` into the already private, current-user ACL-restricted execution-input file. Missing values are omitted, never logged, and deep analysis fails closed with attention required. The file is deleted by the existing Runner lifecycle.
- Store the full validated, typed evaluation envelope (including fit dimensions, hashes, validation metadata, and provider-attempt summaries) in bounded `StructuredEvaluationJson`; normalized quality categories and risks continue to use existing child tables. This avoids duplicating already-versioned Stage 09 schema.
- Cache identity includes evaluator input, scorecard/prompt/schema/persona/config/provider/model policies, activity policy/input, language/tone, and draft limits. Candidate/snapshot correlation IDs and secret material are excluded.
- Founder quality is `round((BaseScore - ctoFit) / 90 * 100, 2, AwayFromZero)`. Fit is the sum of exact 100-point dimensions. Overall confidence is a documented weighted blend of model confidence, profile completeness, and evidence coverage. All arithmetic and recommendation routing are C# authoritative.
- Transient failures receive at most three total provider attempts with exponential delay plus deterministic jitter and `Retry-After` support. Permanent authentication/configuration/model failures stop the batch with an attention-required result. Invalid schema/evidence/draft output shares one targeted repair allowance per candidate.
- Draft validation is conservative and never sends. A failed regenerated draft is persisted as `NeedsReview` while a structurally/evidentially valid evaluation remains usable only through the manual-review route.

## Milestones

1. Add evaluation/prompt/scorecard contracts, strict schema and typed validation, cache hashing, scoring, evidence, retry, and draft policies.
2. Add official SDK provider adapter, deterministic fake, private secret resolution, and focused persistence/cache/draft queries.
3. Compose deep/all/run command paths, bounded concurrency, metrics/checkpoints, and manifest/configuration changes.
4. Add unit, SQLite, command/protocol, fake-provider Runner smoke, and default-skipped live tests.
5. Run restore/build/test/format, EF drift, stage smoke, and documentation gates; update the canonical handoff.

## Progress

- [x] 2026-08-30: Read repository guidance, canonical state, planning rules, architecture, ADR-0007/0009, configuration examples, acceptance matrix, Stage 12 prompt, and current processing/persistence/command tests.
- [x] 2026-08-30: Verified the inherited Release solution builds with 0 warnings and 0 errors.
- [x] 2026-08-30: Inspected current stable OpenAI/Azure packages and official Responses/structured-output endpoint guidance.
- [x] 2026-08-30: Implemented application evaluation, validation, scoring, retry, cache, and draft policies.
- [x] 2026-08-30: Implemented provider/secret/persistence adapters and deep/all CLI composition.
- [x] 2026-08-30: Added focused tests and passed the required 20-profile Runner smoke, including 24 provider requests, four bounded retries/repairs, one cache hit, and 20 persona-triggered reevaluations.
- [x] 2026-08-30: Completed restore, final zero-warning Release build, 251-test solution run, exact format verification, both EF model-drift checks, and the canonical Stage 13 handoff.

## Validation plan

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
dotnet tool run dotnet-ef migrations has-pending-model-changes --project agents/FounderScout/FounderScout.Infrastructure --startup-project agents/FounderScout/FounderScout.Infrastructure --configuration Release --no-build
```

The required real Runner smoke will use 20 synthetic profiles and the explicitly labeled deterministic fake provider. It will cover every recommendation state, one schema repair, one evidence failure, one transient retry, one draft failure, unchanged-input cache reuse, and version-triggered reevaluation without any browser or live provider access. A separate synthetic live-provider test remains skipped unless its explicit opt-in configuration and secret are present.

## Validation outcome

- `dotnet restore HomeBusinessAssistant.sln` succeeded for all 25 projects.
- `dotnet build HomeBusinessAssistant.sln -c Release --no-restore` succeeded with 0 warnings and 0 errors.
- `dotnet test HomeBusinessAssistant.sln -c Release --no-build` passed 251 tests across 10 projects. The explicit synthetic live-provider test did not run by default.
- The first exact format check found one import-order issue in the provider adapter; after correction, `dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore` exited 0.
- Central and Founder Scout `has-pending-model-changes` checks both reported no changes. Stage 12 adds no migration.
- The real Runner/fake-provider smoke passed with 20 profiles, 24 provider requests, 4 retries/repairs, 1 cache hit without another evaluation, 20 persona-triggered reevaluations, grounded drafts, protected-marker exclusion, and `invitationsSent: 0`.

## Recovery and rollback

- Candidate analysis claims remain expiring and single-owner. Per-candidate failure releases only that item; other workers continue.
- Provider calls and validation occur outside SQLite transactions. Existing evaluation/draft uniqueness and cache lookup make a crash after partial persistence recoverable on lease expiry.
- Completed evaluations are append-only versioned records. Changing prompt/scorecard/persona/provider policy creates a new input hash rather than mutating history.
- Removing Stage 12 application/provider code does not invalidate Stage 11 normalized captures. Any additive migration will be documented with its backup/rollback prerequisite.

## Remaining risks and follow-up

- Live model/deployment support varies by provider and region; normal CI proves only the provider contract with the deterministic fake.
- Conservative paraphrase grounding can route truthful but weakly traceable output to manual review. The policy favors false review over fabricated ranking.
- Stage 13 must expose validation errors, evaluation versions, separate score dimensions, recommendations, and human-only invitation review/send recording.
