# V1 release checklist

Status values describe the `1.0.0-rc.1` candidate built from the Stage 16 workspace. Manual items are never implied by an automated pass.

## Automated gates

- [x] `dotnet --info` captured: SDK 10.0.400, runtime 10.0.11, Windows `win-x64`.
- [x] Restore succeeds for all solution projects and `win-x64` publish assets.
- [x] Release build succeeds with zero warnings and errors.
- [x] Full test suite passes: 280 passed; only the documented interactive fixture is counted as skipped, and the live-provider test remains explicit/opt-in.
- [x] `dotnet format --verify-no-changes` succeeds.
- [x] Architecture and both EF migration drift/fresh/upgrade/restore checks pass.
- [x] 40-profile real Runner/Founder Scout E2E passes under a Unicode/space path (51.5 seconds).
- [x] 100,000-event query uses `UX_AgentRunEvents_RunId_Sequence`, returns at most 500 ordered events, and completes inside the broad 10-second gate.
- [x] Dependency vulnerability review reports no known vulnerable packages.
- [x] Deprecated dependency review identifies only a test-tool transitive dependency.
- [x] Rendered Playwright desktop and 390-pixel synthetic UI review is complete with console inspection. The in-app Browser rejected the local URL, so the repository's .NET Playwright harness was used as the documented fallback.
- [x] Self-contained `win-x64` bundle is created; all 1,180 payload entries rehash correctly, with `publish-manifest.json` making 1,181 files total.
- [x] Install/smoke/repair/uninstall runs from a clean Unicode/space path and leaves no product process. Repair reports `invalidFilesReplaced: 0`.
- [x] Release ZIP, `release-manifest.json`, and `SHA256SUMS.txt` independently agree; archive extraction rehashes every manifest entry.
- [x] Archive inspection confirms no database, secret, browser profile, log, capture, backup, unsafe path, or developer configuration.
- [x] `docs/PROJECT_STATE.md` records final verified results and exact next prompt.

## Manual release-environment checks

- [ ] On a healthy separate Windows user profile or clean Windows 11 x64 VM, extract the ZIP, compare SHA-256, install per-user, open the dashboard, run the supplied smoke script, repair, and uninstall. Not yet executed on a separate clean machine.
- [ ] Confirm tray icon, double-click, each menu action, notification selection, exit confirmation, and no orphan Host. Not yet executed interactively.
- [ ] On hardware intended for wake use, register `HostAtLogon`/`NextWake`, lock and sleep, verify a scheduled occurrence wakes and releases its power request. Opt-in; not executed.
- [ ] Complete Founder Scout headed authentication with a dedicated non-production browser account and revalidate current selectors. Opt-in; not executed.
- [ ] Exercise the configured live AI endpoint with non-sensitive synthetic content. Opt-in; not executed.
- [ ] Validate the chosen remote-access product, Windows edition/firewall/router policy, and end-to-end remote session independently. Opt-in; not executed.
- [ ] If distributing outside a trusted channel, complete Authenticode signing/reputation and malware scanning. Out of scope for this unsigned RC.

## Promotion and rollback

Promote only when every required automated gate is checked and the two required manual acceptance rows in `v1-acceptance-evidence.md` have been executed in the intended release environment. Keep the previous application directory and a validated pre-upgrade database backup set.

To roll back: stop the Host and active Runner/agents; preserve the data root; install the previous verified app bundle; restore a pre-upgrade backup only when migration compatibility requires it; re-enter secrets if the Windows account changed. Never copy DPAPI secret files or browser profiles into the release archive.
