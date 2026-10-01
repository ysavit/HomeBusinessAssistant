# Simple Scout console

## Purpose and user-visible outcome

One console command searches a small bounded set of Startup School profiles, saves them to the existing `founders.db`, then evaluates saved candidates with the existing OpenAI pipeline. `list` and `db-path` make storage observable without the web UI.

## Current repository state

`FounderScout.Agent` already implements `start` (authenticate, discover, persist, deterministic screen) and `analyze --phase deep`. The Host/Runner prepare a private execution-input envelope, and `FounderScoutResultsService` queues stored screened candidates. The focused Host UI currently owns the explicit AI action. The local database is under `%LOCALAPPDATA%\HomeBusinessAssistant\data\agents\founder-scout\founders.db` by default. Live OpenAI evaluation has returned a provider rate/quota response; this console cannot change the provider account state.

## Scope and non-goals

Add a small Windows console entry point with `run`, `analyze`, `list`, and `db-path`. Reuse the current agent and persistence rather than introduce another schema. Keep browser authentication manual, invitations manual, and AI failures visible. Scheduling, tray UI, and live provider success are outside this increment.

## Design and data flow

`run` → current `FounderScoutCommand start` → `founders.db` capture/screen → queue bounded screened candidates → current `FounderScoutCommand analyze --phase deep` → `founders.db` evaluation. The console uses the Runner's private execution-input file manager, deletes each envelope after invocation, and reads the existing DPAPI key when present or a process environment variable. It never prints a key or profile body. `list` uses the existing result projection.

## Milestones

- [x] Add console project and bounded command parsing.
- [x] Connect current agent, private input, existing database, and readable progress.
- [x] Add console documentation and regression coverage using temporary paths and the existing fixture-backed agent suite.
- [x] Run solution validation and update project state.
- [ ] Commit and push `simple_scout`.

## Detailed steps

Add `agents/FounderScout/FounderScout.SimpleCli` to the solution. Use the existing `RunTemporaryFileManager`, `FounderScoutCommand`, `FounderScoutDatabaseInitializer`, and `FounderScoutResultsService`. Keep configuration code-owned with a small per-run limit and explicit model selection. Add direct CLI tests for path/list/argument handling and a fixture-backed smoke if the existing test helpers allow it without live network.

## Progress

2026-10-01: inspected current agent, command envelope, repository, and local data layout. Branch `simple_scout` created from the working checkout; current changes remain intact. Added the console project, targeted stored-candidate analysis argument, documentation, and tests. The local `list --max 3` command read 94 candidates from the existing database.

## Decisions

Share the existing `founders.db` and profile directory so the console and UI see the same candidates. Use a bounded default of five new profiles per run and one AI request at a time. Do not automatically send invitations. The console will show captured profiles even when OpenAI returns a rate or quota limit.

## Validation

Executed `dotnet restore HomeBusinessAssistant.sln` (pass), `dotnet build HomeBusinessAssistant.sln -c Release --no-restore` (pass, zero warnings/errors), `dotnet test HomeBusinessAssistant.sln -c Release --no-build` (pass: 348 tests, three opt-in skips), and `dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore` (pass). Targeted console tests passed 3/3 and targeted stored-analysis argument test passed 1/1. `db-path` printed the current-user founder database path; `list --max 3` read 94 saved candidates from that database. A first full test run failed the new project's architecture allow-list and two backup/restore checks while a live Host was active; the graph was updated, the Host was stopped, the three failed tests passed on repeat, and the later full solution run passed. No live site or AI call was made by the console validation.

## Recovery and rollback

Each captured profile is committed before AI. If AI fails, saved candidates remain available for `analyze`. Private execution-input files are removed in `finally`; stale files can be handled by the existing temporary-file manager. The new project can be removed without changing the database schema.

## Remaining risks and follow-up

The existing OpenAI project's rate/quota limit is external. A live end-to-end evaluation remains owner-triggered after provider access is repaired. Future work may choose to replace or simplify the larger Host UI separately.
