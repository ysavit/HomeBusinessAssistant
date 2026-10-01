# Founder Scout candidate analysis and run visibility

## Verified starting state

The Candidates page listed ten rows per page with only a details action. Settings owned the explicit bulk AI action. `start` browsed and screened without AI; `analyze` used a generic pending queue. The latest saved Scout run captured thirteen profiles and completed, while a preceding AI run failed on an OpenAI rate or quota response. The Candidates page showed neither completed run summary nor a clear AI failure reason.

## Implementation

- Add a durable `analyze-candidate` agent command that refreshes one persisted source profile through the dedicated browser account, persists the capture, screens only the resulting snapshot, and evaluates only the selected candidate.
- Keep explicit cost confirmation, move bulk analysis from Settings to Candidates, and show phase, summary, and counts on Candidates.
- Add targeted claim support without changing generic queue behavior.
- Add regression coverage for targeted claims and page actions; run repository validation and fixture checks.

## Safety and limits

The command uses the configured source host and route validation. It stops on authentication or source enforcement signals. AI input comes from the redacted screening result. A provider quota failure leaves captured data persisted. No invitations are sent.

## Progress

Completed on 2026-09-30. Added the targeted agent command, targeted SQLite claims/queueing, request-scoped explicit reanalysis identity, Candidates actions and status, manifest 1.6.0, documentation, and regression tests. No database migration was needed.

`dotnet restore HomeBusinessAssistant.sln` was up to date. `dotnet build HomeBusinessAssistant.sln -c Release --no-restore` finished with zero warnings/errors. The final `dotnet test HomeBusinessAssistant.sln -c Release --no-build` passed 344 tests with three intentional opt-in fixture skips; the initial test pass had stale UI/manifest expectations and a competing Debug Host, and another attempt hit a transient fixture HttpListener error. `dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore` exited zero. Targeted SQLite tests, two rendered Host tests, and a full Runner/browser fixture passed; the fixture evaluated only one selected candidate twice through the fake provider. The local Founder Scout DLLs matched Release hashes, `scan-agents` accepted package 1.6.0, and the Release Host served health, Candidates, and Settings over loopback. The prior OpenAI quota/rate failure is visible on Candidates; a successful live provider evaluation remains unverified.
