# Stage 08 — Agent Management Web UI and Orchestration Controls

Paste this entire prompt into Codex from the repository root after Stage 07 passes.

---

You are implementing **Stage 08: Local Management UI for Agents, Configuration, Schedules, Runs, Audit, Wake State, and Manual Execution**.

Read repository instructions, architecture, all existing use cases, and this prompt. Create/update `docs/exec-plans/stage-08-agent-management-ui.md`.

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

Build the usable local control-plane UI inside `HomeBusinessAssistant.Host`: dashboard, agent list/detail, typed configuration revisions, schedule CRUD, manual runs, pause controls, run history/detail, audit viewer, Wake & Remote settings/status, system health, and configuration history. Keep it server-rendered, local-only, accessible, and backed by real application services.

Founder Scout candidate-domain screens remain Stage 13.

## 1. Dashboard

Show real persisted/current values:

```text
Platform health
Host version and uptime
Current local/UTC time and configured time zone
Agents enabled/paused/attention required
Active and recently completed runs
Failures in last 24 hours
Next scheduled occurrences
Next registered wake task
Manual keep-awake status
Latest deterministic daily/run summary
```

Actions:

- Run agent/command now;
- Pause all / resume all;
- Open Wake & Remote status;
- View failed runs;
- Reconcile schedules/wake task;
- Run diagnostics.

Use confirmation for pause-all and power-related actions. Do not use fake KPIs.

## 2. Agent list/detail

Agent list columns:

- display name and stable ID;
- installed/manifest version;
- enabled/paused/status;
- next occurrence;
- active run;
- last result/time;
- attention reason.

Agent detail:

- manifest/capabilities/supported commands;
- executable health/hash/path displayed safely and relatively;
- current configuration revision and history;
- schedules;
- latest runs/metrics/artifacts;
- actions: enable/disable, pause/resume, run command, edit config, view audit.

Changing enable state is audited. Disabling an agent prevents new schedule execution but does not silently kill an active run; offer explicit cancellation separately.

## 3. Typed configuration editor

Implement configuration adapters/validators for both built-in agents:

### Wake & Remote

Fields:

- time zone;
- network timeout/probe interval;
- keep display on;
- remote provider type;
- required/optional;
- configured service/process names;
- optional DNS/TCP probe settings;
- availability windows/schedule link;
- force sleep shown as unsupported/disabled in V1.

### Founder Scout baseline

Even before the domain is complete, support the configuration shape from `docs/configuration-examples.md`:

- discovery enabled and limits;
- cooldown/fixed-delay values;
- analysis batch/concurrency;
- ranking thresholds;
- invitation length/queue sizes;
- AI endpoint/deployment and secret-reference status;
- retention;
- data/browser root display.

Requirements:

- GET displays current revision;
- POST validates antiforgery and typed options;
- field-specific validation errors;
- secret value entry is handled by a separate `ISecretStore` action and never round-tripped into HTML;
- display only whether a secret exists plus last changed time/audit;
- successful save creates/promotes immutable revision;
- include change summary;
- concurrent edit detection using revision number/hash;
- no-op save behavior is clear;
- configuration history and redacted diff page.

## 4. Schedule management

Pages:

- schedule list with agent, type, local expression, time zone, next due, wake, enabled/paused, last result;
- create/edit schedule;
- schedule detail with future occurrences and run history;
- enable/disable;
- pause until date/time or indefinitely;
- resume;
- run now;
- delete only when safe, preferably soft-disable/archive if history exists.

Editor supports:

- Manual;
- One time;
- Daily;
- Selected weekdays;
- Fixed interval;
- Fixed delay after completion;
- command selection from manifest;
- time zone;
- wake/misfire/concurrency policy;
- timeout/retry bounds;
- agent-specific argument JSON through typed form where available, never arbitrary executable args.

Preview the next 5 occurrences before save using the real schedule calculator. Show DST/misfire behavior help text.

After changes, trigger schedule and wake reconciliation.

## 5. Manual run UI

- Select supported agent command.
- Render command-specific typed inputs where implemented.
- Show current configuration revision.
- Show concurrency conflict before submitting.
- Allow explicit “run despite agent pause” only with confirmation and audit; never bypass disabled/security/configuration errors.
- Create manual occurrence and start Runner.
- Redirect to run detail.

No handler launches agent executable directly.

## 6. Run history/detail

Run list filters:

- agent;
- status;
- trigger;
- date range;
- attention/failure;
- active only.

Use server-side pagination and indexes.

Run detail displays:

- IDs/correlation;
- agent, command, versions, config revision/hash;
- trigger/due/start/end/duration/heartbeat;
- status and terminal reason;
- progress timeline;
- metrics;
- summary text/structured data;
- warnings/errors;
- bounded stderr/protocol diagnostics;
- artifacts with safe local download/view actions;
- audit events;
- cancellation action for eligible states;
- retry action creating a new explicit retry occurrence.

Never render raw untrusted HTML from agent output. Encode all text. Artifact download validates path/root and uses content disposition.

## 7. Audit viewer

- paginated/filterable by time, actor, action, target, outcome, run, agent;
- detail with redacted structured JSON;
- configuration changes link to revisions;
- local CSV/JSON export optional if bounded;
- no edit/delete UI.

## 8. Wake & Remote page

Show:

- supported sleep states summary;
- current managed wake task and occurrence;
- next availability window;
- active keep-awake handle/window;
- latest network/provider readiness;
- last wake test/result;
- last wake reason and wake timers diagnostics, encoded safely;
- attention items.

Actions:

- run diagnostics;
- schedule test wake in 3/5/custom minutes;
- reconcile/re-register task;
- keep awake 30/60/custom minutes;
- release manual keep-awake;
- edit configuration/schedules.

Do not put the machine to sleep automatically during a test. Provide exact user instructions.

## 9. System settings/health

Read-only or typed settings:

- data root;
- agent root;
- loopback URL/port (changing may require restart);
- log/retention defaults;
- scheduler loop health;
- wake reconciliation health;
- Runner presence/version;
- database migration/version/pragmas;
- disk space;
- latest backup placeholder for Stage 15.

Add a diagnostics export that packages non-sensitive health/config metadata and recent relevant logs/audit. Explicitly exclude secrets, browser profiles, cookies, raw profile data, and AI prompts unless opted in later.

## 10. UI engineering

- Razor Pages + partials/view components/tag helpers as appropriate.
- Avoid oversized page models; call application services.
- Server-side validation is authoritative.
- Local CSS and minimal JS only.
- Accessible labels, error summaries, focus behavior, keyboard operation, status text not conveyed by color alone.
- Responsive at desktop and narrow Remote Desktop/window sizes.
- Use Post/Redirect/Get for successful mutations.
- Add flash/toast messages stored safely.
- Poll active run status at a bounded interval with a small JSON endpoint or partial refresh; no SignalR required in V1.

## 11. Tests

Use Razor Pages integration tests and application tests for:

- loopback pages and navigation;
- antiforgery on mutations;
- configuration valid/invalid/no-op/concurrent edits;
- secrets not rendered or persisted in config JSON;
- schedule create/edit/preview/DST validation;
- reconciliation invoked after changes;
- manual run creates occurrence and launches Runner abstraction;
- pause/disable semantics;
- run pagination/filter/detail/cancel/retry;
- safe artifact access and traversal rejection;
- output encoding/XSS regression with hostile event text;
- audit filters/detail redaction;
- wake test/reconcile/manual keep-awake actions;
- dashboard values/empty states;
- diagnostics export exclusions;
- accessibility basics in rendered markup.

## Constraints

- No Founder Scout candidate list/detail yet.
- No LAN/internet binding.
- No user account/login system in V1.
- No SPA framework.
- No automatic invitation sending.
- No direct agent execution from UI.

## Done when

- The local UI can configure both agents, store secrets safely, create schedules, run commands, monitor/cancel/retry runs, inspect audit, and manage wake readiness.
- All mutations are validated/audited.
- Run and configuration details are safe and paginated.
- Wake-test workflow is usable.
- Standard validation and manual browser smoke pass.

## Required manual smoke validation

From the tray-opened dashboard:

1. edit/save Wake Remote config and inspect revision history;
2. set a test secret and confirm it is never displayed;
3. create a short Wake Remote schedule and preview occurrences;
4. run diagnostics manually and inspect run events/artifacts;
5. cancel or retry a fake long run;
6. schedule/reconcile a test wake;
7. inspect audit entries;
8. verify UI at a narrow window size.

## Final report

Include page map, configuration/concurrency behavior, security checks, screenshots only if tooling supports them, test results, and deferred Founder Scout pages. End with:

```text
Next prompt: prompts/09-founder-scout-domain.md
```

