# Founder Scout setup

Founder Scout discovers and evaluates startup-founder profiles using a dedicated local browser profile. Invitations always remain manual: the product drafts, validates, queues, and records outcomes but never sends invitations or messages.

## Browser bootstrap

Install the pinned Playwright Chromium runtime explicitly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\repair.ps1 -InstallPlaywrightBrowser
```

For a new browser account, use headed authentication and complete sign-in yourself. Passwords are never requested or stored. Each configured account has its own persistent browser-profile directory beneath Founder Scout data. Do not copy that directory into backups, diagnostics, source control, or support bundles.

## Configure and validate

1. In the Host UI, create Founder Scout configuration revisions for accounts, source segments, limits, scorecard/fit policy, AI provider, and retention.
2. Store API credentials through the protected secret UI; configuration contains only an opaque secret reference.
3. Run a bounded authentication/fixture check before live discovery.
4. Start with small page/profile limits and review parser health.
5. Process captured profiles, then run shallow/deep analysis; captured data survives AI outages.
6. Review grounded evidence, confidence, risks, drafts, and queue eligibility manually.

Founder Scout stops an account/run on login redirects, authentication expiry, access denial, throttling, challenge/CAPTCHA pages, or parser-health failure. It does not solve CAPTCHAs, spoof fingerprints, rotate proxies, conceal automation, or fail over to another account after an enforcement signal.

## Data and backup

Founder Scout owns `data\agents\founder-scout\founders.db`; normalized evidence, evaluations, queues, and reports are separate from browser state. Ordinary platform backup includes `founders.db` but excludes browser profiles and DPAPI secrets. Profile photographs are not stored. Raw snapshot retention can be shortened without deleting derived evaluation/audit history.
