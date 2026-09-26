# Stage 10 ExecPlan — Founder Scout Browser Discovery

## Purpose and user-visible outcome

Stage 10 turns Founder Scout's deferred browser commands into a bounded, independently executable acquisition workflow. A user can explicitly install the pinned Chromium runtime, authenticate one dedicated account in a headed persistent profile, diagnose browser/account health, discover sequential profile pages through a versioned Startup School adapter, and resume from durable checkpoints. Captures are committed to `founders.db` before later analysis. Authentication, access-denial, throttling, challenge, unexpected-host, parser-health, lock, cancellation, and bounded navigation failures stop the current account without automatic account failover.

## Verified starting state

- The Stage 09 Release solution builds with 0 warnings and 0 errors.
- Founder Scout owns a separate migrated `founders.db`, focused account/segment/checkpoint/candidate/snapshot repositories, and artifact-before-snapshot fixture imports.
- `discover` and `authenticate` intentionally return deferred results. `diagnose` checks only configuration, directories, SQLite, and account counts.
- The Runner already supplies a root-confined per-agent data directory and per-run artifact staging directory and enforces manifest concurrency and timeouts.
- The schema 1.0 discovery limits already include browser concurrency one, per-run/new/day/runtime/consecutive-known limits, deterministic cooldown, enforcement stops, and forbidden failover.
- No Playwright package, installed browser contract, persistent browser session manager, source adapter, browser diagnostic capture, or browser-specific tests exist.

## Scope and non-goals

### In scope

- Centrally pinned `Microsoft.Playwright` 1.62.0 in Founder Scout Infrastructure and an explicit Chromium installation/verification workflow.
- Application-owned browser/session/discovery/extraction/challenge/diagnostic contracts and a versioned `StartupSchoolSourceOptions` model.
- Root-confined persistent profiles under `<runner-agent-data>/browser/<account-id>` with an exclusive cross-process account lock.
- Real `authenticate`, `discover`, `record-fixture`, and account-aware `diagnose` command behavior.
- Sequential list/profile navigation, bounded pagination/load-more/infinite-scroll abstraction, semantic locator candidates, extraction completeness, safe page fingerprints, durable commit-before-analysis, checkpoint/resume, and deterministic spacing.
- Immediate safe-stop mappings and bounded redacted run diagnostics with 14-day retention metadata reported through the agent protocol.
- A recommended fixed-delay schedule policy with ten-minute cooldown, `Forbid` concurrency, configurable wake policy, and a timeout margin.
- Sanitized local HTML fixtures and normal-test coverage that never contacts Startup School or any live service.

### Non-goals

- Production normalization, protected-attribute redaction pipeline, screening, or analysis queue processing (Stage 11).
- AI evaluation, arithmetic scoring, or invitation drafting (Stage 12).
- Founder Scout candidate/account management web pages (Stage 13).
- CAPTCHA solving, stealth behavior, fingerprint spoofing, proxy rotation, enforcement bypass, automatic account failover, password collection, or invitation sending.
- Automatic Playwright browser downloads during ordinary agent execution.

## Architecture and data flow

```text
FounderScout.exe discover --account <id> [--segment <id>]
        |
        +--> validate typed config, account, segment, and canonical profile root
        +--> acquire exclusive profile/account lease
        +--> launch one persistent Playwright context
        +--> verify expected host and authenticated semantic signal
        +--> resume compatible checkpoint or start segment entry URL
        +--> enumerate profile links sequentially
        +--> visit -> classify stop state -> extract bounded envelope
        +--> raw content-addressed artifact
        +--> short founders.db candidate/snapshot transaction
        +--> append checkpoint and emit bounded progress/metrics
        +--> close context and release lease in finally
```

The application layer owns all contracts and orchestration outcomes. Infrastructure is the only Founder Scout project that references Playwright and implements browser lifetime, source navigation/extraction, challenge detection, lock files, and sanitized diagnostics. `founders.db` remains the source of account/segment/checkpoint/capture state; `assistant.db` remains the source of central schedule/run/protocol/audit state. Browser activity never runs inside a database transaction.

Browser profiles intentionally persist the site's ordinary authenticated browser session and are treated as sensitive local state. They never enter configuration JSON, database blobs, logs, protocol summaries, diagnostic payloads, fixtures, documentation, or backups. The agent accepts no password or MFA secret input.

## Milestones

1. Add typed source/browser options, browser contracts, capture commit service, discovery outcomes, and schedule policy.
2. Add Playwright runtime/session/source/extraction/challenge/diagnostic adapters with root confinement and exclusive account locks.
3. Compose real browser commands and protocol state/exit mappings in `FounderScout.exe`.
4. Add sanitized local fixture server/assets and deterministic unit/integration/Runner smoke coverage.
5. Install/verify pinned Chromium explicitly, run all repository and migration gates, and update architecture/configuration/development/project-state documentation.

## Implementation decisions

- Use the Playwright-managed Chromium revision by default. An allow-listed configured Chrome channel is supported only after explicit local validation. The default Windows Chrome profile is never used.
- Keep source options additive to schema 1.0 and provide safe defaults when an older Stage 09 revision lacks them. Saving a current configuration materializes the complete source options.
- Resolve an account profile only as `browser/<validated-account-id>` beneath the Runner-assigned data root. Existing account rows with a different path fail closed.
- An explicit first `authenticate --account <id>` may provision that account record and its canonical profile path; discovery never silently provisions or switches accounts.
- Use a file handle opened with `FileShare.None` as the cross-process browser-profile lease. A second command receives a distinct profile-lock stop state.
- Sanitize page HTML in the browser DOM before it crosses into .NET, then apply a second bounded redaction pass. Diagnostic console output contains counts/types only, not raw message text.
- Treat source adapter/checkpoint version mismatches or invalid continuation state as a safe restart from the segment entry point. Checkpoints never contain cookies, storage state, headers, form values, or tokens.
- Count every viewed profile and every newly created snapshot. The daily cap is based on distinct account captures since the current UTC day boundary, which is conservative for changed known profiles.
- Use only a tiny bounded transient-navigation retry. Enforcement signals and parser failures are never retried and never trigger another account.
- Do not force sleep or keep the browser process alive after the command finishes. Runner timeout/cancellation remains authoritative outside the command's own maximum runtime.

## Progress

- [x] 2026-08-30: Read repository instructions, canonical project state, execution-plan rules, architecture, ADR-0001/0002/0003/0007, configuration examples, acceptance matrix, Stage 09 handoff, Stage 10 prompt, and relevant existing implementation/tests.
- [x] 2026-08-30: Verified the inherited Release solution builds with 0 warnings and 0 errors.
- [x] 2026-08-30: Added application contracts, source options, capture commit reuse, discovery policy, and the fixed-delay schedule recommendation.
- [x] 2026-08-30: Added Playwright infrastructure, root-confined persistent profiles, source adapter, extraction, stop detection, and bounded diagnostics.
- [x] 2026-08-30: Composed real `authenticate`, `discover`, account-aware `diagnose`, and `record-fixture` commands; upgraded the manifest to 1.2.0.
- [x] 2026-08-30: Added loopback fixtures and real browser/Runner coverage for 20 captures, repeat deduplication, resume, enforcement stops, profile locking, cleanup, and diagnostic redaction.
- [x] 2026-08-30: Installed and listed the pinned Chromium revision, passed the 216-test solution plus both migration-drift and format gates, audited runtime artifacts, and completed the Stage 10 handoff documentation.

## Validation plan

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
pwsh agents/FounderScout/FounderScout.Agent/bin/Release/net10.0-windows/playwright.ps1 install chromium
pwsh agents/FounderScout/FounderScout.Agent/bin/Release/net10.0-windows/playwright.ps1 install --list
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
dotnet tool run dotnet-ef migrations has-pending-model-changes --project agents/FounderScout/FounderScout.Infrastructure --startup-project agents/FounderScout/FounderScout.Infrastructure --configuration Release --no-build
```

The browser smoke will run only against a loopback fixture server and will prove at least 20 fixture profiles, duplicates, durable snapshots/checkpoints, account health, central Runner metrics, authentication-required and challenge stops, one-account/no-failover behavior, and no orphan browser/profile lease.

## Validation outcome

- `dotnet restore`: passed; all projects were up to date.
- Release build: passed with 0 warnings and 0 errors.
- Playwright: package 1.62.0; Chromium revision 1234 / Chrome for Testing 151.0.7922.34 installed and listed explicitly.
- Solution tests: 216 passed, 0 failed, 0 skipped. Founder Scout contributed 44 tests.
- Formatting: an initial verification reported import ordering in two new fixture files; `dotnet format` corrected only those imports and the final verification passed with no findings.
- Central and Founder Scout EF model-drift checks: both reported no changes since their current migrations; Stage 10 adds no database migration.
- Required local Runner smoke: passed with 20 durable candidates/snapshots, repeat-run known-profile stop, checkpoints, central metrics, healthy account state, authentication/challenge stop mappings, five sanitized diagnostics per enforcement stop, no failover, and a reacquirable profile lock.
- Sensitive-artifact audit: no database, log, browser, image, or HTML runtime artifact remained in the repository outside ignored build/tool caches.
- Live Startup School authentication/discovery was not performed and is not claimed.

## Recovery and rollback

- A missing or incompatible browser binary fails before navigation with an actionable installation diagnostic; it is never downloaded implicitly.
- Context creation, navigation, extraction, persistence, cancellation, and diagnostic failures all close the browser context and release the account lease in `finally`.
- A process crash can leave the lock marker file, but not the exclusive handle. The next diagnostic/open validates and reuses the marker safely.
- Raw-artifact-before-snapshot and idempotent candidate/snapshot identities preserve Stage 09 recovery semantics if central Runner finalization fails.
- Incompatible/corrupt checkpoints are retained as history and ignored for resume; no destructive rollback of candidate evidence occurs.

## Remaining risks and follow-up

- Startup School HTML and authenticated flows can change independently of this repository; locator reports and the versioned adapter make parser drift observable, but live validation remains opt-in.
- Browser profiles are sensitive, user-bound operational state and require the same Windows user/session. Backup/restore of these directories is intentionally excluded.
- Stage 11 must consume the grounded capture envelopes for normalization, protected-attribute removal, deterministic screening, and analysis queue processing.
- Stage 13 must provide user-friendly account/segment management and surfaced stop-state remediation controls.
