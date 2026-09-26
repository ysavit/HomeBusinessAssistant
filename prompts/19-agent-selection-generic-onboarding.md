# Stage 19 — Agent Selection and Generic Per-Agent Onboarding

Paste this entire prompt into Codex from the repository root after Stage 18 passes.

---

You are implementing **Stage 19: Installed-Agent Selection, Pending-Agent Detection, Extensible Wizard Contracts, and Generic Per-Agent Configuration**.

Read all repository guidance, `docs/PROJECT_STATE.md`, the Stage 18 ExecPlan/state machine, Stage 17 agent registry/schema editor, management configuration services, and this prompt. Create/update `docs/exec-plans/stage-19-agent-selection-generic-onboarding.md`.

## Repository-state handoff

Before coding, verify Stage 18's migration, onboarding entry policy, readiness checks, routes, tests, and handoff against the repository. Before final reporting, update `docs/PROJECT_STATE.md` with verified state and name `prompts/20-founder-scout-onboarding.md` as the exact next prompt. Do not copy secrets or runtime user data into documentation.

## Goal

Let the owner choose which currently installed agents to use, persist that choice inside the active onboarding session, and provide a reusable per-agent wizard for safe generic-schema agents. Founder Scout and Wake Remote receive specialized adapters in Stages 20 and 21; this stage establishes the common contract and useful fallback.

The user-visible flow becomes:

```text
Platform readiness
  -> select installed agents
  -> configure each selected generic agent
  -> specialized agents show "guided setup arrives next" until their adapters exist
```

## 1. Available, pending, selected, and configured semantics

Define these terms explicitly in Application code and UI:

- **Available**: a valid currently installed `AgentDefinition` whose package/schema/executable passed registry validation.
- **Unavailable**: missing, invalid, removed, incompatible, or typed-adapter-required without an installed onboarding adapter.
- **Pending**: available but never completed onboarding for the installed configuration-schema generation, or a completed agent whose adapter reports a material new prerequisite/configuration incompatibility.
- **Selected**: chosen in the current onboarding session. Selection alone never enables or executes it.
- **Configured**: has a current authoritative configuration revision that passes the current validator. Scanner-seeded defaults may be valid but are still presented for review on first onboarding.
- **Ready for validation**: configured and all adapter-declared blocking prerequisites currently pass. This is not the same as enabled.
- **Deferred**: explicitly not selected now; remains disabled and can be added later.

Do not mark every patch-version package update pending. Persist the last reviewed manifest version and configuration schema version, then let adapter policy distinguish informational updates from required re-onboarding. An incompatible configuration-schema major, invalid current configuration, missing required secret, or changed safety capability may require review.

## 2. Persisted agent selections

Extend the onboarding persistence model/migration as needed with a session-scoped `OnboardingAgentSelection` record containing:

- session ID and agent ID unique identity;
- selection status: `Selected`, `Deferred`, `Unavailable`, or `Removed`;
- progress status: `NotStarted`, `Configuring`, `ReadyForValidation`, `NeedsAttention`, `Completed`, or `Skipped`;
- current adapter/step key;
- reviewed manifest/configuration-schema versions;
- starting configuration revision ID/hash optional;
- saved configuration revision ID/hash optional;
- last safe reason code;
- created/updated/completed UTC and bounded revision.

Never copy configuration JSON or secrets into onboarding tables. Reference immutable configuration revision IDs/hashes only.

Selection updates must be transactional, stale-revision safe, idempotent, and audited. If an agent disappears during onboarding, mark its selection unavailable/removed and allow the session to continue without deleting history.

## 3. Agent selection page

Add `/Onboarding/Agents` or an equivalent route that lists only bounded safe projections from the installed registry.

For each agent show:

- display name, description, installed version, configuration schema version;
- enabled/disabled state;
- current configuration status;
- scheduling/manual-run capabilities;
- interactive-session and wake implications;
- onboarding support: specialized, safe generic, or typed adapter required;
- pending/review reason;
- Select now / Configure later choice.

Rules:

- Never preselect an agent solely because it is installed.
- It is permissible to defer every agent; finish the session as `Deferred`, keep a dashboard reminder, and run nothing.
- Built-in agents may be selected even before specialized adapters arrive, but Stage 19 must direct them to the existing Agent Settings page or show a clear specialized-setup-pending state rather than rendering incomplete generic controls.
- Invalid/removed agents cannot be selected and link to bounded package diagnostics.
- A newly scanned agent on an established system produces a non-blocking dashboard/Agents banner: `New agent available — Set up`.
- Do not interrupt an active page or run with a forced redirect for newly discovered agents.

## 4. Extensible onboarding adapter contract

Add an Application-owned, UI-neutral contract such as `IAgentOnboardingAdapter`. Implementations are registered explicitly in Host composition; never discover or load assemblies from agent packages.

The contract should provide bounded typed data for:

- adapter ID/version and supported agent ID;
- display steps and stable step keys;
- prerequisite descriptors and current safe results;
- whether current configuration requires review;
- loading/validating/saving configuration through existing services;
- declared secret references and status only;
- a later diagnostic command plan;
- later schedule templates/capability constraints;
- completion summary data that contains no secret or sensitive raw content.

Do not make Razor depend directly on agent Infrastructure types. Specialized Host adapters may translate existing Application configuration records into onboarding form models. Preserve clean dependency direction.

If no specialized adapter exists:

- use the generic adapter only when the Stage 17 schema parser reports `SupportsGenericEditor`;
- otherwise show `Custom setup adapter required` and keep the agent disabled;
- never render arbitrary controls, HTML, scripts, URLs, or validation messages directly from package content.

## 5. Generic configuration wizard

Reuse and refactor the existing safe generic configuration editor rather than duplicating validation logic. The onboarding route may use wizard-specific presentation, but saving must call the same authoritative configuration service.

Support:

- string, integer/number, Boolean, enum, simple arrays, constants, descriptions/defaults/min/max/pattern;
- separate opaque secret-reference fields and protected secret set/delete actions;
- current revision/hash display and stale edit rejection;
- explicit review of scanner-seeded defaults;
- Save and continue, Save and exit, Back, and Configure later;
- safe field-level validation mapping;
- resume from the saved immutable configuration revision.

Secret behavior:

- secret inputs are always empty on GET and failed POST redisplay;
- show only `Configured`/`Not configured`/`Unavailable` status;
- secret mutation is a separate antiforgery-protected action;
- never serialize a secret value into model state, TempData, query strings, audit data, or configuration JSON;
- deleting a required secret moves the selection back to `NeedsAttention` without deleting its configuration revision.

## 6. Later settings and re-entry

Add safe entry points:

- Dashboard: Resume onboarding / New agents available.
- Agents index/detail: `Set up`, `Continue setup`, or `Run setup again` according to state.
- Settings: Onboarding history/status and `Add or configure agents`.

`Run setup again` creates or joins a `ReconfigureAgent` session. It must not reset existing settings, disable a working agent, or overwrite schedules. New values become authoritative only when the normal configuration save succeeds. Stage 22 will govern diagnostic/activation changes.

## 7. Audit and failure handling

Audit:

- selection saved/changed/deferred;
- generic configuration reviewed/saved/no-op/stale-rejected;
- protected secret set/deleted/status-check failure;
- agent became unavailable during onboarding;
- reconfiguration session opened.

Use safe stable reason codes. A package disappearing, validator becoming incompatible, or schema becoming complex must leave existing runtime history intact and prevent new unsafe activation.

## 8. Tests

Cover at minimum:

- available/pending/configured derivation, including scanner-seeded defaults;
- no automatic preselection or enablement;
- select/defer-all/resume/concurrent stale update;
- newly installed agent banner without forced redirect;
- removed/invalid agent during an active session;
- explicit adapter resolution and no dynamic loading;
- generic field rendering, HTML encoding, validation, defaults, enum/array behavior;
- secret set/delete/no-echo behavior;
- immutable configuration revision and stale hash behavior;
- unsupported complex schema safe message;
- re-entry from Agent Settings preserves working configuration/schedules;
- antiforgery, owner authorization, desktop/narrow accessibility.

Use the Sample Business Agent as the generic happy-path fixture. Normal tests do not use live services or real secrets.

## Constraints

- Do not implement Founder Scout specialized setup in Stage 19.
- Do not implement Wake Remote specialized setup in Stage 19.
- Do not run diagnostics, create schedules, or enable agents yet.
- Do not interpret manifest capabilities as permissions.
- Do not add in-process plugin discovery, arbitrary reflection over package assemblies, or scriptable wizard definitions.

## Done when

- The owner can select/defer valid installed agents and resume the same session.
- New agents appear as pending without disrupting an established installation.
- Safe generic-schema agents can be configured, including protected secrets, through the wizard.
- Unsupported agents fail closed with a clear typed-adapter message.
- Existing Agent Settings can start/re-enter setup without data loss.
- All validation passes.

## Required validation

Run the standard restore/build/test/format sequence plus:

1. fresh install with Founder Scout, Wake Remote, and Sample Business Agent available;
2. select only the sample, save generic config/secret, defer others, restart Host, and resume;
3. scan a newly generated safe generic agent into an established database and verify the non-blocking pending banner;
4. remove an agent mid-session and verify safe continuation/history;
5. hostile schema labels/descriptions encoding test;
6. desktop/narrow browser workflow with zero console/page errors and no secret echo.

## Final report

Include the pending-agent algorithm, selection persistence, adapter resolution boundary, generic/typed decision, settings re-entry behavior, migration, routes, actual validation, limitations, `docs/PROJECT_STATE.md` confirmation, and exact next prompt `prompts/20-founder-scout-onboarding.md`.
