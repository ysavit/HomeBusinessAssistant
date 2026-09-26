# ADR-0002: Local SQLite coordination and CurrentUser secret storage

- Status: Accepted
- Date: 2026-08-29
- Stage: 02
- Owners: Home Business Assistant

## Context

Stage 02 introduces the first durable state and the first credential boundary. The baseline mandates central SQLite, short transactions, atomic occurrence/lease claims, immutable configuration revisions, and Windows-protected secrets, but it does not fully specify how competing local processes establish identity, how SQLite connection pragmas remain consistent, or how opaque references map to recoverable-at-runtime secret files.

Verified Stage 01 state had no database provider, migration, durable data, or secret store. The Stage 02 platform continues to target one Windows workstation and one Windows user profile rather than distributed coordination.

## Decision drivers

- A tray scheduler and wake-launched runner can observe the same work concurrently.
- Configuration equality must not depend on JSON property order.
- SQLite transactions must remain short and must not surround external work.
- Stale lease owners must not release or renew a later owner's lease.
- Secret values must not enter SQLite, configuration JSON, audit data, file names, or normal backups.
- Every context/connection must enforce the same foreign-key, timeout, and durability behavior.

## Decision

Use `assistant.db` as the single-workstation coordination authority. Every operation creates a short-lived `AssistantDbContext` through `IDbContextFactory`. A connection interceptor applies `foreign_keys=ON`, the bounded busy timeout, and `synchronous=NORMAL` whenever SQLite opens; bootstrap applies migrations, establishes persistent WAL mode, and verifies all four pragmas.

Configuration objects are recursively property-sorted into compact canonical UTF-8 JSON. SHA-256 over that representation is the configuration identity. Unique `(AgentId, RevisionNumber)` and `(AgentId, ConfigurationHash)` indexes plus a serializable short transaction make revision creation and current-pointer promotion atomic. Saving the current hash is a no-op without another audit event.

Scheduled occurrence identity is unique on `(ScheduleId, DueAtUtc)` for non-null schedule IDs. Retries are separate occurrences linked by `ParentOccurrenceId`; they do not reuse the scheduled identity. Claims use a conditional update from `Pending` to `Claimed`.

Leases retain their database row on release and set it expired. Reacquisition increments a monotonically increasing fencing token, so an older owner/token pair cannot renew or release a newer lease even after an explicit release.

Secret material is addressed by validated `secret://namespace/name` references. The Windows adapter hashes the full reference with SHA-256 for a non-revealing file name, protects UTF-8 bytes with DPAPI `CurrentUser` and stable application-purpose entropy, and atomically moves files beneath `<data-root>/secrets`. Set/delete audit writes use only the reference hash and compensate the file operation if the audit append fails.

## Alternatives considered

### Process-local locks and in-memory revision checks

These would not coordinate the interactive host and a separately launched runner. Process-local state also disappears during recovery and cannot establish a durable winner.

### Delete lease rows on release

This resets a newly inserted row's fencing token and can let a stale lease record collide with a later lease held by the same owner ID. Retaining an expired row preserves monotonic fencing.

### Store secrets in SQLite or use reference text as a file name

Database storage would mix backup and credential boundaries. Reference-derived paths can disclose provider/account naming and expand path-safety risk. Hashed names plus DPAPI keep addressing opaque and content protected at rest.

### Machine-scope DPAPI

Machine scope would allow other users/process identities on the workstation to unprotect values more broadly than necessary. V1 runs interactively as one Windows user, so `CurrentUser` matches the process and threat boundary.

## Consequences

### Positive

- Database constraints resolve competing local callers without a hidden in-memory authority.
- Revision and occurrence identities remain stable across process restarts.
- Fencing tokens distinguish stale owners after expiry and explicit release.
- Ordinary database backups do not include secret material.
- Copied secret files cannot be unprotected by a different Windows user profile.

### Negative / tradeoffs

- DPAPI-protected files are not portable recovery artifacts; changing Windows user profiles requires explicit secret re-entry.
- Code already running as the same Windows user can call DPAPI and read the secrets.
- File operations and SQLite audit writes cannot share one physical transaction; the adapter uses compensation and can still require operator diagnosis after process or machine failure at an unlucky boundary.
- SQLite remains appropriate only for the documented single-workstation deployment.

### Risks and mitigations

- SQLite lock contention — bounded busy timeout, WAL, short contexts/transactions, conditional writes, and real concurrent integration tests.
- Reparse-point/path escape — validated roots, hashed/safe components, containment checks, and reparse-point rejection where practical on Windows.
- Secret leakage in diagnostics — safe exception messages, hashed audit targets, structured redaction, and tests that search persisted audit/file content.
- Corrupted or copied DPAPI ciphertext — fail closed with no plaintext result and require the user to re-enter the secret.

## Compatibility with baseline architecture

This clarifies and preserves `AGENTS.md` and `docs/architecture.md`: SQLite remains the central local database, secrets remain Windows-protected and outside it, and external work remains outside transactions. The scheduled-occurrence identity is reconciled to the repository rule `(ScheduleId, DueAtUtc)`; explicit retries use parent links instead of extending that identity.

## Implementation and migration

- EF Core SQLite/Design 10.0.11 and `System.Security.Cryptography.ProtectedData` 10.0.11 are centrally pinned.
- `InitialCentralPersistence` creates the 12 central tables and their foreign keys/indexes.
- Runtime bootstrap creates the data root, migrates, configures/verifies pragmas, and idempotently seeds the two built-in manifests.
- Existing Stage 01 installations contain no database, so no data transformation is required.

## Validation

- Fresh real-SQLite migration, schema/index/foreign-key inspection, and pragma verification.
- Concurrent first configuration saves, occurrence claims, and lease acquisitions.
- Lease renewal, release, reacquisition, and stale-token rejection.
- DPAPI round trip, ciphertext inspection/corruption, audit compensation, and deletion.
- Artifact containment, size, atomic-write cleanup, SHA-256, and readback.
- Development Runner persistence smoke in a self-cleaning temporary root.

## Follow-up work

- Stage 03 consumes schedule storage and occurrence identity for recurrence and misfire behavior.
- Stage 04 expands run transitions, heartbeats, recovery, and process supervision around the existing atomic primitives.
- A later backup/restore stage must exclude `secrets` and explicitly explain DPAPI non-portability.
