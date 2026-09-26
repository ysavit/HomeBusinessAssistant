# Stage 08 — Agent Management Web UI and Orchestration Controls ExecPlan

## Purpose and user-visible outcome

Turn the Stage 07 loopback dashboard into the usable local control plane for installed agents. The Host will provide real persisted agent, configuration, schedule, occurrence, run, artifact, audit, wake, and system-health views plus antiforgery-protected orchestration controls. A user can edit typed Wake Remote and Founder Scout baseline configuration, preview and save schedules, create manual Runner work, monitor/cancel/retry runs, inspect redacted audit data, and manage harmless wake/readiness actions without launching an agent executable from a page handler.

## Current repository state

- Stage 07 is recorded as validated with 171 tests, exact-loopback Kestrel, a WinForms tray lifetime, four orchestration loops, durable manual dispatch, global pause, wake reconciliation, and a real Host-to-Runner-to-Wake-Remote smoke.
- Direct Stage 08 baseline inspection found the persisted entities and scheduling/runner/wake application ports required by the management UI. Existing repositories do not expose the paged/filterable projections needed by pages, so Stage 08 will add a focused query service rather than generic repositories.
- The current Host UI has only a dashboard and placeholder section page. Its visual identity already uses a warm stone canvas, pale surfaces, forest green, rust attention, Georgia headings, and system UI text.
- A serial Release build passed with zero warnings/errors after shutting down accumulated build servers. Three EndToEnd tests passed. The Host test baseline was 8/10: two web startup tests attempted to create the default Windows Event Log source and were denied. This is an inherited composition defect because `HostApplication.Build` clears providers only when a production provider is supplied; Stage 08 will clear default providers unconditionally and retain the explicit production Serilog provider.
- Wake Remote already supplies typed configuration validation/defaults. Founder Scout Application is a placeholder and needs the baseline configuration adapter specified in `docs/configuration-examples.md`.
- Existing SQLite tables can represent Stage 08 state. No migration is currently planned.

## Scope and non-goals

Implement real dashboard/navigation, agent list/detail and enable controls, typed configuration editing/history/redacted diff, secret status/set/delete, schedule list/create/edit/detail/preview/control/run-now, manual run creation, paged run list/detail/cancel/retry, safe artifact downloads, paged audit list/detail, Wake Remote diagnostics/test-wake/manual keep-awake controls, system settings/health, and a bounded diagnostics export.

Defer Founder Scout candidate-domain pages, live browser/AI workflows, external binding/login, SPA frameworks, SignalR, automatic invitation sending, machine sleep, installer/backup execution, and Stage 13 results UI.

## Design and data flow

```text
loopback Razor Page GET
  -> IManagementQueryService -> EF AsNoTracking bounded projections -> encoded HTML

loopback Razor Page POST + antiforgery
  -> typed Application management use case
       -> validate installed/enabled/config/schedule/concurrency policy
       -> focused repository transaction / immutable revision / audit
       -> reconcile durable occurrences and managed wake task
       -> Runner dispatcher for ready manual occurrence
  -> Post/Redirect/Get flash message

artifact/diagnostics download
  -> persisted metadata + root-confined store / bounded redacted archive
  -> attachment response, never an arbitrary filesystem path
```

Page models depend on Application-facing management interfaces and small Host desktop interfaces. Infrastructure owns EF queries, artifact streams, diagnostics packaging, and DPAPI-backed secret storage. Page handlers never deserialize executable arguments or start agents directly. Typed form adapters create versioned JSON. Configuration saves include the displayed current revision number/hash and fail on a stale edit. Secret mutation is separate and never re-renders or reads the secret.

The design reference is a native UI rendered in HTML/CSS, not a shipped raster: warm stone `#f4f1ea`, pale surface `#fffdf8`, ink `#17231f`, forest `#0f6b55`, rust `#a7482d`; modest 4–8px radii, one-pixel dividers, minimal shadow, Georgia content headings, and Segoe/system chrome. Desktop uses open table-first work areas and a compact status rail; narrow layouts stack rails/forms and allow table scrolling.

## Milestones

1. Correct the Host logging-provider baseline, add management contracts/read models, typed built-in configuration adapters, and optimistic configuration saves.
2. Add Infrastructure paged projections, safe artifact/diagnostics access, and compose management services in Host runtime.
3. Replace placeholder navigation with real dashboard, Agents, Schedules, Runs, Audit, Wake & Remote, and Settings Razor Pages plus reusable partials/styles.
4. Implement validated POST/Redirect/Get controls for configuration, secrets, schedules, manual runs, run cancellation/retry, global pause, wake, keep-awake, and reconciliation.
5. Add application/integration/security/UI tests, run browser fidelity/accessibility smoke, and finish documentation/state handoff.

## Detailed steps

- Add `Management` contracts/records/services to Application; extend only focused repository ports where a write use case requires it.
- Add Founder Scout baseline configuration record, validator, and safe default provider to FounderScout.Application, referencing the platform Application boundary.
- Add an EF-backed management query/export service to Infrastructure with server-side bounds, stable ordering, and redacted JSON.
- Add management composition to `HostWebComposition` and `HostRuntime`, including Windows CurrentUser secret storage, wake tests/diagnostics, artifacts, schedule calculator/control/reconciliation, manual dispatch, and keep-awake.
- Build route folders and PageModels under `HomeBusinessAssistant.Host/Pages`, local CSS/minimal status polling JavaScript, confirmation components, validation summaries, empty states, and safe flash messages.
- Add Host/Application/Infrastructure tests using temporary SQLite, safe fakes, real antiforgery, hostile encoded output, and path-confined artifact streams.
- Use the in-app browser for the final dashboard/config/schedule/run page smoke; compare the rendered result to the accepted concept on layout, hierarchy, typography, palette, responsive behavior, and states.

## Progress

- [x] 2026-08-30: Read repository instructions, canonical state, plan rules, architecture/ADR context, Stage 08 prompt, existing Host/persistence/scheduling/runner/wake code, and tests.
- [x] 2026-08-30: Reconciled the Stage 07 baseline; serial Release build and EndToEnd tests passed, while two Host tests exposed the default Windows Event Log provider defect recorded above.
- [x] 2026-08-30: Generated and accepted dashboard, configuration, schedule, and run-detail concept references and extracted the native design system.
- [x] 2026-08-30: Added management contracts/services, atomic displayed-revision/hash checks, Founder Scout baseline configuration/default validation, and existing-service orchestration.
- [x] 2026-08-30: Added bounded Infrastructure projections and composed DPAPI secrets, artifacts, wake/power diagnostics, schedule reconciliation, manual dispatch, and management queries in Host.
- [x] 2026-08-30: Implemented the dashboard, Agents/configuration history, Schedules, Runs, Audit, Wake & Remote, and Settings Razor Pages with local CSS/minimal polling JavaScript.
- [x] 2026-08-30: Added persistence/configuration/Host integration and hostile-output regressions; completed real local browser desktop/narrow navigation, labeling, overflow, console, secret-text, and schedule-preview checks.
- [x] 2026-08-30: Completed standard validation, migration drift verification, ADR/architecture/development/README updates, and canonical project-state handoff.

## Decisions

- Use one purpose-built management query service for bounded cross-table read models. Existing focused repositories remain the write/use-case boundary and no generic repository is introduced.
- Preserve immutable configuration JSON as the durable contract while forms use typed adapters. The UI will not expose a raw configuration JSON editor.
- Keep secret actions independent from configuration POSTs; pages can query only existence/metadata and cannot call secret retrieval.
- Treat schedule removal as disable/pause in V1 when durable occurrence/run history may exist. No history-destructive delete is exposed.
- Keep page handlers thin and invoke the same scheduler, Runner-dispatch, wake, and power abstractions used by the tray/runtime.
- Clear ASP.NET default log providers for every Host build. Production adds the existing redacting Serilog provider; tests no longer touch Windows Event Log.

## Validation

Required final commands:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/HomeBusinessAssistant.Infrastructure --startup-project src/HomeBusinessAssistant.Infrastructure --configuration Release --no-build
```

Also validate real antiforgery behavior, configuration valid/invalid/no-op/stale edits, secret non-rendering, schedule preview/save/reconcile, durable manual launch, run filters/detail/cancel/retry, hostile text encoding, confined artifact download, audit filters/redaction, harmless wake test/reconcile, keep-awake acquire/release, diagnostics exclusions, navigation, narrow layout, and active-run polling. Actual results will be recorded here when run.

Actual Stage 08 results on 2026-08-30:

- `dotnet restore` passed for all 25 projects.
- Final exact Release build passed with zero warnings/errors.
- The first full test run preserved one architecture allow-list failure after the intentional Host/Founder Scout Application reference additions. Updating the exact graph assertion superseded it; the final run passed 175 tests with zero failures/skips.
- The first format verification reported one `HostRuntime.cs` import-order finding. The import order was corrected; the final verification exited 0 with no findings.
- EF reported no pending central model changes. Stage 08 added no migration; `AddWindowsWakeAndPowerMetadata` remains current.
- Real ephemeral Kestrel integration passed page/navigation, antiforgery 400, DPAPI secret non-rendering, diagnostics ZIP exclusions, CSP, and hostile run-output encoding checks.
- In-app Browser smoke loaded all real management routes with the production stylesheet, found no unlabeled editable controls or console warnings/errors, exercised the real five-occurrence schedule preview, and found no document overflow at desktop or 720-pixel width. The accepted concept and final desktop/narrow screenshots were visually compared; temporary browser-host data was removed.
- Machine-global wake registration, sleep, and destructive production-data flows were deliberately not exercised. The existing fake/native-boundary tests cover them; the opt-in tray workflow remains documented.

## Recovery and rollback

No schema migration is planned. Every mutation uses existing immutable/audited records or optimistic concurrency, and unsuccessful validation produces no side effects. Failed schedule reconciliation leaves the saved durable schedule visible with an attention result and the periodic Host loops retry. Failed Runner launch follows the existing occurrence failure transition. Secret writes use the DPAPI store's atomic replacement/cleanup. Diagnostics archives are created under a controlled temporary/output root and deleted on failure.

## Remaining risks and follow-up

Native tray dialog appearance, real Task Scheduler registration, sleep/wake, and provider readiness depend on an interactive Windows user session and remain opt-in manual checks. Founder Scout candidate discovery, profiles, scoring, and invitation drafts begin in Stage 09 and receive UI in Stage 13. Backup execution and installer/operations work remain Stage 15.
