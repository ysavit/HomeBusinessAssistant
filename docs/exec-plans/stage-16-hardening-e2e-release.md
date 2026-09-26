# Stage 16 ExecPlan — Hardening, End-to-End Verification, and V1 Release

## Purpose and user-visible outcome

Close the verified V1 gaps without adding product scope. The resulting repository has a complete acceptance-evidence index, deterministic synthetic end-to-end and scale coverage, a source-backed security/privacy review, rendered desktop/narrow UI evidence, reconciled operator documentation, and a versioned self-contained `win-x64` release-candidate archive with independent checksums and rollback guidance.

## Current repository state

- `docs/PROJECT_STATE.md` and direct inspection agree that Stages 00–14 are validated and Stage 15 is implemented with 272 passing tests, clean build/format, two current EF migration heads, a successful self-contained publish/install/smoke/upgrade/repair/uninstall/restore workflow, and one environment-blocked real `HostAtLogon` registration.
- The solution contains 25 projects. Production boundaries remain Host, Runner, Founder Scout, and Wake Remote; `assistant.db` and `founders.db` remain separately migrated SQLite stores.
- Existing tests already cover most of the 122 acceptance scenarios, including real SQLite, actual Runner/agent child processes, local browser fixtures, deterministic fake AI, fake wake/power adapters, 10,000-candidate paging, online backup/restore, and published install smoke.
- Repository search found no executable V1 `TODO`, `FIXME`, or `NotImplementedException`. One stale XML comment still calls the implemented Founder Scout aggregate report contract a placeholder. `PlatformNotSupportedException` is a deliberate Windows guard; test-only `NotSupportedException` members are unreachable fake boundaries.
- Analyzer suppressions are limited to generated EF migration code, two intentional CA1711 contract names, and the isolated official OpenAI Responses `OPENAI001` surface documented by ADR-0010.
- Material gaps found before implementation: no single 40-or-more-profile discovery-to-outcome synthetic workflow; no 100,000-run-event query/plan measurement; no line-item acceptance evidence index; the security/privacy document is only a short operator note rather than the required threat model; final Stage 16 desktop/narrow rendered QA and release-candidate ZIP/checksum/release notes/checklist are absent.
- The security review is being run as one standard source-backed repository scan. Its access preflight passed; optional TAC context was unavailable (`not_granted`), which is advisory and does not block the scan.

## Scope and non-goals

### In scope

- Map every acceptance-matrix row to automated evidence, an exact manual procedure, or a narrowly justified deferred item.
- Add deterministic consolidated E2E, concurrency/recovery, and scale tests where existing evidence is not sufficient.
- Fix confirmed correctness, privacy, cleanup, unsafe rendering/path/process, and V1 placeholder defects.
- Complete the source-backed security/privacy threat model and dependency vulnerability/deprecation inspection.
- Measure the documented local-scale operations using broad non-flaky thresholds and query-plan/index evidence.
- Verify the rendered local UI at desktop and narrow widths through the in-app Browser and capture only synthetic data.
- Reconcile implementation/operator docs and produce the V1 checklist, release notes, dependency inventory, versioned archive, checksums, and rollback procedure.

### Non-goals

- No cloud architecture, Windows Service, automatic updater, external notification transport, automatic invitation sender, browser evasion, live external account/provider requirement, MSI/WiX work, or new agent capability.
- No claim that firmware sleep/wake, live Startup School markup, a live AI deployment, remote-provider reachability, SmartScreen reputation, or Task Scheduler health was validated unless it is actually exercised.

## Design and data flow

Stage 16 adds verification and release composition around existing product boundaries; it does not create a new runtime process or database.

```text
synthetic fixtures + temporary Unicode roots
    -> migrated assistant.db + founders.db
    -> Host/Runner/FounderScout/WakeRemote existing boundaries
    -> persisted runs/events/candidates/evaluations/reports/audit
    -> acceptance assertions + scale measurements

source-backed threat model + dependency inspection
    -> confirmed fixes
    -> docs/security-privacy.md + residual risks

validated publish directory
    -> install/smoke
    -> versioned release-candidate ZIP
    -> SHA-256 checksum + release notes/checklist/inventory
```

All synthetic roots are uniquely named below the system temporary directory and include spaces plus Unicode. Destructive cleanup revalidates the full path and expected prefix. Normal tests use loopback fixtures, deterministic provider behavior, fake Windows integrations, bounded process waits, and no external service or workstation sleep.

## Milestones

1. [x] Reconcile Stage 15 handoff, architecture/ADRs/plans, acceptance matrix, migrations, tests, suppressions, placeholders, scripts, and known environment limitations.
2. [x] Publish the 122-row acceptance evidence map and implement missing deterministic E2E/concurrency/recovery coverage.
3. [x] Add scale/query-plan measurements for 10,000 candidates, 100,000 run events, scheduler/queue/report/startup/backup, and confirm cleanup bounds.
4. [x] Complete the standard security scan, fix reportable critical defects, and expand `docs/security-privacy.md` with the verified threat model and residual risk.
5. [x] Run desktop/narrow rendered QA over a synthetic migrated workspace and fix confirmed V1 usability/safe-rendering defects.
6. [x] Reconcile documentation and create the V1 release checklist, release notes, dependency/license inventory, release-candidate archive, and checksums.
7. [x] Run restore/build/test/format, migration drift/fresh/upgrade/restore, architecture/E2E, dependency, publish/install/smoke, and final artifact verification; update the canonical handoff.

## Detailed steps

1. Add a Stage 16 E2E fixture/test that uses the existing real Runner and independently executable Founder Scout under a temporary path containing spaces and Unicode. Discover at least 40 local fixture profiles, process them in bounded batches, exercise duplicate/changed capture and deterministic deep evaluation, build queue/report state, record explicit manual-sent/outcome actions, generate operational summary/notification state, and assert central/Founder hashes/counts plus zero automatic sends.
2. Extend focused tests for any missing enforcement-stop, process cleanup, lease contention, interrupted-write, stale fencing, and restart-recovery scenario found in the line-item map. Reuse existing fake time/Windows/provider contracts rather than introduce sleeps or machine mutation.
3. Add a 100,000-event fixture using bulk SQLite insert and the production paged run-detail query. Assert bounded page shape, supporting query plan/index use, stable order, and a broad release-build duration. Consolidate existing candidate/report/backup/scheduler/queue measurements into a recorded Stage 16 performance table.
4. Verify loopback binding, antiforgery, encoding, argument lists, allowed-root/reparse handling, manifest/config/schema validation, secret redaction, backup exclusions, raw retention, protected-attribute exclusion, no-send/no-evasion boundaries, diagnostic bounds, CSV formula neutralization, and archive extraction safety. Apply only source-backed fixes.
5. Run `dotnet list package --vulnerable --include-transitive`, `--deprecated`, and `--outdated` as available; record actual results and distinguish actionable vulnerabilities from ordinary updates.
6. Serve a temporary synthetic Host workspace, state the tested flow, and use the in-app Browser to check primary navigation, status/error/attention pages, candidate/detail/queue/operations, configuration/history, schedules, runs/audit/artifacts, Wake, Settings/About, confirmation affordances, focus/labels, desktop and narrow layouts, and console errors.
7. Update architecture, configuration/operator docs, troubleshooting, release/rollback, security/privacy, and `docs/PROJECT_STATE.md` only where current source or executed validation establishes the behavior.
8. Publish 1.0.0-rc.1 (or the repository-compatible normalized assembly version with RC identity in release metadata) to a clean Stage 16 directory, run installed smoke, create a deterministic-path ZIP, write its SHA-256 checksum, and verify the archive contains no mutable or sensitive data.

## Progress

- 2026-08-31: Read repository instructions, current handoff, Stage 16 prompt, acceptance matrix, planning rules, architecture, ADR history, stage plans, source/test inventory, packaging scripts, and security/UI skill instructions.
- 2026-08-31: Completed the standard security scan configuration preflight. Delegated baseline and independent architecture review are in progress; optional TAC access was not granted and was reported as an advisory.
- 2026-08-31: Completed the initial placeholder/suppression/migration/test/release inventory and recorded the verified material gaps above.
- 2026-08-31: Completed scan `2f29aa7c-357b-44ce-972c-21bff3212247`. Four source-validated findings were fixed: missing owner authorization, globally accessible desktop activation IPC, non-loopback HTTP browser navigation, and sensitive/indefinitely retained browser diagnostics. The canonical scan indexed successfully; its coverage artifact retained two stale preliminary deferred entries that duplicate the finalized High/Medium findings, so the generated coverage label is conservatively `partial` despite completed investigation and remediation.
- 2026-08-31: Added the 40-profile real Runner/Founder Scout workflow (51.5 seconds), 100,000-event indexed query check, backup failure-audit regression, task ownership checks, artifact-expiry propagation, transport/diagnostic redaction tests, and 122-row evidence map (120 automated, 2 manual, 0 deferred).
- 2026-08-31: The in-app Browser rejected the loopback URL with `ERR_BLOCKED_BY_CLIENT`; the skill-prescribed .NET Playwright fallback rendered all primary routes at 1440x900 and 390x844, asserted focus/landmarks/overflow/console safety, and captured synthetic screenshots. It exposed and fixed a nested `main`, unsafe source link, stale screenshot copy, and narrow navigation clipping.
- 2026-08-31: Final restore, zero-warning Release build, 280-test pass, format, architecture, and both migration-drift gates passed. One initial full run retained two stale five-artifact expectations after screenshot removal; the corrected four-artifact cases and definitive suite passed.
- 2026-08-31: A 1,181-entry self-contained preliminary bundle installed under a Unicode/space path and passed smoke. Repair exposed missing installed release docs and an unbounded no-primary shutdown launch; install/repair/uninstall were corrected, republished, and verified with zero invalid files, a second passing smoke, successful data-removing uninstall, and zero remaining product processes.
- 2026-08-31: Sealed `HomeBusinessAssistant-1.0.0-rc.1-win-x64.zip` at 221,826,197 bytes with SHA-256 `20ceef3d9a221fd60302d81b1af595b5f15e9e8e87eb262c8bee6c4191a929c8`. Independent extraction rehashed all 1,180 payload entries and found zero invalid entries, unsafe paths, or forbidden mutable/sensitive files; `release-manifest.json` and `SHA256SUMS.txt` agree. Temporary preliminary/install/extraction roots were removed while the final RC and synthetic UI evidence were retained.

## Decisions

- Treat the existing focused integration tests as valid acceptance evidence when they exercise the same production boundary; do not duplicate 122 scenarios into one slow monolith.
- Add one coherent 40-profile release workflow to prove cross-boundary composition, while keeping fault cases in focused deterministic tests so failures remain diagnosable.
- Use broad duration ceilings plus query-plan/index assertions. Exact millisecond gates are inappropriate across CI/workstation hardware.
- Keep live website, live provider, real sleep/wake, real remote access, native tray visibility, and environment-broken Task Scheduler registration manual-only. Automated V1 correctness never depends on them.
- A security hypothesis is not a defect. Only independently source-validated, reachable findings are fixed/reported; same-Windows-user file/DPAPI access remains a documented residual trust assumption.
- Keep the release folder deployment defined by ADR-0013. Stage 16 wraps the validated bundle in versioned release metadata/ZIP instead of introducing an MSI or signer.

## Validation

Required commands:

```powershell
dotnet --info
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Stage-specific validation:

- Filtered Stage 16 E2E and architecture suites with no required skipped tests.
- Both EF migration drift checks plus fresh central/Founder migration and compatible-upgrade/restore tests.
- Dependency vulnerable/deprecated/outdated inspection.
- Source-backed standard security scan and canonical report.
- In-app Browser desktop and narrow synthetic UI workflow with console inspection.
- Self-contained `win-x64` publish, manifest validation, clean installed smoke, archive extraction/readback, and independent SHA-256 verification.
- Manual-only procedures are listed in the acceptance evidence file. Results remain `Not executed` unless actually performed.

Actual results:

- SDK 10.0.400 / runtime 10.0.11 on Windows `win-x64`; restore passed for all 25 projects and publish RID assets.
- Release build passed with 0 warnings and 0 errors; definitive suite passed 280 with 0 failures and one counted interactive-fixture skip; format and architecture passed.
- Both EF drift checks reported no model changes. Fresh, upgrade, online backup/restore, and rollback behavior passed in the definitive integration and installed-smoke suites; Stage 16 adds no migration.
- Vulnerability review found no known vulnerable package; only the test toolchain carries deprecated transitive `Microsoft.ApplicationInsights` 2.22.0.
- Rendered Playwright QA passed desktop and narrow layouts with no console/page errors. The in-app Browser was blocked by local URL policy and is not claimed as executed.
- Published install, two smokes, corrected zero-replacement repair, uninstall/data removal, archive extraction, manifest rehash, forbidden-content scan, and orphan-process checks passed.

## Recovery and rollback

- Stage 16 is expected to add no database migration unless a proven query/index defect requires one. If a migration is necessary, backup/restore and downgrade compatibility will be documented before release creation.
- Tests and smokes use validated unique temporary roots. Interrupted outputs remain `.pending`/temporary files and are not promoted as backups, reports, installs, or release archives.
- Installation rollback uses the Stage 15 retained prior `app` directory and verified pre-install database backup. Database restore remains the explicit maintenance command with `RESTORE`, integrity/hash checks, staging, and rollback copies.
- Release rollback means stop Host/Runner/agents, preserve the data root, install the previous validated app bundle, and restore a pre-upgrade database set only if its migration compatibility requires it. DPAPI secrets and browser profiles are preserved and are never embedded in a release archive.

## Remaining risks and follow-up

- Real Task Scheduler registration is currently blocked by this workstation's root-folder path failure; XML and adapter failure behavior are automated, but another healthy Windows machine is needed for release-environment registration evidence.
- Live Startup School selectors/session behavior, live provider/model behavior, firmware wake, remote reachability, native tray appearance, SmartScreen reputation, and Authenticode remain explicit opt-in/manual or distribution limitations.
- Optional Stage 17 can create a reusable new-agent template after V1; it is not required for this release.
