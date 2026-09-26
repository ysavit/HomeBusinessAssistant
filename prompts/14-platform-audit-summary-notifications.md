# Stage 14 — Platform Audit, Summaries, Notifications, Retention, and Health

Paste this entire prompt into Codex from the repository root after Stage 13 passes.

---

You are implementing **Stage 14: Operational Audit Completion, Deterministic Summaries, Attention/Notification Pipeline, Retention, and Platform Health**.

Read all repository guidance, central audit/run data, Host tray/UI, Founder Scout metrics, Wake Remote metrics, and this prompt. Create/update `docs/exec-plans/stage-14-platform-audit-summary-notifications.md`.

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

Complete the platform's operational visibility. Implement deterministic per-run and daily summaries, durable attention items/local notifications, notification deduplication/throttling, tray delivery, improved health/status pages, abandoned-run and schedule attention detection, retention cleanup, log correlation, and safe diagnostics export. Do not add external email/SMS/push delivery in V1.

## 1. Durable attention and notification model

Add central entities/migration as needed:

### AttentionItem

- ID;
- category;
- severity: Info, Warning, Error, Critical;
- title;
- bounded safe message;
- agent ID/run/occurrence/candidate/account reference IDs as strings/Guids;
- deduplication key;
- status: Active, Acknowledged, Resolved, Suppressed;
- first/last observed UTC;
- occurrence count;
- acknowledged/resolved UTC/by;
- data JSON redacted;
- expires UTC optional.

### LocalNotification

- ID;
- attention item ID optional;
- title/message;
- severity;
- created UTC;
- not-before UTC;
- delivered UTC;
- delivery attempts;
- status;
- deduplication/throttle key.

Do not use a database notification row for high-frequency progress. This is for actionable/summary notifications.

## 2. Attention detectors

Implement idempotent detectors for:

- agent run failed/timed out/abandoned;
- repeated agent failure threshold;
- agent authentication required;
- access denied/throttled/challenge detected/parser failure;
- missing/invalid configuration or secret;
- next wake task reconciliation failure;
- wake test failed or late beyond threshold;
- Wake Remote network/provider not ready;
- stale scheduler/reconciliation/host health heartbeat;
- low disk space;
- database migration/backup/retention failure;
- Founder Scout new Strong Connect candidate;
- Founder Scout invitation draft NeedsReview;
- primary queue ready count reaches configured threshold;
- overdue manually tracked follow-up optional.

Each detector must use a stable dedupe key, update the same active item, resolve it when the condition clears, and avoid leaking profile raw text or secrets.

## 3. Run summary service

Implement deterministic `IRunSummaryService` that builds a standard summary even if an agent did not emit one.

Common fields:

- agent/run/trigger/status;
- due/start/end/duration;
- command;
- key metrics;
- warning/error counts;
- artifact links;
- terminal reason;
- attention items created/resolved;
- next scheduled occurrence.

Agent-specific adapters:

### Founder Scout

- viewed/new/duplicate/changed;
- parsed/screened/deep analyzed;
- recommendation counts;
- invitation drafts ready/needs review;
- top bounded candidate list;
- account/segment status;
- next discovery/analysis occurrence.

### Wake Remote

- scheduled/actual start and delay;
- network readiness/time;
- remote provider readiness;
- keep-awake duration;
- availability end;
- next wake.

Persist `SummaryText` and versioned `SummaryJson`. A summary can be regenerated from events/metrics with an explicit version/revision without changing historical raw events.

## 4. Daily summary

Implement `IDailySummaryService` and a durable `DailySummary` entity or report artifact.

For a configured local date/time zone, aggregate:

```text
Agent runs total/success/failed/timeout/cancelled
Wake sessions and readiness
Founder profiles visited/new/analyzed
Strong/exploratory/manual-review candidates
Invitation drafts and manually sent/accepted/calls
Active attention items
Resolved attention items
Next scheduled activity
Backup/retention/health status
```

Output:

- summary JSON;
- readable Markdown/HTML/text artifact;
- one local notification when ready, subject to settings;
- link from dashboard/tray.

Run once daily through the normal schedule/Runner path or as a platform internal occurrence with full audit. Do not require AI to generate operational summaries.

## 5. Tray notification delivery

Implement bounded local delivery through the existing tray app:

- poll/subscribe to pending `LocalNotification` rows;
- show Windows tray balloon/toast mechanism available to the app without adding heavy packaging requirements;
- severity-specific icon/title, but include textual severity;
- click opens a validated local dashboard route for the related item;
- mark delivered only after delivery attempt accepted by the local API;
- bounded retry;
- quiet hours configuration;
- deduplicate repeated items;
- throttle storms globally and per key;
- tray unavailable means notification remains pending or expires according to policy;
- no arbitrary external URL in click targets.

Default notify:

- failed run;
- authentication/challenge attention;
- failed wake test/remote readiness;
- new Strong Connect candidates as a grouped notification, not one per candidate;
- daily summary ready.

## 6. Dashboard and health enhancements

Add:

- active attention list with severity, age, owner/reference, acknowledge/resolve actions;
- system health timeline/status;
- scheduler last success and lag;
- wake reconciler last success/task fingerprint;
- runner/agent executable health;
- DB pragmas/migration status;
- disk space;
- last backup/retention run placeholders or actual when available;
- notification status/quiet hours;
- daily summary history.

Acknowledgment does not resolve the underlying detector condition. Resolution is automatic when condition clears or explicit when the item is user-action-only.

## 7. Correlation and logs

Ensure structured scopes/properties include when known:

```text
CorrelationId
AgentId
OccurrenceId
AgentRunId
CandidateId
BrowserAccountId
DiscoverySegmentId
ConfigurationRevisionId
```

Implement a local log query/download view only if it can be bounded and safe. Prefer diagnostics export over a full log-search engine.

Log policy:

- rolling files;
- configurable level/retention;
- redaction middleware/enricher;
- no cookie/session/API key/raw profile content by default;
- stack traces allowed in local files but encoded/redacted in UI.

## 8. Retention service

Implement scheduled/idempotent cleanup for:

- logs;
- central run diagnostic artifacts;
- Founder Scout error artifacts;
- raw profile artifacts according to Stage 13 rules;
- temporary config/XML files;
- delivered/expired notifications;
- resolved attention items after retention period;
- old report exports;
- stale checkpoints when safe;
- database event rows only under explicit, conservative policy.

Requirements:

- never delete active-run files;
- use allowed-root/path validation;
- audit counts/bytes/failures;
- dry-run mode;
- cancellation;
- partial failure isolation;
- do not delete browser profile directories;
- do not delete secret store;
- do not delete immutable configuration revisions unless a future archival policy is explicitly designed.

## 9. Diagnostics export

Create a ZIP export under artifacts containing only selected safe content:

- application/agent versions;
- bootstrap settings redacted;
- active configuration schema/field names and redacted values;
- schedule summaries;
- latest relevant audit/attention/run summaries;
- power diagnostics;
- migration/DB metadata;
- bounded redacted logs;
- no secrets;
- no cookies/browser profiles;
- no raw founder profiles or screenshots by default;
- no invitation text unless explicitly included through a checkbox/config and clearly labeled.

Generate a manifest with file hashes and exclusions. Enforce size cap.

## 10. Tests

Cover:

- each detector create/update/resolve/dedupe behavior;
- notification grouping/throttling/quiet hours/retry/expiry;
- safe local route validation;
- standard run summary fallback and agent-specific summaries;
- daily aggregation across local date/time-zone boundaries and DST;
- top candidate list bounded and safe;
- no raw profile/secret in summary/notification;
- attention acknowledge versus resolve semantics;
- retention dry run/execute, active-run protection, path safety, partial failures;
- diagnostics ZIP contents, hashes, exclusions, size cap;
- structured correlation properties through representative flows;
- dashboard pages and antiforgery actions;
- large event/attention sets remain paginated.

## Constraints

- No email/SMS/push/cloud notification provider.
- No LLM required for summaries.
- No browser/session content in diagnostics.
- No destructive audit editing.
- No automatic invitation sending.

## Done when

- Important failures and opportunities become deduplicated attention items and local tray notifications.
- Every run has a deterministic summary.
- Daily summaries are generated and visible.
- Retention is safe, auditable, and dry-runnable.
- Diagnostics export is useful without exposing sensitive data.
- Health/dashboard show operational truth.
- All validation passes.

## Required smoke validation

Generate synthetic/fixture conditions for:

1. failed run;
2. authentication-required account;
3. failed wake readiness;
4. five Strong Connect candidates;
5. one draft NeedsReview;
6. low disk warning via fake provider;
7. daily summary;
8. retention dry-run and execution;
9. diagnostics export.

Verify grouped tray notifications, attention lifecycle, summary artifacts, and ZIP exclusions.

## Final report

Include attention categories, dedupe/throttle rules, summary schemas, retention table, diagnostics exclusions, smoke results, and deferred external notifications. End with:

```text
Next prompt: prompts/15-packaging-installation-operations.md
```

