# Stage 11 — Founder Scout Processing Pipeline

Paste this entire prompt into Codex from the repository root after Stage 10 passes.

---

You are implementing **Stage 11: Versioned Parsing, Normalization, Identity Resolution, Deduplication, Protected-Attribute Redaction, Change Detection, and Fast Screening**.

Read repository guidance, Founder Scout capture/domain code, fixtures, configuration, and this prompt. Create/update `docs/exec-plans/stage-11-founder-scout-processing-pipeline.md`.

## Repository-state handoff

Before planning or coding:

1. Read `docs/PROJECT_STATE.md` completely.
2. Inspect the repository and verify the previous stage's claimed state; do not trust the state file blindly.
3. Reconcile any mismatch in the stage ExecPlan before implementation.

Before the final report:

1. Update `docs/PROJECT_STATE.md` with capabilities actually implemented, stage status, material files/projects, migrations/configuration/protocol versions, actual validation results, known limitations, and the exact next prompt.
2. Distinguish `Implemented` from `Validated` and preserve failed checks until a later verified pass.
3. Do not include secrets, cookies, authentication state, tokens, or real founder profile content.

## Goal

Transform raw captured profiles into normalized, evidence-preserving candidate snapshots; resolve duplicate identities across accounts/segments; detect profile changes; remove irrelevant/protected attributes from evaluator input; perform deterministic fast screening; and queue deep analysis idempotently. No AI provider call in this stage.

## 1. Normalized profile model

Create a versioned `NormalizedFounderProfile` DTO containing only fields relevant to founder evaluation and operations:

- source profile key and canonical URL;
- display name;
- normalized location/time-zone compatibility text when visible;
- technical/non-technical/unknown status;
- full-time/part-time/unknown commitment;
- committed-to-idea/open-to-ideas/unknown status;
- roles the founder can own: design, sales/marketing, operations, product, domain, fundraising, other;
- introduction/about text;
- career/education/building/leadership background text;
- startup/problem/customer/solution description;
- progress/traction/validation claims exactly as stated;
- co-founder desired skills/role/commitment/equity posture when stated;
- industries/interests;
- last-seen/activity text and parsed timestamp/range only when reliably inferable;
- parser warnings and missing-field list;
- profile completeness score;
- parser/source versions.

Do not include age, gender, race/ethnicity, religion, photo/image data, marital/family status, disability/health data, sexual orientation, or other protected/irrelevant attributes in this DTO.

Raw snapshot retention is separate and may contain visible page text temporarily; evaluator inputs and normalized tables must exclude these attributes.

## 2. Versioned parser

Implement `IFounderProfileParser` with a parser version.

Inputs:

- captured structured fields;
- raw visible text;
- source adapter metadata.

Outputs:

- normalized profile;
- field-level evidence mapping to source sections/text spans or safe snippets;
- completeness;
- warnings/errors;
- parser-health result.

Requirements:

- prefer structured semantic sections from the browser adapter;
- use deterministic text parsing for known labels;
- no LLM parsing in V1 pipeline stage;
- unknown stays unknown;
- do not infer revenue/customers/commitment/equity from vague language;
- preserve exact claim text separately from normalized enums;
- parser failure must not overwrite a previously valid normalized snapshot;
- store parser version and normalized JSON on the snapshot or related entity;
- create fixtures for common profile variations, missing sections, formatting changes, Unicode, and hostile text.

## 3. Canonical normalization and hashes

Implement deterministic canonicalization:

- Unicode normalization;
- normalized line endings/whitespace;
- stable ordering of sets/lists where order is semantically irrelevant;
- normalized URLs (allowed host, fragment removal, conservative query handling);
- lowercased keys where appropriate without altering evidence text;
- canonical JSON with stable property ordering.

Calculate:

```text
RawContentHash
NormalizedProfileHash
EvaluatorInputHash
```

Use SHA-256. Document what each controls.

A profile is unchanged for analysis only when the relevant normalized/evaluator hash and all evaluator version inputs are unchanged.

## 4. Identity resolution and deduplication

Implement `ICandidateIdentityResolver` with ordered evidence:

1. stable source profile key;
2. canonical profile URL;
3. trusted source-specific ID extracted from URL/page;
4. strong deterministic fingerprint using normalized display name + location + stable introduction/background fragments;
5. manual merge/split action.

Rules:

- strong IDs merge automatically;
- weak fingerprints produce a match candidate/conflict record when ambiguous, not an automatic destructive merge;
- cross-account/segment sightings attach aliases and snapshots to one Candidate;
- display-name changes do not create a new candidate when stable ID matches;
- manual merge preserves all snapshots/actions/evaluations and records an audit/action;
- manual split is supported or explicitly deferred with a safe migration strategy;
- identity conflicts are visible for later UI/manual review.

Add a `CandidateIdentityConflict` entity if needed.

## 5. Profile change detection

When a captured snapshot arrives:

```text
resolve candidate
compare normalized hash to current valid snapshot
if identical:
    update last seen/activity/account metrics
    no analysis queued
if changed:
    persist normalized snapshot
    mark current snapshot
    create profile-change action with bounded changed-field summary
    queue screening/analysis
```

Do not compare raw HTML hashes for analysis decisions because layout noise can change. Preserve both raw and normalized hashes.

Implement a safe structured diff for relevant fields, not a full raw-text diff in audit.

## 6. Protected-attribute redaction

Implement `IProfileRedactor` that produces `FounderEvaluationInput` from normalized profile/evidence.

Requirements:

- exclude protected/irrelevant fields and image references;
- remove clearly labeled age/gender/family/religion/health passages from unstructured sections where feasible through deterministic label/pattern rules;
- do not use protected values for scoring, ranking, dedupe, or invitation drafts;
- preserve only operational location/time-zone/relocation information;
- emit redaction reason codes/counts, not removed values;
- test false positives and ensure business terms are not unnecessarily removed;
- allow manual review when redaction confidence is low.

## 7. Deterministic fast screening

Implement a versioned rules engine producing:

```text
Outcome: DeepAnalyze | Monitor | FilterOut | ManualReview
ScreeningScore 0..100 optional
ReasonCodes
Evidence snippets
MissingEvidence
RulesetVersion
```

Initial configurable signals:

Positive/preferred:

- non-technical or complementary founder;
- explicitly seeking technical co-founder/CTO;
- full-time or credible transition plan;
- owns sales, operations, design, domain, product, or distribution;
- clear customer/problem;
- traction/validation/customer access evidence;
- founder-level/equal partnership posture;
- US/time-zone compatibility when configured.

Risk/filter/manual-review signals:

- explicitly seeking only unpaid implementation labor/contract developer;
- indefinite part-time posture without transition;
- expects technical co-founder to own product, engineering, sales, fundraising, and operations;
- idea depends on unavailable external access with no validation;
- no meaningful profile content;
- technical founder seeking only another identical technical role when preference excludes it;
- contradictory or parser-low-confidence content.

Important:

- missing traction is not proof of zero traction;
- missing equity information is unknown, not negative by itself;
- rules produce grounded evidence;
- protected attributes never participate;
- thresholds and hard-filter rules are configuration/versioned;
- a hard filter must have an explicit reason code and be reversible/manual-overridable.

## 8. Analysis queue

Implement processing command behavior:

```powershell
FounderScout.exe analyze --phase screen --max <N>
```

It should:

- claim candidates/snapshots needing parsing/screening;
- parse/normalize/redact/screen;
- persist decisions/actions atomically per candidate;
- transition to `PendingAnalysis`, `FilteredOut`, `Monitor`, or `ManualReview` equivalent state;
- emit progress/metrics/checkpoints;
- avoid duplicate work under concurrent process attempts;
- release/expire claims on failure;
- create an immediate deep-analysis occurrence or leave pending work for Stage 12, according to existing orchestration pattern.

Update `FounderScout.exe run` to compose discovery and currently implemented processing accurately. It must not wait/sleep for cooldown.

## 9. Parser health

Define health thresholds:

- required field groups found;
- expected section labels/roots;
- minimum visible text bounds;
- ratio of recognized sections;
- source adapter/parser version compatibility.

If a run encounters repeated parser-health failures, stop discovery/processing and set account/segment `ParserFailure` rather than generating low-quality evaluations.

## 10. Tests

Cover:

- all normalized fields and unknown handling;
- protected fields absent from DTO/schema/evaluator input;
- Unicode/whitespace/URL canonicalization;
- hash stability and changes only when relevant data changes;
- stable ID, canonical URL, strong fingerprint, weak ambiguity, cross-account duplicates;
- candidate rename with stable ID;
- profile-change structured diff;
- no requeue for normalized unchanged profile despite raw layout noise;
- redaction patterns/false positives/low confidence;
- every screening reason code;
- missing evidence semantics;
- configurable thresholds/ruleset version;
- hard filter override/audit;
- parser-health stop threshold;
- work claim concurrency/idempotency/recovery;
- 20-profile batch protocol metrics;
- no AI calls.

Use sanitized fixture profiles with a broad variety of realistic content, but no real personal profile data.

## Constraints

- No AI provider call or semantic embedding dedupe.
- No protected-attribute scoring.
- No candidate UI beyond existing generic agent/run pages.
- No invitation sending.
- Do not rewrite browser acquisition unless required for a proven capture envelope issue.

## Done when

- Captured profiles become stable normalized snapshots.
- Cross-account duplicates converge safely.
- Unchanged profiles skip analysis.
- Protected attributes are excluded.
- Fast screening is grounded, deterministic, versioned, configurable, and auditable.
- Pending deep-analysis queue is populated idempotently.
- All validation passes.

## Required smoke validation

Using local fixtures through Runner:

1. import/discover 20 profiles containing duplicates, one changed profile, missing fields, and protected labels;
2. run screening batch;
3. verify candidate/snapshot/alias/decision/action counts;
4. rerun and verify no duplicate work;
5. change one relevant field and verify one candidate requeues;
6. prove evaluator input artifacts contain no protected values from the fixture markers;
7. print reason-code distribution and protocol summary.

## Final report

Include normalized model, hash purposes, identity precedence, redaction boundaries, screening rules/version, smoke counts, and deferred AI behavior. End with:

```text
Next prompt: prompts/12-founder-scout-ai-evaluation.md
```

