# Stage 17 — New Agent Template and Extension Workflow

Paste this entire prompt into Codex after the V1 release stage when you want a repeatable way to add future agents.

---

You are implementing **Stage 17: Reusable New-Agent Template, Scaffolding Command, Example Agent, and Extension Documentation**.

Read all repository guidance, the final agent protocol, manifest/configuration/run patterns, V1 release code, and this prompt. Create/update `docs/exec-plans/stage-17-new-agent-template.md`.

## Repository-state handoff

Before planning or coding:

1. Read `docs/PROJECT_STATE.md` completely.
2. Inspect the repository and verify the previous stage's claimed state; do not trust the state file blindly.
3. Reconcile any mismatch in the stage ExecPlan before implementation.

Before the final report:

1. Update `docs/PROJECT_STATE.md` with capabilities actually implemented, stage status, material files/projects, migrations/configuration/protocol versions, actual validation results, known limitations, and the exact next prompt.
2. Distinguish `Implemented` from `Validated` and preserve failed checks until a later verified pass.
3. Do not include secrets, cookies, authentication state, tokens, or real founder profile content.

## Goal

Make future Home Business Assistant agents easy to add without copying Founder Scout internals or modifying central orchestration for every agent. Create a supported template/scaffolder, a small example agent, manifest/config schema conventions, registration/installation hooks, tests, and a developer guide.

The architecture remains external-process agents communicating through the Agent SDK. Do not introduce in-process dynamic plugin loading.

## 1. Agent template

Create either:

- a `dotnet new` template package, preferred; or
- a repository-local scaffolding command/script if a template package is disproportionate.

Invocation example:

```powershell
dotnet new hba-agent \
  --name InvoiceMonitor \
  --agent-id invoice-monitor \
  --display-name "Invoice Monitor"
```

Generated structure:

```text
agents/InvoiceMonitor/
├── InvoiceMonitor.Application/
├── InvoiceMonitor.Infrastructure/
├── InvoiceMonitor.Agent/
├── InvoiceMonitor.Tests/
├── manifest.json
├── configuration.schema.json
├── README.md
└── AGENTS.md optional agent-specific instructions
```

Only generate a Domain project when the agent's model warrants it; document the option.

## 2. Generated agent behavior

The example/template agent must:

- reference `HomeBusinessAssistant.AgentSdk`;
- parse standard execution context;
- support `run`, `diagnose`, and `protocol-demo`;
- load/validate versioned typed configuration;
- emit started/progress/metric/artifact/summary/completed JSONL events;
- write human diagnostics to stderr only;
- honor cancellation;
- use assigned data/artifact directories safely;
- return stable exit codes;
- contain no secrets or fake production integrations.

Generate meaningful tests for protocol, config validation, cancellation, artifact path use, and Runner integration.

## 3. Manifest/config schema workflow

Template provides:

- valid manifest matching final schema;
- supported commands/capabilities;
- default timeout/concurrency/session requirements;
- versioning fields;
- JSON Schema or typed schema metadata for configuration;
- sample config with secret references;
- instructions for implementing a platform UI adapter when generic rendering is insufficient.

Add tooling/test that validates every installed agent manifest/config schema in the repository.

## 4. Agent registration and packaging

Implement a generic installer/registry scan:

- scan only immediate validated directories under configured AgentDirectory;
- require manifest and executable;
- validate paths/hashes/versions;
- upsert `AgentDefinition` safely;
- preserve user configuration/schedules;
- disable/report invalid agents rather than executing them;
- audit install/update/remove state;
- packaging automatically includes opted-in agent projects/manifests through explicit build configuration, not arbitrary directory execution.

Do not auto-run newly installed agents.

## 5. Generic configuration UI

If not already implemented, add a limited JSON-schema-driven form renderer for safe primitive V1 fields:

- string;
- integer/number;
- boolean;
- enum;
- arrays of simple values;
- secret-reference field type;
- descriptions/defaults/min/max/pattern.

Requirements:

- server-side validation remains authoritative;
- no arbitrary HTML/scripts from schema;
- unsupported complex schema shows a clear message and requires a custom typed adapter;
- secrets use `ISecretStore` and are never echoed;
- configuration revision/audit behavior remains unchanged.

Do not replace the strongly typed Founder Scout/Wake UI if it is better.

## 6. Example `SampleBusinessAgent`

Generate and install a harmless example agent, such as a local folder/report watcher, that demonstrates:

- schedule;
- config;
- progress/metrics;
- one artifact;
- summary;
- failure/attention mode;
- cancellation;
- no external credentials.

Mark it disabled by default and exclude from production install unless a sample flag is set.

## 7. Documentation

Create `docs/adding-an-agent.md` covering:

```text
scaffold
project boundaries
manifest
configuration and secrets
commands
protocol events
data/artifacts
scheduling/wake/concurrency
runner behavior
audit/metrics/summary/attention
UI extension
tests
packaging/versioning/upgrade
security checklist
```

Add a concise checklist and a sample end-to-end command sequence.

## 8. Tests

Cover:

- template generation into a temporary folder;
- generated solution/projects build and tests pass;
- generated manifest/config schema validate;
- generated agent runs through real Runner/fake occurrence;
- agent scan accepts valid/rejects invalid/traversal/duplicate IDs;
- upgrade preserves config/schedules;
- generic config renderer encoding/validation/secret behavior;
- sample agent schedule/run/artifact/summary/cancellation;
- production packaging excludes disabled sample by default.

## Constraints

- No in-process assembly loading/plugin execution.
- No arbitrary script agents.
- No auto-run after install.
- No central database tables specific to a future agent's domain.
- No reduction of Runner validation/security.

## Done when

- A new external-process agent can be scaffolded, built, registered, configured, scheduled, run, audited, and packaged with minimal central changes.
- Generated code follows repository standards and has tests.
- Invalid agents cannot be executed.
- Documentation is complete.
- All validation passes.

## Required smoke validation

1. generate a temporary `InvoiceMonitor` or equivalent agent;
2. add it to a temporary solution or build it independently as designed;
3. install/register it under a temp AgentDirectory;
4. configure and schedule it;
5. run through Runner;
6. inspect events/metrics/artifact/summary/audit;
7. uninstall/disable it without harming other agents;
8. run full repository tests.

## Final report

Include scaffolding command, generated tree, generic versus custom UI decision, registration security, smoke results, and the exact documented procedure for the next real agent.

