# Stage 02 — Persistence, Configuration, Audit, and Secrets

Paste this entire prompt into Codex from the repository root after Stage 01 passes.

---

You are implementing **Stage 02: Central Persistence, Configuration Revisions, Audit, Artifacts, and Windows-Protected Secrets**.

Read all repository instructions and relevant docs. Create/update `docs/exec-plans/stage-02-persistence-configuration-audit.md` before implementation.

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

Implement the central `assistant.db` persistence layer and application services required by all future scheduling, runner, tray, and UI stages. Provide EF Core migrations, focused repositories, immutable configuration revisions, append-only audit events, artifact metadata, leases, bootstrap database pragmas, path safety, redaction, and a Windows CurrentUser protected secret store.

Do not implement schedule calculation or process execution yet.

## Packages

Add centrally managed stable packages compatible with .NET 10:

- EF Core SQLite;
- EF Core Design for migrations, scoped appropriately;
- Serilog core plus rolling file sink and ASP.NET Core integration only if needed by this stage's composition/logging bootstrap;
- Windows DPAPI support package only if required by the target framework/runtime.

Document package choices and avoid prerelease dependencies unless the repository already intentionally uses them.

## 1. Central database model

Implement `AssistantDbContext` and mappings for the following entities. Use explicit configurations and indexes. Do not expose EF entities directly to the UI/application boundary.

### AgentDefinition

Fields:

- `Id` stable string/AgentId primary key;
- display name, description;
- manifest version;
- installed version;
- executable relative path;
- working-directory relative path;
- capabilities JSON;
- enabled;
- created/updated UTC timestamps.

### AgentConfiguration

- `Id` Guid primary key;
- `AgentId` unique foreign key;
- `CurrentRevisionId`;
- schema version;
- updated UTC timestamp.

### ConfigurationRevision

- Guid primary key;
- agent ID;
- monotonically increasing revision number per agent;
- canonical configuration JSON containing secret references only;
- SHA-256 configuration hash;
- changed-by actor;
- optional change summary;
- created UTC timestamp.

Revisions are immutable after creation. Enforce uniqueness on `(AgentId, RevisionNumber)` and `(AgentId, ConfigurationHash)` as appropriate. Define no-op save behavior explicitly.

### AgentSchedule

Create the storage entity now so Stage 03 can implement behavior:

- ID, agent ID, name;
- schedule type string/enum;
- schedule JSON;
- time-zone ID;
- wake, misfire, and concurrency policy;
- timeout seconds;
- retry policy JSON;
- enabled;
- paused-until UTC;
- created/updated UTC timestamps.

### ScheduleOccurrence

Create full durable storage:

- ID;
- optional schedule ID;
- agent ID;
- command name;
- arguments JSON;
- due UTC;
- status;
- trigger source;
- configuration revision ID;
- attempt number;
- optional parent occurrence ID;
- claimed-by/claimed-at;
- started/completed timestamps;
- optional run ID;
- cancellation requested UTC and reason;
- created UTC.

Create indexes supporting due work, agent/status queries, and uniqueness of scheduled occurrences.

### AgentRun

- ID and occurrence ID;
- agent/runner/manifest versions;
- executable hash;
- configuration revision/hash;
- machine name and process ID;
- status;
- start, heartbeat, completion, duration;
- exit code;
- summary text/JSON;
- error type/message;
- created/updated concurrency fields as needed.

### AgentRunEvent

- ID, run ID, sequence, timestamp, level, event type, message, data JSON.
- Unique `(RunId, Sequence)` where sequence originates from valid agent protocol; reserve a strategy for runner-generated events so no collision occurs.

### AgentRunMetric

- ID, run ID, name, numeric value, text value, unit, tags JSON.

### RunArtifact

- ID, run ID, artifact type, file name, relative path, content type, size, SHA-256, created UTC, delete-after UTC.

### AuditEvent

- ID, timestamp, actor type/ID, action, target type/ID, optional run ID, correlation ID, outcome, redacted data JSON.

### AgentLease

- lease name primary key;
- owner ID;
- acquired/expires UTC;
- fencing token or version value to prevent stale owners from extending/releasing a newer lease.

### SystemSetting

- key, value JSON, schema version, updated UTC, optional concurrency token.

## 2. Database bootstrap

Implement a database initializer that:

1. validates and creates the data directory;
2. opens SQLite;
3. applies migrations;
4. sets/verifies:
   - `journal_mode=WAL`;
   - `foreign_keys=ON`;
   - bounded `busy_timeout`;
   - `synchronous=NORMAL` unless an ADR justifies another value;
5. records a clear startup failure if migration or pragmas fail.

Use migrations, not `EnsureCreated`.

Create the initial migration and verify it against a fresh temporary database.

## 3. Focused application ports and repositories

Create focused interfaces and implementations, not a generic repository:

- `IAgentDefinitionRepository`
- `IAgentConfigurationService`
- `IScheduleRepository`
- `IOccurrenceRepository`
- `IAgentRunRepository`
- `IAuditWriter`
- `IArtifactStore`
- `ILeaseManager`
- `ISystemSettingRepository`

Design methods around use cases and atomic operations. Examples:

- get enabled agents;
- create a configuration revision and promote it atomically;
- create an occurrence if absent;
- atomically claim one occurrence;
- request cancellation;
- append run events/metrics;
- mark run/occurrence terminal;
- acquire/renew/release lease with fencing.

Do not hold a DbContext across external work. Prefer `IDbContextFactory<AssistantDbContext>` for host/runner concurrency.

## 4. Canonical configuration and revisions

Implement:

- JSON canonicalization with stable property ordering and normalized representation;
- SHA-256 hashing over UTF-8 canonical JSON;
- secret-reference validation such as `secret://<namespace>/<name>`;
- options/schema validation hook per agent;
- atomic revision creation and current-revision update;
- immutable history queries;
- a redacted change summary/diff that does not reveal secrets.

If a semantically identical configuration is saved, return the existing revision and record either no audit event or a documented no-op audit event. Do not create unlimited duplicate revisions.

## 5. Secret store

Define `ISecretStore` in Application and implement a Windows CurrentUser protected file-backed store in `HomeBusinessAssistant.Windows`:

```text
SetAsync(reference, secret)
GetAsync(reference)
ExistsAsync(reference)
DeleteAsync(reference)
```

Requirements:

- use DPAPI/CurrentUser protection;
- use an application-specific entropy value derived from stable non-secret application identity;
- map secret references to safe hashed file names;
- store under the configured data root in a dedicated secrets directory;
- write atomically via temp file + replace/move;
- do not expose secret values in exception messages, logs, audit, or tests;
- zero temporary byte buffers where practical;
- validate allowed references and reject traversal;
- provide an in-memory fake for tests;
- do not back up secret files in the ordinary unencrypted database backup stage.

Document the threat model: this protects data at rest from casual file inspection but code running as the same Windows user can retrieve the secrets.

## 6. Artifact store and path safety

Implement a file-system artifact store rooted under the validated application data directory:

- per-agent/per-run directories;
- safe relative paths only;
- atomic writes;
- SHA-256 and size calculation;
- metadata persistence;
- bounded maximum size configurable later;
- collision-safe names;
- deletion by retention service in later stage;
- reject symlink/reparse/path traversal escapes where practical on Windows.

## 7. Audit and redaction

Implement:

- append-only `AuditWriter`;
- correlation IDs;
- redaction utility for known secret property names, URI credentials, authorization headers, cookie fields, and secret references where appropriate;
- audit records for configuration created/changed, secret set/deleted, agent installed/enabled/disabled, and database initialization/recovery events;
- structured JSON data with bounded size.

Do not put full raw exceptions, stack traces, configuration JSON, or secret values in audit records. Logs may contain stack traces but must still redact sensitive content.

## 8. Seed built-in agent definitions

During bootstrap, idempotently load the two supplied manifests or create minimal manifest files from `docs/configuration-examples.md`:

- `founder-scout`
- `wake-remote`

Seeding must not overwrite user-managed current configuration revisions. Record manifest/version changes safely.

## Tests

Use temporary file-system roots and real SQLite databases. Cover at minimum:

- migrations on a fresh database;
- all required indexes/foreign keys;
- WAL/foreign-key/busy-timeout setup;
- atomic configuration revision creation;
- canonical JSON and hash stability across property order;
- duplicate/no-op revision behavior;
- concurrent revision attempts;
- focused repository queries;
- atomic occurrence claim with competing callers;
- lease acquisition, fencing, renewal, expiry, stale release rejection;
- audit append and redaction;
- DPAPI secret round-trip on Windows, plus negative invalid-reference tests;
- artifact path traversal rejection, atomic write, hash/size;
- database bootstrap idempotency;
- agent seeding idempotency.

Do not use EF Core InMemory.

## Constraints

- No schedule calculation or hosted scheduler.
- No child-process launch.
- No UI forms.
- No Playwright or AI.
- No real Windows scheduled task registration.
- No broad generic repository/unit-of-work abstraction.

## Done when

- `assistant.db` can be created and migrated from a clean data directory.
- Built-in agents are present.
- Configuration revisions are immutable, canonical, hashed, auditable, and secret-safe.
- Occurrence claiming and leases are concurrency-safe.
- Audit, artifacts, and DPAPI secret storage work with tests.
- All standard validation passes.

## Required smoke validation

Add a development-only CLI or test harness that:

1. creates a temporary platform data directory;
2. migrates the database;
3. seeds agents;
4. writes and reads a non-sensitive configuration revision;
5. writes/reads/deletes a temporary secret;
6. acquires/releases a lease;
7. writes an audit event and artifact;
8. prints only non-sensitive IDs/statuses.

Run it and include the actual result.

## Final report

Include migration name, central tables/indexes, secret-store threat model, SQLite pragmas, concurrency approach, and command results. End with:

```text
Next prompt: prompts/03-scheduling-engine.md
```

