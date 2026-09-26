# Stage 18 — First-Run Onboarding Foundation and Platform Readiness

Paste this entire prompt into Codex from the repository root after Stage 17 passes.

---

You are implementing **Stage 18: Durable First-Run Onboarding Foundation, Entry Policy, and Platform Readiness Wizard**.

Read all repository guidance, `docs/PROJECT_STATE.md`, `.agent/PLANS.md`, the Host management UI, persistence/configuration/audit boundaries, Windows task/power adapters, Stage 17 agent registry, and this prompt. Create/update `docs/exec-plans/stage-18-first-run-onboarding-foundation-readiness.md`.

## Repository-state handoff

Before planning or coding:

1. Read `docs/PROJECT_STATE.md` completely.
2. Inspect the repository and verify Stage 17's claims directly.
3. Read the relevant ADRs for persistence, management UI, Windows lifecycle, packaging, and agent extension.
4. Reconcile any mismatch in the ExecPlan before implementation.

Before the final report:

1. Update `docs/PROJECT_STATE.md` with only verified Stage 18 state.
2. Record the migration name, schema/configuration versions, implemented routes, actual validation, manual checks, and known limitations.
3. Preserve failed validation evidence until a later verified pass supersedes it.
4. Name `prompts/19-agent-selection-generic-onboarding.md` as the exact next prompt.
5. Never include secrets, browser authentication state, remote PINs, cookies, or real founder data.

## Goal

Add a resumable, audited onboarding lifecycle and an accessible first-run web wizard that explains and checks platform prerequisites before any agent is configured, enabled, scheduled, or executed.

The user-visible flow begins as:

```text
Welcome
  -> platform readiness
  -> review warnings/blockers
  -> continue to agent selection (Stage 19)
```

This stage does not configure agents. It establishes the durable state, entry policy, platform checks, and safety language that later agent-specific wizards use.

## Cross-stage onboarding invariants

These rules apply to Stages 18–22:

- Onboarding is a platform orchestration workflow, not an agent process and not a replacement for immutable agent configuration revisions.
- Newly discovered agents remain disabled. No wizard step may silently enable an agent, create an enabled schedule, or launch a process.
- Agent commands always execute through a durable occurrence and the central Runner.
- Secrets are stored only through `ISecretStore`; secret values are never persisted in onboarding rows, configuration JSON, audit, logs, summaries, or rendered HTML.
- Readiness checks are bounded, cancellable, redacted, and read-only unless the user explicitly confirms a narrowly described repair action.
- Task Scheduler and power checks report capability; they do not register a task, alter a power plan, sleep the machine, enable RDP, change a firewall, or install remote-access software.
- All state-changing Razor handlers use antiforgery, current-owner authorization, optimistic concurrency/idempotency, and append-only audit.
- The wizard can be deferred and resumed. Existing configuration, schedules, runs, and history are never destroyed by restarting onboarding.
- The ordinary Agent Settings pages remain authoritative and usable after onboarding.

## 1. Durable onboarding model

Add Application/domain contracts and a central EF Core migration for a small, generic onboarding workflow. Use names consistent with the repository, but preserve these semantics.

### OnboardingSession

Persist:

- session ID;
- schema version;
- kind: `FirstRun`, `AddAgents`, or `ReconfigureAgent`;
- status: `InProgress`, `Deferred`, `Completed`, or `Cancelled`;
- current step key;
- started/updated/completed/deferred UTC timestamps;
- actor identifier and correlation ID;
- bounded revision/concurrency token;
- optional safe completion/warning counts.

There may be historical sessions, but at most one active `InProgress` session may own first-run/add-agent progress. Enforce this transactionally. A per-agent reconfiguration request must join or safely supersede an applicable active session rather than creating conflicting work.

### OnboardingCheck

Persist the latest safe result per `(SessionId, Scope, CheckKey, AgentId optional)`:

- status: `Passed`, `Warning`, `Blocked`, `Unknown`, or `NotApplicable`;
- stable reason code;
- bounded user-safe title/message;
- observed and optional expiry UTC;
- safe details JSON with an explicit schema version;
- remediation route/action identifier from a platform allow list, never arbitrary HTML, shell text, or URL.

Use audit for history. Do not accumulate unlimited probe output in onboarding tables.

### State-machine rules

- `Completed` and `Cancelled` are terminal.
- A deferred session can resume explicitly and becomes `InProgress` with a new revision.
- Step transitions are allow-listed and reject stale revision submissions.
- A `Blocked` check prevents progression only when the next step cannot operate safely. Warnings require acknowledgment but do not become false blockers.
- Rechecks replace the latest result and append an audit event.
- Defer/cancel never changes agent enabled state or schedule state.

Add focused repositories/services, not a generic repository. Use `TimeProvider`, UTC timestamps, short transactions, and cancellation tokens.

## 2. First-run and re-entry policy

Implement an Application-owned `IOnboardingEntryPolicy` or equivalent that returns a safe decision and reason codes. Do not put persistence queries directly in Razor filters.

Required behavior:

- A genuinely fresh installation with no completed onboarding session starts a `FirstRun` session and routes the owner's dashboard entry to `/Onboarding`.
- Health endpoints, static files, authentication infrastructure, shutdown/activation endpoints, and the onboarding routes themselves must never be redirected.
- Do not create redirect loops or prevent access to About, troubleshooting, repair guidance, or diagnostics when readiness is blocked.
- An upgraded Stage 17 installation must not be mistaken for a fresh install merely because onboarding tables are new.
- Define and test a conservative backfill rule. Existing agents with user-authored configuration, schedules, or completed runs should be treated as pre-existing/managed and should receive a non-blocking onboarding invitation rather than a forced redirect. Scanner-seeded defaults alone do not prove setup was completed.
- Once first-run onboarding is completed, ordinary launches go to the dashboard.
- Deferred first-run state produces a visible dashboard reminder with Resume and Dismiss-for-session actions; it must not redirect every request.
- Concurrent tabs must converge on the same active session.
- If no valid installed agent exists, the readiness page remains usable and shows a bounded `onboarding.no-agents` blocker plus an explicit rescan/repair path. It does not invent an agent.

Stage 19 will add newly discovered-agent pending banners and selection. Provide extension points without implementing selection prematurely.

Implement and test this entry decision table:

| Installation state | Available/pending agents | Entry behavior |
|---|---|---|
| Fresh | One or more valid agents | Start first-run onboarding automatically at platform readiness |
| Fresh | No valid agents | Start onboarding, show `No valid agents available`, and offer rescan/repair/defer |
| Established/completed | No pending agents | Open the normal dashboard; do not show onboarding |
| Established/completed | One or more pending/new agents | Keep the normal dashboard and expose a non-blocking setup invitation in Stage 19 |
| Any | User chooses Add agents / Run setup again | Start or resume an explicit `AddAgents`/`ReconfigureAgent` session |

Do not infer that an agent is pending merely because it is disabled; intentional disablement after setup is a normal operating state.

## 3. Platform readiness checks

Implement a composable Application contract such as `IOnboardingReadinessCheck` with stable keys, declared scope, timeout, and safe result. Infrastructure/Windows owns the adapters. The orchestrator runs checks with bounded concurrency and a total timeout; one failed check does not suppress unrelated results.

Required platform checks:

1. **Host identity and binding**
   - current interactive Windows user is available;
   - Host remains exact `127.0.0.1` and owner-authorized;
   - failures are reported without SID leakage beyond what the existing owner UI safely exposes.
2. **Central persistence**
   - database opens;
   - expected migration is current;
   - SQLite foreign keys/WAL/busy timeout are healthy;
   - data directory is contained and writable.
3. **Runner and package integrity**
   - Runner exists and reports a compatible version;
   - configured AgentDirectory/ManifestDirectory are valid;
   - latest package scan summary is safe to display;
   - invalid/missing packages are blockers for those agents, not necessarily for the whole platform.
4. **Secret-store capability**
   - verify DPAPI/current-user availability through a narrowly scoped explicit probe that writes only a random temporary test value, reads it, and deletes it in `finally`;
   - do not run this mutation automatically on every page GET;
   - report cleanup failure as attention and never print the value.
5. **Task Scheduler status**
   - reuse the existing managed-task status/ownership boundary;
   - distinguish service unavailable, folder unavailable, policy/permission denial, unmanaged occupant, managed task absent, and healthy;
   - do not register, replace, or remove a task in this stage.
6. **Power/wake capability**
   - reuse bounded power diagnostics;
   - report supported sleep states, wake timer visibility/permission, battery/policy caveats, and current-user interactive-session requirement;
   - inability to wake is a warning for ordinary/manual agents and a blocker only for a future selected workflow that explicitly requires wake.
7. **Storage and operational safety**
   - bounded available disk check;
   - logs/artifact/backup roots are contained;
   - show whether a verified backup exists, but do not require one on a fresh empty install.

Check output must use stable reason codes and actionable, plain-language remediation. Never render raw command output or exceptions.

## 4. Readiness wizard UI

Add server-rendered Razor Pages under `/Onboarding` with small progressive-enhancement JavaScript only.

Required pages/states:

- welcome and concise explanation of local-only operation;
- progress stepper with current/complete/future semantics;
- readiness cards grouped as Required, Optional, and Agent-dependent;
- visible Passed/Warning/Blocked/Unknown labels using text and icons, not color alone;
- `Run checks`, `Recheck`, `Continue`, and `Set up later` actions; in Stage 18, Continue records readiness completion and renders a safe agent-selection-pending state that Stage 19 replaces, rather than linking to a missing route;
- warning acknowledgment for Task Scheduler/power limitations;
- safe links to Settings, About, repair documentation, and diagnostics;
- empty/loading/error states;
- keyboard navigation, visible focus, semantic headings, and narrow-window layout.

Do not offer `Fix automatically` for Task Scheduler, power policy, browser installation, RDP, firewall, or provider configuration in this stage. A remediation instruction may show a safe copyable command already documented by the repository, but never derive shell text from external/check output.

## 5. Audit, attention, and privacy

Audit at least:

- onboarding session started/resumed/deferred/completed/cancelled;
- readiness batch requested/completed;
- warning acknowledged;
- temporary secret-store probe succeeded/failed/cleanup-failed.

Use correlation IDs across the session and checks. Create/update operational attention only for durable actionable failures such as migration failure, Runner missing, invalid package, secret-store cleanup failure, or managed-task ownership conflict. Do not create an attention storm for repeated page refreshes.

Onboarding data must not contain:

- secret values or API keys;
- browser profile paths beyond already approved safe relative identifiers;
- cookie/session/authentication payloads;
- full power/task command output;
- raw exception stacks;
- real founder profiles or persona text.

## 6. Tests

Cover at minimum:

- migration from empty and populated Stage 17 databases;
- upgrade backfill distinguishes established users from fresh installs;
- exactly one active session under concurrent starts;
- allowed/forbidden state transitions and stale revision rejection;
- fresh dashboard entry redirects once to onboarding;
- health/static/about/onboarding routes never loop;
- deferred/completed behavior;
- no-agent blocker;
- readiness timeout/failure isolation and deterministic ordering;
- Task Scheduler/power warnings are non-mutating;
- secret-store probe deletes its value on success, failure, and cancellation;
- safe encoding/redaction of hostile check messages;
- antiforgery and owner authorization;
- desktop and narrow rendered layout.

Use fake Windows/task/power boundaries in normal tests. Do not register tasks, change power settings, install browsers, access live services, or use real API keys.

## Constraints

- Do not configure or enable agents in Stage 18.
- Do not create schedules or occurrences.
- Do not launch Runner diagnostics from the wizard yet.
- Do not introduce a SPA, SignalR, distributed messaging, or external binding.
- Do not add MCP as part of onboarding.
- Do not weaken existing owner authentication, path confinement, secret isolation, or append-only audit.

## Done when

- A fresh install enters a durable, resumable onboarding session.
- An existing installation is not incorrectly forced through first-run onboarding.
- Platform readiness results are useful, safe, non-mutating, and persisted/audited appropriately.
- Task Scheduler and power limitations are clearly explained without blocking unrelated manual use.
- The wizard can safely record readiness completion, defer, or resume; it does not depend on a Stage 19 route that has not been implemented yet.
- All required validation passes.

## Required validation

Run the repository-required restore/build/test/format commands plus:

1. fresh-database Host smoke showing automatic onboarding entry;
2. Stage 17 database upgrade smoke showing non-destructive backfill;
3. concurrent-session test;
4. fake Task Scheduler unavailable and unsupported-wake UI smoke;
5. secret-store probe cleanup test;
6. desktop and narrow browser QA with zero console/page errors and no sensitive content.

## Final report

Include the persisted state machine, fresh-versus-existing decision, readiness check table, task/power warning semantics, migration/backfill result, routes, actual test/browser results, known limitations, confirmation that `docs/PROJECT_STATE.md` was updated, and exact next prompt `prompts/19-agent-selection-generic-onboarding.md`.
