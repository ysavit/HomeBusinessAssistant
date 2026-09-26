# Stage 21 — Wake Remote Guided Onboarding

Paste this entire prompt into Codex from the repository root after Stage 20 passes.

---

You are implementing **Stage 21: Wake Remote Specialized Onboarding, Provider/Network Checks, and Task Scheduler/Power Guidance**.

Read all repository guidance, `docs/PROJECT_STATE.md`, Wake Remote configuration/workflow/provider adapters, Windows wake/power/task code, Stages 18–20 onboarding implementation, and this prompt. Create/update `docs/exec-plans/stage-21-wake-remote-onboarding.md`.

## Repository-state handoff

Verify prior onboarding state, selection/adapter behavior, Founder Scout integration, and current Wake Remote settings/diagnostics directly. Update `docs/PROJECT_STATE.md` only with verified Stage 21 results and name `prompts/22-onboarding-validation-scheduling-completion.md` as the exact next prompt.

## Goal

Provide a specialized Wake Remote setup wizard that explains the safety boundary, configures a pre-existing remote-access provider and optional network probes, performs read-only local readiness checks, and gives clear Task Scheduler/power warnings before Stage 22 creates any diagnostic occurrence or schedule.

The flow is:

```text
Purpose and safety
  -> choose already-installed provider
  -> network readiness
  -> provider readiness check
  -> availability/power preferences
  -> Task Scheduler and wake capability
  -> review configuration
  -> Ready for diagnostic (Stage 22)
```

## 1. Specialized adapter

Implement an explicit `WakeRemoteOnboardingAdapter` behind the Stage 19 contract. Reuse the current typed Wake Remote configuration/default provider/validator, network/provider diagnostic ports, power diagnostics, wake task status, configuration revision service, and safe settings UI.

The adapter must:

- use stable versioned step keys;
- preserve existing valid configuration on re-entry;
- save through authoritative immutable configuration revisions;
- keep onboarding rows limited to revision/hash and safe status references;
- declare diagnostic command and schedule capabilities for Stage 22;
- never call Windows APIs from Razor Page models;
- never enable/disable RDP, Chrome Remote Desktop, services, firewall rules, router state, credentials, or Windows power plans.

## 2. Purpose and safety step

Explain:

- Wake Remote verifies local readiness for an already configured remote-access product;
- it can request that Windows keep the system awake during a bounded window;
- a scheduled wake depends on Task Scheduler, firmware, sleep model, battery, policy, and the same signed-in user session;
- it does not prove end-to-end internet reachability or successful remote login;
- it does not expose the Home Business Assistant web UI remotely;
- it never forces the computer to sleep, shuts down, hibernates, enables RDP, changes firewall/router settings, or stores a remote-access PIN/password;
- releasing keep-awake returns control to normal Windows policy.

Require acknowledgment before allowing wake-enabled preferences. Manual-only/provider-diagnostic use remains possible when wake is unavailable.

## 3. Provider selection and configuration

Support current production choices:

- Chrome Remote Desktop;
- Windows RDP.

Keep `DiagnosticFake` visible only under an explicit Development/Test environment and label every fake result.

For Chrome Remote Desktop, configure only bounded known service/process identifiers already supported by the adapter. For RDP, configure only read-only service/listener expectations. Reject arbitrary shell commands, executable paths, credentials, public hosts, scripts, and registry mutations.

Show prerequisites:

- provider must already be installed and configured under the same Windows user where appropriate;
- RDP hosting requires a compatible Windows edition and independently reviewed policy/firewall/network configuration;
- provider process/service readiness is local evidence only.

## 4. Network readiness step

Configure the existing bounded checks:

- local interface/network availability;
- optional DNS name;
- optional TCP host/port;
- retry interval and total readiness timeout;
- provider-required versus provider-optional policy.

Security and privacy:

- validate hostnames/ports and bounded collection sizes;
- do not accept URLs with credentials;
- do not scan port ranges or discover network devices;
- do not log resolved addresses or full error output when unnecessary;
- DNS/TCP probes are disabled by default and run only after explicit user action or a later occurrence;
- explain that a successful probe is not proof that remote login will work.

## 5. Explicit read-only provider check

Add a user-triggered local `Check provider` operation through the existing Infrastructure/Application boundary.

- It must be read-only and bounded by timeout/cancellation.
- Report `Ready`, `Not installed`, `Service stopped`, `Process absent`, `Listener unavailable`, `Unsupported Windows edition/policy`, `Permission limited`, or `Unknown` using safe stable codes.
- Do not automatically start a service/process or modify policy.
- Do not ask for or test a remote-access credential/PIN.
- Persist only safe status, provider kind, observed UTC, and bounded reason code; no raw process list, service configuration, listener table, or command output.
- Audit the check and deduplicate repeated attention.

Normal tests use fake process/service/network boundaries.

## 6. Availability and power preferences

Configure typed preferences:

- default availability-window duration and grace;
- network/provider readiness timeouts;
- keep system awake;
- optional keep display on, default false with a privacy/power warning;
- provider required/optional;
- wake preference: `Never`, `Offer when scheduling`, or `Require for selected schedule` as wizard metadata interpreted in Stage 22;
- battery/policy acknowledgment when relevant.

Do not create a schedule here. Configuration must reject forced sleep, shutdown, hibernate, credential storage, unbounded windows, or unsafe endpoints.

## 7. Task Scheduler and power readiness step

Reuse Stage 18 platform checks and add Wake-specific interpretation.

Display separately:

- interactive signed-in user requirement;
- Task Scheduler service/folder status;
- HostAtLogon managed-task status/ownership;
- NextWake managed-task ownership/capability;
- supported sleep states;
- wake timer visibility and permission limitations;
- AC/battery/policy caveats;
- firmware/Modern Standby uncertainty;
- read-only provider readiness.

Semantics:

- Task Scheduler unavailable blocks wake-enabled schedules but not manual diagnostics or non-wake schedules.
- Wake timers unsupported/unknown produces a prominent warning and disables `Require wake` until explicitly rechecked/acknowledged according to policy.
- An unmanaged task occupant is a blocker; never replace it.
- Do not register/remove/reconcile a task during page GET or ordinary check.
- If an explicit harmless task-registration test is offered, it must remain the already documented separate confirmed manual workflow; the onboarding page links to it and later imports only safe status. Do not sleep the machine.

## 8. Review and save behavior

Review shows:

- chosen provider and local readiness status;
- enabled network probes without sensitive target expansion;
- window/timeouts;
- keep-awake/display choices;
- Task Scheduler/wake status and acknowledged warnings;
- immutable configuration revision number/hash;
- blockers versus optional warnings;
- reminder that remote software/network setup remains external.

Save idempotently through the typed validator. Mark `ReadyForValidation` when manual `diagnose` can safely run. A missing wake capability does not block a manual-only/non-wake setup. Leave newly selected Wake Remote disabled until Stage 22. Reconfiguring an already enabled installation must not silently change its enabled or schedule state.

## 9. Tests

Cover at minimum:

- specialized adapter resolution and typed configuration round trip;
- provider choices and Development-only fake visibility;
- invalid/unbounded network target rejection;
- provider-ready/missing/stopped/permission/unsupported safe results;
- proof no provider/service/firewall/policy mutation occurs;
- Task Scheduler unavailable/unmanaged/healthy interpretations;
- unsupported/unknown wake timer warnings;
- manual-only flow succeeds without wake capability;
- `Require wake` blocked when prerequisites fail;
- keep-display warning/default;
- configuration revision, resume, stale edit, re-entry preservation;
- antiforgery, owner authorization, encoding, desktop/narrow rendering;
- no raw task/power/process/network output in HTML/audit/onboarding rows.

Automated tests must not register tasks, change power settings, enable RDP, alter firewall/router state, suspend/hibernate/shutdown the machine, or contact a real remote provider.

## Constraints

- Do not create occurrences or schedules in Stage 21.
- Do not enable Wake Remote.
- Do not install/configure remote-access software.
- Do not force sleep or add Wake-on-LAN.
- Do not expose local Kestrel beyond `127.0.0.1`.
- Do not claim provider readiness equals successful remote reachability.

## Done when

- A selected Wake Remote agent can be configured through a safe specialized wizard.
- Provider/network/task/power status is understandable and accurately scoped.
- Manual-only setup remains usable when scheduled wake is unavailable.
- No check changes Windows/provider/network configuration.
- The selection is ready for Stage 22 diagnostic/schedule activation without already being active.
- All validation passes.

## Required validation

Run standard validation plus:

1. fake Chrome Remote Desktop ready/not-installed/stopped flows;
2. fake RDP supported/unsupported/listener-missing flows;
3. Task Scheduler folder-unavailable and unmanaged-occupant flows;
4. wake-timer unsupported/permission-limited flows;
5. manual-only configuration through restart/resume;
6. desktop/narrow browser QA with explicit warnings and no sensitive/raw output;
7. optional read-only `power-diagnostics` smoke on the current machine, recorded without claiming hardware wake.

## Final report

Include provider and network choices, exact non-mutation boundary, Task Scheduler/power warning rules, manual-only behavior, typed revision evidence, routes, actual tests/browser/read-only diagnostics, hardware limitations, project-state update, and exact next prompt `prompts/22-onboarding-validation-scheduling-completion.md`.
