# ADR-0007: Founder Scout bounded context and import commit order

- Status: Accepted
- Date: 2026-08-30

## Context

The platform owns durable schedules, occurrences, process runs, protocol events, audit, and installation state in `assistant.db`. Founder Scout needs a substantially different lifecycle for browser accounts, profile captures, candidate identity, screening, evaluation, invitation drafts, and retention. Coupling those tables to the central database would make agent evolution and a future service boundary harder, while a distributed transaction across two SQLite databases would be fragile and unnecessary.

Fixture and later browser captures also create a commit-order hazard. Queueing analysis before raw content is durable can leave unrecoverable work. Committing domain data before the central Runner finalizes can leave a successful local import paired with a failed central finalization.

## Decision

- Founder Scout owns a separate migrated `founders.db` beneath the Runner-assigned per-agent data directory. `assistant.db` contains no Founder Scout domain entities.
- The databases correlate by central run ID and bounded correlation ID values only. There are no cross-database foreign keys and no distributed transaction.
- The Runner-assigned `--data-directory` is the filesystem authority for a launched agent. Editable configuration cannot redirect database, fixture, snapshot, or report access outside that root.
- Raw capture files are bounded, content-addressed, and atomically moved into Founder Scout storage before a short local transaction creates the snapshot, updates the candidate, queues analysis, and appends actions.
- Strong candidate aliases have database-enforced active ownership. Weak fingerprints remain non-unique until a deliberate manual merge.
- Snapshot replay is idempotent by `(CandidateId, SourceProfileKey, ContentHash)`. A central finalization failure is recovered by replaying the operation, not by deleting already committed local domain evidence.
- Candidate actions and discovery checkpoints are append-only through the application boundary. Analysis work uses conditional database claims with worker ownership and expiry.

## Consequences

- Founder Scout migrations, backups, retention, and performance tuning are independent of central scheduling state.
- A failure between raw-file commit and the local transaction can leave an unreferenced content-addressed file, but cannot queue analysis without its raw evidence. Later retention work may safely remove only proven-unreferenced files.
- A failure after the local commit but before Runner finalization can produce a central failed run whose domain effects exist. Run/correlation values make that visible, and idempotency makes a retry safe.
- Repository and report queries cannot rely on central-table joins. The Host and future Founder Scout UI must compose bounded reads using identifiers rather than EF relationships across contexts.
- Browser discovery, parsing, scoring, AI evaluation, candidate UI, and automatic retention deletion remain separate later-stage concerns.
