# Stage 16 — Hardening, End-to-End Verification, and V1 Release

Paste this entire prompt into Codex from the repository root after Stage 15 passes.

---

You are implementing **Stage 16: Security/Privacy Hardening, Failure Injection, Concurrency and Recovery Verification, Performance Validation, End-to-End Tests, Documentation Reconciliation, and V1 Release Candidate**.

Read every repository instruction, ADR, ExecPlan, architecture/configuration document, the entire `docs/acceptance-test-matrix.md`, and current code/tests. Create/update `docs/exec-plans/stage-16-hardening-e2e-release.md`.

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

Bring the complete system to a releasable V1 state. Close gaps against the acceptance matrix, eliminate placeholder behavior, verify process/database/power cleanup, run end-to-end synthetic workflows, harden local security and privacy boundaries, measure important query/run behavior, validate packaging on a clean path, and produce a release checklist with honest known limitations.

Do not add new product scope unless required to satisfy documented V1 behavior.

## 1. Repository-wide gap analysis

Before coding:

- map every acceptance-matrix item to an existing automated test, manual test, or missing implementation;
- inspect all TODO/FIXME/NotImplemented/placeholder responses;
- inspect warnings/analyzer suppressions;
- inspect migration history and upgrade path;
- inspect UI routes/actions for direct agent launch or unsafe rendering;
- inspect logs/audit/artifacts for secret/raw data leakage;
- inspect browser stop/failover behavior;
- inspect invitation code for any network send path;
- inspect power-request disposal and child-process cleanup;
- inspect schedule/occurrence/run state transitions and idempotency;
- inspect file path normalization/traversal/reparse handling;
- inspect test flakiness and real-time sleeps.

Record findings and priorities in the ExecPlan. Then fix them.

## 2. End-to-end synthetic environment

Build a deterministic E2E harness using:

- temporary install/data roots with spaces and Unicode;
- real SQLite databases/migrations;
- actual Runner and agent child processes;
- fake Test Agent;
- local Startup School-like fixture HTTP server/pages;
- dedicated temporary Playwright profile;
- fake AI provider server/client with scripted valid/invalid/transient responses;
- fake Windows wake/task/power adapters for automated tests;
- fake `TimeProvider` where in-process, and short bounded real durations only where process boundaries require it.

No normal CI test may access real Startup School, a live AI endpoint, remote desktop provider, or sleep the workstation.

## 3. Required end-to-end scenarios

Automate or explicitly document manual-only parts of all acceptance-matrix scenarios. At minimum automate:

### Platform lifecycle

- fresh bootstrap/migrations/seeding;
- Host starts loopback/tray-service composition where UI automation permits;
- second-instance signal;
- schedule creation/reconciliation;
- Runner launch;
- event/audit/summary/notification;
- restart recovery;
- backup/restore;
- retention.

### Founder Scout happy path

```text
fixture discovery of >= 40 profile pages
-> new/duplicate/changed capture
-> parser/normalizer/dedupe/redaction
-> fast screening
-> fake structured AI deep analysis
-> C# scores/risks/confidence/priority
-> short/detailed draft validation
-> candidate UI query models
-> primary/reserve queue
-> all report formats
-> manual sent/outcome action
-> daily summary/notification
```

Verify counts and hashes across central and Founder Scout databases.

### Founder Scout failure paths

- login redirect;
- access denied;
- throttle;
- challenge;
- parser layout change;
- browser crash;
- profile lock;
- AI auth/config error;
- AI transient retries;
- invalid schema/evidence/draft;
- cancellation/timeout;
- unchanged cache hit;
- no automatic account failover;
- no invitation send.

### Wake Remote paths

- generated wake task semantics through fake bridge;
- Runner starts Wake Remote short window;
- network ready/not ready;
- provider required/optional;
- cancellation/timeout;
- nested power handles;
- release in all paths;
- next wake reconciliation.

## 4. Concurrency and crash recovery

Add stress/failure-injection tests for:

- simultaneous schedule reconcilers;
- simultaneous Runner claim attempts;
- Host and Task Scheduler bridge launch race;
- two Founder Scout discovery attempts for one account;
- two analysis workers claiming candidates;
- report/backup/retention lease contention;
- SQLite busy/lock transient handling;
- Runner crash after child launch;
- agent crash after domain commit before summary;
- Host crash during config save/schedule reconciliation;
- interrupted artifact/temp file write;
- stale lease fencing;
- PID reuse-safe recovery;
- backup during active read/write;
- restore rollback.

Use bounded loops and deterministic assertions. Ensure no flaky unbounded timing tests.

## 5. Security and privacy review

Create/update `docs/security-privacy.md` with threat model:

Assets:

- API secrets;
- browser profiles/session state;
- founder raw profiles/evaluations/drafts;
- remote availability configuration;
- audit/logs/backups.

Trust boundaries:

- current Windows user;
- local Host/UI;
- Runner/agent processes;
- browser/external website;
- AI provider;
- file system/backups.

Verify/fix:

- loopback-only binding;
- antiforgery;
- output encoding/no raw HTML;
- argument injection prevention;
- allowed-root path handling, symlinks/reparse points where practical;
- manifest/config/schema validation;
- DPAPI secret handling and zero/log behavior;
- no secret in SQLite/config revisions/audit/logs/diagnostics;
- browser profile excluded from backups/exports;
- raw profile retention;
- no protected attributes in evaluator/ranking/report;
- no auto invitation sending;
- no browser stealth/CAPTCHA/proxy/account failover after enforcement signals;
- diagnostic artifact bounds/redaction;
- CSV formula injection;
- zip-slip prevention for future restore/diagnostic handling;
- package dependency vulnerability/deprecation review using available .NET tooling.

Document residual risk: code running as the same Windows user can access local files/secrets/session profiles.

## 6. Performance and scale checks

Measure and optimize only proven bottlenecks:

- candidate list/filter/sort with 10,000 candidates and multiple evaluations;
- run/event queries with 100,000 events;
- scheduler reconciliation with representative schedules;
- batch analysis queue claim with 20/100 items;
- report generation with top 30 and full CSV;
- startup/migration/readiness;
- backup duration/size;
- memory/process cleanup after repeated short browser/agent runs.

Add indexes/projections where query plans or timings show need. Keep tests tolerant of CI variability; enforce structural bounds and broad performance thresholds, not fragile milliseconds.

## 7. Reliability cleanup

- Remove or resolve all V1 placeholders.
- Ensure every background loop exposes health and cancellation.
- Ensure all process, browser, file, DB, timer, power, tray, and IPC resources are disposed.
- Ensure every terminal run/occurrence path is idempotent.
- Ensure summaries/attention remain available after agent failure.
- Ensure errors are actionable and not swallowed.
- Ensure no infinite retries or tight loops.
- Ensure fixed-delay schedules wait after completion rather than internal sleeping.
- Ensure external error states pause/suspend appropriately and require user action.

## 8. UI/user acceptance review

Verify all pages at typical and narrow remote-desktop window sizes:

- navigation and empty states;
- keyboard/focus/labels/error summaries;
- running/failure/attention states;
- configuration revisions/secrets;
- schedules/preview;
- runs/audit/artifacts;
- Wake status/test instructions;
- Founder candidate/detail/queue/reports;
- destructive confirmations;
- no secret/raw HTML exposure.

Fix obvious usability issues without introducing a frontend framework.

## 9. Documentation reconciliation

Update all docs to actual implementation. Remove stale planned behavior. Include:

- architecture diagram/process boundaries;
- data models and migrations;
- command reference;
- agent protocol;
- schedule semantics and defaults;
- browser authentication/discovery behavior and stop conditions;
- AI provider setup and synthetic/live test distinction;
- scoring formulas/versions;
- invitation draft/manual workflow;
- wake setup/test/limitations;
- install/upgrade/backup/restore/uninstall;
- security/privacy;
- troubleshooting/attention codes;
- release/rollback.

Generate `docs/v1-release-checklist.md`.

## 10. Release build

Create a versioned V1 release candidate artifact:

- clean Release build/test/format;
- self-contained win-x64 publish;
- manifest/checksum file;
- install package directory or ZIP;
- installer/repair/uninstall/smoke scripts;
- release notes;
- known limitations;
- database migration list;
- dependency/license inventory where feasible.

Do not publish real data or secrets.

## 11. Tests and required validation

Run:

```powershell
dotnet --info
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Also run:

- complete E2E synthetic suite;
- architecture tests;
- migration fresh/upgrade/restore tests;
- published install smoke script;
- dependency vulnerability/deprecation checks available in current SDK;
- test filtering/report proving no skipped required tests except documented opt-in live/manual tests.

For each manual-only test, provide exact steps and do not claim success unless executed.

## Constraints

- No new cloud architecture.
- No Windows Service.
- No automatic updater.
- No external notification delivery.
- No automatic invitation sending.
- No anti-detection/evasion browser features.
- No live external tests required for CI/release correctness.

## Done when

- Every acceptance-matrix item has automated evidence or an explicit manual-only reason/procedure.
- Synthetic E2E workflow passes.
- Security/privacy review is documented and critical findings fixed.
- Concurrency/recovery tests pass without leaked processes/files.
- Performance is acceptable for V1 local scale.
- Published clean-install smoke passes.
- V1 release artifact/checksums/release notes/checklist exist.
- Known limitations are honest and bounded.

## Final report

Provide:

1. acceptance coverage totals: automated/manual/deferred;
2. critical fixes made;
3. E2E scenario results;
4. security findings/residual risks;
5. performance measurements;
6. publish/install artifact paths and hashes;
7. manual tests actually executed;
8. known limitations;
9. release/rollback procedure.

End with:

```text
Next optional prompt: prompts/17-new-agent-template.md
```

