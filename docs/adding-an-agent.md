# Adding an Agent

This guide is the complete Stage 17 extension path for an independently executable Home Business Assistant agent. An agent is a separate process discovered from an explicitly configured package directory; it is never loaded into Host or Runner as an in-process plugin.

## 1. Create the scaffold

Install the repository-local template. Use a custom hive in automation; ordinary developer setup may use the default user template hive.

```powershell
dotnet new install templates/hba-agent
Push-Location agents
dotnet new hba-agent --name InvoiceMonitor --agent-id invoice-monitor --display-name "Invoice Monitor"
Pop-Location
```

The default `--platform-root ..\..\..` is correct for `agents/<Name>/<Project>`. Supply `--platform-root C:\path\to\sources` when scaffolding elsewhere. Use `--includeDomain true` only when the agent has real entities, value objects, or business invariants that warrant a pure Domain project; a wrapper around SDK or I/O types does not.

The scaffold contains:

```text
agents/InvoiceMonitor/
  Directory.Build.props
  Directory.Packages.props
  InvoiceMonitor.Application/
  InvoiceMonitor.Infrastructure/
  InvoiceMonitor.Agent/
  InvoiceMonitor.Tests/
  manifest.json
  configuration.schema.json
  sample.configuration.json
  README.md
  AGENTS.md
```

Add the projects to the solution. Omit the Domain line when it was not requested.

```powershell
dotnet sln HomeBusinessAssistant.sln add agents/InvoiceMonitor/InvoiceMonitor.Application/InvoiceMonitor.Application.csproj
dotnet sln HomeBusinessAssistant.sln add agents/InvoiceMonitor/InvoiceMonitor.Infrastructure/InvoiceMonitor.Infrastructure.csproj
dotnet sln HomeBusinessAssistant.sln add agents/InvoiceMonitor/InvoiceMonitor.Agent/InvoiceMonitor.Agent.csproj
dotnet sln HomeBusinessAssistant.sln add agents/InvoiceMonitor/InvoiceMonitor.Tests/InvoiceMonitor.Tests.csproj
```

## 2. Preserve project and process boundaries

- Application owns typed configuration, use cases, validators, policies, and I/O ports.
- Infrastructure implements file, database, browser, HTTP, AI, or Windows adapters. It must not leak those dependencies into Domain.
- Agent is the executable composition root. It parses the SDK invocation, loads the Runner-owned execution input, composes adapters, and writes versioned JSONL only to stdout.
- Tests may reference implementation projects. Production projects never reference an agent executable.
- Add a Domain project only for real domain rules. It remains dependency-free and is referenced inward from Application.

The executable must support `run` and `diagnose` through `AgentExecutionContextParser`; `protocol-demo` may use `AgentProtocolDemo` directly. Human diagnostics go to stderr. Stdout is reserved for schema-valid started, heartbeat/progress, metric, artifact, warning/error, summary, and completed events. Use `AgentExecutionSession` so cancellation and unhandled failure produce a stable terminal lifecycle.

Never read or write outside `AgentExecutionContext.DataDirectory` and `ArtifactDirectory` unless the product requirement explicitly defines and validates another user-selected root. Keep enumeration, network operations, output size, and retries bounded. Propagate `CancellationToken` through every I/O boundary. Return SDK exit codes rather than inventing process-specific values.

## 3. Define the manifest

`manifest.json` is the installed package identity. Required conventions:

- `manifestVersion` and `configurationSchemaVersion` use supported protocol versions.
- `id` is stable lower-case kebab case and exactly matches the immediate package directory name.
- `version` is the agent implementation version; increment it whenever executable behavior changes.
- `executable` is a safe relative path beneath the package directory.
- `supportedCommands` lists only commands the Runner may invoke with the standard execution context.
- `capabilities` are bounded descriptive identifiers, not permissions.
- `defaultTimeoutSeconds`, `defaultConcurrencyPolicy`, scheduling/manual support, and interactive-session requirements are explicit.

Changing a manifest never runs an agent. A scan validates the package and safely upserts manifest-owned fields while preserving enabled state, immutable configuration revisions, schedules, occurrences, runs, and audit history.

## 4. Define configuration and secrets

`configuration.schema.json` must be a bounded draft 2020-12 root object with `additionalProperties: false`. The safe generic editor supports:

- string, integer, number, and Boolean properties;
- string enums;
- arrays of strings, integers, or numbers;
- descriptions, defaults, constants, min/max, length, item-count, and regular-expression metadata;
- opaque secret references marked with `"format": "hba-secret-reference"`.

The server parses schema metadata into its own records; schema content never contributes HTML or JavaScript. Server-side validation is authoritative. Secret fields store only references such as `secret://invoice-monitor/api-token`; values are written separately to `ISecretStore`, never echoed, and never persisted in configuration JSON.

Nested objects, dictionaries, union types, conditionals, or other complex schema vocabulary require a typed adapter. Add an `IAgentConfigurationValidator` before `GenericAgentConfigurationValidator`, add the agent ID to the typed-adapter set in Host and Runner composition, and add a dedicated safe form/serialization branch in `Pages/Agents/Configuration.cshtml(.cs)`. Keep existing revision/hash concurrency, antiforgery, encoding, separate secret mutations, and no-echo behavior.

The package scanner seeds schema defaults only when a valid simple-schema agent has no current configuration. Defaults must therefore form a valid complete document. Updating a package never overwrites a current user revision.

## 5. Implement and test behavior

Start with the generated bounded folder-report behavior or delete it when implementing the real use case. Do not leave a fake production integration. At minimum test:

- manifest and schema validity;
- configuration parsing, bounds, unknown fields, secret references, and defaults;
- complete protocol ordering and stable exit codes;
- cancellation during real asynchronous work;
- data/artifact root confinement, reparse points, missing files, and artifact metadata;
- Runner execution with a real child process and persisted summary/event/metric/artifact/hash evidence;
- failure and operator-attention behavior;
- schedule compatibility for every manifest-supported scheduled command.

Normal tests use local fixtures and fakes. Live external tests are opt-in and never run in the standard suite.

## 6. Add explicit package composition

Publishing never scans arbitrary source directories. Add one entry to `packaging/agents.json` with repository-relative `project`, `manifest`, and `configurationSchema` paths. Use `includeByDefault: true` only after the agent is production-ready. The checked-in Sample Business Agent is false and is included only with:

```powershell
./scripts/publish.ps1 -IncludeSampleAgent
```

The publisher emits `app/agents/<agent-id>/` with the executable, dependencies, `manifest.json`, `configuration.schema.json`, and build metadata. It also emits a legacy global manifest copy while built-in bootstrap compatibility remains supported.

## 7. Install, update, disable, and remove

`install.ps1`, `repair.ps1`, and `smoke-test.ps1` run the central command after migration:

```powershell
HomeBusinessAssistant.Runner.exe scan-agents `
  --data-directory <data> `
  --agent-directory <app\agents> `
  --manifest-directory <app\manifests>
```

The configured agent directory is an explicit packaging/application root. The scanner inspects at most 128 immediate, non-reparse child directories and never recursively discovers or executes content. Every package requires a matching directory/manifest ID, valid manifest, valid schema, contained non-reparse executable, bounded size, and SHA-256 readback.

New definitions are disabled. Review configuration and schedules, then enable explicitly in the owner-authenticated local UI. A valid update preserves enabled state. Invalid packages are rejected; a corresponding existing definition is disabled and audited. When a registered package directory disappears, the definition is disabled and `agent.package-removed` is audited while all historical rows remain. Restore a valid package and rescan to recover it; do not delete database history to simulate uninstall.

## 8. Compatibility and release checklist

- Keep manifest/protocol major versions compatible with the installed Runner.
- Increment configuration schema version only with an explicit migration/default and UI compatibility plan.
- Do not remove a command while enabled schedules or pending occurrences still reference it.
- Re-run scanner/integrity/path/hash tests after changing package layout.
- Confirm first-install disabled state, explicit enable, manual run, scheduled occurrence, timeout, cancellation, failure, attention, artifact download, update, and removal behavior.
- Run restore, Release build, full tests, format verification, migration drift, and package/install smoke before release.
- Document permissions, data/diagnostic retention, secrets, network access, manual prerequisites, rollback, and any live-test opt-in in the agent README.

See [ADR-0014](adr/ADR-0014-safe-agent-extension-and-generic-configuration.md), [agent protocol](agent-protocol.md), [architecture](architecture.md), and the checked-in [Sample Business Agent](../agents/SampleBusinessAgent/README.md).
