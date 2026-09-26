# Stage 10 — Founder Scout Browser Discovery

Paste this entire prompt into Codex from the repository root after Stage 09 passes.

---

You are implementing **Stage 10: Playwright Browser Sessions, Manual Authentication, Bounded Profile Discovery, Capture, Checkpointing, and Diagnostics**.

Read repository guidance, Founder Scout domain/configuration, acceptance matrix, and this prompt. Create/update `docs/exec-plans/stage-10-founder-scout-browser-discovery.md`.

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

Implement the Founder Scout browser acquisition phase using Microsoft Playwright for .NET. It must reuse dedicated persistent browser profiles, support manual headed authentication, navigate sequentially through configured founder-search discovery segments, capture profile data and raw diagnostics, persist snapshots before analysis, enforce hard run limits, checkpoint progress, and stop safely on authentication/access/throttling/challenge/parser-health signals.

Do not implement stealth, CAPTCHA solving, fingerprint spoofing, proxy rotation, human-movement simulation, automatic invitation sending, or automatic account failover following an enforcement signal.

## 1. Playwright setup

Add centrally managed stable Playwright package(s) compatible with .NET 10.

Provide documented installation/bootstrap command for Chromium binaries, for example through Playwright's generated PowerShell installer. Do not download browsers silently during every agent run.

Create abstractions:

- `IBrowserRuntimeFactory`
- `IBrowserSessionManager`
- `IProfileDiscoverySource`
- `IProfileCaptureExtractor`
- `IBrowserChallengeDetector`
- `IBrowserDiagnosticCapture`

The source adapter for this project should be versioned as `StartupSchoolSourceAdapter`, but keep generic browser/session infrastructure reusable.

## 2. Dedicated persistent account profiles

For each enabled `BrowserAccount`:

```text
<data>/founder-scout/browser/<account-id>/
```

Requirements:

- resolve only beneath the configured data root;
- one active browser context per account via a domain/platform lease;
- never point automation at the user's ordinary default Chrome profile;
- use Chromium or a configured Chrome channel if installed and validated;
- store no passwords in configuration/DB;
- browser profile folders are excluded from logs, reports, normal backups, fixtures, and source control;
- account health/status updated from observed session outcomes.

## 3. Authenticate command

Implement:

```powershell
FounderScout.exe authenticate --account <id>
```

Behavior:

1. validate account and profile path;
2. launch headed persistent browser context;
3. navigate to configured Startup School Co-Founder Matching entry URL;
4. let the user sign in manually;
5. detect authenticated matching page through configured/semantic signals;
6. allow the user to close/confirm completion;
7. update account status and timestamp;
8. emit JSONL progress/summary;
9. never prompt for or capture a password in the console/app.

If MFA or challenge appears, the user handles it manually in the headed browser. Do not automate challenge completion.

Add a timeout that is generous but bounded and cancellable.

## 4. Source configuration and locator strategy

Create versioned `StartupSchoolSourceOptions` containing:

- entry/discovery URL;
- allowed host names;
- authenticated-page signals;
- login-page signals;
- profile-card/profile-link locator candidates;
- next-page/load-more/infinite-scroll behavior;
- profile-page root/field locator candidates;
- challenge/throttle/access-denied signals;
- navigation timeout;
- settle delay/minimum request spacing;
- source adapter version.

Prefer resilient Playwright locators by accessible role, link text, labels, headings, and stable attributes. Avoid brittle absolute XPath and `nth-child` selectors where alternatives exist.

Because credentials/live page access may not be available to Codex or CI:

- implement the adapter against checked-in sanitized local HTML fixtures and a local fixture server;
- add `record-fixture`/diagnostic capability that saves sanitized page HTML/screenshot/locator report from an authenticated developer session;
- when a live authenticated session is available during implementation, inspect and configure real locators, but do not claim live operation if it was not verified;
- keep locator config/version separate from compiled control flow where practical.

Do not commit captured real profile data.

## 5. Discovery command

Implement:

```powershell
FounderScout.exe discover --account <id> [--segment <id>]
```

Normal flow:

```text
validate config/account/segment and acquire account lease
launch persistent context
navigate to discovery entry
verify authenticated healthy page
restore safe checkpoint/segment position where possible
enumerate visible profile links sequentially
for each profile:
    enforce limits and stop conditions
    navigate/open profile
    verify profile page health
    extract stable source key/url/display fields/raw text/structured fields
    save raw HTML/text/screenshot only according to retention/diagnostic policy
    calculate capture hash
    persist candidate/snapshot before any analysis
    emit metrics/progress/checkpoint
    return to discovery/list or continue through stable URLs
persist final checkpoint and domain run summary
close context and release lease
```

Do not keep a database transaction open during browser navigation.

## 6. Hard run limits

Enforce all configured bounds locally:

- maximum new profiles per run, default 20;
- maximum viewed profile pages per run;
- maximum new profiles per day across runs;
- maximum run duration, default 10 minutes;
- browser concurrency 1;
- stop after configured consecutive already-known unchanged profiles;
- minimum spacing/cooldown between navigation actions as a deterministic load bound;
- cancellation;
- account/segment paused or disabled state.

At completion, the platform fixed-delay schedule can create the next discovery occurrence after configured cooldown, default 10 minutes. The agent must not sleep internally for 10 minutes and must not self-schedule.

Emit distinct no-work/completed/capped summaries.

## 7. Stop conditions and session health

Stop the current account/run immediately on:

- redirect to login or missing authenticated-page signals;
- HTTP/navigation result or page signal indicating access denied;
- throttling/too-many-requests signal;
- challenge/CAPTCHA/verification page;
- persistent unexpected host/navigation outside allow-list;
- parser/extraction health below threshold;
- repeated navigation failure;
- browser profile lock/conflict;
- cancellation.

Behavior:

- update BrowserAccount status and bounded reason;
- capture a diagnostic screenshot, URL metadata, sanitized HTML/locator report when allowed;
- emit warning/error/summary and appropriate Agent exit code;
- do not switch to another account automatically;
- do not retry access denial/throttling/challenge in the same run;
- transient network/browser crashes may receive at most a small bounded retry according to config.

## 8. Profile capture envelope

Populate the Stage 09 capture envelope with as much structured data as can be grounded:

- source adapter/version;
- account/segment;
- source profile key;
- canonical URL;
- captured UTC;
- display name;
- raw visible profile text;
- structured sections keyed by stable semantic labels;
- last-seen text if visible;
- extraction completeness and warnings;
- source page fingerprint.

Do not store profile image bytes. If an `<img>` exists, ignore it.

Persist raw HTML only under configured retention and artifact size limits. Prefer extracted visible text/structured JSON for ordinary snapshots; save full HTML/screenshot mainly for diagnostics or explicit configuration.

## 9. Checkpointing and resume

Checkpoint after each successfully persisted profile:

- account/segment;
- last source key/url;
- discovered link set hash or continuation info;
- profiles viewed/new/duplicate;
- current page/scroll marker when reliable;
- adapter version;
- UTC.

Resume only when:

- checkpoint version matches;
- account and segment match;
- source page still validates;
- continuation metadata is safe.

Otherwise restart discovery conservatively and rely on deduplication. Never persist cookies/tokens in checkpoint JSON.

## 10. Diagnostics

On failure, create bounded per-run artifacts:

```text
screenshot.png
page.html or sanitized-page.html
locator-report.json
browser-error.json
console-summary.json
```

Rules:

- artifact paths stay under assigned run directory;
- redact form values, cookie/storage data, authorization values, and sensitive query parameters;
- cap HTML/console sizes;
- retention default 14 days;
- report artifacts through Agent SDK;
- do not include raw diagnostics in ordinary summary.

Provide `FounderScout.exe diagnose --account <id>` that checks browser binary, profile path, lock, configured URL/host, and optionally page authentication in headed or headless mode without performing discovery.

## 11. Scheduling integration

Seed or provide UI action for a recommended discovery schedule:

```text
Type: FixedDelayAfterCompletion
Command: discover
Batch limit: config-driven, default 20 new
Delay: config-driven, default 10 minutes
Concurrency: Forbid
Wake policy: configurable, default IfSleeping only if the user wants unattended runs
Timeout: config max runtime plus startup/cleanup margin
```

When agent returns authentication-required, access-denied, throttled, challenge-detected, or parser-failure exit codes:

- suspend/pause that account's discovery schedule according to explicit mapping;
- require user review/reauthentication before resuming;
- do not create rapid retries.

Transient browser/network failure may follow bounded retry policy.

## 12. Tests

Create sanitized fixture pages and a local test server to cover:

- authenticated list and profile navigation;
- multiple locator candidates;
- pagination/load more/infinite-scroll abstraction;
- duplicate/known profiles;
- new profile capture committed before downstream status;
- max new/viewed/runtime/daily/consecutive-known limits;
- checkpoint/resume and incompatible checkpoint fallback;
- login redirect/authentication required;
- access denied;
- throttle;
- challenge/CAPTCHA detection;
- unexpected host;
- parser-health failure;
- navigation transient retry bound;
- account lease/profile lock;
- diagnostic redaction/size/path/retention;
- no images stored;
- stdout valid JSONL;
- account status and exit-code mapping;
- fixed-delay next occurrence after completion;
- no automatic account failover after an enforcement signal.

Live Startup School tests must be opt-in and disabled by default. Do not put credentials or real profile content in fixtures/CI artifacts.

## Constraints

- No AI evaluation yet.
- No automatic invitations or messages.
- No stealth/anti-detection packages or behavior.
- No proxy rotation or CAPTCHA solving.
- No multi-browser concurrency.
- No Azure Function/cloud browser deployment.

## Done when

- A user can manually authenticate a dedicated browser account.
- A bounded discovery run captures and persists profile snapshots sequentially.
- Limits, checkpointing, deduplication handoff, diagnostics, and session-health states work.
- Fixed-delay scheduling runs batches without internal sleep.
- Local fixtures fully test page flows and stop conditions.
- Any live verification is reported honestly.

## Required smoke validation

1. Install Playwright Chromium through the documented command.
2. Run discovery against the local fixture server through Runner.
3. Capture at least 20 fixture profiles with duplicates and a stop condition.
4. Verify candidate/snapshot/checkpoint/account status/central run metrics.
5. Run an authentication-required and challenge fixture.
6. Confirm no account failover and no orphaned browser process.
7. Optionally run `authenticate` against a real account manually; clearly state whether live profile discovery was tested.

## Final report

Include Playwright/browser version, profile storage strategy, locator/fixture approach, run limits, stop-state mapping, smoke results, and live-verification status. End with:

```text
Next prompt: prompts/11-founder-scout-processing-pipeline.md
```

