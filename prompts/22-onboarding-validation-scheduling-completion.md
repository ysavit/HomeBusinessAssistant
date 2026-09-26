# Stage 22 — Guided Diagnostics, Scheduling, Activation, and Onboarding Completion

Paste this entire prompt into Codex from the repository root after Stage 21 passes.

---

You are implementing **Stage 22: Runner-Backed Guided Diagnostics, Safe Schedule Creation, Explicit Agent Activation, Completion, and Onboarding End-to-End Hardening**.

Read all repository guidance, `docs/PROJECT_STATE.md`, Stages 18–21 onboarding implementation, scheduler/Runner/management/audit/attention services, built-in and generic adapters, and this prompt. Create/update `docs/exec-plans/stage-22-onboarding-validation-scheduling-completion.md`.

## Repository-state handoff

Verify all prior onboarding stages directly, including migrations, adapter status, browser/provider checks, and known failures. Before final reporting, update `docs/PROJECT_STATE.md` with the completed onboarding capability, actual validation, limitations, and exact next prompt status. There is no checked-in prompt after Stage 22 unless the repository adds one.

## Goal

Complete onboarding by guiding each selected agent through an explicit Runner-backed diagnostic, optional safe schedule creation, final review, and explicit activation. Make the entire workflow resumable, idempotent, auditable, and re-runnable from Agent Settings.

The complete user flow is:

```text
Platform readiness
  -> select agents
  -> configure each selected agent
  -> run explicit diagnostic per agent
  -> optionally create schedule(s)
  -> final safety review
  -> explicitly enable selected agents/schedules
  -> dashboard completion summary
```

## 1. Diagnostic plan contract

Finalize the adapter diagnostic contract with a bounded `AgentOnboardingDiagnosticPlan` or equivalent containing:

- agent ID and supported manifest command;
- code-owned safe occurrence arguments;
- prerequisite keys and required statuses;
- timeout and interactive-session requirement;
- whether the check makes a network/provider request;
- expected safe completion evidence/metrics;
- success, warning, and attention reason mappings;
- whether success may expire and require recheck.

Plans are produced by explicitly registered adapters or a constrained generic policy. Never accept an executable path, arbitrary command, script, raw arguments, timeout, environment variables, or secret values from the browser or package schema.

Generic agents may use `diagnose` only when the validated manifest declares it. Unsupported agents remain configurable but cannot complete activation through the generic wizard until a code-owned diagnostic policy exists.

## 2. Runner-backed guided diagnostic

For each selected `ReadyForValidation` agent:

1. Show what the diagnostic will do, whether it opens a browser or contacts a provider, expected duration, and any cost/privacy implications.
2. Require explicit user action.
3. Create a durable manual occurrence through existing orchestration services, recording the exact configuration revision/hash and onboarding session/selection correlation.
4. Dispatch only through the central Runner.
5. Poll persisted occurrence/run/events/summary state with bounded intervals and progressive enhancement; no direct in-memory progress dependency.
6. Provide cancel only through the existing durable cancellation path.
7. On completion, translate persisted status/metrics into a safe adapter result.
8. Persist only occurrence/run IDs, terminal safe reason, observed UTC, and verified summary/metric references in onboarding state.

Do not consider client disconnection a cancellation. Restarting Host or the browser must resume display from persisted state.

### Diagnostic outcomes

- `Passed`: expected command, terminal status, protocol lifecycle, and adapter evidence are valid.
- `PassedWithWarnings`: diagnostic completed but optional provider/wake/browser capability is unavailable.
- `NeedsAttention`: configuration/secret/authentication/provider failure requires correction.
- `Failed`: Runner/process/protocol/timeout failure.
- `Cancelled`: user/system cancellation completed.
- `Stale`: the configuration revision or installed executable/manifest changed after the diagnostic.

A stale or failed result cannot activate a newly selected agent. Allow retry after remediation. Allow explicit `Skip for now`, which defers that agent and keeps it disabled.

## 3. Schedule templates and wizard

Finalize the adapter schedule-template contract. Templates are code-owned suggestions, not hidden schedules. Each declares:

- stable template ID/version;
- manifest-supported command;
- safe default arguments;
- supported schedule kinds;
- recommended time zone/local time or interval;
- timeout, concurrency, retry, misfire, wake, keep-awake/display defaults;
- prerequisite/warning keys;
- concise explanation.

Required built-in suggestions:

### Founder Scout

- Manual only;
- bounded discovery with fixed delay after completion and conservative initial limits;
- optional separate screen/deep-analysis cadence only when dependencies are configured;
- interactive-session requirement for browser commands;
- no automatic invitation/message action exists.

### Wake Remote

- Manual diagnostic only;
- one-time test availability window;
- selected weekdays/daily availability window;
- wake optional/required only when Stage 21 capability allows it;
- keep display off by default.

### Generic agents

- Manual only;
- one-time/daily/selected-weekdays/fixed-interval/fixed-delay choices constrained by manifest and existing scheduler policy;
- no invented command arguments.

The page must show a production schedule preview including time zone, DST behavior, next occurrences, timeout, concurrency, misfire, retry, wake, keep-awake, and display policy. The user can choose `No schedule — run manually`.

Create schedules initially disabled and tagged/correlated to the onboarding selection through an application-owned reference or safe audit correlation. Reuse the production schedule validator/calculator. Do not duplicate schedule arithmetic in Razor or JavaScript.

## 4. Two-phase final review and activation

Before activation display one bounded summary per selected agent:

- installed agent/manifest version;
- current configuration revision number/hash;
- secret references shown only as configured/missing;
- prerequisite and diagnostic status/time;
- proposed disabled schedules and next-occurrence preview;
- interactive-session, browser, provider, network, task, power, wake, display, privacy, and cost warnings;
- capabilities intentionally unavailable/deferred;
- whether the agent was already enabled before reconfiguration.

Require a final explicit confirmation. The confirmation token is an opaque server-issued nonce bound to session revision and expires quickly; do not encode configuration/secrets in it.

### Activation ordering

Implement an idempotent Application use case with short transactions and compensating semantics:

1. Revalidate package integrity, current configuration revision/hash, required secret existence, adapter prerequisites, diagnostic freshness, and proposed schedule definitions.
2. Persist/finalize any schedule drafts as disabled.
3. Enable the agent only after validation succeeds.
4. Enable only schedules explicitly selected by the user.
5. Mark selection completed with final references.
6. Reconcile occurrences and the single next-wake task after the database transaction.
7. Mark session `Completed`, `CompletedWithWarnings` if the model supports it, or retain completion with a warning count according to Stage 18 schema.

If post-transaction wake reconciliation fails, do not delete the valid schedule or pretend completion failed atomically. Create/update attention, show the schedule as enabled with wake reconciliation warning, and provide repair/recheck. If schedule activation fails before agent enablement, leave the newly selected agent disabled. Never roll back or disable an already working agent merely because a reconfiguration session was deferred/failed.

Do not automatically run a non-diagnostic business command after activation.

## 5. Completion, dashboard, and later changes

Completion page:

- names configured/activated/deferred agents;
- shows diagnostic run links and schedule links;
- lists warnings/attention with remediation routes;
- links to Agents, Schedules, Runs, Audit, Settings, Founder Scout, and Wake Remote;
- explains that all settings remain editable later;
- includes `Add another agent` and `Return to dashboard`.

After completion:

- dashboard no longer forces first-run onboarding;
- deferred or newly installed agents appear in a non-blocking banner;
- Agent Settings offers `Run setup again`;
- ordinary direct configuration edits remain supported and create normal immutable revisions/audit;
- when a direct edit invalidates a previously passed check, derive `Needs review` rather than silently rerunning or disabling unless existing runtime safety already requires fail-closed behavior;
- removing an agent disables it through Stage 17 behavior and retains onboarding/run/schedule history.

## 6. First-run help and recovery UX

Add clear recovery paths for:

- Runner missing or incompatible;
- configuration stale/invalid;
- required secret missing;
- browser runtime missing;
- authentication/challenge/access failure;
- AI invalid key/model/quota/network failure;
- remote provider not installed/ready;
- Task Scheduler unavailable/unmanaged occupant;
- unsupported/unknown wake timers;
- diagnostic timeout/cancel/protocol failure;
- package changed/removed during onboarding;
- schedule preview/activation conflict;
- Host restart or browser closed mid-run.

Every state must say what remains safe and usable. Task Scheduler/wake failure must not imply manual execution is broken. Provider readiness must not be overstated. Never expose raw exception, command output, secrets, browser state, profile content, or full sensitive paths.

## 7. Audit, metrics, and attention

Audit at least:

- diagnostic requested/dispatched/completed/cancelled/retried/stale;
- schedule proposal saved/changed/deferred;
- final review confirmed/rejected/stale;
- agent enabled and schedule enabled through onboarding;
- selection/session completed/deferred/attention;
- setup re-run opened/completed.

Correlate onboarding session, agent, occurrence, run, configuration revision, and schedule IDs. Add low-cardinality operational metrics for onboarding completion/defer/failure duration and reason codes without user-entered content.

Use existing attention categories where possible; add stable onboarding-specific categories only when materially distinct. Deduplicate repeated checks.

## 8. End-to-end tests

Add comprehensive fresh-install, upgrade, restart, and concurrency coverage.

### Fresh-install E2E

With temporary migrated databases and packaged fake/local agents:

1. Host enters onboarding.
2. Platform readiness passes with fake Windows boundaries and warnings.
3. Select Sample Business Agent plus one built-in; defer another.
4. Save generic/typed configuration and protected test secret.
5. Run a real Runner/real child diagnostic.
6. Create a disabled schedule and preview occurrences.
7. Confirm activation.
8. Verify agent/schedule enabled exactly once, no duplicate occurrence, hashes/references/audit persisted, session complete, and dashboard no longer redirects.

### Failure/recovery E2E

- Host restart while diagnostic runs;
- diagnostic failure then remediation/retry;
- stale configuration between diagnostic and final confirmation;
- package executable/manifest changed after diagnostic;
- agent removed mid-session;
- concurrent final submissions;
- task/wake reconciliation failure after schedule enable;
- defer all agents;
- reconfigure an already enabled agent without disrupting active schedules until explicit save/activation.

### Security/UI E2E

- no secret echo in HTML, redirects, logs, audit, diagnostics, or onboarding rows;
- hostile agent/schema/check text encoded;
- antiforgery and current-owner authorization;
- external binding remains rejected;
- destructive/power/wake actions remain confirmed;
- desktop and narrow layouts, keyboard/focus, browser back/refresh, loading/empty/error/retry states;
- zero console/page errors.

Normal tests use fake providers/local fixtures. Do not access Startup School, OpenAI/Azure, real remote-access services, or alter Task Scheduler/power state.

## 9. Documentation and operations

Update:

- `README.md` first-use section;
- `docs/installation.md` first launch and `-SkipStartupTask` implications;
- `docs/founder-scout-setup.md` onboarding path;
- `docs/wake-remote-setup.md` onboarding path;
- `docs/operations.md` resume/re-run/diagnostic recovery;
- `docs/troubleshooting.md` onboarding reason codes;
- `docs/security-privacy.md` onboarding/secret/provider-check data boundaries;
- architecture and a new ADR covering durable onboarding, explicit activation, and adapter boundaries;
- acceptance matrix/evidence and release notes as appropriate.

Document how a newly scaffolded agent participates:

- safe generic schema requires no specialized settings page;
- complex configuration needs an explicit onboarding adapter;
- diagnostic and schedule templates are code-owned and reviewed;
- installation never auto-selects, auto-enables, or auto-runs it.

## Constraints

- No business-command auto-run after setup.
- No automatic invitation/message sending.
- No task registration/power/provider mutation without an already documented explicit confirmation path.
- No in-process plugin loading, arbitrary commands/scripts, remote web binding, SPA, or distributed bus.
- Do not make onboarding a prerequisite for Runner recovery or installed-data repair.
- Do not delete configuration/schedule/run/audit history on reset/reconfigure.

## Done when

- A fresh owner can select available agents and receive the correct specialized or generic wizard.
- Browser/provider/API-key/task/power prerequisites are checked safely.
- Each selected agent can run a durable guided diagnostic and optionally receive a validated schedule.
- Newly selected agents and schedules activate only after explicit final confirmation.
- The workflow survives restart/concurrency/failure and can be rerun later from Agent Settings.
- Pending new agents receive a non-blocking setup invitation.
- All validation and documentation pass.

## Required validation

Run from the repository root:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Also run:

1. both database migration/drift checks;
2. full synthetic fresh-install onboarding E2E with real Runner/Sample child;
3. upgrade/backfill and reconfiguration E2E;
4. failure/restart/concurrent-finalization E2E;
5. default and sample-enabled package/install/smoke, proving first launch behavior;
6. rendered desktop/narrow browser QA for the complete flow;
7. an interactive manual smoke for tray launch, onboarding resume, and safe warnings;
8. optional live provider/browser/hardware checks only when explicitly authorized and prerequisites exist.

## Final report

Include the full flow, state transitions, diagnostic/schedule/activation ordering, adapter behavior per agent, pending/re-entry rules, migrations/configuration/protocol changes, files/projects, actual commands/results, manual steps, privacy/security evidence, limitations, `docs/PROJECT_STATE.md` confirmation, and exact next prompt status (`none` unless a later prompt has been added).
