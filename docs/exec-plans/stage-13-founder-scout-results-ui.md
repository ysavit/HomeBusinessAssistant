# Stage 13 ExecPlan — Founder Scout Results UI

## Purpose and user-visible outcome

Stage 13 turns persisted Founder Scout captures, evaluations, and drafts into a complete local review workflow. The Host gains a Founder Scout dashboard, stable server-paged candidate results, evidence-rich detail/history, explicit primary and reserve invitation queues, immutable user draft revisions, manual outcomes, browser account/segment controls, report history, and raw-profile retention. Agent-backed discovery, analysis, authentication, diagnostics, and report generation continue to run only through durable occurrences and the central Runner. No route, command, or adapter sends an invitation.

## Current repository state

- The verified Stage 12 solution builds in Release with 0 warnings and 0 errors before Stage 13 edits.
- `founders.db` currently has 15 tables through `20260830231727_AddFounderScoutProcessingPipeline`. It already stores candidates, immutable snapshots, structured evaluations/categories/risks, generated drafts, append-only actions, manual invitation windows, and report metadata.
- Existing ranked and invitation queries expose only status/minimum-priority paging and split `QueuedForInvite` candidates by rank. They do not support the Stage 13 filter surface, explicit queue membership/order/history, or raw-text-free list projections.
- Generated drafts can transition through review/manual-send states but have no immutable human-edit revision record.
- Report generation is still the Stage 09 two-file count placeholder. `FounderScout report` has no type/top options or lease.
- The Host references Founder Scout Application for typed configuration only. It has no Founder Scout Infrastructure composition or result pages.
- The accepted Stage 13 visual references are the generated 1536x1024 Founder Scout dashboard/list and corrected candidate-detail concepts. They preserve the existing parchment/ink/forest design language, table-first density, quiet score rails, and explicit “manual invitation only” boundary.

## Scope and non-goals

In scope are bounded result projections; stable filters/sort/pagination; candidate evidence/history/diffs; explicit queue entries and windows; draft edit revisions and deterministic validation; manual outcome transitions; browser account/segment read and safe mutation controls; consistent HTML/Markdown/CSV/JSON reports; report leasing; report CLI options; artifact metadata; raw snapshot retention; Razor Pages; antiforgery; browser smoke; and performance/security tests.

Out of scope are automatic invitations/messages, LinkedIn/email enrichment, protected-attribute collection, raw captured HTML rendering, profile photographs, CAPTCHA/stealth behavior, automatic reserve promotion, scorecard self-tuning from outcomes, provenance-aware manual split, remote UI exposure, and a SPA.

## Design and data flow

```text
Razor Page GET
  -> FounderScout Application query contract
  -> Infrastructure AsNoTracking projection over founders.db
  -> bounded encoded view model (never raw capture HTML)

Razor Page POST
  -> antiforgery + typed validation
  -> FounderScout command service for local durable state
       queue / revision / outcome / account / segment / retention
  -> central IManagementCommandService for agent work
       durable occurrence -> Runner -> FounderScout.exe

FounderScout report occurrence
  -> acquire founders.db report lease
  -> one ordered report selection model
  -> HTML + Markdown + CSV + JSON + operational Markdown views
  -> atomic durable and Runner-staged writes
  -> append immutable ReportExport metadata
  -> JSONL artifact events + deterministic summary
  -> release lease in finally
```

The Host opens the Runner-authoritative Founder Scout data root at `<platform-data>/agents/founder-scout`; it does not honor the editable metadata path as an execution redirect. Central and Founder Scout databases remain separate and correlate only by run/correlation values.

## Milestones

1. Add Stage 13 domain records, application query/command contracts, entity mappings, indexes, and the additive Founder Scout migration.
2. Implement stable raw-text-free list/detail projections plus queue, draft-revision, outcome, account/segment, retention, and report-lease commands.
3. Replace placeholder reports with one selected candidate set and all required formats; complete the report CLI and protocol artifacts.
4. Compose the Founder Scout database/services in Host and add dashboard, candidate list/detail, invitation queue, accounts/segments, reports, and retention Razor Pages.
5. Add focused relational, performance, report, XSS/encoding, antiforgery, no-send-surface, and Host integration tests.
6. Run migration drift, restore/build/test/format, CLI/report smoke, and in-app Browser desktop/mobile workflow and fidelity checks; update this plan and `docs/PROJECT_STATE.md`.

## Detailed steps

- Extend `FounderScout.Domain` with queue-entry, draft-revision, lease, raw-artifact deletion, and action-state records/enums while preserving generated draft immutability.
- Add `FounderScoutResultsContracts.cs` and focused services in Application. Keep view projections bounded and independent of EF types.
- Extend Infrastructure entities/configurations/context/repository with `InvitationQueueEntries`, `InvitationDraftRevisions`, and `FounderScoutLeases`, plus retention markers and covering indexes for the default rank/filter order.
- Generate and apply one additive migration. Existing generated drafts, windows, candidates, and reports remain valid; no destructive backfill is required.
- Implement safe report rendering and path-confined retention adapters. CSV prefixes formula-leading cells, HTML encodes every value, Markdown escapes structural characters, and JSON has an explicit schema version.
- Extend `FounderScoutCommand` parsing for `report --type all|top-candidates|invitation-queue [--top 1..1000]` without accepting a send command.
- Add a Founder Scout composition to Host and thin Razor Page models using Post/Redirect/Get. Every agent action uses `IManagementCommandService.RunNowAsync`; local state mutations stay in the Founder Scout command boundary.
- Extend the existing design tokens/CSS rather than introducing a JavaScript framework. Copy feedback is the only new clipboard JavaScript and never persists clipboard contents.

## Progress

- [x] 2026-08-31: Read repository guidance, canonical state, planning rules, architecture, ADR-0006 through ADR-0010, acceptance matrix, Stage 13 prompt, current Founder Scout persistence/report paths, Host composition/pages/tests, and frontend/browser skill guidance.
- [x] 2026-08-31: Verified the inherited Release solution builds with 0 warnings and 0 errors.
- [x] 2026-08-31: Generated and corrected coordinated 1536x1024 dashboard/list and candidate-detail visual references.
- [x] 2026-08-31: Added the results contracts/service, explicit invitation queue/revision/lease persistence, retention metadata, action/outcome states, indexes, and `20260831163034_AddFounderScoutResultsWorkflow` migration.
- [x] 2026-08-31: Replaced the placeholder reports with one canonical selection model, six safe atomic formats, durable metadata, a fenced report lease, and selective `report --type ... --top ...` protocol output.
- [x] 2026-08-31: Added the complete Razor Pages workflow for overview, candidate list/detail, manual queue, accounts/segments/operations, reports, outcomes, draft revisions, and raw retention, with responsive styling and small clipboard enhancement.
- [x] 2026-08-31: Added focused relational/performance/report/security/UI tests, completed interactive desktop/mobile Browser QA, and completed the full validation and documentation handoff.

## Decisions

- Explicit `InvitationQueueEntry` rows are window-scoped and position-bearing. They preserve queue history, enforce one active candidate entry per window, and make reserve promotion an explicit mutation instead of a derived rank side effect.
- Generated `InvitationDraft` rows remain immutable content. A human edit creates an immutable `InvitationDraftRevision`; one selected revision is active for copy/review, and deterministic validation is rerun on every edit.
- Manual outcomes use legal candidate transitions plus structured reason codes in append-only actions. They never change scoring weights automatically.
- List projections exclude `NormalizedProfileJson`, `StructuredEvaluationJson`, raw paths, and draft bodies. Detail and report queries select only bounded fields required by those surfaces.
- Raw retention clears the persisted raw-artifact path only after a root-confined delete succeeds and records deletion time/reason. Derived snapshots, normalized data, evaluations, drafts, actions, and scores remain.
- A small SQLite lease table provides cross-process report serialization. Atomic temporary-file replacement remains the final filesystem defense.
- The visual implementation follows the generated table-first concepts but uses the repository’s real navigation and data vocabulary. Synthetic concept numbers/names are demonstration content only and are never seeded as production data.

## Validation

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
dotnet tool run dotnet-ef migrations list --project agents/FounderScout/FounderScout.Infrastructure --startup-project agents/FounderScout/FounderScout.Infrastructure --configuration Release --no-build
dotnet tool run dotnet-ef migrations has-pending-model-changes --project agents/FounderScout/FounderScout.Infrastructure --startup-project agents/FounderScout/FounderScout.Infrastructure --configuration Release --no-build
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/HomeBusinessAssistant.Infrastructure --startup-project src/HomeBusinessAssistant.Infrastructure --configuration Release --no-build
```

Final results on 2026-08-31: restore passed; Release build passed with 0 warnings and 0 errors; 257 tests passed across all 10 test projects with 0 failures and the gated interactive Browser fixture skipped in the ordinary suite; exact format verification exited 0. Both central and Founder Scout drift checks reported no changes. A fresh temporary `founders.db` successfully applied all three migrations through `AddFounderScoutResultsWorkflow` and was removed. Selective report command tests passed for `top-candidates --top 30` (four artifacts) and `invitation-queue` (one artifact), while the Runner smoke produced all six artifacts.

The first full Stage 13 test run exposed two stale assertions rather than product failures: the allowed Host dependency graph did not yet include the new composition-root reference to Founder Scout Infrastructure, and the Stage 11 Runner smoke still expected two placeholder reports. Both expectations were updated to the Stage 13 contract; the final full suite passed. The first format verification found only two long-expression wrapping locations in `FounderScoutResultsService`; the repository formatter corrected them and the final verification passed.

Stage-specific validation used temporary migrated databases and synthetic profiles only. It exercised every report format from the same row order, 10,000-row pagination, hostile output encoding, CSV formula safety, queue/draft/outcome transitions, retention, report lease contention, Runner report artifacts, antiforgery, and an in-app Browser desktop/mobile workflow. The Browser pass loaded production CSS on loopback, confirmed stable candidate filtering, evidence/history, primary/reserve queue content, an explicit no-send boundary, responsive layout, and zero browser warnings/errors; its gated fixture passed 1/1 and cleaned its temporary data. No live Startup School, model provider, external send route, or real candidate data was used.

## Outcomes

- Founder Scout now has an end-to-end local decision-support surface from persisted capture/evaluation data through evidence review, immutable draft revision, explicit queue planning, human-confirmed outcomes, reporting, and raw-artifact retention.
- All report formats share one filter/sort hash and canonical candidate ordering. The agent emits one artifact event per file and a schema `1.0` summary, and concurrent report writers are serialized by a durable fencing lease.
- The Host remains a composition root and never executes agent code inside Razor handlers. Discovery, analysis, authentication, diagnostics, and report generation create durable central occurrences for Runner; queue, review, outcome, and retention actions are local Founder Scout transactions.
- `ADR-0011-founder-scout-results-review-and-report-boundary.md` records the durable UI/report/manual-send boundary.

## Recovery and rollback

- The migration is additive. Rolling application code back requires restoring a pre-migration backup only if the older binary rejects extra tables/columns; the existing candidate/evaluation data is not rewritten.
- Queue removal and rollover preserve historical entries/windows. A failed reorder transaction leaves the prior positions intact.
- Draft edits append revisions. A validation failure retains the revision as review-required and leaves the generated source unchanged.
- Report generation holds a bounded expiring lease and writes temporary files beside the target. A crash leaves the lease reclaimable and temporary files non-authoritative.
- Retention updates the snapshot only after successful deletion. File-not-found is idempotent; path violations fail closed and retain metadata for review.

## Remaining risks and follow-up

- Real browser session health and live provider output remain opt-in prerequisites; Stage 13 uses synthetic data and persisted health only.
- Outcome feedback is exportable but does not tune scorecard weights in V1.
- Manual identity split and external enrichment remain deferred.
- Stage 14 consumes the new attention, audit, outcome, and report signals for platform summaries and notifications.
