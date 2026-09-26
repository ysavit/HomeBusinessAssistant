# Troubleshooting

## Host does not start or the page is unavailable

Run the installed Runner's `diagnose`, inspect `logs`, and query `host-startup-status`. Confirm the configured URL is `127.0.0.1`, the port is free, paths are writable, the Runner exists, and migrations are current. Launching Host twice should not create two processes; the second invocation signals the first.

Use `repair.ps1` to verify hashes, paths, databases, and tasks. If a health check reports a safe reason code, correct that dependency rather than deleting databases.

## Install or upgrade fails

Read the install log under the install root. The installer retains/restores the prior app and pre-install databases when possible. Common causes are an active run, a Host that did not stop, a damaged source manifest, an occupied port, Task Scheduler permission policy, or insufficient disk space (the self-contained bundle is large and staging temporarily needs another copy).

Do not bypass a hash failure. Republish or obtain a trusted complete bundle. Downgrades are refused because database compatibility is not guaranteed.

## Task Scheduler problems

Use:

```powershell
.\HomeBusinessAssistant.Runner.exe host-startup-status
.\HomeBusinessAssistant.Runner.exe reconcile-wake
```

Only tasks with the application-managed description/action are updated or removed. HostAtLogon uses the current interactive user, least privilege, no stored password, optional delay, and `WakeToRun=false`. Corporate policy may prevent task registration; the installer reports this rather than elevating or bypassing policy.

## Backup or restore fails

Validate the set and hashes first. A backup lease conflict means another backup/restore is active. Restore also refuses a running Host/agent or active persisted run, missing `RESTORE` token, incomplete `.pending-*` set, incompatible product/schema, missing expected tables, or failed SQLite integrity check.

Never repair this by copying a live SQLite file. Stop processes and use the guarded restore path.

## Founder Scout browser problems

Run repair with `-InstallPlaywrightBrowser` if the pinned Chromium revision is missing. Authentication, throttling, challenge, and parser-health stops require manual review. Do not delete or share the browser profile as a routine fix. Test fixtures can verify parser behavior without live access.

## Excessive `dotnet` workers during source publishing

On some constrained Windows environments, parallel MSBuild project-reference discovery may leave worker processes after a failed publish. Stage 15 publishing disables parallel build, node reuse, and shared compilation. If an earlier failed command left workers, identify only the processes created by that command before stopping them; do not terminate unrelated development processes.

## What to collect

Collect the redacted diagnostics export, build/commit/migration identities from About, timestamps, safe reason codes, and the relevant bounded structured log. Do not include secrets, cookies, authorization headers, remote PINs, browser profiles, or unredacted founder profiles.
