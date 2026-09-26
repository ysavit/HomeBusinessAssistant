# Home Business Assistant — Repository Instructions

## Product goal

Build a Windows-first, local home-business automation platform that schedules, launches, monitors, audits, and summarizes independently executable agents. The first agents are Founder Scout and Wake & Remote. The platform must remain small enough to run on one Windows workstation while preserving clear boundaries that allow future agents and a future Windows Service host.

## Required reading

Before changing code:

1. Read this file completely.
2. Read `docs/PROJECT_STATE.md` for the current verified implementation and handoff state.
3. Read `.agent/PLANS.md` for any stage that spans multiple projects, introduces infrastructure, changes persistence, or changes process boundaries.
4. Read `docs/architecture.md` and any relevant ADR under `docs/adr/`.
5. Read the current stage prompt under `prompts/`.
6. Inspect existing implementation and tests. Do not assume a prior stage is complete merely because the prompt says it should be.

## Working method

- Implement a complete working increment, not only a design or placeholder.
- Keep each stage within its stated scope. Do not silently implement future stages.
- Make reasonable, documented assumptions when details are absent.
- For substantial work, create or update a living ExecPlan under `docs/exec-plans/`.
- Keep the repository buildable at every stopping point.
- Do not modify unrelated code or perform opportunistic rewrites.
- Do not create Git commits, tags, branches, or pull requests unless explicitly requested.
- Do not claim tests passed unless you executed them and captured the result.
- Do not suppress warnings merely to make builds pass. Fix root causes or document a narrowly scoped exception.

## Repository state and stage handoff

- `docs/PROJECT_STATE.md` is the canonical current implementation/handoff state.
- Read it before planning or implementing every stage, then validate it against the repository.
- Update it at the end of every completed, partially completed, or blocked stage.
- Record only verified repository state. A prompt, plan, or expected architecture is not evidence that functionality exists.
- Distinguish `Implemented` from `Validated`; record actual restore, build, test, format, migration, CLI, host, browser-fixture, and smoke-test results.
- Preserve failed validation evidence until a later verified pass supersedes it.
- Keep stable engineering rules in `AGENTS.md`, durable rationale in `docs/adr/`, implementation detail in source/tests/ExecPlans, and current handoff state in `docs/PROJECT_STATE.md`.
- Do not copy complete prompts or architecture documents into the project-state file.
- Never place secrets, credentials, tokens, cookies, browser authentication state, remote-access PINs, or real founder profile contents in project-state documentation.
- Every stage report must identify the exact resulting project-state status and next prompt.

## Technology baseline

- .NET 10 LTS and C# 14.
- Nullable reference types enabled.
- Implicit usings enabled where appropriate.
- Warnings treated as errors for first-party projects.
- ASP.NET Core Razor Pages for the local UI.
- Windows Forms only for `NotifyIcon`, tray menus, and interactive desktop lifecycle.
- EF Core 10 with SQLite.
- `System.Text.Json` for serialization.
- Built-in DI, options validation, `HttpClientFactory`, health checks, and `TimeProvider`.
- Serilog for structured rolling-file logs.
- nUnit and Moq for tests.
- Do not add a JavaScript SPA framework or Node-based build pipeline in V1.

## Architecture rules

- Follow clean architecture and explicit dependency direction.
- Domain projects contain business entities, value objects, enums, and domain rules. They do not reference infrastructure, EF Core, ASP.NET Core, Windows APIs, or agent executables.
- Application projects contain use cases, ports/interfaces, DTOs, validators, orchestration policies, and transaction boundaries.
- Infrastructure projects implement database, file system, process, AI, browser, Windows, and logging adapters.
- Host projects compose dependencies and own process lifetime.
- Use focused repositories, not a generic repository abstraction.
- Use asynchronous APIs and propagate `CancellationToken` through I/O boundaries.
- Use `DateTimeOffset` in UTC for persisted timestamps. Obtain time from injected `TimeProvider`.
- Store time-zone IDs with schedule definitions and convert only at scheduling/display boundaries.
- No static mutable state, service locator, ambient DbContext, or hidden global configuration.
- Avoid cross-agent database coupling. The platform owns `assistant.db`; Founder Scout owns `founders.db`.
- Agents communicate with the runner through command-line arguments, exit codes, and versioned JSON Lines events on stdout. Human-readable diagnostics go to stderr or structured logs.
- The web UI and tray host may poll persisted run state in V1. Do not add distributed messaging merely for local progress updates.

## Project/process boundaries

Expected processes:

- `HomeBusinessAssistant.Host.exe`: interactive system-tray process plus Kestrel/Razor Pages on loopback.
- `HomeBusinessAssistant.Runner.exe`: one-shot durable executor for scheduled or manual occurrences.
- `FounderScout.exe`: independently executable agent.
- `WakeRemote.exe`: independently executable wake/availability agent.

The scheduler is the source of truth for schedules and occurrences. Windows Task Scheduler is only the OS bridge for startup and wake-to-run. Scheduled tasks must call the central runner, not call agent executables directly.

## Persistence rules

- Use EF Core migrations. Never rely on `EnsureCreated` for production databases.
- Enable SQLite WAL, foreign keys, and a bounded busy timeout at database initialization.
- Use short transactions and avoid holding a transaction open during browser, AI, network, or process work.
- Claim occurrences and leases atomically.
- Persist captured Founder Scout profiles before invoking AI analysis.
- Store large diagnostic artifacts on disk and store path, size, hash, type, and retention metadata in SQLite.
- Configuration changes create immutable revisions. Every run records the exact configuration revision and hash it used.
- Audit and run-event records are append-only through application APIs.
- Redact secrets and sensitive browser/session data before logs, audit, summaries, or artifacts are written.

## Configuration and secrets

- `appsettings.json` contains only bootstrap settings: data directory, loopback URL, logging bootstrap, and database path.
- UI-editable agent settings, schedules, thresholds, and policies live in SQLite.
- Secrets are addressed by opaque secret references and stored through `ISecretStore` using Windows-protected storage.
- Never persist API keys, account passwords, cookies, remote-access PINs, or authentication-state payloads in plain text.
- Browser profile folders are sensitive. Never copy them into reports, test fixtures, source control, or backups unless explicitly encrypted.
- Options must be validated at startup or before execution. Invalid configuration must fail clearly before side effects occur.

## Scheduling rules

- Support durable schedule occurrences with unique `(ScheduleId, DueAtUtc)` identity.
- Schedule types in V1: manual, one-time, daily, selected weekdays, fixed interval, and fixed delay after previous completion.
- Explicitly implement misfire policy, concurrency policy, timeout, wake policy, enabled/paused state, and time zone.
- Use `TimeProvider` so schedule and timeout behavior can be deterministically tested.
- Prevent overlapping runs through both scheduler policy and a database lease/claim.
- A wake task and a tray scheduler may observe the same due occurrence; only one runner may claim it.
- Reconcile the next Windows wake task whenever schedules, occurrences, run completion, pause state, or clock/time-zone relevant state changes.

## Runner rules

- Claim the occurrence before launching an agent.
- Record runner version, agent version, manifest version, executable hash, configuration revision, trigger source, machine, PID, start, heartbeat, completion, exit code, and summary.
- Parse only schema-valid versioned JSONL events from stdout. Preserve malformed lines as diagnostics without crashing the whole run.
- Capture stderr separately.
- Enforce timeout and cancellation. After a grace period, terminate the entire child process tree.
- Mark stale running records as abandoned during recovery.
- Never rerun a completed occurrence automatically.
- Retries create explicit retry attempts and remain bounded.

## Windows and wake rules

- The app is Windows-first and may use Windows-only APIs in the Windows infrastructure and host projects.
- Bind Kestrel only to `127.0.0.1` in V1.
- Use a single-instance mutex for the tray host.
- Use Windows Task Scheduler wake-to-run for scheduled wake behavior.
- Use `SetThreadExecutionState` or a more appropriate documented Windows power request while work or a remote-access window requires the system to remain awake.
- Release power requests reliably on completion, cancellation, shutdown, and failure.
- Do not force the computer back to sleep in V1. Release the keep-awake request and allow normal Windows power policy to resume.
- Automated tests must not actually suspend, hibernate, shut down, or alter permanent power settings.

## Founder Scout rules

- Invitation sending remains manual. The agent generates and validates drafts only.
- Separate discovery/capture from analysis so captured data survives AI outages.
- Use versioned parsers, normalized snapshots, stable hashes, and cross-account deduplication.
- Reanalyze only when the snapshot, scorecard, prompt, evaluator, or explicit user request changes.
- Final arithmetic is calculated in C#, not trusted to the model.
- Every scored category must include evidence from the captured profile or explicitly state that evidence is missing.
- Missing information lowers confidence; it must not be invented.
- Exclude protected and irrelevant personal attributes from scoring and invitation generation.
- Store two scores: founder quality and fit for the local founder persona. Rank invitation priority separately.
- Every deep evaluation generates a short and detailed introduction draft, the facts used, confidence, and validation status.
- Do not auto-send invitations or messages.

### Browser automation boundaries

- Use a dedicated persistent browser profile directory per configured browser account.
- Authentication is completed manually in headed mode; do not store passwords.
- Use ordinary browser automation, sequential navigation, explicit run limits, and deterministic cooldowns.
- Stop the current account/run on login redirects, authentication expiry, access denial, throttling, challenge/CAPTCHA pages, or parser-health failure.
- Do not implement CAPTCHA solving, stealth plugins, fingerprint spoofing, proxy rotation, traffic laundering, or automated account failover following an enforcement signal.
- Do not randomize human-like mouse movement or keystrokes to conceal automation.
- Save bounded diagnostics for failure analysis and apply retention rules.

## UI rules

- Razor Pages with server-rendered HTML and small progressive-enhancement JavaScript only.
- Provide clear empty, loading, success, failure, paused, authentication-required, throttled, and challenge-detected states.
- Use antiforgery protection for state-changing requests.
- No external network binding or remote UI access in V1.
- Do not expose secrets, browser session paths, raw cookies, or full sensitive exception data.
- Prefer accessible semantic HTML, keyboard navigation, visible focus, and responsive layouts.
- Destructive or power-related actions require a clear confirmation.

## Logging, audit, and privacy

- Use structured logs with run, occurrence, agent, account, and candidate correlation IDs.
- Never log secrets, cookies, authorization headers, raw AI keys, or remote-access PINs.
- Run summaries must be deterministic from persisted metrics in V1. Optional LLM summarization must not be required for operational correctness.
- Store diagnostic HTML/screenshots only when needed and delete them according to retention settings.
- Do not store profile photographs.
- Raw profile retention is configurable; derived evaluation and audit retention are separate.

## Testing requirements

- Use nUnit and Moq.
- Prefer real temporary SQLite databases for persistence tests; do not use EF Core InMemory for relational behavior.
- Unit-test schedule calculations, DST behavior, state machines, scoring arithmetic, validation, redaction, deduplication, and message checks.
- Integration-test migrations, occurrence claiming, runner process protocol, cancellation, timeouts, task XML generation, file artifacts, and Razor Page handlers.
- Use fake agents and local HTML fixtures in CI. Do not access Startup School, real remote services, or live AI endpoints in the normal test suite.
- Live AI/browser tests must be opt-in, clearly labeled, and skipped unless required environment variables or local session prerequisites are present.
- Each bug fix requires a regression test when practical.

## Required validation before finishing a stage

Run from the repository root:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Also run stage-specific migrations, CLI, host, fixture, or smoke checks named in the prompt.

## Final response from Codex

At the end of every stage report:

1. What was implemented.
2. Important design decisions and assumptions.
3. Files/projects added or changed.
4. Database migrations/configuration changes.
5. Commands executed and their actual results.
6. Manual verification steps.
7. Known limitations or deferred items.
8. Confirmation that `docs/PROJECT_STATE.md` was updated with verified state.
9. The exact next prompt file to run.

