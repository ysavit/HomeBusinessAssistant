# Development Guide

## Supported environment

The V1 product and all Windows integration projects target Windows. Pure domain, application, and SDK projects target `net10.0`; Windows adapters and executable hosts target `net10.0-windows`. `global.json` pins SDK feature band `10.0.400` and accepts servicing patches within that band.

Run development commands from the repository root. If an execution environment cannot write to the normal .NET or NuGet user directories, set `DOTNET_CLI_HOME` and `NUGET_PACKAGES` to ignored directories under the repository; never commit those caches.

## Restore, build, test, and format

The required stage-validation sequence is:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Warnings are errors for first-party projects. Fix the cause of warnings; do not broadly suppress analyzers or weaken the repository quality gates.

To apply formatting intentionally:

```powershell
dotnet format HomeBusinessAssistant.sln --no-restore
```

Then inspect the diff and rerun the verification command.

## Executable smoke checks

After a Release build, run:

```powershell
dotnet run --project src/HomeBusinessAssistant.Runner -c Release --no-build
dotnet run --project agents/FounderScout/FounderScout.Agent -c Release --no-build
dotnet run --project agents/WakeRemote/WakeRemote.Agent -c Release --no-build
& .\src\HomeBusinessAssistant.Host\bin\Release\net10.0-windows\HomeBusinessAssistant.Host.exe --urls http://127.0.0.1:5180
```

The Host runs as the system-tray application. In a second terminal, request liveness and readiness:

```powershell
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:5180/health/live
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:5180/health/ready
```

The Host rejects non-HTTP and non-`127.0.0.1` bindings. Exit from the tray menu so Kestrel, hosted loops, IPC, the icon, manual power state, and logging shut down through the normal desktop lifetime.

Stage 07 owns four sequential, failure-isolated loops: schedule reconciliation every 30 seconds, wake-task reconciliation every 60 seconds, stale-run recovery every 120 seconds, and failure notification polling every 15 seconds. Bootstrap-only overrides use `HBA_Host__<Name>` environment variables or `--Host:<Name>` command-line keys; use `--urls` for the URL. Intervals must be from one second through one hour.

The automated Host suite uses ephemeral Kestrel ports, temporary SQLite roots, fake native boundaries, and a real tray-controller-to-Runner-to-Wake-Remote process smoke. It does not create the machine-global wake task. Run it with:

```powershell
dotnet test tests/HomeBusinessAssistant.Host.Tests/HomeBusinessAssistant.Host.Tests.csproj -c Release --no-build
```

For the native desktop smoke, use an interactive Windows session and verify:

1. Start the built Host and confirm one `Home Business Assistant` tray icon appears.
2. Open the tray menu and dashboard; verify `/`, `/health/live`, and `/health/ready` on `127.0.0.1`.
3. Start the same Host command again; it should signal the primary instance to open the dashboard and then exit without a second icon.
4. Exercise `System Status`, `Keep Awake` then `Release Keep-Awake`, global pause/resume, and the current next-run/summary empty or populated states.
5. If a Wake Remote executable has been staged at the configured agent root, run its diagnostic and confirm the durable result appears after Runner completes.
6. Choose `Exit`, confirm when prompted for active work/power, and verify the icon disappears and a subsequent launch becomes the primary instance.

Stage 08 replaces the placeholder pages with the real local control plane at `/Agents`, `/Schedules`, `/Runs`, `/Audit`, `/WakeRemote`, and `/Settings`. Agent configuration forms are typed and revision-aware; the Founder Scout secret field is a separate password action and is never populated from storage. Schedule preview uses the production calculator, manual execution still creates a durable occurrence through Runner, and run/audit history is server-paged.

Stage 09 adds Founder Scout's separate `founders.db`, fixture import, domain diagnostics, and count-only JSON/Markdown reports. The normal suite includes a real Runner-to-child smoke that imports three unique candidates plus one duplicate, repeats the import, diagnoses the database, generates reports, and verifies cross-database run correlation:

```powershell
dotnet test tests/FounderScout.Tests/FounderScout.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~FounderScoutRunnerIntegrationTests"
```

Direct `FounderScout.exe import` input must be an absolute `.json`, `.jsonl`, or `.txt` path beneath the Runner-assigned `data/agents/founder-scout/imports` directory. Normal Runner use supplies the same path through occurrence JSON, for example `--arguments-json '{"inputPath":"C:\\...\\imports\\fixture.json"}'`. Only synthetic fixture data belongs in source/test workflows; never use a real browser export or authenticated session payload.

Stage 10 pins `Microsoft.Playwright` 1.62.0 and requires its matching Chromium runtime to be installed explicitly after a Release build. Ordinary agent execution never downloads a browser. Install and verify the exact revision with:

```powershell
pwsh agents/FounderScout/FounderScout.Agent/bin/Release/net10.0-windows/playwright.ps1 install chromium
pwsh agents/FounderScout/FounderScout.Agent/bin/Release/net10.0-windows/playwright.ps1 install --list
```

Re-run the install command whenever the pinned Playwright package changes. The Windows cache is under the current user's Playwright cache, not the repository. Do not copy that cache or an authenticated profile into source control, fixtures, reports, or backups.

The normal Founder Scout suite uses a loopback-only synthetic HTML server and the installed Chromium revision. It covers pagination, load-more, infinite-scroll, profile leases, capture persistence, diagnostics redaction, and Runner safe-stop behavior; it never accesses Startup School:

```powershell
dotnet test tests/FounderScout.Tests/FounderScout.Tests.csproj -c Release --no-build
```

Manual authentication must be launched through a Runner occurrence for `authenticate` with `{"accountId":"account-01"}`. The explicit command provisions only the canonical `browser/account-01` profile and opens a headed browser; complete login/MFA directly in the site UI. Founder Scout accepts no password input. After the configured authenticated signal appears, schedule `discover` with the same `accountId` and optional `segmentId`. `record-fixture` saves only bounded sanitized developer diagnostics: images, form values, cookies/storage, authentication data, and sensitive query values are excluded. Ordinary profile capture stores visible text and structured fields; optional `storeRawHtml` remains off by default and, when enabled, adds only bounded DOM-sanitized HTML to the raw retention artifact.

Live Startup School validation is optional and was not part of the automated Stage 10 smoke. Use a dedicated non-production account/profile, keep the configured limits low, and stop on any authentication, access-denial, throttle, challenge, unexpected-host, or parser-health signal. Never commit resulting profiles or diagnostic pages.

Stage 11 turns captured snapshots into protected-attribute-free normalized/evaluator inputs and deterministic screening decisions. Launch it through a Runner occurrence using command `analyze` with occurrence arguments `{"phase":"screen","max":20}`; a direct development invocation may use `FounderScout.exe analyze --phase screen --max 20` together with the normal SDK execution arguments. The operation uses a separate expiring snapshot lease, emits reason-code metrics plus a screening checkpoint, and never calls an AI provider. `run` now composes the acquisition/import path supplied by the occurrence and then screening without sleeping for cooldown.

The required local Stage 11 Runner smoke is part of the Founder Scout integration tests. It imports 20 sanitized captures (18 logical candidates, one exact duplicate, and one relevant change), screens 19 snapshots, proves the next screen is no-work, imports one additional relevant change, and proves exactly one snapshot is reprocessed. Final fixture state is 18 active candidates, 20 snapshots, 54 identity aliases, 20 screening decisions, zero conflicts, and no protected marker in any evaluator input:

```powershell
dotnet test tests/FounderScout.Tests/FounderScout.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~RequiredFixtureSmokeRunsThroughRunnerAndCorrelatesSeparateDatabases"
```

Raw fixture capture artifacts remain subject to retention and may contain visible protected labels. Normalized profile JSON, screening evidence, evaluator input, metrics, checkpoints, and audit/change summaries must never contain removed protected values. Parser-health failure preserves the previous valid current snapshot; repeated failures pause the account/segment instead of producing low-quality screening output.

Stage 13 exposes the persisted Founder Scout workflow at `/FounderScout`, with server-paged candidate filters, evidence/history, immutable draft revisions, explicit primary/reserve queues, human-confirmed outcomes, account/segment operations, reports, and raw-profile retention. Discovery, analysis, authentication, diagnostics, and report buttons still create durable occurrences for Runner; Razor handlers never run agent code and no send command or route exists. Direct report development forms are:

```powershell
FounderScout.exe report --type all <standard Agent SDK options>
FounderScout.exe report --type top-candidates --top 30 <standard Agent SDK options>
FounderScout.exe report --type invitation-queue <standard Agent SDK options>
```

All six report files come from one canonical ordered model. Outputs are staged for Runner and stored beneath the Founder Scout report root with hashes, sizes, and retention metadata; HTML/Markdown are encoded, CSV formula-leading text is neutralized, and JSON schema 1.0 excludes raw/browser/session/protected/model payloads. Focused synthetic validation is available with:

```powershell
dotnet test tests/FounderScout.Tests/FounderScout.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~FounderScoutResultsTests|FullyQualifiedName~ReportTypeArgumentsSelectTheRequestedArtifactFamily"
dotnet test tests/HomeBusinessAssistant.Host.Tests/HomeBusinessAssistant.Host.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~ManagementUiIntegrationTests"
```

The gated `Stage13BrowserSmokeTests` fixture is intended for an interactive in-app Browser QA session and is skipped in the ordinary suite. It uses synthetic candidates, a loopback-only Host, production CSS, migrated temporary databases, and a stop-file handshake; it never opens Startup School or a model endpoint.

Stage 14 exposes the durable operational ledger at `/Attention` and notification/retention/diagnostic state at `/Settings`. Acknowledge records operator awareness but deliberately leaves the condition open until a later detector scan observes recovery. Tray delivery is local only; quiet hours are 22:00–07:00, global delivery is throttled for 15 seconds, category delivery for 30 minutes, and a missing tray leaves the item pending. The deterministic local-day digest is generated after 08:00 local or explicitly from Settings.

Focused smoke coverage is available with:

```powershell
dotnet test tests/HomeBusinessAssistant.Infrastructure.Tests/HomeBusinessAssistant.Infrastructure.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~OperationalServiceTests"
dotnet test tests/HomeBusinessAssistant.Windows.Tests/HomeBusinessAssistant.Windows.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~DashboardLauncherAllowsOnlyValidatedLocalNotificationRoutes"
```

For an installed interactive manual check, start Host in the current user session, create a synthetic failed occurrence or temporarily use an isolated development database, wait for the notification loop, and confirm the tray balloon opens only its validated `127.0.0.1` Attention/Run route. Confirm the row remains pending when Host runs without a tray subscriber, quiet hours suppress presentation without discarding it, acknowledgement does not resolve it, and a later healthy scan moves it to resolved history. Retention should always be previewed before applying. Inspect the downloaded diagnostics ZIP manifest and `EXCLUSIONS.txt`; it must not contain databases, secrets, configuration JSON, browser/session data, raw profiles, prompts/responses, invitation drafts, screenshots, artifact contents, or raw log messages.

For an interactive Stage 08 smoke from the tray-opened dashboard:

1. Save a non-sensitive Wake Remote edit, then inspect its immutable configuration history and redacted diff.
2. Set a synthetic test secret, navigate away and back, and confirm that only existence is shown and the input remains empty; delete the synthetic value afterward.
3. preview and save a short Wake Remote schedule, then inspect its detail and next occurrences.
4. Run `diagnose`, follow the run detail/events/artifacts, and exercise cancellation or explicit retry only on eligible synthetic work.
5. Inspect the resulting audit rows and diagnostics ZIP exclusions.
6. Use Wake & Remote diagnostics freely. Register a wake test only as an intentional machine-global action; the application never sleeps the machine automatically.
7. Resize the browser to a narrow Remote Desktop-style window and verify stacked panels, focus visibility, and table scrolling.

Stage 02 adds a self-cleaning persistence smoke that migrates/seeds a temporary database, saves a non-sensitive configuration revision, round-trips/deletes a temporary DPAPI secret, acquires/releases a lease, and writes audit/artifact records:

```powershell
dotnet run --project src/HomeBusinessAssistant.Runner -c Release --no-build -- persistence-demo
```

Its output contains non-sensitive statuses and generated identifiers only. Pass `--data-directory <absolute-path>` only when intentionally retaining a development database for inspection; do not point it at a production or broad directory.

Stage 03 adds a controlled, self-cleaning scheduling smoke. It seeds a Founder Scout ten-minute fixed-delay schedule and a Wake & Remote weekday schedule, reconciles them, completes the Founder Scout occurrence at fake time, and verifies the successor is due exactly ten minutes later:

```powershell
dotnet run --project src/HomeBusinessAssistant.Runner -c Release --no-build -- scheduling-demo
```

Stage 04 Runner operations use explicit durable commands. Use `--data-directory`, `--agent-directory`, and `--manifest-directory` when validating an isolated installation:

```powershell
dotnet run --project src/HomeBusinessAssistant.Runner -c Release --no-build -- execute --occurrence-id <guid>
dotnet run --project src/HomeBusinessAssistant.Runner -c Release --no-build -- run-agent --agent-id <id> --command <command>
dotnet run --project src/HomeBusinessAssistant.Runner -c Release --no-build -- recover
dotnet run --project src/HomeBusinessAssistant.Runner -c Release --no-build -- diagnose
```

Stage 05 adds explicit Windows wake/power commands:

```powershell
dotnet run --project src/HomeBusinessAssistant.Runner -c Release --no-build -- reconcile-wake
dotnet run --project src/HomeBusinessAssistant.Runner -c Release --no-build -- power-diagnostics
dotnet run --project src/HomeBusinessAssistant.Runner -c Release --no-build -- prepare-wake-test --minutes 3
dotnet run --project src/HomeBusinessAssistant.Runner -c Release --no-build -- wake-test-status --occurrence-id <guid>
dotnet run --project src/HomeBusinessAssistant.Runner -c Release --no-build -- remove-wake-task
```

`reconcile-wake`, `prepare-wake-test`, and `remove-wake-task` change the one managed Windows task and must be invoked intentionally. Automated tests use fake process/native boundaries and never register a task, sleep/hibernate the workstation, or change a power plan. `power-diagnostics` is read-only and bounds/redacts results from `/a`, `/waketimers`, `/lastwake`, and `/devicequery wake_armed`.

For the complete opt-in hardware workflow, first build Release, review `scripts/manual-wake-smoke.ps1`, then run:

```powershell
.\scripts\manual-wake-smoke.ps1 -Minutes 3 -ConfirmTaskRegistration
```

The script never initiates sleep. It stages the already-built Wake Remote diagnostic executable, registers `\HomeBusinessAssistant\NextWake` as the current signed-in user, displays wake timers, pauses for the operator to lock/sleep manually if desired, reads persisted actual Runner start/wake delay, and removes the managed task in `finally`. Hardware/firmware, sleep model, Windows policy, battery state, and permissions can still prevent wake.

Stage 06 adds a real Runner/child-process availability-window smoke. It uses local network checks and an explicitly labeled `DiagnosticFake` remote provider; it never registers a task, changes remote-access configuration, or sleeps the machine. The normal suite uses an eight-second window. Run the required 60-second variant with:

```powershell
dotnet test tests/HomeBusinessAssistant.Runner.Tests/HomeBusinessAssistant.Runner.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~WakeRemoteRunnerIntegrationTests" --environment HBA_WAKE_REMOTE_SMOKE_SECONDS=60
```

The test verifies persisted wake-delay, network/provider readiness, heartbeats, terminal summary, agent power-handle release, private-input cleanup, and that the temporary database has no remaining next-wake occurrence. The OS `NextWake` task is deliberately not reconciled by this automated smoke because it is a machine-global managed object; Stage 05 fake-bridge tests verify the same selection/reconciliation contract without altering the workstation.

`supervision-demo` is a bounded development check requiring an empty temporary data path and the built `HomeBusinessAssistant.TestAgent` output. It installs only the synthetic fixture, executes success/artifact/timeout scenarios through the real Runner path, prints persisted counts, and verifies that no fixture process remains.

The command prints only generated occurrence IDs, UTC timestamps, counts, and verification flags. It does not launch an agent or register a Windows task.

## Project boundaries

- Domain projects contain domain types and rules only.
- Application projects depend inward on domain/platform application contracts; Wake Remote Application reuses platform power, persistence, and scheduling ports without referencing Windows APIs.
- Infrastructure projects implement future adapters and depend on application boundaries.
- Executables compose the approved inward dependencies; libraries never reference executables.
- Agent processes depend on `HomeBusinessAssistant.AgentSdk`; actual protocol contracts begin in Stage 01.

`tests/HomeBusinessAssistant.EndToEndTests/ProjectArchitectureTests.cs` verifies the exact production reference graph, detects cycles, and checks forbidden domain dependencies.

## Packages

Package versions are centralized in `Directory.Packages.props`. EF Core SQLite/Design 10.0.11, Windows ProtectedData 10.0.11, Stage 04 Serilog packages, and Founder Scout's Microsoft.Playwright 1.62.0 remain centrally pinned. EF Design is private to Infrastructure tooling. Playwright is referenced only by Founder Scout Infrastructure; its browser binary installation remains an explicit development/deployment action.

## Database migrations

The central platform migrations are owned by `HomeBusinessAssistant.Infrastructure`. Founder Scout's separate `founders.db` migration is owned by `FounderScout.Infrastructure`; both runtime initializers apply migrations and verify WAL, foreign keys, busy timeout, and `synchronous=NORMAL`. Production initialization uses migrations, never `EnsureCreated`.

The repository pins `dotnet-ef` in `dotnet-tools.json`. After changing the central model, use the local tool and verify there is no drift:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations add <MigrationName> --project src/HomeBusinessAssistant.Infrastructure --startup-project src/HomeBusinessAssistant.Infrastructure --output-dir Persistence/Migrations
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/HomeBusinessAssistant.Infrastructure --startup-project src/HomeBusinessAssistant.Infrastructure
```

After changing the Founder Scout EF model, use its isolated migration assembly:

```powershell
dotnet tool run dotnet-ef migrations add <MigrationName> --project agents/FounderScout/FounderScout.Infrastructure --startup-project agents/FounderScout/FounderScout.Infrastructure --output-dir Persistence/Migrations
dotnet tool run dotnet-ef migrations has-pending-model-changes --project agents/FounderScout/FounderScout.Infrastructure --startup-project agents/FounderScout/FounderScout.Infrastructure --configuration Release --no-build
```

## Future publishing

Publishing and installation conventions are deliberately deferred to Stage 15. Do not check `publish/`, packaged binaries, runtime data, or machine-local configuration into source control.

## Documentation and handoff

- Stable engineering rules: `AGENTS.md`.
- Target architecture: `docs/architecture.md`.
- Durable cross-cutting rationale: `docs/adr/`.
- Living stage implementation details: `docs/exec-plans/`.
- Concise verified cross-session state: `docs/PROJECT_STATE.md`.

Update the living ExecPlan while a stage is in progress. Update project state before handing off any completed, partial, or blocked stage, and record only commands actually executed.
