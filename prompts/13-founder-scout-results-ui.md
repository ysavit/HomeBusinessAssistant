# Stage 13 — Founder Scout Results UI, Shortlist, Manual Invitation Queue, and Reports

Paste this entire prompt into Codex from the repository root after Stage 12 passes.

---

You are implementing **Stage 13: Founder Scout Candidate Dashboard, Detail, Ranking, Profile History, Manual Invitation Workflow, Outcome Tracking, and Exports**.

Read repository guidance, Host UI patterns, Founder Scout domain/evaluation code, acceptance matrix, and this prompt. Create/update `docs/exec-plans/stage-13-founder-scout-results-ui.md`.

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

Expose Founder Scout as a usable local decision-support tool inside the system-tray web UI. Implement candidate ranking/filtering, evidence-rich details, profile/evaluation history, short and detailed introduction draft review/copy, a configurable manual invitation queue and reserve queue, manual outcome tracking, discovery/account/segment status, and consistent Markdown/CSV/JSON/HTML reports.

No invitation may be sent automatically.

## 1. Founder Scout dashboard

Show real values:

```text
Profiles/candidates total
New/updated in selected period
Pending screening/deep analysis
Analyzed
Strong Connect / Exploratory / Monitor / Pass / Manual Review
Invitation drafts ready / needs review
Primary queue and reserve counts
Manually sent/accepted/calls scheduled
Browser account health
Discovery segment yield
Latest discovery/analysis/report runs
Attention items
```

Actions:

- run discovery for selected healthy account/segment;
- run pending analysis;
- regenerate reports;
- open primary queue;
- review account/session attention;
- open latest top-candidate report.

Actions create occurrences and start Runner. Do not execute agent code in web handlers.

## 2. Candidate list

Columns:

- rank/invitation priority;
- founder/display name;
- recommendation/status;
- founder quality;
- our fit;
- confidence;
- activity;
- risk penalty;
- last seen/captured;
- current queue/action state;
- profile changed indicator.

Filters:

- recommendation;
- candidate lifecycle status;
- minimum/maximum score and confidence;
- full-time/technical/idea posture;
- has traction/customer-access evidence;
- specific risk keys;
- new/changed date range;
- activity recency;
- invitation state;
- account/segment;
- needs manual review;
- text search over safe normalized fields.

Requirements:

- stable server-side sort and pagination;
- default sort by InvitationPriority descending, then confidence/activity/name/ID tie breakers;
- indexes and query projection for performance;
- no raw profile text in list query;
- bulk select only for local queue/status operations, never sending.

## 3. Candidate detail

Sections:

### Summary

- recommendation;
- founder quality, fit, confidence, activity, priority;
- concise profile summary;
- current status and latest activity;
- source link opened manually in browser with safe external-link handling.

### Why this candidate ranks

- category score breakdown with maximum;
- fit dimensions;
- evidence under each category;
- positive signals;
- risks/penalties;
- missing evidence;
- recommendation rationale.

### First-call preparation

- priority questions;
- expected CTO workload/risk signals;
- topics to validate;
- notes/actions.

### Introduction draft

- short and detailed drafts;
- facts/persona strengths used;
- validation status/errors;
- copy buttons using local JS;
- mark reviewed;
- manually edit into a new revision while preserving generated original;
- mark ready/manual sent;
- no Send button or network integration.

### Profile/evaluation history

- snapshot timeline;
- safe relevant-field diff;
- parser/source versions;
- evaluation history with scorecard/prompt/model/persona versions;
- why reevaluated;
- previous invitation drafts.

### Candidate timeline

- discovered, updated, analyzed, queued, reviewed, manually sent, accepted/declined/no response, calls/trial/selected;
- bounded notes;
- audit/correlation links.

Encode all untrusted text. Do not render raw captured HTML.

## 4. Manual invitation queue

Provide:

```text
Primary queue: configurable size, default 15
Reserve queue: configurable size, default 15
```

The queue is a local planning tool, not a claim about external reset timing.

Features:

- automatically suggest top eligible candidates by priority;
- user explicitly adds/removes/reorders;
- prevent duplicate active queue entry;
- show draft readiness and source-profile link;
- review checklist;
- copy draft;
- mark manually sent with date/time and optional note;
- track accepted/declined/no response/call scheduled;
- maintain user-managed invitation window dates and sent count;
- rollover/reset creates a new local window without deleting history;
- reserve promotion is manual or user-confirmed, not silent.

Eligibility default:

- valid evaluation;
- confidence above threshold or manually approved;
- invitation draft valid/reviewed;
- not already sent/declined/passed;
- no unresolved identity conflict;
- not stale beyond configured threshold unless manually included.

## 5. Draft editing and versioning

Generated invitation drafts are immutable. A user edit creates an `InvitationDraftRevision` or equivalent:

- source draft ID;
- edited short/detailed text;
- edited by/time;
- validation results;
- active selection;
- no secret or protected content.

Re-run deterministic validation after editing. Similarity is advisory for manual edits but still shown.

Copy action may be client-side and need not store clipboard contents. Record “copied” only if valuable and not noisy; manual-sent is authoritative.

## 6. Account and discovery segment pages

Browser account page:

- status/attention;
- enabled;
- profile path displayed only as safe relative path;
- last auth/success/failure;
- assigned segments;
- cooldown/suspension;
- actions: authenticate (launch headed agent command), diagnose, enable/disable, clear status after successful manual review;
- never display cookies/passwords/session files.

Discovery segment page:

- filters/config summary;
- priority;
- viewed/new/duplicate yield;
- consecutive low-yield runs;
- paused state;
- last/next run;
- account assignment;
- actions: run now, pause/resume, edit safe configuration.

Authentication command launch must be clearly interactive and occur in the current user session.

## 7. Reports and exports

Implement one report query model so all formats use the same selected candidate set and ordering.

Formats:

### `top-candidates.html`

Local standalone or application-rendered HTML with ranking, summary, reasons, risks, questions, and drafts. Encode content and avoid external scripts/assets.

### `top-candidates.md`

Readable Markdown containing:

- generation UTC/local time;
- filters/scorecard versions;
- reviewed counts;
- top candidates;
- why connect/concerns/questions;
- short introduction drafts;
- attention/manual-review list;
- common positive/risk reason distribution.

### `candidates.csv`

Stable UTF-8 CSV with score/status columns and safe one-line summary. Prevent spreadsheet formula injection by escaping values beginning with `=`, `+`, `-`, or `@` where relevant.

### `candidates.json`

Versioned export schema without raw browser/session/secrets/protected attributes.

### `discovery-summary.md`

- accounts/segments/runs;
- visited/new/duplicate/changed;
- analyzed/recommendations;
- top candidates;
- common reasons;
- attention required;
- next scheduled runs.

### `manual-invitation-queue.md`

Primary/reserve candidates, draft, source link, review status, and manual checklist.

Store report artifact metadata/hash/retention. Use atomic temp + replace.

## 8. Report command

Complete:

```powershell
FounderScout.exe report --type all
FounderScout.exe report --type top-candidates --top 30
FounderScout.exe report --type invitation-queue
```

It must emit artifact events and a structured summary through Runner. A successful deep-analysis run can enqueue a report occurrence or generate reports as a final phase based on configuration; avoid concurrent writers through a report lease.

## 9. Profile raw-data retention

Add UI/service to apply configured raw snapshot retention:

- derived normalized/evaluation/action records remain;
- delete eligible raw HTML/text/screenshot artifacts safely;
- mark snapshot raw artifact as deleted/expired;
- never delete artifacts needed by active runs or unresolved parser/identity diagnostics without policy;
- audit counts and failures;
- allow manual delete of one candidate's raw data with confirmation;
- do not delete candidate/evaluation history unless a separate explicit delete workflow is built.

## 10. Outcome feedback

Support manual outcomes:

- invitation sent;
- accepted;
- declined;
- no response;
- call scheduled/completed;
- second call;
- passed after call with reason;
- trial project;
- selected.

Use structured reason codes plus optional notes. Do not automatically change scorecard weights. Provide an export/query that later compares initial score/recommendation with outcomes.

## 11. Tests

Cover:

- candidate ranking formula/order/tie breakers;
- filters, pagination, projections, performance indexes;
- detail evidence/history/diffs;
- hostile profile text encoded/no XSS;
- immutable generated draft + editable revision;
- draft validation after edit;
- queue eligibility, capacity, duplicate prevention, reorder, primary/reserve, window rollover;
- manual send/outcomes and legal transitions/audit;
- no network send route/action exists;
- account/segment attention/actions;
- report format set consistency;
- Markdown/HTML encoding;
- CSV formula-injection protection and Unicode;
- JSON schema version;
- atomic report generation and lease;
- raw retention without breaking derived data;
- artifact path safety;
- 10,000 seeded candidate list query remains paginated and reasonably bounded;
- UI antiforgery and validation.

## Constraints

- No automatic invitation/message sending.
- No LinkedIn/email enrichment or external research in V1.
- No protected attributes in ranking/export.
- No raw HTML rendering.
- No SPA rewrite.

## Done when

- Founder Scout has a complete local review workflow from discovery through manual invitation outcome.
- Candidate evidence, risks, questions, and drafts are easy to review.
- Queue is configurable and manual.
- Reports are consistent, safe, and generated through the agent/Runner.
- Browser session health and discovery yield are visible.
- Retention and outcome tracking work.
- All validation passes.

## Required manual smoke validation

Using synthetic fixture/evaluation data:

1. open Founder Scout dashboard and candidate list;
2. filter/sort/paginate;
3. review one candidate with changed snapshots and multiple evaluations;
4. copy/review/edit a draft;
5. add/reorder primary and reserve queues;
6. mark one manually sent/accepted and one passed;
7. generate all report formats and compare row set/order;
8. test hostile text encoding and CSV safety;
9. run raw-data retention on expired synthetic artifacts;
10. confirm no send action/network call exists.

## Final report

Include page map, ranking/query design, queue rules, report schemas/paths, retention behavior, outcome states, smoke results, and deferred enrichment. End with:

```text
Next prompt: prompts/14-platform-audit-summary-notifications.md
```

