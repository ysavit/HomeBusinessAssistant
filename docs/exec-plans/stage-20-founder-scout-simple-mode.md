# Founder Scout simple mode

## Purpose and user-visible outcome

Make the existing Founder Scout implementation immediately usable without the multi-step onboarding/configuration workflow. Opening the local Host requires no browser credential negotiation, Founder Scout uses one code-owned configuration/account/segment, the agent is enabled for explicit manual runs, and its Settings page displays effective values without editable controls.

The only unavoidable setup action is manual Startup School authentication in the dedicated headed browser profile. No password, cookie, browser session, or API key is compiled into the product.

## Current repository state

- Stage 20 is validated and currently exposes a seven-step Founder Scout onboarding wizard.
- `FounderScoutDefaults` already owns safe code defaults, but only seeds a missing central configuration and does not replace an existing revision.
- Browser accounts and discovery segments are currently created by onboarding and persist in `founders.db`.
- Founder Scout starts disabled and normal manual execution requires the central agent definition to be enabled.
- `/Agents/founder-scout/configuration` is a large editable typed form.
- Host pages require Windows Negotiate authentication plus the startup owner's SID, which has proved brittle in ordinary localhost browser use.

## Scope and non-goals

Implement:

- one hardcoded capture-and-screen configuration;
- one default browser account and one default Startup School segment;
- automatic immutable-revision promotion and manual-run enablement at Host startup;
- read-only Founder Scout settings;
- a one-action Founder Scout home flow that reuses healthy authentication, authenticates when needed, saves discovery results first, and then attempts deterministic screening;
- one bounded per-run delay control for sequential profile exploration;
- loopback-only browser access without Negotiate in simple mode;
- onboarding bypass/reminder suppression for the simple flow.

Do not:

- compile secrets or browser authentication state;
- auto-submit credentials or solve challenges;
- create schedules or automatically start discovery;
- enable deep AI analysis without a separately protected API key;
- add invitation/message sending.

## Design and data flow

```text
Host startup
  -> migrate assistant.db and founders.db
  -> promote code-owned Founder Scout configuration as an immutable revision
  -> create/update startup-school-primary browser-account metadata
  -> create/update startup-school-default segment metadata
  -> enable Founder Scout for explicit manual runs

Web
  -> loopback Host/Origin validation + antiforgery
  -> read-only effective settings
  -> Start Founder Scout with a 1–60 second delay
  -> Runner -> FounderScout start
  -> reuse healthy session, or headed manual authentication on the official site
  -> capture and commit each profile
  -> best-effort deterministic screen, with no AI call
```

Runtime health, account authentication state, run counters, checkpoints, candidates, and reports remain durable mutable state. Only configuration choices are code-owned.

## Milestones

- [x] Add code-owned simple-mode settings and idempotent startup initializer.
- [x] Compose the initializer into Host startup.
- [x] Replace Founder Scout editing/setup UI with read-only settings and direct actions.
- [x] Remove Negotiate/browser onboarding friction while retaining exact loopback, Host/Origin, CSP, and antiforgery protections.
- [x] Add regression coverage and validate the full solution.
- [x] Update `docs/PROJECT_STATE.md` with verified results.

## Detailed steps

1. Refactor `FounderScoutDefaults` to expose the typed hardcoded configuration and set the default operational mode to capture plus deterministic screening with deep AI disabled.
2. Add a focused initializer that promotes the exact code document through `IAgentConfigurationService`, preserves runtime account/session/counter state, ensures the canonical account/segment metadata, and enables the agent without creating work.
3. Call the initializer after both databases are ready in `HostRuntime`.
4. Make the Founder Scout configuration route display-only and reject crafted save/secret mutations for that agent.
5. Simplify Founder Scout to Overview and Candidates, with one Start action and no links into the other platform workflows.
6. Disable the Host fallback Negotiate policy in simple mode. Preserve exact `127.0.0.1` binding/Host validation, unsafe-Origin validation, CSP, antiforgery, and no CORS.
7. Skip the first-run onboarding redirect/banner while simple mode is active.

## Decisions

- Capture-and-screen is the zero-secret default. Deep analysis is deferred because an API key cannot be safely hardcoded.
- Existing browser session health and discovery counters survive startup reconciliation.
- Start reuses a healthy browser session. A missing or stale session opens headed authentication and retries discovery once.
- Capture is the success boundary: saved candidates remain visible when later screening or a separately requested AI evaluation fails.
- Start never invokes deep AI analysis, even when a provider happens to be configured.
- No schedules are created; enabling the agent permits only explicit manual actions.
- Loopback HTTP authentication is removed for usability. This accepts any process/browser running as a local machine user as inside the V1 loopback trust boundary; exact Host/Origin and antiforgery checks remain.

## Validation

Run:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Also verify:

- anonymous loopback GET renders without `401`/`403`;
- Founder Scout configuration POST cannot modify the code-owned document;
- Host startup creates/reconciles the canonical account and segment idempotently;
- settings render read-only at desktop and narrow viewport;
- Start creates one central Runner occurrence, reuses or repairs manual browser authentication, forwards the bounded delay, persists captures before screening, makes no AI request, and exposes no invitation-send route.

## Recovery and rollback

All configuration changes are immutable revisions. Removing the startup initializer stops future enforcement; an earlier configuration can then be promoted through the existing configuration service. Browser profiles and Founder Scout domain history are never deleted by simple-mode initialization.

## Remaining risks and follow-up

- Real Startup School authentication still requires the owner to complete credentials/MFA in the dedicated headed browser. The current dedicated profile is authenticated and was verified live; the application still never receives credentials.
- The official source currently redirects `/cofounder-matching/candidate/next` to a stable nested `/cofounder-matching/candidate/<opaque-id>` profile route instead of exposing the former card list. Adapter `startup-school-1.6` implements that bounded route shape, rejects navigation tabs, validates the final candidate URL, uses that URL as the profile identity when markup has no self-link, and stops after two consecutive no-progress batches.
- Current production candidate markup does not expose stable semantic profile sections. Parser `founder-profile-parser-1.1` therefore accepts only route-validated, sufficiently complete visible candidate text as an explicitly marked unstructured fallback; missing fields remain missing and the live results are sent to manual review rather than receiving invented structure.
- Deep AI evaluation remains off until a later code change selects a provider and a protected key is supplied.
- The Codex in-app browser may independently block localhost; normal Edge/Chrome is the supported browser.

## Validation record

- Restore: all 29 projects up to date.
- Release build: succeeded with 0 warnings and 0 errors.
- Full solution tests: 335 passed, 0 failed; 3 explicitly gated Host interactive fixture servers skipped.
- Founder Scout suite: 100 passed, including real child processes, loopback browser fixtures, current nested candidate-route coverage, navigation-tab rejection, and a Start proof with an enabled fake AI provider that persisted captures/screening decisions and 0 evaluations.
- Host suite: 18 passed, including desktop/narrow one-button simple-mode rendering and dispatch; 3 gated interactive fixtures skipped.
- Format verification: passed after one import-order correction.
- Runtime: anonymous dashboard, Founder Scout, and settings returned HTTP 200 on `127.0.0.1:5180`; no save/password controls rendered; package scan returned 1 valid and 0 invalid packages; real Runner diagnose completed with exit code 0.
- Database migrations: none added or changed.
- 2026-09-28 live compatibility: hardcoded system Chrome, the current dashboard URL, official YC/Startup School hosts, current sign-in-card markers, and a source-application-host requirement for authenticated markers. Live authentication still requires the owner's credential/MFA action.
- 2026-09-29 simplification checkpoint: only Overview and Candidates are visible; the single Start action dispatches `start`, performs headed authentication, then continues to discovery/screening with a validated 1–60 second per-profile delay. Release build, 329 tests, formatting, Playwright desktop/narrow rendering, dispatch capture, antiforgery, and installed-package/HTTP 200 checks passed. Live post-login discovery was pending at that checkpoint and is superseded by the repair validation below.
- 2026-09-29 production repair: an authenticated structural probe confirmed `/cofounder-matching`, one direct `/cofounder-matching/{id}` candidate, and `/cofounder-matching/candidate/next`. The installed package was hash-verified after refresh. A final real Start run completed in 16.336 seconds with exit code 0, captured two new snapshots, processed 2/2 profiles, recorded zero parser/processing failures, and conservatively placed both unstructured profiles in manual review. The Candidates page returned HTTP 200 and rendered two rows; all dedicated Runner/agent/browser processes exited. Final exact validation passed restore, zero-warning Release build, 331 tests, and format verification.
- 2026-09-29 capture-first hardening: Start now reuses a healthy session, falls back to headed authentication only when needed, retries once after stale authentication, commits discovery before best-effort screening, and never invokes AI. AI-boundary failures are safely reported by explicit analysis commands without deleting captures. The Candidates route ignores stale score/review filters and shows active saved candidates newest first. Exact validation passed restore, zero-warning Release build, 332 tests, format verification, desktop/narrow Playwright rendering, a real installed-package diagnose, and a read-only check of the local database showing 2 active candidates and 6 snapshots.
- 2026-09-29 current-route and progress repair: read-only inspection proved the two prior rows were navigation pages, so result queries now exclude them without deleting audit history. Adapter `startup-school-1.6` enters through the current next-candidate route and treats its validated nested final URL as the candidate identity when no self-link exists. Overview and Candidates expose Running now/Not running state, current heartbeat, a disabled Start button while active, and three-second active-run refresh. A final live authenticated run visibly advanced 0 → 4 → 11 → 19 → 20, stored 20 real profiles, completed deterministic screening with zero pending, and released both child processes. Final exact validation passed restore, zero-warning Release build, 334 tests, and format verification; the installed package hash matched Release output.
- 2026-09-30 direct-launch styling repair: the live page reproduced correct HTML and 20 saved candidates with raw browser defaults because `/css/site.css` and `/js/site.js` returned 404. Normal Host builds now copy `wwwroot` into the output folder, and Host composition uses the executable web root unless the caller deliberately supplies `--contentRoot`. The regression asserts CSS/JavaScript are present in direct build output. A direct Release executable launch returned 200 for both assets and rendered the intended desktop layout; the existing desktop/narrow Playwright interaction test also passed. Final exact validation passed restore, zero-warning Release build, 335 tests, and format verification.
