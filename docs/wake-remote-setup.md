# Wake Remote setup

Wake Remote checks local connectivity and configured remote-access provider availability during an explicit availability window. It does not install or configure Chrome Remote Desktop/RDP, change permanent power settings, force sleep, or expose the local web UI remotely.

## Configure

1. Install and configure the chosen remote-access provider manually under the same Windows user.
2. Open the loopback Host UI and select Wake Remote.
3. Create a validated configuration revision with the provider, endpoints, timeouts, availability-window behavior, and protected secret references where required.
4. Create or enable a schedule. Enable wake only when an unattended availability window truly requires it.
5. Run `diagnose` or a manual check before relying on a schedule.

The central scheduler owns occurrences. `\HomeBusinessAssistant\NextWake` is a single disposable bridge that starts Runner for one claimed occurrence. Concurrent Host/wake observers cannot run the same occurrence twice.

## Power behavior

During a qualifying run/window, Runner holds a Windows system-required execution-state request and reliably releases it on completion, cancellation, timeout, shutdown, or failure. Display wake is not enabled by default. Releasing the request allows normal Windows power policy to resume; the application does not command the computer to sleep.

## Validate safely

Use the UI wake-test flow or the explicit script only after reviewing its task registration:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\manual-wake-smoke.ps1 -ConfirmTaskRegistration
```

Automated tests never suspend the workstation. A real sleep/wake observation remains an operator-controlled manual test. Review `power-diagnostics` output for unsupported sleep states, missing wake timers, or permissions rather than changing system policy automatically.
