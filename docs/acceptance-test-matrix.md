# System Acceptance Test Matrix

Stage 16 must automate or document every scenario below. Normal CI must use fake agents, temporary SQLite databases, local HTTP/HTML fixtures, fake time, and fake Windows adapters. Live browser, live AI, real sleep, and real remote access remain opt-in manual tests.

## Repository and startup

1. Fresh checkout restores, builds, tests, and formats cleanly.
2. Fresh data directory applies all migrations and seeds built-in agent manifests.
3. Existing compatible database upgrades through migrations without data loss.
4. Invalid bootstrap path or port configuration fails with a clear message.
5. A second Host process detects the mutex and asks the first instance to open the dashboard, then exits.
6. Host binds only to `127.0.0.1`.

## Configuration and audit

1. Saving valid agent configuration creates a new immutable revision.
2. Saving invalid configuration performs no partial write.
3. A no-op configuration save does not create a duplicate revision unless explicitly requested.
4. Every manual run references the exact configuration revision active at creation.
5. Secret values are stored through `ISecretStore`; SQLite contains only opaque references.
6. Logs, audit records, summaries, and rendered UI do not reveal secrets.
7. Configuration history displays redacted changed fields.

## Scheduling and occurrences

1. Daily and weekday schedules calculate correctly in the configured time zone.
2. DST spring-forward missing local time follows the documented policy.
3. DST fall-back ambiguous time creates at most one intended occurrence.
4. Fixed interval uses scheduled-time semantics.
5. Fixed delay uses previous completion-time semantics.
6. Paused or disabled schedules create no runnable occurrences.
7. Misfire `Skip` skips an expired occurrence.
8. Misfire `RunImmediately` creates/claims one immediate occurrence.
9. Concurrent host loops cannot create duplicate scheduled occurrences.
10. Manual run creates one occurrence with `ManualUI` or `TrayMenu` trigger.

## Runner

1. Runner atomically claims an occurrence.
2. A second runner cannot claim the same occurrence.
3. Fake successful agent events become persisted events, metrics, artifacts, summary, and completed status.
4. Malformed stdout lines are preserved as diagnostics and do not crash event processing.
5. Nonzero agent exit creates failed run status.
6. Timeout cancels and then terminates the child process tree.
7. User cancellation is distinguished from timeout.
8. Stale heartbeat recovery marks orphaned runs abandoned.
9. Artifact paths outside the assigned run directory are rejected.
10. A completed occurrence is never automatically executed again.

## Windows wake bridge

1. Generated task XML includes WakeToRun and the exact runner occurrence action.
2. Task registration command uses argument-safe process invocation.
3. Reconciliation updates the task when the earliest wake occurrence changes.
4. Reconciliation deletes/disables the wake task when no wake occurrence exists.
5. Failure to register a wake task creates an actionable health/audit event.
6. Power request is released on normal completion, cancellation, and exception.
7. Unit/integration tests never actually sleep or change permanent power policy.

## Wake & Remote agent

1. Network already ready produces immediate readiness.
2. Network becomes ready before timeout and records elapsed time.
3. Network timeout produces failed/partial summary according to configuration.
4. Required remote provider unavailable fails readiness.
5. Optional remote provider unavailable creates a warning but retains machine-ready status.
6. Keep-awake remains active through the configured window and is released afterward.
7. Cancellation releases keep-awake.
8. No code path forces sleep in V1.

## Tray and local UI

1. Tray double-click and menu action open the local dashboard.
2. Pause/resume agent and pause-all actions are audited.
3. Manual run starts Runner, not the agent executable directly.
4. UI renders idle, running, success, failed, paused, authentication-required, throttled, and challenge states.
5. State-changing requests require antiforgery tokens.
6. Run detail displays events, metrics, artifacts, stdout diagnostics, stderr diagnostics, and summary safely.
7. Destructive actions require confirmation.

## Founder Scout capture/discovery

1. Fixture import persists raw snapshot before analysis.
2. Dedicated browser account path is resolved only under the configured data directory.
3. Missing or expired session sets `ReauthenticationRequired` and stops the run.
4. Access denial, throttling, or challenge detection stops the current account/run and records diagnostics.
5. Parser-health failure stops rather than silently emitting incomplete profiles.
6. Identical profile content is not reanalyzed.
7. Changed profile creates a new snapshot and queues a new evaluation.
8. Cross-account discovery resolves to one candidate when stable identity matches.
9. Run batch, viewed, daily, runtime, and consecutive-known limits are enforced.
10. Invitations are never sent automatically.

## Founder Scout processing

1. Normalization is deterministic.
2. Hashing the same normalized profile produces the same hash.
3. Stable source key outranks weaker fingerprint identity.
4. Protected/irrelevant attributes are removed from evaluator input.
5. Missing fields lower completeness/confidence but do not fabricate negative facts.
6. Fast-screen rules produce evidence and reason codes.
7. Pending-analysis work is idempotently claimable.
8. Raw, normalized-profile, and evaluator-input SHA-256 hashes have distinct documented purposes.
9. Layout-only/raw changes preserve history but do not create another screening decision.
10. Relevant normalized changes create a bounded field-name-only diff and requeue exactly once.
11. Strong identity evidence converges candidates; weak ambiguity creates an identity-conflict record and manual review.
12. Processing claims are single-owner, expire for recovery, and are independent from deep-analysis claims.
13. Repeated parser-health failures stop the batch and pause the affected account/segment.
14. Hard-filter decisions are versioned and reversibly manual-overridable with an append-only action.
15. A 20-capture Runner smoke emits reason metrics/checkpoints, performs no AI calls, and proves protected fixture markers are absent from all evaluator inputs.

## AI evaluation and scoring

1. Strict valid model JSON maps to typed evaluation.
2. Invalid schema is rejected and retried only according to bounded policy.
3. Transient and permanent provider failures are distinguished.
4. Category scores outside bounds are rejected.
5. Final arithmetic is performed in C#.
6. Every nonzero category score has supporting profile evidence.
7. Missing evidence is explicit.
8. Scorecard/prompt/evaluator version changes trigger reevaluation.
9. Cached identical evaluation inputs avoid duplicate provider calls.
10. Live provider test is opt-in and skipped by default.
11. Exact quality and fit keys occur once each; key-specific bounds are enforced after strict schema deserialization.
12. Founder quality excludes `ctoFit` and is normalized from 90 to 100 in C#.
13. Fit, confidence, activity, risk, adjusted score, and invitation priority remain separate persisted dimensions.
14. A risk without grounded evidence applies no penalty; fabricated numeric claims force review.
15. Confidence below the configured threshold forces `ManualReview` regardless of numeric score.
16. Permanent provider/authentication failures stop the batch with attention required; transient failures use at most three total attempts.
17. A 20-profile real Runner/fake-provider smoke covers every recommendation, schema repair, evidence failure, transient retry, draft failure, cache reuse, and persona-triggered reevaluation.

## Introduction drafts

1. Every deep evaluation produces short and detailed drafts.
2. Draft references at least one grounded candidate-specific fact.
3. Draft states complementary founder value without unsupported claims.
4. Draft does not reference scoring, automation, protected attributes, or fabricated traction.
5. Draft respects configurable length.
6. Near-duplicate recent draft triggers one regeneration or manual-review status.
7. The product never sends the draft.
8. Validation failure retains the draft as `NeedsReview`; it is not discarded or sent.
9. Similarity checks exclude the same candidate's superseded draft while still comparing other active candidates.

## Candidate ranking/UI/reports

1. Founder quality, fit, confidence, activity, penalties, and invitation priority are stored separately.
2. Ranking is deterministic from stored values and configured formula.
3. Candidate filters and pagination return stable results.
4. Manual queue size is configurable and reserve candidates are displayed separately.
5. Marking an invitation sent requires user action and records an audit event.
6. Markdown, CSV, JSON, and HTML exports contain the same ranked candidate set.
7. Exported reports do not contain secrets, cookies, browser paths, or profile images.
8. Raw profile retention can delete raw content without corrupting derived evaluation/history.

## Recovery, backup, and performance

1. Backup uses a SQLite-safe mechanism while databases are active.
2. Restore validation opens the backup and verifies expected migrations/tables.
3. Interrupted backup leaves no file that is mistaken for a valid backup.
4. Host restart recovers due occurrences, stale leases, abandoned runs, and next wake task.
5. 10,000 candidate records remain usable for filtered/paginated list queries.
6. 100,000 run events remain queryable with appropriate indexes and bounded pages.
7. Retention deletes eligible artifacts and records audit results without deleting active-run data.
8. Published self-contained win-x64 binaries pass a clean-machine smoke script.
