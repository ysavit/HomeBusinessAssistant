# Home Business Assistant 1.0.0-rc.1 release notes

Release date: 2026-08-31  
Target: Windows 11 x64, per-user folder deployment

## Included

- Local tray Host and owner-authenticated Razor Pages control plane bound to `127.0.0.1`.
- Durable SQLite schedules, occurrences, claims, retries, audits, attention, notifications, summaries, backups, restore, and retention.
- One-shot Runner with executable/configuration identity, JSONL protocol, timeout/cancellation/tree termination, recovery, artifacts, stderr isolation, and power-lifetime supervision.
- Wake & Remote readiness windows with generated current-user wake tasks, fake/Chrome Remote Desktop/RDP readiness adapters, and no firewall, credential, or forced-sleep mutation.
- Founder Scout fixture/browser discovery, deterministic processing/redaction/deduplication, structured AI evaluation, C# scoring, review-only drafts, candidate/queue/report UI, and manual sent/outcome recording.
- Self-contained Host, Runner, Founder Scout, and Wake Remote executables; install, repair, uninstall, smoke, manifest, backup, and rollback tooling.

## Stage 16 hardening

- Added Windows-owner authorization for management pages plus exact Host/Origin checks.
- Restricted desktop activation IPC to the current Windows user.
- Required HTTPS for external Founder Scout navigation; loopback HTTP remains test-only.
- Removed diagnostic screenshots, hardened DOM redaction, and carried artifact expiry through protocol persistence.
- Refused replacement/removal of unmanaged Task Scheduler occupants.
- Added a 45-page fixture/40-profile real-process E2E workflow and a 100,000-event indexed query test.
- Corrected backup failure auditing so an absent target ID cannot mask the original failure.

No database migration was added in Stage 16. Agent protocol remains major/minor `1.0`; `ArtifactPayload` gained an optional backward-compatible UTC `deleteAfterUtc` field.

## Known limitations

- Binaries are not Authenticode signed and no MSI/MSIX or automatic updater is supplied.
- The application trusts code running as the same Windows user; DPAPI and owner-only local controls do not sandbox that account.
- HostAtLogon and NextWake require an interactive signed-in user. Firmware/power policy determine actual wake behavior.
- Real Startup School markup/session, live AI deployments, hardware sleep/wake, remote reachability, native tray appearance, and SmartScreen reputation are opt-in/manual checks.
- The release includes deterministic local fixtures, not browser binaries. Founder Scout browser use requires the explicit Playwright browser installation step.
- Ordinary backup excludes secrets, browser profiles, logs, diagnostics, and external artifacts. Restore DPAPI secrets only under the same Windows account or re-enter them.

See `installation.md`, `security-privacy.md`, and `v1-release-checklist.md` in the archive.
