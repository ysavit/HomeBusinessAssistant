# Target Architecture

## 1. System purpose

Home Business Assistant is a local Windows control plane for independently executable agents. It owns configuration, durable scheduling, wake integration, process execution, run supervision, audit, metrics, summaries, and the local management UI. Agents own their domain data and domain-specific workflows.

The first two agents are:

- **Founder Scout**: capture/discover founder profiles, parse, normalize, deduplicate, screen, evaluate, rank, generate introduction drafts, and maintain a manual invitation queue.
- **Wake & Remote**: coordinate scheduled wake events, wait for network readiness, verify a configured remote-access provider, hold a bounded keep-awake request, and publish readiness/audit results.

## 2. V1 process model

```text
Windows interactive user session
┌────────────────────────────────────────────────────────────────────┐
│ HomeBusinessAssistant.Host.exe                                     │
│                                                                    │
│  WinForms NotifyIcon + tray menu                                   │
│  ASP.NET Core Kestrel on http://127.0.0.1:<port>                   │
│  Razor Pages UI                                                    │
│  schedule planner/reconciler                                       │
│  task-scheduler wake reconciler                                    │
│  stale-run recovery and health polling                             │
└───────────────┬────────────────────────────────────────────────────┘
                │ create occurrence / start runner / read database
                ▼
┌────────────────────────────────────────────────────────────────────┐
│ HomeBusinessAssistant.Runner.exe                                   │
│                                                                    │
│  atomically claim occurrence                                       │
│  load immutable config revision                                    │
│  acquire lease                                                     │
│  launch agent process                                              │
│  parse JSONL stdout                                                 │
│  capture stderr                                                     │
│  heartbeat, timeout, cancellation, process-tree termination         │
│  persist events, metrics, artifacts, summary, result                │
└───────────────┬───────────────────────────────┬────────────────────┘
                │                               │
                ▼                               ▼
     ┌──────────────────────┐        ┌────────────────────────┐
     │ FounderScout.exe     │        │ WakeRemote.exe         │
     │ founders.db          │        │ platform config/state  │
     │ browser profiles     │        │ power/network checks   │
     │ reports/artifacts    │        │ bounded keep-awake     │
     └──────────────────────┘        └────────────────────────┘

Windows Task Scheduler
┌────────────────────────────────────────────────────────────────────┐
│ \HomeBusinessAssistant\HostAtLogon                                 │
│ \HomeBusinessAssistant\NextWake                                   │
│                                                                    │
│ HostAtLogon starts Host.exe in the interactive user session.       │
│ NextWake has WakeToRun=true and invokes Runner for one occurrence. │
└────────────────────────────────────────────────────────────────────┘
```

## 3. Solution boundaries

The optional `FounderScout.SimpleCli` is an interactive console composition root. It reuses `FounderScout.Agent` commands and the private execution-input manager, shares `founders.db` and the dedicated browser profile with the Host workflow, and prints protocol progress locally. It does not create central occurrences or replace the scheduled Host/Runner path. See [ADR-0020](adr/ADR-0020-simple-scout-console.md).

```text
src/
├── HomeBusinessAssistant.Domain
├── HomeBusinessAssistant.Application
├── HomeBusinessAssistant.AgentSdk
├── HomeBusinessAssistant.Infrastructure
├── HomeBusinessAssistant.Windows
├── HomeBusinessAssistant.Host
└── HomeBusinessAssistant.Runner

agents/
├── FounderScout/
│   ├── FounderScout.Domain
│   ├── FounderScout.Application
│   ├── FounderScout.Infrastructure
│   └── FounderScout.Agent
└── WakeRemote/
    ├── WakeRemote.Application
    ├── WakeRemote.Infrastructure
    └── WakeRemote.Agent

tests/
├── HomeBusinessAssistant.Domain.Tests
├── HomeBusinessAssistant.Application.Tests
├── HomeBusinessAssistant.AgentSdk.Tests
├── HomeBusinessAssistant.Infrastructure.Tests
├── HomeBusinessAssistant.Windows.Tests
├── HomeBusinessAssistant.Runner.Tests
├── HomeBusinessAssistant.Host.Tests
├── FounderScout.Tests
├── WakeRemote.Tests
└── HomeBusinessAssistant.EndToEndTests
```

### Dependency direction

```text
HomeBusinessAssistant.Domain <- HomeBusinessAssistant.Application <- Infrastructure <- Hosts
              ^
              └── HomeBusinessAssistant.AgentSdk <- Runner and agent executables
```

The SDK depends inward on pure platform identifiers and policies from `HomeBusinessAssistant.Domain`; Domain does not depend on the SDK. See [ADR-0001](adr/ADR-0001-agent-contract-ownership-and-event-discrimination.md).

No domain project references EF Core, ASP.NET Core, Playwright, Windows APIs, or executable projects.

Stage 02 keeps EF entities internal to Infrastructure. Application-facing persistence ports exchange immutable records and typed Domain identifiers. Infrastructure also reads versioned SDK manifests for built-in seeding; Windows depends on Application only to implement `ISecretStore`; Runner composes Infrastructure and Windows adapters. Stage 04 adds focused Infrastructure execution adapters for executable integrity, shell-free process launch, bounded protocol ingestion, heartbeats/cancellation, finalization, and stale recovery; Runner remains the thin CLI composition root. See [ADR-0002](adr/ADR-0002-local-sqlite-coordination-and-current-user-secret-storage.md).

Stage 03 keeps schedule definitions, time-zone calculation, validation, planning, policy, manual-run, completion, and reconciliation use cases in Application. Infrastructure supplies SQLite queries and atomic occurrence transitions. Scheduler time and occurrence semantics are fixed by [ADR-0003](adr/ADR-0003-deterministic-schedule-time-and-occurrence-semantics.md). Stage 05 adds Application wake/power ports, Windows `schtasks`/`powercfg`/execution-state adapters, and Runner composition without allowing Windows dependencies into Domain or Application. See [ADR-0004](adr/ADR-0004-windows-wake-task-and-power-request-semantics.md).

Stage 06 keeps Wake Remote configuration, occurrence/window mapping, probe ports, and workflow orchestration in `WakeRemote.Application`, which reuses platform Application/Domain contracts. Windows network/provider implementations remain in `WakeRemote.Infrastructure`; `WakeRemote.Agent` composes those adapters and the SDK, while Runner composes the agent's default configuration provider. This host-level dependency does not introduce a library-to-executable edge or a cycle.

Stage 07 composes Application, Infrastructure, Windows, and Wake Remote Application in the interactive Host. Host-only read models expose bounded safe dashboard state; Windows owns current-user mutex/pipe activation, validated browser opening, and detached Runner launch. Agents remain outside the Host process. See [ADR-0005](adr/ADR-0005-interactive-host-lifecycle-and-runner-dispatch.md).

Stage 08 adds an Application-owned management query/command boundary. Infrastructure supplies bounded, stable `AsNoTracking` projections; Razor Page models translate typed forms and never receive EF entities. The Host also composes the Founder Scout baseline configuration adapter, DPAPI secret-status/mutation boundary, root-confined artifacts, wake tests, power diagnostics, and existing schedule/Runner orchestration. See [ADR-0006](adr/ADR-0006-local-management-ui-boundary.md).

Stage 15 adds composition-root-only dependencies from Host to AgentSdk for shared release identity and from Runner to Founder Scout Infrastructure for explicit migration and two-database backup/restore. No domain/application layer depends outward, and neither edge references an executable. See [ADR-0013](adr/ADR-0013-windows-folder-deployment-and-online-backup.md).

Stage 17 adds no new process or database. Application owns a bounded primitive configuration-schema model and validator; Infrastructure owns root-confined schema loading and installed-package scanning; Host renders platform-owned generic controls; Runner exposes explicit reconciliation. Agents remain independent executables and publishing remains an allow-listed composition step. See [ADR-0014](adr/ADR-0014-safe-agent-extension-and-generic-configuration.md).

Stage 18 adds an Application-owned durable onboarding state machine and composable readiness contract. Infrastructure owns the central session/check repository plus database/package/storage checks; Windows owns query-only Task Scheduler and power projections; Host owns exact-loopback/current-owner verification, dashboard entry routing, and the Razor flow. The explicit protected-storage probe uses only `ISecretStore`. No onboarding component configures, enables, schedules, or executes an agent. See [ADR-0015](adr/ADR-0015-durable-onboarding-readiness-and-entry-policy.md).

Stage 19 adds Application-owned per-agent selection/progress and `IAgentOnboardingAdapter` contracts. Host composes a fixed specialized-adapter map and the platform generic fallback; package assemblies are never discovered or loaded. Infrastructure owns bounded non-executing package inspection and selection persistence. Razor reuses the shared platform generic form codec, while authoritative immutable configuration and protected-secret writes remain behind `IAgentConfigurationService` and `ISecretStore`. Selection does not enable, schedule, diagnose, or execute an agent. See [ADR-0016](adr/ADR-0016-explicit-agent-onboarding-adapters-and-durable-selection.md).

Stage 20 replaces the Founder Scout placeholder with a typed specialized adapter/service and seven-step Razor wizard. Existing immutable configuration, focused `founders.db` account/segment records, DPAPI secret storage, safe agent-scoped onboarding checks, and central Runner dispatch remain authoritative. A manual `authenticate` occurrence may carry an immutable `AllowDisabledAgent=true` only for this confirmed setup action; all other occurrences default false. Browser checks never download, provider checks use one minimal official-SDK Responses request with no retry or founder/persona content, and capture/screen-only mode can defer AI. Stage 20 does not enable, schedule, discover, analyze, or send. See [ADR-0017](adr/ADR-0017-founder-scout-onboarding-and-disabled-authentication.md).

## 4. Central platform data

The platform database is `assistant.db`.

### AgentDefinition

```text
Id                         string stable agent ID
DisplayName                string
Description                string?
ManifestVersion            string
ExecutablePath             string
WorkingDirectory           string
Enabled                    bool
InstalledVersion           string
CapabilitiesJson           JSON
CreatedAtUtc               DateTimeOffset
UpdatedAtUtc               DateTimeOffset
```

### AgentConfiguration

```text
Id                         Guid
AgentId                    string
CurrentRevisionId          Guid
SchemaVersion              string
UpdatedAtUtc               DateTimeOffset
```

### ConfigurationRevision

```text
Id                         Guid
AgentId                    string
RevisionNumber             long
ConfigurationJson          JSON, secret references only
ConfigurationHash          SHA-256
ChangedBy                  string
ChangeSummary              string?
CreatedAtUtc               DateTimeOffset
```

Revisions are immutable. A run references one revision.

### AgentSchedule

```text
Id                         Guid
AgentId                    string
Name                       string
CommandName                string manifest-supported command
ArgumentsJson              JSON object
ScheduleType               enum
ScheduleJson               versioned JSON typed by schedule type
TimeZoneId                 Windows time-zone ID
WakePolicy                 enum
MisfirePolicy              enum
ConcurrencyPolicy          enum
TimeoutSeconds             int
MisfireGracePeriodSeconds  int
RetryPolicyJson            JSON
PinnedConfigurationRevisionId Guid?
AllowDisabledAgent         bool
Enabled                    bool
IsPaused                   bool
PausedUntilUtc             DateTimeOffset?
CreatedAtUtc               DateTimeOffset
UpdatedAtUtc               DateTimeOffset
```

### ScheduleOccurrence

```text
Id                         Guid
ScheduleId                 Guid?
AgentId                    string
CommandName                string
ArgumentsJson              JSON
DueAtUtc                   DateTimeOffset
Status                     OccurrenceStatus enum
TriggerSource              enum
RequiresWake               bool immutable occurrence policy
KeepSystemAwake            bool immutable Runner lifetime policy
KeepDisplayOn              bool explicit opt-in, false by default
AllowDisabledAgent         bool immutable explicit setup exception, false by default
ConfigurationRevisionId    Guid
AttemptNumber              int
ParentOccurrenceId         Guid?
ClaimedBy                  string?
ClaimedAtUtc               DateTimeOffset?
ClaimExpiresAtUtc          DateTimeOffset?
StartedAtUtc               DateTimeOffset?
CompletedAtUtc             DateTimeOffset?
AgentRunId                 Guid?
CancellationRequestedAtUtc DateTimeOffset?
CancellationReason         string?
TerminalReasonCode         string?
TerminalMessage            string?
CreatedAtUtc               DateTimeOffset
UpdatedAtUtc               DateTimeOffset
```

Unique scheduled identity: `(ScheduleId, DueAtUtc)` for non-null schedule IDs. Retries are new occurrences linked through `ParentOccurrenceId`; see ADR-0002.

Occurrence status is separate from process-run status:

```text
Planned -> Ready -> Claimed -> Starting -> Running
Planned/Ready -> Skipped
Planned/Ready/Claimed/Starting/Running -> CancellationRequested
Running/CancellationRequested -> Completed | Failed | TimedOut | Cancelled | Abandoned
```

Terminal rows are never repurposed for a different due time. Machine-readable reason codes remain separate from bounded redacted human messages.

### AgentRun

```text
Id                         Guid
OccurrenceId               Guid
AgentId                    string
AgentVersion               string
RunnerVersion              string
ManifestVersion            string
ExecutableSha256           string
ConfigurationRevisionId    Guid
ConfigurationHash          string
MachineName                string
ProcessId                  int?
Status                     enum
StartedAtUtc               DateTimeOffset
LastHeartbeatAtUtc         DateTimeOffset
CompletedAtUtc             DateTimeOffset?
DurationMilliseconds       long?
ExitCode                   int?
SummaryText                string?
SummaryJson                JSON?
ErrorType                  string?
ErrorMessage               string?
CreatedAtUtc               DateTimeOffset
UpdatedAtUtc               DateTimeOffset
```

### AgentRunEvent / AgentRunMetric / RunArtifact

Run events are append-only children with a unique `(RunId, Sequence)` identity; runner-generated events reserve negative sequence numbers and agent protocol events use positive numbers. Metrics can carry a numeric value, text value, unit, and tags JSON. Artifact content lives on disk; SQLite stores its bounded metadata, SHA-256, size, type, retention timestamp, and relative path.

### AuditEvent

```text
Id                         Guid
TimestampUtc               DateTimeOffset
ActorType                  enum: User, Host, Runner, Agent, Recovery
ActorId                    string
Action                     string
TargetType                 string
TargetId                   string?
AgentRunId                 Guid?
CorrelationId              string
Outcome                    enum
DataJson                   redacted JSON
```

### AgentLease

Used for scheduler reconciliation, occurrence execution, analysis, discovery, backup, and other single-writer work.

### OnboardingSession / OnboardingCheck / OnboardingAgentSelection

`OnboardingSession` is the durable owner workflow with schema version, kind, status, allow-listed current step, lifecycle timestamps, actor/correlation identity, optimistic revision, and warning/acknowledgement/completed-step counts. One filtered active slot prevents competing first-run/add-agent workflows, and one initialization key identifies the unique first-run baseline.

`OnboardingCheck` stores only the latest safe result for `(SessionId, Scope, CheckKey, AgentScopeKey)`: Passed, Warning, Blocked, Unknown, or NotApplicable; safe reason/title/message; observation/expiry; schema-versioned bounded details; and an allow-listed remediation key. Rechecks update this latest row while append-only audit preserves batch history.

`OnboardingAgentSelection` is unique on `(SessionId, AgentId)` and stores explicit selected/deferred/unavailable/removed state, independent per-agent progress, adapter/step identity, reviewed package/schema versions, optional starting/saved immutable configuration revision IDs and hashes, safe reason/timestamps, and its own optimistic revision. It never stores configuration JSON or secret values. Missing packages are reconciled in place so configuration, schedule, run, and onboarding history remain intact.

## 5. Scheduling model

Supported V1 schedule types:

- Manual only
- One time
- Daily at local time
- Selected weekdays at local time
- Fixed interval from scheduled time
- Fixed delay after prior completion

Founder Scout recommended use:

```text
Discovery: fixed delay after completion; process a bounded batch; next due after cooldown.
Analysis: immediate follow-up occurrence when pending profiles exist, plus periodic recovery polling.
Reports: after successful analysis or once daily.
```

The host scheduler periodically:

1. acquires a schedule-reconciliation lease;
2. calculates missing future occurrences;
3. applies misfire rules;
4. identifies the earliest enabled occurrence requiring wake;
5. reconciles the Windows `NextWake` task;
6. starts due non-wake occurrences through Runner when the user session is active.

The runner atomically claims an occurrence. This prevents a due occurrence from running twice when both the tray scheduler and Windows Task Scheduler observe it.

Stage 03 implements steps 1–4 as a host-independent bounded reconciliation use case and reports the earliest wake occurrence without registering it. Defaults are a seven-day calendar horizon, at most 100 creations per schedule per reconciliation, a 30-second reconciliation lease, and initial Windows time zone `Central Standard Time`.

Calendar DST policy is deterministic: an invalid spring-forward local time advances to the first valid local second after the gap; an ambiguous fall-back time uses the larger UTC offset, producing the earlier UTC instant. One intended local slot creates one UTC occurrence.

Fixed interval cadence advances from scheduled due times. Fixed delay creates its next normal occurrence only after terminal completion, at `CompletedAtUtc + Delay`; retries are separate and do not change that cadence. See ADR-0003.

## 6. Windows wake integration

`NextWake` is a generated Task Scheduler task with:

- one trigger for the earliest pending occurrence requiring wake;
- `WakeToRun=true`;
- the current Windows user as principal;
- an action that calls `HomeBusinessAssistant.Runner.exe execute --occurrence-id <id>`;
- bounded execution settings;
- a task description containing the occurrence and configuration IDs for diagnostics.

Stage 05 implements this as deterministic Task Scheduler 2.x XML registered through shell-free `schtasks.exe` argument lists. `StartBoundary` is the exact persisted UTC due instant with `Z`; schedule-local time and the Windows time-zone ID are diagnostic description fields. `MultipleInstancesPolicy=IgnoreNew` prevents overlapping task instances, while the durable occurrence claim remains the authoritative duplicate defense. `StartWhenAvailable` follows misfire intent: RunImmediately and the guided wake test enable it; skip/next-scheduled policies do not.

The principal is the current Windows user with `InteractiveToken` and least privilege. No password is stored, so V1 requires that user to remain signed in (a locked or sleeping session is supported; sign-out is not). Battery start/stop settings are explicit; the initial default permits the requested work to start and continue on battery.

Occurrences persist `RequiresWake`, `KeepSystemAwake`, and `KeepDisplayOn`. Wake-enabled schedules set wake/system true and display false; retries inherit the parent flags. Runner acquires a reference-counted `ES_CONTINUOUS | ES_SYSTEM_REQUIRED` request before child launch and releases it after finalization. Display-required is used only by an explicit future policy. All native calls share a dedicated thread because the Win32 execution state is thread-scoped. Releasing the last handle lets normal Windows idle policy resume; the platform never forces sleep.

Read-only local diagnostics wrap `powercfg /a`, `/waketimers`, `/lastwake`, and `/devicequery wake_armed` with bounded/redacted output. The harmless `wake-test` occurrence records actual Runner start and a timestamped JSON artifact. Real task registration and sleep remain an explicit manual workflow.

After the occurrence starts or finishes, the platform registers the next task.

Wake is expected from sleep/hibernate when supported by the device, firmware, Windows power plan, and task settings. The platform exposes diagnostics and a guided test; it does not promise unsupported hardware behavior.

## 7. Agent protocol

Agents are ordinary console executables. Stdout is reserved for UTF-8 JSON Lines protocol events. Stderr is diagnostic text.

Envelope:

```json
{
  "protocolVersion": "1.0",
  "type": "progress",
  "runId": "11111111-1111-1111-1111-111111111111",
  "sequence": 4,
  "timestampUtc": "2026-08-29T18:00:00Z",
  "payload": {}
}
```

Required event types:

- `started`
- `heartbeat`
- `progress`
- `metric`
- `checkpoint`
- `artifact`
- `warning`
- `error`
- `summary`
- `completed`

Every normal run emits exactly one terminal summary and one completed event. The runner still determines authoritative status from process exit, timeout, cancellation, and protocol validity.

Agent command invocation:

```text
Agent.exe <command>
  --run-id <guid>
  --occurrence-id <guid>
  --agent-id <stable agent ID>
  --config-file <temporary redacted-resolved config path>
  --data-directory <agent data path>
  --artifact-directory <run artifact path>
  --protocol-version <major.minor>
```

The runner creates a restrictive temporary execution-input envelope containing the exact immutable configuration revision and occurrence arguments. Resolved non-secret values and, only when required, short-lived secret material remain inside that private file rather than the command line. It deletes the file after execution. Prefer environment variables or process-local secret injection when practical.

The complete manifest, invocation, event payload, exit-code, compatibility, and security contract is defined in [Agent Manifest, Invocation, and JSONL Protocol 1.0](agent-protocol.md).

## 8. Founder Scout architecture

```text
Playwright source / file fixture
        │
        ▼
Capture raw page and metadata
        │ commit snapshot first
        ▼
Normalize and parse versioned fields
        │
        ▼
Resolve candidate identity and deduplicate
        │
        ├── unchanged → mark seen, no analysis
        ▼
Deterministic fast screening
        │
        ├── filtered → persist decision
        ▼
Protected-attribute redaction
        │
        ▼
Structured AI evaluation
        │
        ▼
Validate JSON and evidence
        │
        ▼
C# score calculation and penalties
        │
        ▼
Invitation draft validation
        │
        ▼
Persist evaluation, ranking, reports, and manual queue
```

Founder Scout database: `data/agents/founder-scout/founders.db` beneath the central Runner data root. The Runner-assigned per-agent directory is the execution-time filesystem authority; the editable `dataDirectory` value remains validated configuration metadata and cannot redirect a launched process outside that assigned root.

Stage 09 implements the file-fixture edge of this pipeline, the durable domain schema, strong-identity resolution, snapshot/action persistence, analysis claims, diagnostics, and count-only JSON/Markdown reports. Browser discovery, production parsing/screening, and AI evaluation remain later stages. Raw fixture artifacts are moved atomically into content-addressed storage before a short transaction can point the candidate at the snapshot or queue analysis.

Stage 10 implements browser acquisition through application-owned browser-neutral contracts and a Playwright adapter confined to Founder Scout Infrastructure. Every account uses one root-confined persistent profile at `browser/<account-id>` plus an exclusive cross-process file-handle lease. Authentication is an explicit headed command and accepts no credentials; ordinary discovery is sequential and bounded. The versioned Startup School adapter uses ordered semantic/stable locators, requires HTTPS outside explicit loopback fixtures, persists a raw artifact and snapshot before every checkpoint, and stops without failover on authentication, access, throttle, challenge, untrusted transport/host, or parser-health signals. Bounded diagnostics omit screenshots/page images, active content, metadata, form values, URL/data attributes, session material, and sensitive URL query data; their retention deadline crosses protocol 1.0 into central artifact metadata. See [ADR-0008](adr/ADR-0008-founder-scout-browser-session-and-stop-semantics.md).

Stage 11 implements a no-AI processing boundary. `analyze --phase screen --max <N>` atomically leases captured snapshots, then performs artifact I/O, parsing, canonicalization, redaction, identity derivation, and screening outside database transactions. One short transaction persists the normalized/evaluator hashes, safe evidence and redaction metadata, identity aliases or conflicts, bounded field-name-only change summary, screening decision, candidate state, and append-only actions. Snapshot processing leases are deliberately separate from Stage 12 analysis leases. Normalized/evaluator equality suppresses duplicate screening even when raw layout changes. Repeated parser-health failures fail closed by pausing the source segment and marking the account `ParserFailure`.

Stage 12 implements `analyze --phase deep|all --max <N>` over candidate-scoped expiring claims. Application owns the provider-neutral request/response contract, behavior-complete SHA-256 cache identity, exact scorecard/fit/risk policy, schema/evidence validation, deterministic arithmetic, retry/repair policy, and invitation validation. Infrastructure owns the official OpenAI .NET Responses client for both OpenAI and Azure OpenAI v1 endpoints plus the explicit deterministic fake. Runner resolves opaque secret references from the protected central store into the current-user private execution-input envelope; secrets never enter configuration revisions, Founder Scout rows, protocol events, summaries, or logs. See [ADR-0010](adr/ADR-0010-founder-scout-responses-provider-and-validation-boundary.md).

Stage 13 composes a separate Founder Scout result query/command boundary into the loopback Host. Candidate lists are raw-text-free, server-filtered, stably ordered, and paged; detail reads assemble bounded normalized evidence, safe diffs, evaluation versions, immutable generated drafts and human revisions, explicit queue membership, and append-only history. Primary and reserve queues are user-managed window-scoped planning rows with capacity, eligibility, duplicate, and optimistic window-count rules. No interface or page handler can send an invitation. The report command takes one canonical ordered report model through a cross-process fencing lease and atomically writes encoded HTML/Markdown, formula-safe UTF-8 CSV, versioned JSON, discovery summary, and manual queue artifacts. Raw retention deletes only contained eligible files and marks snapshots while preserving normalized/evaluation/action data. See [ADR-0011](adr/ADR-0011-founder-scout-results-review-and-report-boundary.md).

Stage 14 adds a central operational feedback boundary without copying agent-owned data into `assistant.db`. Deterministic detectors upsert `AttentionItem` by a stable non-secret dedupe key; acknowledgement records awareness while a later detector observation owns resolution. A new occurrence revision can enqueue exactly one `LocalNotification`, which remains pending through quiet hours, throttling, restart, or tray absence and becomes delivered only after an interactive subscriber accepts it. Terminal runs receive a versioned deterministic platform summary envelope; local-day/time-zone daily summaries are immutable regenerable revisions with source hashes. Founder Scout supplies safe current aggregate/account signals through an application port while profiles, evaluations, and drafts remain in `founders.db`. Root-confined retention protects active runs and append-only audit/configuration history. Diagnostics use a six-entry allow list, per-entry SHA-256 manifest, explicit exclusions, and a 10 MiB cap. See [ADR-0012](adr/ADR-0012-durable-local-operational-feedback.md).

Provider calls, prompt loading, and validation occur outside database transactions. Exact quality/fit keys and key-specific bounds are checked deterministically after strict JSON-schema deserialization. Every scoring evidence item, risk, invitation fact, and numeric claim must trace conservatively to the redacted evaluator input. Invalid evidence is retained as `NeedsReview` without applying its scores to candidate ranking. A structurally valid evaluation with an unsafe draft also routes the candidate to manual review while preserving both drafts and validation codes. No application or infrastructure interface sends an invitation.

Identity precedence is stable source key, canonical URL, trusted source ID, then deterministic name/location/stable-background fingerprint. Strong unambiguous signals converge aggregates; weak or ambiguous signals create reviewable conflict records. Merge retains a logical source tombstone so append-only history remains attributable, moves mutable dependents when safe, and never discards colliding snapshots. Manual split remains deferred until a provenance-aware clone-and-reassign workflow can preserve evidence safely. See [ADR-0009](adr/ADR-0009-founder-scout-processing-identity-and-hash-semantics.md).

`assistant.db` owns occurrences, runs, protocol events, metrics, audit, and centrally ingested run artifacts. `founders.db` owns Founder Scout domain state. They have no cross-database foreign keys or distributed transaction: `CandidateAction.RelatedRunId` and correlation IDs link the records as values, and strong identity plus `(CandidateId, SourceProfileKey, ContentHash)` makes import replay safe if central finalization must be retried. See [ADR-0007](adr/ADR-0007-founder-scout-bounded-context-and-import-order.md).

Core data:

- `BrowserAccount`
- `DiscoverySegment`
- `DiscoveryCheckpoint`
- `Candidate`
- `ProfileSnapshot`
- `CandidateIdentityAlias`
- `CandidateIdentityConflict`
- `ScreeningDecision`
- `Evaluation`
- `EvaluationCategory`
- `EvaluationRisk`
- `InvitationDraft`
- `CandidateAction`
- `ManualInvitationWindow`
- `ReportExport`

Candidate state:

```text
Discovered -> Captured -> Parsed -> PendingAnalysis -> Analyzing
                         └-> FilteredOut
Analyzing -> Shortlisted -> QueuedForInvite -> MessageReviewed -> ManuallySent
Analyzing -> Monitor | Passed | ManualReview
Shortlisted | Monitor | Passed | ManualReview -> PendingAnalysis (changed behavior input)
ManuallySent -> Accepted | Declined | NoResponse | CallScheduled
CallScheduled -> PassedAfterCall | TrialProject | Selected
```

Browser account state:

```text
Unknown | Healthy | ReauthenticationRequired | AccessDenied |
Throttled | ChallengeDetected | ParserFailure | Disabled
```

A blocked, throttled, or challenged account stops. The system does not silently move the same run to another account.

An agent safe-stop summary can request an indefinite pause for the occurrence's own central schedule. Runner applies that pause before fixed-delay completion planning, so authentication/enforcement/parser stops create no rapid successor. Profile-lock and bounded transient-navigation failures do not request an indefinite pause and remain governed by the explicit retry policy.

## 9. Founder scoring

Store separate dimensions:

```text
FounderQualityScore 0..100
OurFitScore         0..100
Confidence          0..1
ActivityScore       0..100
RiskPenalty         0..100
InvitationPriority  0..100
```

Baseline category weights:

```text
Founder execution quality           20
Commitment and co-founder posture   15
Traction and validation             15
Market potential                    15
GTM and domain advantage            10
CTO fit                             10
Idea and problem clarity             5
Moat potential                       5
Technical feasibility                5
                                    ---
                                    100
```

The evaluator returns category evidence and bounded scores. C# validates ranges and calculates totals. Unknown facts remain unknown and lower confidence.

`BaseScore` is the sum of the nine 100-point quality categories. `FounderQualityScore` removes the personal `ctoFit` category and normalizes the remaining 90 points: `round((BaseScore - ctoFit) / 90 * 100, 2, AwayFromZero)`. `OurFitScore` is the sum of the separate 100-point fit dimensions. Known evidence-backed risks apply their configured caps; absent evidence never creates a penalty. Overall confidence is `70%` maximum-weighted category/fit confidence, `20%` profile completeness, and `10%` grounded-evidence coverage. Activity uses configured UTC recency buckets, and the current bucket participates in cache identity so a stale ranking is not reused.

Recommended invitation priority formula is configurable. Initial formula:

```text
OurFitScore         * 0.55
+ FounderQuality    * 0.25
+ Confidence*100    * 0.15
+ ActivityScore     * 0.05
- explicit penalties
```

## 10. Invitation draft requirements

Every deep evaluation generates:

- a short draft;
- a detailed draft;
- one or two candidate-specific facts used;
- the complementary strengths from the configured founder persona;
- a reason to connect;
- one relevant topic/question;
- confidence and validation errors.

Drafts are never sent automatically.

Deterministic validation checks:

- candidate-specific fact present;
- complementarity statement present;
- no unsupported claims;
- no protected attributes;
- length within configured maximum;
- not empty or generic;
- no mention of automated scoring;
- similarity below configured threshold compared with recently queued drafts.

## 11. Wake & Remote architecture

The wake task starts `Runner`, which starts `WakeRemote.exe`.

Workflow:

```text
Start
  -> acquire/confirm run
  -> hold system-required power request
  -> wait for network readiness within timeout
  -> check configured remote provider
  -> publish MachineReady/RemoteReady metrics
  -> remain alive until configured availability-window end
  -> emit summary
  -> release power request
  -> exit
```

V1 provider checks:

- Chrome Remote Desktop host service/process configured and running.
- Optional Windows RDP service/listener health check for Windows Pro.

The agent does not open firewall ports, expose RDP publicly, change router settings, force sleep, or manage remote-access credentials.

## 12. Host and UI

`HomeBusinessAssistant.Host` is a `WinExe` Windows target using the web SDK plus Windows Forms support. It owns:

- single-instance mutex;
- tray icon/menu;
- local Kestrel host;
- schedule and wake reconciliation loops;
- stale-run recovery;
- notification delivery;
- opening the default browser to the dashboard.

The primary instance runs an STA Windows Forms message loop and owns all interactive resources. A per-user/session `Local` mutex selects the primary. A secondary instance sends one bounded allow-listed command through a local named pipe and exits. The mutex/pipe suffix combines the user/session identity with a persistent random 192-bit capability stored below the validated data root; this permits cross-process operation across managed Windows token variants without exposing a guessable transport name. The capability is not included in ordinary database backups. Startup order is database migration/default seeding, stale-run recovery, Kestrel, then tray exposure. Shutdown reverses the owned resources and always releases the manual power request, tray icon, pipe, mutex, and log pipeline.

Four independent hosted loops reconcile schedules, reconcile the one managed wake task, recover stale executions, and scan/deliver durable operational attention. Each loop is sequential and failure-isolated. Their default intervals are 30 seconds, 60 seconds, 120 seconds, and 15 seconds respectively. Every schedule pass also invokes wake reconciliation immediately after planning/dispatch, including while globally paused; the independent wake loop remains the bounded recovery poll. The operational loop detects run/schedule/storage/agent conditions, creates the due local-day summary once, and offers at most one policy-eligible notification to the tray per pass. SQLite claims, leases, dedupe keys, and delivery rows remain authoritative when processes restart or the tray is unavailable.

Manual tray actions create durable `TrayMenu` occurrences and launch only the central Runner. Host detaches after process start and observes progress through SQLite. It never runs an agent in-process or calls an agent executable directly. Manual keep-awake is a separate bounded Host-owned system request; it never keeps the display awake and is released instead of forcing sleep.

Liveness (`/health/live`) means the local process and Kestrel are responsive. Readiness (`/health/ready`, with `/health` retained as a compatibility alias) additionally requires database/migration access, writable runtime directories, Runner availability, and at least one successful scheduler pass. A readiness failure keeps the local page available with safe reason codes.

The Stage 08 web surface uses one bounded management query service for cross-table views and existing focused write services behind an orchestration command facade. Queries are server-paged/filterable where history can grow. Stage 16 originally required Windows Negotiate authentication and the startup owner's SID for every management page. Founder Scout simple mode temporarily removes that browser authentication requirement to eliminate local `401`/`403` friction. Exact IPv4-loopback binding, configured Host/unsafe-Origin checks, CSP, antiforgery, Post/Redirect/Get, and the no-CORS boundary remain; remote binding is still rejected. Mutations reconcile schedules plus the managed wake task when relevant. Manual and retry handlers create explicit durable occurrences; no page handler launches an agent executable. See [ADR-0018](adr/ADR-0018-founder-scout-code-owned-simple-mode.md).

Both built-in configuration editors produce typed schema 1.0 JSON and carry the displayed current revision number/hash. Other installed agents may use the constrained generic editor for primitive, enum, simple-array, and opaque secret-reference fields; unsupported nested or conditional schemas require a typed code adapter. Schema text is encoded and never supplies HTML or scripts. The transactional save rejects stale edits and reports idempotent no-op saves. Secret entry is a separate DPAPI-backed action: HTML exposes only existence status and the opaque reference, never a retrieved value. Run/audit/protocol text remains encoded, and artifact downloads require persisted run ownership plus root-confined storage validation.

Primary UI sections:

- Dashboard
- Agents
- Agent configuration
- Schedules
- Runs and run detail
- Audit
- Wake & Remote
- Founder Scout candidates
- Founder Scout candidate detail
- Invitation queue
- Reports/exports
- Settings/system health
- First-run onboarding/readiness

State-changing handlers use antiforgery and server-side validation.

Stage 18 routes only a fresh owner's dashboard entry to `/Onboarding`. Established upgrades receive a non-blocking review invitation; deferred sessions receive a resume reminder. Readiness GETs perform no checks. Antiforgery-protected POSTs run bounded checks or the separately labeled protected-storage probe. Warnings require acknowledgement, blockers prevent readiness completion, and the Stage 18 terminal view is an honest `agent-selection-pending` handoff rather than a completed agent setup.

Stage 19 routes the handoff to `/Onboarding/Agents`. Installed agents are unchecked by default; the owner may select any safe available subset or defer all. Built-in agents use explicit specialized placeholders until Stages 20/21, safe generic schemas use `/Onboarding/Agent`, and unsupported complex schemas fail closed. Dashboard, Agents, agent detail, and Settings expose non-blocking setup/re-entry links. Generic configuration saves use the normal immutable revision service; protected values use separate non-echoing POSTs. Newly scanned agents add a banner but never force navigation, and no Stage 19 handler creates a schedule, enables an agent, or launches a diagnostic.

Stage 20 routes selected Founder Scout setup to `/Onboarding/FounderScout`: purpose/privacy and live consent, disabled-first browser account, allow-listed source limits, screening/fit/persona/retention, optional Responses provider and separate protected key, explicit browser/provider checks and Runner-backed headed authentication, then final review. Agent-scoped checks contain safe status metadata only. The selection becomes `ReadyForValidation` only when the chosen live/local and capture/deep prerequisites pass; activation remains a Stage 22 action.

The subsequent Founder Scout simple-mode increment bypasses that specialized route. At Host startup it promotes the code-owned operational document through normal immutable configuration persistence, enables Founder Scout for explicit manual runs, and reconciles one canonical account and segment while preserving browser health and counters. The primary header is temporarily limited to Dashboard and Founder Scout; Founder Scout pages use a separate Overview, Candidates, and Settings tab set. `/FounderScout` creates one explicit `start` occurrence with the canonical account/segment and a validated 1–60 second per-profile delay. Active pages show durable active/terminal state and poll every three seconds only during an active run. The agent reuses a healthy dedicated session; if authentication is missing or proves stale, it opens the official site for manual authentication and retries discovery once. The versioned source adapter enters through the official next-candidate route, accepts only validated stable candidate final routes (including the current nested form), rejects navigation tabs, and uses the final URL as identity when profile markup has no self-link. Discovery commits each profile before best-effort deterministic screening, and `start` never invokes deep AI analysis, so later processing failures do not hide captured candidates. Passwords never cross the browser boundary.

`/FounderScout/Settings` is the narrow exception to fully code-owned configuration: it preserves a bounded OpenAI model, analysis batch size, and optional local founder context in immutable revisions while startup continues to replace the safety-critical browser, discovery, screening, privacy, and invitation policy. The provider is fixed to the standard OpenAI Responses API; the official SDK remains the adapter, and Azure setup is not exposed in simple mode. The API key is a write-only mutation through `ISecretStore`, protected with current-user DPAPI outside SQLite, and Razor receives existence only. ChatGPT Memory and conversations are not imported; owner-pasted text becomes explicit versioned persona context. On `/FounderScout/Candidates`, **Analyze candidates now** is an antiforgery-protected, cost-confirmed action that queues bounded screened candidates without an existing evaluation, then creates a durable manual `analyze --phase deep` occurrence. Each row also has a cost-confirmed **Analyze** action that runs `analyze-candidate`: it refreshes the selected profile through the saved dedicated browser account, persists and screens its exact snapshot, and evaluates only that candidate. The Candidates page shows current phase, progress, and separate latest Scout and AI outcomes. Schedules, Host-start execution, automated provider calls, and invitation sending remain disabled. The prior wizard and legacy agent-configuration route redirect to the focused settings page. See [ADR-0019](adr/ADR-0019-founder-scout-focused-openai-settings.md).

## 13. File-system layout

```text
data/
├── assistant.db
├── secrets/
│   └── <sha256-reference>.secret
├── agents/
│   └── founder-scout/
│       ├── founders.db
│       ├── browser/
│       │   └── <account-id>/
│       ├── snapshots/
│       ├── errors/
│       └── reports/
├── artifacts/
│   └── <agent-id>/<run-id>/
├── logs/
├── temp/
└── backups/
```

All paths resolve from a single validated application data root. Reject traversal outside allowed roots.

## 14. Security and privacy

- Loopback-only web host.
- Antiforgery for mutations.
- Windows-protected secrets.
- Secret files use DPAPI `CurrentUser`, stable application-purpose entropy, and hashed opaque-reference file names. This protects at rest from casual file inspection, not from code already executing as the same Windows user.
- No browser credentials in app settings.
- No cookies/session data in logs or audit.
- No profile images stored.
- Configurable raw-profile retention.
- Hash artifacts and executables.
- Validate all manifests, command names, arguments, config revisions, and artifact paths.
- Never construct a shell command string from untrusted input; use `ProcessStartInfo.ArgumentList`.
- No automatic invitation sending.
- No CAPTCHA solving, stealth browser behavior, proxy rotation, or enforcement bypass behavior.

## 15. Operational defaults

```text
Host loopback URL:             http://127.0.0.1:5180
Schedule reconciliation:      30 seconds
Wake-task reconciliation:     60 seconds
Stale-run recovery:           120 seconds
Failure notification polling: 15 seconds
Founder discovery batch:       20 new profiles
Founder discovery cooldown:    10 minutes after completion
Founder analysis batch:        20 pending profiles
Founder analysis concurrency:  2
Browser concurrency:           1
Raw profile retention:         30 days
Error artifact retention:      14 days
Log retention:                 30 days
Daily backups:                 7
Weekly backups:                4
```

These are application defaults, not guarantees about external-service access limits. All are editable and audited.

## 16. Packaging, installation, and recovery

The release is a normal-folder, self-contained `win-x64` bundle. Host, Runner, Founder Scout, and Wake Remote remain separate executables; additional agents are selected only through `packaging/agents.json`. The Sample Business Agent is excluded unless explicitly requested. Single-file, trimming, and ReadyToRun are disabled so Razor content, EF migration assemblies, Playwright support, prompts, and per-agent assets retain predictable paths. One sorted SHA-256 manifest covers every distributable file, while `build-info.json` supplies one product version, optional Git revision, build UTC, runtime target, and framework identity across processes.

The per-user installer validates the entire source manifest, stages and swaps immutable application files, preserves `config` and `data`, migrates both databases, seeds built-in manifests idempotently, safely scans immediate package children, optionally installs the published Playwright Chromium revision, and can start/health-check Host. New discovered agents are disabled; valid updates preserve user state; invalid or removed packages disable definitions while retaining history. Upgrades refuse downgrade, require quiescent owned work unless explicitly forced, and create a verified database backup before migration. Repair revalidates hashes, package registration, paths, migrations, tasks, Playwright, wake reconciliation, and health. Uninstall removes owned tasks and application files while preserving databases, DPAPI secrets, browser profiles, and other data unless a separately confirmed safe-root removal is requested.

`\HomeBusinessAssistant\HostAtLogon` is a current-user `InteractiveToken`, least-privilege logon task with optional delay, `IgnoreNew`, no stored password, and `WakeToRun=false`. `\HomeBusinessAssistant\NextWake` remains the separate exact-occurrence wake bridge into Runner. Both use managed XML fingerprints and idempotent reconciliation. Permission/service/path failures are bounded operator-visible results; the software never elevates or bypasses Task Scheduler policy.

Backup creation holds the central `operations.database-backup` lease and uses SQLite online backup connections to create independent `assistant.db` and `founders.db` images beneath one set ID. A `.pending-*` set becomes visible only after `quick_check`, expected-table/migration checks, size, and SHA-256 validation pass for both files. Retention keeps configurable daily/weekly points and excludes secrets, browser/session state, logs, SQLite sidecars, and diagnostic/raw artifacts.

Restore is CLI-only maintenance in V1. It requires an exact confirmation token, no active Host/agent/run use, a valid compatible manifest, and creates a pre-restore backup. Both files are staged and validated before replacement; rollback copies survive until migrations, stale-run recovery, and wake reconciliation finish. The restored backup lease is explicitly expired before returning so a new backup can run immediately.

Daily summary, retention, diagnostics, and backup operations are implemented through Runner/UI, but optional durable platform-maintenance schedules are not auto-seeded. The scheduler currently targets versioned independently executable agent commands, and the published contract contains only Founder Scout and Wake Remote. A versioned platform-maintenance command/agent boundary is required before automatic schedule seeding can preserve the scheduler's executable-contract guarantees.
