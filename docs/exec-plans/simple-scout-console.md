# Simple Scout console

## Purpose and user-visible outcome

One console command searches a small bounded set of Startup School profiles, saves them to the configured `founders.db`, then evaluates saved candidates with the existing OpenAI pipeline. `list` makes storage observable without the web UI.

## Current repository state

`FounderScout.Agent` already implements `start` (authenticate, discover, persist, deterministic screen) and `analyze --phase deep`. The Host/Runner prepare a private execution-input envelope, and `FounderScoutResultsService` queues stored screened candidates. The focused Host UI currently owns the explicit AI action. The local database is under `%LOCALAPPDATA%\HomeBusinessAssistant\data\agents\founder-scout\founders.db` by default. Live OpenAI evaluation has returned a provider rate/quota response; this console cannot change the provider account state.

## Scope and non-goals

Keep a small Windows console entry point with only `run` and `list`. Reuse the current agent and persistence rather than introduce another schema. Keep browser authentication manual, invitations manual, and AI failures visible. Scheduling, tray UI, and live provider success are outside this increment.

## Design and data flow

`run` → current `FounderScoutCommand start` → `founders.db` capture/screen → queue bounded screened candidates → current `FounderScoutCommand analyze --phase deep` → `founders.db` evaluation. The console uses the Runner's private execution-input file manager, deletes each envelope after invocation, and reads the API key from a local, ignored `appsettings.json`. It never prints a key or profile body. `list` uses the existing result projection.

## Milestones

- [x] Add console project and bounded command parsing.
- [x] Connect current agent, private input, existing database, and readable progress.
- [x] Add console documentation and regression coverage using temporary paths and the existing fixture-backed agent suite.
- [x] Run solution validation and update project state.
- [x] Commit and push `simple_scout`.

## Detailed steps

The console remains in the solution and uses the existing `RunTemporaryFileManager`, `FounderScoutCommand`, `FounderScoutDatabaseInitializer`, and `FounderScoutResultsService`. Settings live in a local JSON file with an absolute database path, a maximum of 50 new candidates per run, and a bounded daily limit. The committed example omits the key; the filled-in file is ignored. Direct CLI tests cover config validation, list, and command restriction.

## Progress

2026-10-01: inspected current agent, command envelope, repository, and local data layout. Branch `simple_scout` created from the working checkout; current changes remain intact. Added the console project, targeted stored-candidate analysis argument, documentation, and tests. The local `list --max 3` command read 94 candidates from the existing database.

2026-10-01: the requested source, tests, and prior Founder Scout working-tree changes were committed as `aaca25e` and pushed to `origin/simple_scout`. This plan/status handoff is recorded in a follow-up documentation commit on the same branch.

2026-10-01: user narrowed the console to `run` and `list` and moved the DB path, API key, model, delay, and limits to local `appsettings.json`. The local file is ignored by Git; `appsettings.example.json` documents the shape. Direct AppData listing revealed that database initialization requires writes; `list` now uses a read-only SQLite connection. Build, full tests, focused tests, and format passed; live search/AI was not invoked.

2026-10-01: user supplied a durable CTO/co-founder evaluation brief. It was added as the code-owned default persona context, copied into the checked-in console example and the ignored local config, and kept out of this plan and project-state documentation. The existing Host configuration revision has separate saved context and is not overwritten by this console edit.

2026-10-01: owner requested up to 50 saved profiles and continued capture despite AI failure. The console bound was raised to 50 and the daily cap exposed in local settings. The first live 50-limit run hit the old daily cap after one capture; a second run with daily cap 150 saved and screened 12 more, reaching 112 total, then stopped at `discovery.noProgress` after two batches with no unseen links. AI received an authentication/model-access error; 52 candidates remained pending. The console now treats expected AI failure as incomplete analysis after successful capture and surfaces the discovery reason in readable output. Focused console tests passed 8/8. No immediate third browse pass was made against the same no-progress route.

## Decisions

Share the existing `founders.db` and profile directory when `DatabasePath` points to the Host's DB, so the console and UI see the same candidates. Enforce a ceiling of 50 new profiles per run, a bounded daily cap, and one AI request at a time. Do not automatically send invitations. The console will show captured profiles even when OpenAI returns a provider error. The requested plain-text key exists only in the local ignored config, never in a commit or SQLite.

## Validation

Executed `dotnet restore HomeBusinessAssistant.sln` (pass), `dotnet build HomeBusinessAssistant.sln -c Release --no-restore` (pass, zero warnings/errors), `dotnet test HomeBusinessAssistant.sln -c Release --no-build` (pass: 348 tests, three opt-in skips), and `dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore` (pass). Targeted console tests passed 3/3 and targeted stored-analysis argument test passed 1/1. `db-path` printed the current-user founder database path; `list --max 3` read 94 saved candidates from that database. A first full test run failed the new project's architecture allow-list and two backup/restore checks while a live Host was active; the graph was updated, the Host was stopped, the three failed tests passed on repeat, and the later full solution run passed. No live site or AI call was made by the console validation.

For the configuration revision, `dotnet restore HomeBusinessAssistant.sln` passed; final `dotnet build HomeBusinessAssistant.sln -c Release --no-restore` passed with zero warnings/errors; final `dotnet test HomeBusinessAssistant.sln -c Release --no-build` passed 353 tests with three opt-in skips; `dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore` passed; focused console tests passed 8/8. `dotnet run ... -- run` stopped before database/browser work because the local key is blank, as intended. A direct `list` against the current-user AppData DB failed with SQLite Error 14 under the restricted workspace; the new regression lists a real migrated temporary SQLite DB successfully. No live browser or provider call was made.

For the 50-profile revision, `dotnet restore HomeBusinessAssistant.sln` passed; `dotnet build HomeBusinessAssistant.sln -c Release --no-restore` passed with zero warnings/errors; `dotnet test HomeBusinessAssistant.sln -c Release --no-build` passed with zero failures and three opt-in skips; `dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore` passed; and focused console tests passed 8/8. Two authorized live runs saved one and then twelve additional candidates. The second exited successfully after capture despite an AI provider authentication/model-access error. Discovery stopped at `discovery.noProgress`, not at the configured 50-profile maximum.

## Recovery and rollback

Each captured profile is committed before AI. If AI fails, saved candidates remain in SQLite for explicit analysis through the existing candidate controls; a later console `run` can continue capture. Private execution-input files are removed in `finally`; stale files can be handled by the existing temporary-file manager. The new project can be removed without changing the database schema.

## Remaining risks and follow-up

The existing OpenAI project's rate/quota limit is external. A live end-to-end evaluation remains owner-triggered after provider access is repaired. Future work may choose to replace or simplify the larger Host UI separately.
