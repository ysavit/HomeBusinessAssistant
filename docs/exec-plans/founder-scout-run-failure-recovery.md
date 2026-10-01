# Founder Scout run failure recovery

## User-visible problem

The local owner attempted AI analysis and then Start. The two live analysis runs each made 50 provider requests, completed no evaluations, and reported only a generic failure. The persisted Founder Scout actions show that each run released the same candidate 50 times with `analysis.provider.throttled`. Start during an active analysis occurrence raised an unhandled concurrency exception and returned HTTP 500. An old Debug Host remained alive after logging shutdown and stopped answering loopback requests.

## Working increment

- Stop a deep-analysis batch when a provider-wide transient failure remains after its configured request retries. Preserve the released candidate and other pending work for a later explicit run.
- Translate the provider's safe failure code into a useful run summary and protocol error without exposing key or response contents.
- Handle manual-run concurrency conflicts on Start and Analyze as visible status messages.
- Add regressions for a repeated claim after throttling and for Start during active work.
- Rebuild, run the required solution validation, refresh the local package/Host if permitted, and record verified handoff state.

## Evidence and validation

- Read-only copies of local SQLite files showed two failed analysis runs on 2026-09-30. Each had 50 provider requests, zero completed evaluations, and 50 `AnalysisReleased` actions for one candidate; the persisted reason was `analysis.provider.throttled`. The temporary copies were deleted after diagnosis.
- The Host log showed a 500 on `POST /FounderScout?handler=Start` while the first analysis run was active. The manual-run service forbids overlapping Founder Scout occurrences.
- A stale Debug Host process was stopped after its log showed shutdown and it ceased answering loopback requests. Both initially failed restore-related tests passed individually once it was gone.
- Targeted regression tests, restore, Release build, full suite, and format results are recorded in `docs/PROJECT_STATE.md`.

## Limits

The application cannot raise the owner's OpenAI project quota. The owner must check usage/billing or wait for the rate limit before explicitly retrying analysis. Start remains capture-and-screen only; no automatic provider calls were added.
