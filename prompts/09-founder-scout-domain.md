# Stage 09 — Founder Scout Domain, Persistence, and Agent Shell

Paste this entire prompt into Codex from the repository root after Stage 08 passes.

---

You are implementing **Stage 09: Founder Scout Domain Model, Separate SQLite Persistence, State Machines, Repository Layer, and Agent CLI Shell**.

Read all repository guidance, architecture, configuration examples, acceptance matrix, and existing platform/agent SDK code. Create/update `docs/exec-plans/stage-09-founder-scout-domain.md`.

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

Create the Founder Scout bounded context and its separate `founders.db`. Implement candidate/profile/evaluation/invitation/discovery entities, states, focused repositories, migrations, domain state transitions, work claiming, domain metrics contracts, and a working `FounderScout.exe` command shell. Provide fixture/file import and report placeholders so the agent can be executed through Runner before browser or AI stages.

Do not implement live browser automation or AI calls yet.

## 1. Founder Scout database

Create `FounderScoutDbContext` in `FounderScout.Infrastructure`, resolved beneath the configured Founder Scout data directory. Use EF Core migrations, SQLite WAL, foreign keys, busy timeout, and short transactions.

### BrowserAccount

Fields:

- stable ID/string;
- display name;
- browser-profile relative path;
- enabled;
- authentication/session health status;
- optional assigned discovery segment IDs;
- last authenticated/successful/failed run UTC;
- last error reason code/message bounded;
- cooldown/suspended-until UTC where applicable;
- created/updated UTC.

Session statuses:

```text
Unknown
Healthy
ReauthenticationRequired
AccessDenied
Throttled
ChallengeDetected
ParserFailure
Disabled
```

Do not store password, cookies, or session state in SQLite.

### DiscoverySegment

- ID/name;
- enabled/priority;
- configuration JSON for source filters/navigation;
- assigned account ID optional;
- last run UTC;
- viewed/new/duplicate/error counters;
- consecutive low-yield runs;
- paused-until UTC;
- created/updated UTC.

### DiscoveryCheckpoint

- ID;
- account ID;
- segment ID;
- domain run/correlation ID;
- last stable source profile key or continuation metadata;
- bounded JSON state;
- captured UTC;
- status/version.

Checkpoint data must not contain cookies, access tokens, raw passwords, or whole HTML pages.

### Candidate

- Guid ID;
- current source profile key and canonical source URL when available;
- display name;
- normalized location text only where operationally relevant;
- technical/non-technical status;
- commitment status;
- idea commitment status;
- candidate lifecycle status;
- first seen/last seen UTC;
- last activity text/parsed UTC optional;
- current snapshot ID;
- latest evaluation ID;
- founder quality, fit, confidence, activity, risk penalty, invitation priority summary columns for sorting;
- created/updated UTC;
- row/concurrency version where needed.

Do not add age, gender, race, religion, photo, marital status, health, or similar personal fields to the normalized candidate table.

Candidate statuses:

```text
Discovered
Captured
Parsed
FilteredOut
PendingAnalysis
Analyzing
Analyzed
Shortlisted
Monitor
Passed
QueuedForInvite
MessageReviewed
ManuallySent
Accepted
Declined
NoResponse
CallScheduled
PassedAfterCall
TrialProject
Selected
```

Centralize allowed transitions with reason codes and tests.

### ProfileSnapshot

- Guid ID;
- candidate ID;
- source account/segment IDs;
- stable source key/url;
- content hash;
- parser/source adapter version;
- capture UTC;
- raw snapshot relative artifact path and retention/deletion timestamp;
- normalized profile JSON optional until Stage 11;
- extraction completeness/confidence;
- status/error code;
- unique identity preventing duplicate identical snapshots per candidate.

Persist the snapshot record/artifact before later analysis.

### CandidateIdentityAlias

- ID;
- candidate ID;
- alias type (`SourceKey`, `CanonicalUrl`, `Fingerprint`, `ManualMerge`);
- normalized alias value/hash;
- source account;
- confidence;
- created UTC;
- uniqueness/indexes preventing two active candidates from owning the same strong alias.

### ScreeningDecision

- ID;
- candidate/snapshot;
- ruleset version;
- outcome;
- score optional;
- reason codes/evidence JSON;
- created UTC.

### Evaluation

Create schema now; Stage 12 fills behavior:

- ID;
- candidate/snapshot;
- scorecard version;
- evaluator provider/model/deployment version;
- prompt version;
- input hash/cache key;
- founder quality score;
- our fit score;
- confidence;
- activity score;
- base/risk/final/priority values;
- recommendation;
- structured evaluation JSON;
- raw provider response artifact reference optional;
- status/error/retry metadata;
- created UTC.

### EvaluationCategory / EvaluationRisk

Normalized children for filtering/reporting plus the original structured evaluation JSON. Include key, score, maximum, evidence JSON, reason, and penalty fields.

### InvitationDraft

- ID;
- evaluation/candidate;
- short draft;
- detailed draft;
- facts used JSON;
- confidence;
- validation status/errors JSON;
- similarity fingerprint;
- created UTC;
- reviewed UTC/by;
- superseded flag.

### CandidateAction

Append-only domain timeline:

- ID;
- candidate;
- action type;
- occurred UTC;
- actor;
- notes bounded;
- structured data JSON;
- related invitation/evaluation/run IDs.

### ManualInvitationWindow

- ID;
- start/end UTC or user-configured tracking window;
- configured primary queue size;
- configured reserve size;
- sent count;
- notes;
- created/updated UTC.

Do not assume an external service's reset time; this is user-managed tracking.

### ReportExport

- ID;
- report type/format;
- filter/sort configuration hash;
- relative path;
- row count;
- file hash/size;
- created/delete-after UTC.

## 2. Domain repository/use-case interfaces

Create focused APIs:

- `IBrowserAccountRepository`
- `IDiscoverySegmentRepository`
- `IDiscoveryCheckpointRepository`
- `ICandidateRepository`
- `IProfileSnapshotRepository`
- `ICandidateIdentityRepository`
- `IScreeningRepository`
- `IEvaluationRepository`
- `IInvitationDraftRepository`
- `ICandidateActionWriter`
- `IInvitationQueueRepository`
- `IReportExportRepository`
- `IFounderScoutWorkQueue`

Important atomic operations:

- find/create candidate by strong identity;
- attach alias with conflict detection;
- add snapshot if content hash is new;
- update current snapshot atomically;
- claim pending candidate for analysis with lease/claimed-until/worker ID;
- release/retry/finalize analysis claim;
- transition candidate state legally;
- create/supersede invitation draft;
- query ranked/paginated candidates;
- append action.

Avoid a generic repository.

## 3. Founder Scout configuration contracts

Move/implement typed versioned Founder Scout configuration in `FounderScout.Application`, matching `docs/configuration-examples.md`:

- data paths;
- discovery limits/cooldown/stop policies;
- analysis batch/concurrency/retries;
- ranking thresholds/formula weights;
- invitation limits/validation settings;
- AI provider endpoint/deployment/secret reference;
- retention;
- founder persona reference/content.

Add thorough validation. No secret values in configuration.

Provide an adapter implementing the platform agent configuration validator used by Stage 08.

## 4. State machines and reason codes

Implement explicit services for:

- candidate lifecycle transitions;
- account session-health transitions;
- snapshot status;
- evaluation status;
- invitation review/send outcome.

Record append-only CandidateAction entries for meaningful user/domain changes. Reject illegal transitions with typed errors.

## 5. Agent CLI shell

Implement real commands using the Stage 01 Agent SDK:

```powershell
FounderScout.exe run
FounderScout.exe discover
FounderScout.exe analyze
FounderScout.exe authenticate
FounderScout.exe import --input <path>
FounderScout.exe report
FounderScout.exe diagnose
FounderScout.exe protocol-demo
```

In this stage:

- `import` works with local JSON/text fixture files and persists candidates/snapshots;
- `diagnose` validates config, data directory, DB migrations, browser-account records, and report/artifact directories;
- `report` emits a minimal domain-count summary and JSON/Markdown placeholder based on actual DB data;
- `discover`, `analyze`, and `authenticate` emit explicit “not implemented until Stage XX” warning/summary with stable non-success or no-work semantics as appropriate, not a false success;
- `run` composes currently implemented phases and accurately summarizes what ran;
- stdout remains JSONL; stderr is human diagnostics.

Register/update the Founder Scout manifest and command list through platform seeding without overwriting user configuration.

## 6. Fixture import format

Define a versioned capture/import envelope that Stage 10 can also produce:

```json
{
  "captureSchemaVersion": "1.0",
  "source": "fixture",
  "sourceAccountId": "fixture-account",
  "sourceSegmentId": "fixture-segment",
  "sourceProfileKey": "candidate-001",
  "profileUrl": "https://example.invalid/profile/candidate-001",
  "capturedAtUtc": "...",
  "displayName": "Candidate A",
  "rawText": "...",
  "structuredFields": {},
  "sourceAdapterVersion": "fixture-1.0"
}
```

The import service must:

- validate input size/count;
- reject traversal/unsafe source paths;
- normalize URL/key enough for identity;
- save raw content as a bounded artifact under Founder Scout data root;
- calculate hash;
- persist snapshot before any downstream work;
- be idempotent for the same source key/hash;
- emit protocol metrics/events.

## 7. Cross-database correlation

Founder Scout runs are owned centrally in `assistant.db`, while candidates live in `founders.db`.

Use run ID/correlation ID values, not cross-database foreign keys. Do not attempt distributed transactions. Required order:

```text
central Runner starts run
Founder Scout commits domain changes locally
Founder Scout emits events/summary to Runner
Runner commits platform audit/summary
```

Operations must be idempotent so a Runner finalization failure does not require reimporting duplicate profiles.

## 8. Tests

Use real temporary SQLite. Cover:

- migrations/pragmas/indexes;
- candidate/account/evaluation/invitation state transitions;
- illegal transitions;
- identity alias uniqueness/conflict;
- snapshot idempotency by source key/hash;
- separate candidate for weak ambiguous fingerprint until manually resolved;
- analysis work claim concurrency/expiry;
- configuration validation;
- fixture import size/format/security/idempotency;
- raw artifact committed before state queues analysis;
- no protected fields in Candidate schema/DTO;
- CandidateAction append behavior;
- queue size/window tracking;
- paginated ranking query shape with seeded records;
- CLI stdout valid JSONL;
- central run correlation without cross-DB FK;
- fresh migration and upgrade test.

## Constraints

- No Playwright or live browsing.
- No AI provider call.
- No full parser/scoring.
- No candidate UI.
- No automatic invitation sending.
- Do not put Founder Scout domain entities in `assistant.db`.

## Done when

- Founder Scout has a separate migrated database and stable domain model.
- Fixture import creates durable candidates/snapshots idempotently.
- Agent CLI works through Runner and emits correct protocol summaries.
- State machines and work claims are explicit/tested.
- Platform configuration UI can validate/save Founder Scout settings.
- All validation passes.

## Required smoke validation

Through Runner:

1. import a fixture file with at least three candidates and one duplicate;
2. rerun import and verify idempotency;
3. run `diagnose`;
4. run placeholder `report` and inspect artifact/summary;
5. query candidate/snapshot/action counts;
6. verify central AgentRun references the same run ID emitted in Founder Scout actions/events.

## Final report

Include Founder Scout schema, state diagrams, fixture format, CLI commands, cross-database approach, smoke results, and exact migration name. End with:

```text
Next prompt: prompts/10-founder-scout-browser-discovery.md
```

