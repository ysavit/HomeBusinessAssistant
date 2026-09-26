# ADR-0008: Founder Scout browser session and safe-stop semantics

- Status: Accepted
- Date: 2026-08-30
- Stage: 10
- Owners: Home Business Assistant

## Context

Founder Scout now needs authenticated browser acquisition while remaining a small local agent. Browser profiles contain sensitive user-bound session state, source pages can change independently of the application, and both the tray scheduler and a wake-launched Runner can observe the same account work. The baseline requires dedicated profiles, ordinary headed authentication, bounded sequential navigation, checkpoints, and immediate enforcement stops, but it does not define which project owns Playwright, how a profile is leased across processes, or how a stopped agent prevents a fixed-delay schedule from creating rapid successor work.

## Decision drivers

- Browser/session state must never enter configuration, SQLite, logs, fixtures, or protocol summaries.
- Domain and Application projects must remain independent of Playwright and browser-native types.
- One account profile must have at most one persistent context across local processes.
- Source parser changes must be versioned and testable against sanitized loopback fixtures.
- Authentication and enforcement stops must not trigger account failover or a rapid scheduled retry.
- Browser downloads must be explicit operational actions, not a runtime side effect.

## Decision

`FounderScout.Application` owns browser-neutral contracts for runtime validation, session lifetime, discovery, extraction, challenge detection, diagnostics, and orchestration. `FounderScout.Infrastructure` alone references centrally pinned `Microsoft.Playwright` and implements those contracts with Chromium.

Each account resolves to exactly `<runner-agent-data>/browser/<account-id>`. Existing reparse components are rejected, the default Chrome profile is never used, and a `FileShare.None` handle on a marker inside the account directory is the cross-process lease. The persistent profile may contain the site's ordinary browser session and is classified as sensitive excluded runtime state.

Authentication uses an explicit headed `authenticate --account <id>` command. Founder Scout accepts no password or MFA secret. Discovery normally uses headless mode from typed configuration, performs sequential navigation, applies deterministic spacing and hard limits, and commits each raw artifact and snapshot before its checkpoint.

The `StartupSchoolSourceAdapter` and locator/source options are versioned independently. Ordered semantic roles, text, labels, and stable attributes are allowed; XPath and positional selectors are rejected. Checkpoints contain only bounded navigation metadata and hashes, never cookies, headers, storage state, forms, or page bodies.

Authentication required, access denied, throttling, challenge, unexpected host, and parser-health failure stop the current account without failover. The agent records the account health and returns a bounded summary field requesting `scheduleAction=Pause`. The central Runner honors that generic request for the occurrence's own schedule before fixed-delay completion planning. Transient navigation and profile-lock failures do not request an indefinite pause and remain eligible only for the configured bounded retry policy.

Playwright 1.62.0 and its matching Chromium revision are installed explicitly from the built `playwright.ps1` command. Runtime commands validate availability and fail clearly; they never invoke browser installation.

## Alternatives considered

### Reference Playwright directly from the agent command or Application project

This would leak browser implementation types into orchestration and make limit/stop/checkpoint testing require a browser. It was rejected in favor of application ports and an infrastructure adapter.

### Use the user's default Chrome profile

This risks profile corruption, unintended account reuse, and uncontrolled access to unrelated browsing state. It was rejected.

### Store Playwright storage-state JSON in SQLite

Storage state contains authentication material and would mix session secrets with database backups and reports. Persistent profile storage under the sensitive agent root was selected instead.

### Automatically switch accounts or retry enforcement signals

This can turn an enforcement signal into bypass behavior and can amplify load. The current account and run stop immediately and its schedule is paused.

### Download Chromium when discovery starts

This creates an undeclared network and disk mutation during scheduled execution and can change browser versions without an operator action. Explicit installation was selected.

## Consequences

### Positive

- Browser implementation and session sensitivity remain localized and auditable.
- Limit, stop, checkpoint, and commit-order policies can be tested with fakes; the real adapter is tested against loopback HTML only.
- A profile lock prevents concurrent persistent contexts even outside central schedule policy.
- Fixed-delay scheduling cannot rapidly recreate work after an enforcement stop.
- Browser and adapter versions are visible in metrics and diagnostics.

### Negative / tradeoffs

- The Playwright browser cache consumes several hundred megabytes and must be installed again after an incompatible Playwright upgrade.
- Persistent browser authentication is bound to the Windows user/profile and is intentionally non-portable.
- Source locator drift still requires a versioned configuration/adapter update and optional live developer validation.
- A crash can leave a harmless lock marker file; only the open handle represents ownership.

## Implementation and migration

- No Founder Scout schema migration is required. Stage 09 account, segment, checkpoint, snapshot, and action tables already carry the required durable state.
- Configuration schema remains 1.0 with additive `startupSchool` source options; older revisions resolve safe defaults and the typed editor materializes the full options on save.
- Founder Scout manifest 1.2.0 adds browser discovery/authentication/diagnostic/checkpoint capabilities and `record-fixture`.
- Diagnostic artifacts are staged under the Runner run root with bounded 14-day retention metadata.

## Validation

- Application policy tests cover daily/disabled/runtime/cancellation gates, incompatible checkpoint fallback, and all stop-to-account-state mappings.
- Real loopback Playwright tests cover runtime launch, profile exclusivity, ordered locator fallbacks, semantic extraction, pagination/load-more/infinite-scroll abstractions, authentication/challenge/unexpected-host detection, image exclusion, and bounded diagnostic redaction/retention.
- Runner/child tests capture 20 profiles, hit new and consecutive-known limits, persist checkpoints/metrics, verify account state, and prove authentication/challenge stops without failover or orphan profile locks.
- The central finalizer integration test proves an agent-requested enforcement stop indefinitely pauses its own fixed-delay schedule before successor planning.

## Follow-up work

- Stage 11 consumes the grounded capture envelope for normalization, redaction, screening, and processing.
- Stage 13 adds user-facing browser account/segment management and remediation controls.
- Packaging must install/verify the pinned browser explicitly and preserve profile exclusions.
