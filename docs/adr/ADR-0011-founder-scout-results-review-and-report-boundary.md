# ADR-0011: Founder Scout results, manual review, and report boundary

- Status: Accepted
- Date: 2026-08-31

## Context

Founder Scout needs to expose ranked founder evidence, draft review, invitation planning, outcomes, operational attention, reports, and raw-data retention in the local Host. The workflow crosses `assistant.db` and `founders.db`, can be observed by Host and Runner concurrently, and must not turn a human-review draft into an outbound-message capability. Growing candidate history also makes loading EF aggregates or raw profile text into list pages unacceptable.

## Decision

The Host composes focused `IFounderScoutResultsQuery` and `IFounderScoutResultsCommands` boundaries over the separate Founder Scout database. List queries return bounded raw-text-free projections, apply filters and stable tie breakers in SQLite, and page before materialization. Candidate detail is a separately bounded read that assembles normalized evidence, safe field diffs, evaluation/version metadata, draft history, queue state, identity conflicts, and append-only actions.

Invitation planning uses explicit `InvitationQueueEntry` rows inside immutable-history `ManualInvitationWindow` records. Primary and reserve lanes have user-defined capacities, complete-order replacement, duplicate prevention, eligibility checks, and manual override provenance. Generated drafts remain immutable; each edit appends an `InvitationDraftRevision` and activates it while retaining prior revisions. Human-confirmed lifecycle transitions are the only source of manual-send and later outcome state. The command interface deliberately has no send method.

Every report format is rendered from one canonical selected candidate set and order. A SQLite report lease retains a monotonically increasing fencing token across releases. Writers use root-confined atomic temp-and-replace output, persist hash/size/retention metadata per file, encode HTML/Markdown, neutralize spreadsheet formulas in CSV, and omit raw/model/session/protected data from JSON.

Raw retention is a separate explicit command. It rejects escaped/reparse paths, skips current or active diagnostic material according to policy, deletes eligible files, and marks the snapshot while keeping every normalized profile, evaluation, draft, score, and action record.

## Consequences

- Host page handlers remain orchestration and typed-form adapters; discovery, analysis, authentication, diagnostics, and report generation still create durable Runner occurrences.
- Candidate list performance is independent of raw profile size and remains testable at 10,000 rows.
- Queue state and report output survive Host restarts and do not rely on an external platform quota/reset assumption.
- There is no automatic invitation transport, reserve promotion, external enrichment, or silent score-weight learning.
- A future provenance-aware split workflow and live-site/provider validation remain separate work.
