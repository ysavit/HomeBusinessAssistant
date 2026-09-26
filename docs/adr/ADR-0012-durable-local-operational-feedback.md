# ADR-0012: Durable local operational feedback

- Status: Accepted
- Date: 2026-08-31

## Context

The interactive Host previously inferred attention from a bounded run query and held notified run identifiers only in memory. Restarting the Host could repeat a balloon or lose a pending notification. Run summaries also accepted agent output directly without a stable platform envelope, while diagnostics and retention were implemented at individual UI/domain boundaries.

The product remains a single-workstation, loopback-only system. Adding a broker or remote notification service would create unnecessary operational and privacy cost.

## Decision

The central `assistant.db` owns three additive operational records:

- `AttentionItem` is a deduplicated condition with explicit Active, Acknowledged, Resolved, and Suppressed lifecycle. Acknowledgement records operator awareness and never resolves the detector-owned condition.
- `LocalNotification` is a durable local tray-delivery request with not-before, attempt, throttle, expiry, and delivered state. Delivery is recorded only after an interactive Host subscriber accepts it.
- `DailySummary` is an immutable, versioned local-date/time-zone aggregation with a source hash and generation number.

Terminal run summaries use a deterministic `1.0` platform envelope. Agent-specific summary JSON is nested as bounded input, while status, duration, exit, timeout, cancellation, and protocol counts are calculated by platform code. Regeneration reads persisted events, metrics, and artifacts.

The Host periodically runs detectors and delivers at most one eligible notification per iteration. Quiet hours, global throttling, per-category throttling, bounded retries, and restart-safe deduplication are persistence-backed. Founder Scout contributes safe current signals through an application port; its profiles and evaluations remain in `founders.db`.

Retention is previewable and root-confined. Active-run artifacts, append-only audit, and immutable configuration revisions are protected. Diagnostics use a strict allow list, a 10 MiB cap, explicit exclusions, and a SHA-256 entry manifest; database files and payload-bearing sensitive sources are excluded.

## Consequences

- Tray absence leaves notifications pending rather than falsely delivered.
- Attention history survives Host restart and repeated signals do not create alert storms.
- Daily/run summaries remain operationally useful without an AI provider.
- The Host continues to be the sole interactive delivery process and no remote transport is introduced.
- Stage 15 can build backup/restore around explicit operational and retention boundaries without changing this model.
