# Founder Scout setup

Founder Scout discovers and evaluates startup-founder profiles using a dedicated local browser profile. Invitations always remain manual: the product drafts, validates, queues, and records outcomes but never sends invitations or messages.

## Small console workflow

`FounderScout.SimpleCli` is a direct console entry point over the existing Founder Scout browser, parser, SQLite database, and AI evaluator. It does not require the tray UI or central Runner. Run it in an interactive Windows session so manual Startup School sign-in can open in Chrome. Keep the Host's Scout run idle while using the console, because both use the same dedicated browser profile and database.

From the repository root:

```powershell
dotnet run --project agents/FounderScout/FounderScout.SimpleCli -c Release -- db-path
dotnet run --project agents/FounderScout/FounderScout.SimpleCli -c Release -- list --max 20
dotnet run --project agents/FounderScout/FounderScout.SimpleCli -c Release -- run --max 5 --delay 5
```

`run` searches up to five new profiles by default, saves each before AI, screens them, then analyzes the screened candidates touched by that scan one at a time. It prints each phase and the final stored counts. On the first run, complete Startup School sign-in in the headed Chrome window. The console never asks for or stores your Startup School password. Use `scan` for capture and screening without an AI call, or `analyze` to retry saved candidates later:

```powershell
dotnet run --project agents/FounderScout/FounderScout.SimpleCli -c Release -- scan --max 5
dotnet run --project agents/FounderScout/FounderScout.SimpleCli -c Release -- analyze --max 5
```

For AI, the console uses `OPENAI_API_KEY` from its process environment when set. Otherwise, it reads the existing current-user DPAPI-protected key saved in Founder Scout Settings. The key is never printed or stored in SQLite. Set a model with `--model <model-id>`; the default is the code-owned model. OpenAI account rate or quota errors stop the analysis; captured candidates remain in SQLite for a later `analyze` retry. The console uses the code-owned local founder persona and does not import the web UI's customized context.

The default database is `%LOCALAPPDATA%\HomeBusinessAssistant\data\agents\founder-scout\founders.db`. `db-path` prints the exact path. `list` reads safe candidate summaries from that database. To inspect tables with a SQLite client, open this file **read-only** and keep its `-wal` and `-shm` files beside it while the app is running. `assistant.db` in the parent data directory stores platform runs and configuration; candidate profiles and evaluations live in `founders.db`. Use `--data-root <absolute-path>` to run against a separate test data directory.

## Browser bootstrap

Install the pinned Playwright Chromium runtime explicitly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\repair.ps1 -InstallPlaywrightBrowser
```

For a new browser account, use headed authentication and complete sign-in yourself. Passwords are never requested or stored. Each configured account has its own persistent browser-profile directory beneath Founder Scout data. Do not copy that directory into backups, diagnostics, source control, or support bundles.

## Run simple mode

Founder Scout keeps browser, discovery, screening, privacy, and invitation-safety policy in code. A focused settings page at `/FounderScout/Settings` exposes only the OpenAI model, candidates per analysis run, non-secret local founder context, and a protected OpenAI Platform API key. Azure AI Foundry is not required for this simple flow.

1. Start Host and open `http://127.0.0.1:5180/FounderScout` in Edge or Chrome. No web username or password is required.
2. Choose the delay between profiles. Five seconds is the default; the allowed range is 1–60 seconds.
3. Click **Start Founder Scout**.
4. Founder Scout reuses a healthy dedicated system-Chrome session. If a Chrome window asks you to sign in, enter the password and complete MFA there. Founder Scout never receives the password. The browser may use `account.ycombinator.com` before returning to the Startup School co-founder dashboard.
5. Leave that browser open when prompted. The same run retries discovery after authentication, saves each retrieved profile first, and then performs best-effort deterministic screening.
6. Watch the **Running now** panel and saved-candidate count. Overview and Candidates refresh every three seconds while work is active and return to **Not running** when the Runner finishes.
7. Open **Candidates** to review every active locally saved result, newest first. A score or AI evaluation is not required for a candidate to appear. Invitation sending remains manual and outside the application.

The **Start Founder Scout** action never calls the AI provider. AI work remains a separate explicit action, and provider/prompt failures preserve all captured candidates.

## Enable AI summaries and drafts

1. Create or obtain an OpenAI Platform API key for an API project with billing and access to the selected model. A ChatGPT subscription does not supply an API key.
2. Open `http://127.0.0.1:5180/FounderScout/Settings`.
3. Keep the default model or enter another model identifier available to the API project, choose the bounded batch size, and optionally paste non-secret founder context or evaluation instructions. The OpenAI API cannot import ChatGPT Memory or prior chats, so copy only the useful text you want Founder Scout to use.
4. Save the non-secret settings. They create a normal immutable local configuration revision.
5. Enter the API key under **Protected OpenAI API key**. It is stored outside SQLite through Windows DPAPI for the current user, is never redisplayed, and is not the Startup School password.
6. Open **Candidates**. Click **Analyze candidates now** to queue screened candidates with no prior AI evaluation, or click **Analyze** on one row to refresh that profile and evaluate it again. Confirm the provider-cost warning. The Candidates status panel shows the current phase, saved counts, and the latest Scout and AI outcomes.
7. Return to **Overview** to watch durable progress, then open a candidate to inspect the AI score, evidence, confidence, recommendation, and short/detailed introduction drafts.

Provider calls remain sequential by default. Invalid credentials, unavailable models, rate limits, malformed output, or other provider failures do not delete captured profiles. Remove the protected key from the same page when it is no longer needed.

Host startup creates or reconciles the safe account and segment metadata, promotes the code-owned immutable configuration, and enables only explicit manual Founder Scout runs. It does not create a schedule or start discovery automatically.

Founder Scout stops an account/run on login redirects, authentication expiry, access denial, throttling, challenge/CAPTCHA pages, or parser-health failure. It does not solve CAPTCHAs, spoof fingerprints, rotate proxies, conceal automation, or fail over to another account after an enforcement signal.

## Data and backup

Founder Scout owns `data\agents\founder-scout\founders.db`; normalized evidence, evaluations, queues, and reports are separate from browser state. Ordinary platform backup includes `founders.db` but excludes browser profiles and DPAPI secrets. Profile photographs are not stored. Raw snapshot retention can be shortened without deleting derived evaluation/audit history.
