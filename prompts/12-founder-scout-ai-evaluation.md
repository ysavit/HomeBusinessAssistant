# Stage 12 — Founder Scout AI Evaluation, Scoring, and Introduction Drafts

Paste this entire prompt into Codex from the repository root after Stage 11 passes.

---

You are implementing **Stage 12: Structured AI Evaluation, Deterministic Score Calculation, Evidence Validation, Caching, Retry Policy, Personalized Introduction Drafts, and Invitation Priority**.

Read all repository guidance, normalized profile/redaction/screening code, configuration examples, architecture, acceptance matrix, and this prompt. Create/update `docs/exec-plans/stage-12-founder-scout-ai-evaluation.md`.

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

Implement deep candidate evaluation through a provider-neutral AI boundary with an initial configurable OpenAI/Azure OpenAI provider. Require strict structured output, grounded evidence, explicit unknowns, deterministic C# scoring, model/prompt/scorecard versioning, caching, bounded retries, two personalized introduction drafts per deep evaluation, deterministic draft validation, and ranked invitation priority.

The system must never auto-send an invitation.

## 1. Provider architecture

Define:

```csharp
public interface IFounderEvaluationModelClient
{
    Task<ModelEvaluationResponse> EvaluateAsync(
        FounderEvaluationRequest request,
        CancellationToken cancellationToken);
}
```

Keep provider SDK/HTTP details in `FounderScout.Infrastructure`.

Support configuration fields:

- provider: `OpenAI` or `AzureOpenAI`;
- endpoint;
- model/deployment;
- API key secret reference;
- request timeout;
- optional API version only when required by the selected Azure endpoint;
- max output size/tokens;
- reasoning/temperature controls only when supported and useful.

Before selecting a package/API approach:

- inspect current official stable .NET SDK support available in the environment;
- prefer an official OpenAI/Azure SDK that supports the current Responses API and strict JSON schema;
- if the required Azure structured-output behavior is not reliably supported by the SDK, implement a focused `HttpClient` adapter and record an ADR with endpoint/auth/version assumptions;
- do not hardcode a model name or obsolete API version;
- use `HttpClientFactory`, timeout, cancellation, and redacted logging;
- secrets are resolved from `ISecretStore` and never persisted in evaluation/config/logs.

Provide a fake deterministic model client for tests.

## 2. Evaluation request and cache key

Create `FounderEvaluationRequest` containing:

- redacted `FounderEvaluationInput`;
- scorecard version/content hash;
- prompt version/content hash;
- evaluator schema version;
- founder persona version/content hash;
- relevant configuration/threshold versions;
- candidate/snapshot IDs for local correlation only, not necessarily sent to the model;
- requested language/tone;
- maximum invitation lengths.

Calculate a deterministic input/cache SHA-256 from all behavior-affecting non-secret inputs. If a completed valid evaluation with the same input hash exists, reuse it without a provider call and record cache metrics.

Changing normalized input, scorecard, prompt, provider/model policy, persona, or evaluator schema must create a new evaluation.

## 3. Score model

Preserve the existing YC evaluation scorecard as the primary category model:

```text
Founder execution quality           20
Commitment and co-founder posture   15
Traction and validation             15
Market potential                    15
GTM and domain advantage            10
CTO fit                             10
Idea and problem clarity             5
Moat potential                       5
Technical feasibility                5
                                    ---
                                    100
```

Every category response contains:

- key;
- integer score in `0..maximum`;
- evidence snippets grounded in the provided profile input;
- explanation;
- missing evidence;
- confidence `0..1`.

The model must also return explicit fit dimensions, each `0..maximum`, total 100:

```text
Complementary skills/ownership      30
Founder-level/equity posture        25
Technical/domain alignment          20
Timing/location/working compatibility 15
CTO workload realism                10
                                    ---
                                    100
```

Do not treat unstated equity as a confirmed negative; mark unknown and lower confidence.

### Founder quality calculation

Calculate in C#:

- primary `BaseScore` from the 100-point scorecard;
- `FounderQualityScore` from the scorecard excluding the personal `CTO fit` category, normalized from 90 points to 100, or document a better exact formula that preserves comparability;
- `OurFitScore` from fit dimensions;
- explicit risk penalties;
- final recommendation.

Do not trust arithmetic returned by the model.

## 4. Risk penalties

Versioned risk definitions and maximum penalties:

```text
unpaidDeveloperRisk                 20
founderReservesMostEquity           15
overscopedTechnicalBuild            10
noCustomerAccessPath                10
ctoExpectedToOwnEverything          10
indefinitePartTimeCommitment        10
unavailableExternalDependency       10
```

The model may propose only known risk keys and must provide evidence. C# validates and applies configured penalty values/caps. Unknown risk keys are rejected or placed in non-scoring warnings.

Do not apply a penalty solely because evidence is absent. Use missing evidence/confidence.

## 5. Structured response schema

Create a strict typed schema similar to:

```json
{
  "schemaVersion": "1.0",
  "profileSummary": "...",
  "qualityCategories": [
    {
      "key": "founderExecution",
      "score": 16,
      "evidence": ["..."],
      "explanation": "...",
      "missingEvidence": [],
      "confidence": 0.82
    }
  ],
  "fitDimensions": [],
  "risks": [
    {
      "key": "overscopedTechnicalBuild",
      "evidence": ["..."],
      "explanation": "...",
      "confidence": 0.75
    }
  ],
  "positiveSignals": [],
  "redFlags": [],
  "missingEvidence": [],
  "priorityQuestions": [],
  "recommendationRationale": "...",
  "invitation": {
    "shortDraft": "...",
    "detailedDraft": "...",
    "candidateFactsUsed": ["..."],
    "personaStrengthsUsed": ["..."],
    "conversationTopic": "...",
    "confidence": 0.9
  }
}
```

Requirements:

- exact required category/fit keys once each;
- bounded arrays and string lengths;
- no HTML;
- no unsupported fields used for scoring;
- no raw chain-of-thought request/storage;
- explanations are concise assessment rationales and evidence, not hidden reasoning traces;
- reject malformed/out-of-range/incomplete results before persistence as completed.

## 6. Evaluation prompt

Create version-controlled prompt templates under `agents/FounderScout/prompts/` rather than hardcoding one massive string in C#.

System/developer-style rules must state:

- evaluate only supplied profile evidence;
- unknown remains unknown;
- do not browse or invent external facts;
- ignore age, gender, race, religion, image, family, health, and other protected attributes;
- distinguish founder quality from fit for Yuriy;
- identify “technical co-founder” versus “unpaid developer” risk carefully;
- do not overreward idea novelty without customer evidence;
- use exact category keys and bounds;
- return only schema-valid JSON;
- generate candidate-specific, truthful introduction drafts;
- never state that Yuriy has decided to join;
- never mention automated analysis/scoring/scraping;
- no invitation sending instruction.

Include the founder persona from configuration and the scorecard definitions.

Add a prompt fixture test that verifies required policy sections are present without snapshotting every word.

## 7. Evidence validation

Implement deterministic post-validation:

- each quoted/paraphrased evidence item must be findable or meaningfully traceable to normalized input/evidence map;
- category score above zero requires evidence or a narrowly documented exception for purely structural assessments;
- risk penalty requires evidence;
- claimed revenue/customer/partnership numbers must appear in profile input;
- candidate facts used in invitation must be grounded;
- protected-attribute terms/values from redaction markers must not appear;
- unsupported claims move evaluation to `NeedsReview`/failed validation, not completed ranking.

Use conservative normalized substring/token matching plus evidence IDs from the request where possible. Do not require exact punctuation.

## 8. Retry/error policy

Classify:

- transient network/timeout/429/5xx/provider-unavailable;
- authentication/configuration/model-not-found/permanent;
- invalid structured output;
- evidence-validation failure.

Policy:

- bounded retries, default 3 total attempts;
- exponential backoff with jitter for transient provider failures only;
- respect provider retry-after when available;
- at most one repair/regeneration attempt for invalid schema/evidence/draft validation, with a concise validation-error payload;
- no tight retry loop;
- configuration/auth errors pause analysis and surface attention required;
- persist attempt metadata without secrets or full sensitive request headers.

## 9. Deterministic scoring and recommendation

Implement `ICandidateScoreCalculator`.

Calculate and store:

- BaseScore;
- FounderQualityScore;
- OurFitScore;
- risk penalty total;
- adjusted evaluation score(s);
- overall confidence derived from weighted category/fit confidence, completeness, and evidence coverage;
- activity score from parsed last-seen data using configurable buckets;
- InvitationPriority.

Initial priority formula:

```text
OurFitScore * 0.55
+ FounderQualityScore * 0.25
+ OverallConfidence*100 * 0.15
+ ActivityScore * 0.05
- configured invitation-priority penalties
```

Clamp `0..100` and document rounding.

Initial recommendations based on adjusted/base/fit/confidence thresholds:

```text
StrongConnect
ExploratoryCall
Monitor
Pass
ManualReview
```

Low confidence below configured threshold forces `ManualReview` regardless of high numeric score.

## 10. Introduction drafts

Every valid deep evaluation must produce:

- short draft;
- detailed draft;
- grounded candidate facts used;
- complementary persona strengths used;
- one conversation topic/question;
- confidence.

Deterministic `IInvitationDraftValidator` checks:

- candidate-specific grounded fact;
- complementary value statement;
- clear founder-to-founder reason to connect;
- optional question/topic;
- configured maximum length;
- no protected attributes;
- no unsupported revenue/customer/company claims;
- no mention of scoring/automation/scraping;
- no commitment to join/invest/build for free;
- no generic-only text;
- similarity fingerprint compared with recent queued drafts under configurable threshold.

If validation fails, perform at most one targeted regeneration. Otherwise persist draft as `NeedsReview` with errors; do not discard the underlying evaluation.

## 11. Analyze command

Complete:

```powershell
FounderScout.exe analyze --phase deep --max <N>
FounderScout.exe analyze --phase all --max <N>
```

Flow:

```text
claim pending analysis item
load normalized redacted input and versions
check cache
call provider if needed
validate schema/evidence
calculate scores/recommendation
validate/regenerate invitation drafts
persist evaluation/categories/risks/draft/action
transition candidate state
emit metrics/progress/checkpoint
release claim
```

Default batch 20, concurrency 2, configurable. Per-candidate failures do not lose other completed work. Use a bounded channel/semaphore and one short DB scope per operation.

Update `run` to compose discover -> screen -> deep analyze -> report according to command configuration, without sleeping internally.

## 12. Metrics and summary

Emit at minimum:

```text
analysis.claimed
analysis.completed
analysis.cacheHit
analysis.filtered
analysis.manualReview
analysis.failed
analysis.providerRequests
analysis.providerRetries
candidates.strongConnect
candidates.exploratory
candidates.monitor
candidates.pass
invitations.generated
invitations.needsReview
```

Summary includes counts and top candidate IDs/names/scores, bounded to a small number.

## 13. Tests

Use fake model client by default. Cover:

- cache key stability and invalidation by every versioned input;
- strict response schema and exact category keys;
- score bounds and C# arithmetic;
- founder quality normalization;
- fit score;
- risk application only with evidence;
- unknown/missing evidence behavior;
- low-confidence forced manual review;
- activity buckets and priority formula;
- evidence grounding and fabricated numeric claim rejection;
- protected marker exclusion;
- provider transient/permanent failures and retries;
- invalid schema repair limit;
- configuration/auth pause behavior;
- short/detailed draft requirements;
- length, generic, unsupported claim, automation mention, protected attribute, commitment, and similarity validation;
- one regeneration maximum;
- per-candidate batch isolation/concurrency/idempotency;
- prompt required policies;
- secret redaction;
- valid JSONL metrics/summary;
- no auto-send path/API/action exists.

Add opt-in live integration test guarded by required secret reference/environment configuration. It must be skipped by default and use synthetic profiles only.

## Constraints

- Do not browse external web sources during candidate evaluation.
- Do not auto-send invitations.
- Do not expose raw API keys or provider payload headers.
- Do not use protected attributes.
- Do not let model-provided totals override C# calculations.
- Do not make live provider tests part of normal CI.

## Done when

- Deep analysis is structured, grounded, versioned, cached, retry-safe, and deterministic after model output.
- Founder quality, fit, confidence, risk, activity, and invitation priority are separate.
- Every deep evaluation has validated short/detailed introduction drafts or explicit NeedsReview errors.
- Batch analysis works through Runner with correct metrics and no duplicate work.
- Standard validation passes.

## Required smoke validation

Using synthetic fixture profiles and the fake deterministic model:

1. analyze at least 20 candidates across all recommendation states;
2. include one invalid-schema repair, one evidence failure, one cache hit, one provider transient retry, and one invitation validation failure;
3. verify score calculations and ranking values;
4. rerun unchanged inputs and verify cache/no duplicate evaluation behavior;
5. change scorecard or persona version and verify reevaluation;
6. inspect drafts for grounding and no protected markers;
7. optionally run one synthetic live-provider evaluation and clearly label it.

## Final report

Include provider/SDK decision and ADR, prompt/scorecard/schema versions, formulas, evidence validation, retry policy, draft checks, smoke counts, and whether live provider testing occurred. End with:

```text
Next prompt: prompts/13-founder-scout-results-ui.md
```

