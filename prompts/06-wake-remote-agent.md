# Stage 06 — Wake & Remote Agent

Paste this entire prompt into Codex from the repository root after Stage 05 passes.

---

You are implementing **Stage 06: Wake & Remote Agent**.

Read repository guidance, the agent protocol, Windows wake/power implementation, schedule/runner code, and this prompt. Create/update `docs/exec-plans/stage-06-wake-remote-agent.md`.

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

Implement `WakeRemote.exe` as a small independently executable agent that runs after Windows wakes the machine, obtains a bounded keep-awake lease, waits for network readiness, verifies a configured remote-access provider, remains alive through a scheduled availability window, emits structured metrics/events/summary, and releases all resources. Integrate its typed configuration and schedule occurrence arguments with the platform.

V1 must not force the machine to sleep, change router/firewall settings, expose RDP to the internet, or manage remote-access credentials.

## 1. Typed configuration

Implement validated versioned configuration in `WakeRemote.Application`:

```text
SchemaVersion
TimeZoneId
NetworkReadyTimeoutSeconds
NetworkProbeIntervalSeconds
KeepDisplayOn
ReleaseToNormalPowerPolicyAfterWindow = true
ForceSleepAfterWindow = false and unsupported in V1
RemoteProvider settings
Optional readiness probe settings
```

Availability windows are configured centrally as schedules, but the agent must accept an immutable occurrence argument payload:

```json
{
  "windowInstanceId": "...",
  "scheduledWakeAtUtc": "...",
  "availableUntilUtc": "...",
  "remoteProviderRequired": true
}
```

Validate:

- `availableUntilUtc > scheduledWakeAtUtc`;
- maximum window duration is bounded;
- occurrence is not too stale unless misfire policy allowed immediate execution;
- probe intervals/timeouts are bounded;
- force sleep remains false in V1;
- provider configuration contains no plaintext credentials.

## 2. Commands

Implement:

```powershell
WakeRemote.exe run <standard Agent SDK args>
WakeRemote.exe diagnose <standard Agent SDK args>
WakeRemote.exe check-remote <standard Agent SDK args>
WakeRemote.exe protocol-demo ...
```

`run` is the normal agent command. All normal stdout must be valid Agent SDK JSONL. Human diagnostics go to stderr.

## 3. Network readiness

Define `INetworkReadinessProbe` returning structured attempts/results.

Initial implementation should combine conservative local checks without depending on a public third-party service:

- `NetworkInterface.GetIsNetworkAvailable()`;
- presence of at least one suitable non-loopback interface in an operational state;
- optional DNS lookup of a configured hostname;
- optional TCP connection to a configured host/port, disabled by default.

Behavior:

```text
start timeout window
probe immediately
if ready -> continue
otherwise emit progress/heartbeat and wait configured interval
stop on ready, timeout, or cancellation
```

Record:

- attempts;
- elapsed milliseconds;
- final readiness;
- last bounded reason code.

Do not log IP configuration beyond what is required for local diagnostics.

## 4. Remote-provider readiness

Create `IRemoteAccessProviderProbe` and implementations/configuration for:

### Chrome Remote Desktop

- provider config may specify expected Windows service names and/or process names because installations can differ;
- inspect configured services/processes safely;
- report `Ready`, `NotReady`, `NotConfigured`, or `Unknown` with reason codes;
- do not attempt login, PIN entry, browser automation, extension installation, or credential retrieval.

### Windows RDP

- optional provider;
- check configured service, normally `TermService`, and optionally local listener health;
- do not enable RDP, alter firewall, or expose ports;
- document Windows edition/host prerequisites as an operational concern.

A composite provider may allow `AnyConfiguredProviderReady` in the future, but keep V1 clear and typed.

If the provider is required and not ready after network readiness, fail the run. If optional, emit a warning and continue the machine availability window.

## 5. Keep-awake lifecycle

`run` must:

1. emit `started`;
2. acquire `IPowerRequestService` handle as early as practical;
3. emit scheduled/actual wake-delay metrics;
4. wait for network;
5. verify remote provider;
6. emit `machine.ready`, `network.ready`, and `remote.ready` metrics/status events;
7. remain active until `availableUntilUtc`, emitting bounded heartbeat/progress at a sensible interval;
8. emit summary and completed;
9. release the power handle in `finally`.

If Runner already holds an execution power request, nested/reference-counted handles must remain correct.

If the availability deadline is already past:

- apply a documented stale-window policy, normally exit with a non-success/skipped semantic instead of holding awake;
- do not extend the window silently.

Cancellation/timeout must release the handle immediately.

## 6. Summary contract

Emit deterministic summary JSON:

```json
{
  "scheduledWakeAtUtc": "...",
  "agentStartedAtUtc": "...",
  "wakeDelaySeconds": 0,
  "networkReady": true,
  "networkReadySeconds": 12,
  "remoteProvider": "ChromeRemoteDesktop",
  "remoteProviderRequired": true,
  "remoteProviderReady": true,
  "machineReadyAtUtc": "...",
  "availableUntilUtc": "...",
  "keepAwakeSeconds": 10800,
  "result": "Completed"
}
```

Use reason codes for partial/failure states. Do not include process lists, credentials, or sensitive network details in summary.

## 7. Platform integration

- Seed a valid default Wake Remote configuration revision based on docs examples, without overwriting user data.
- Create an application use case that transforms configured weekday/daily availability windows into Wake Remote schedule definitions and occurrence argument payloads.
- Set wake policy to `Required` for normal wake windows.
- Set timeout to availability duration plus bounded startup grace.
- Ensure `NextWake` invokes Runner, which invokes Wake Remote.
- After completion, wake-task reconciliation selects the next window.
- Expose configuration validation errors for Stage 08 UI.

Do not create multiple independent Windows wake tasks per window; central `NextWake` remains the bridge.

## 8. Diagnostics command

`diagnose` should emit protocol events/artifact containing:

- current UTC/local time and configured time zone;
- current network readiness result;
- remote-provider readiness;
- power diagnostics summary from the platform adapter when accessible;
- next configured availability window if supplied;
- no state changes.

## 9. Tests

Use fake `TimeProvider`, fake power handle, fake network probe, fake provider probe, and actual JSONL writer. Cover:

- valid/invalid configuration and occurrence arguments;
- immediate network ready;
- network becomes ready after retries;
- network timeout;
- cancellation during network wait;
- required provider ready/not ready/not configured;
- optional provider warning;
- active window wait using fake time;
- deadline already passed;
- maximum duration validation;
- power acquired before readiness and released in every path;
- no display-required flag unless configured;
- deterministic metrics/summary;
- stdout contains only valid JSONL;
- no sensitive service/process detail in summary;
- schedule/window integration and next wake reconciliation;
- Runner execution integration with the real agent process using short fake-time/test modes.

Normal tests must not wait real hours. Abstract delays through `TimeProvider`/testable delay service.

## Constraints

- No tray or full web UI yet.
- No forced sleep.
- No Wake-on-LAN relay.
- No public network configuration.
- No remote credential management.
- No requirement for live Chrome Remote Desktop or RDP in CI.

## Done when

- Wake Remote runs through Runner and Agent SDK protocol.
- It holds the machine awake for a bounded occurrence window.
- Network and provider readiness are observable and summarized.
- Power handle is released in every path.
- Platform schedules/occurrences can produce correct wake-window arguments.
- Tests and validation pass.

## Required smoke validation

Run Wake Remote through Runner with a short 60-second test window and local fake/diagnostic provider mode. Verify:

- wake-delay metric;
- network result;
- provider result;
- heartbeats;
- terminal summary;
- power-handle release;
- next wake reconciliation state.

Clearly label fake provider mode in all output.

## Final report

Include configuration, provider checks, lifecycle, summary schema, power cleanup, actual smoke results, and deferred on-demand Wake-on-LAN. End with:

```text
Next prompt: prompts/07-tray-and-local-web-host.md
```

