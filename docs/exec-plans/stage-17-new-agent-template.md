# Stage 17 ExecPlan — New Agent Template and Generic Registration

## Purpose and user-visible outcome

Make a new independently executable Home Business Assistant agent a repeatable, testable extension rather than a copy-and-edit exercise. The completed increment provides a `dotnet new hba-agent` template, a harmless Sample Business Agent, safe installed-agent discovery, a constrained JSON-schema configuration editor, explicit packaging integration, and an end-to-end Runner proof for a freshly scaffolded agent.

## Current repository state

- Stage 16 is validated as the V1 release candidate with 25 solution projects and 280 passing tests.
- Founder Scout and Wake Remote already use the SDK process protocol and folder packaging convention, but database bootstrap still seeds two global manifest filenames explicitly.
- Runtime executable integrity supports safe agent working directories and hashes, but reads manifests only from the legacy global manifest directory.
- The local configuration page has typed adapters only for Founder Scout and Wake Remote; an unknown installed agent currently fails instead of receiving a safe generic editor.
- Publishing and installation enumerate the two built-in agents directly. There is no explicit agent packaging catalog, registration command, reusable scaffold, or checked-in sample agent.
- Existing persistence can preserve enabled state, configuration revisions, schedules, and run history during a manifest upsert. No database shape change is required for this stage.

## Scope and non-goals

### In scope

- Add a repository-local .NET template with agent ID, display name, platform-root, and optional Domain-project parameters.
- Generate Application, Infrastructure, executable, tests, manifest, configuration schema, sample configuration, README, and agent-local instructions.
- Add a disabled-by-default Sample Business Agent that watches a configured local folder, writes a bounded report artifact, emits the standard protocol lifecycle, and supports diagnose/protocol-demo/cancellation.
- Add safe immediate-child discovery from the configured agent directory, manifest/schema/executable validation, hash/version/path checks, conservative update/disable/removal reconciliation, and append-only audit evidence.
- Preserve legacy global manifests for existing deployments while preferring the per-agent packaged manifest at execution.
- Add a constrained server-side JSON-schema parser/validator and generic Razor editor while retaining both existing typed adapters.
- Drive package composition from an explicit checked-in agent catalog and exclude the sample unless a publish flag opts in.
- Add documentation, contract/tooling tests, Runner integration, and a real temporary scaffold/register/run/disable smoke.

### Non-goals

- No in-process plugin loading, arbitrary DLL discovery, script/HTML schema extensions, external marketplace, automatic production integration, credential storage in configuration JSON, live network dependency, or automatic execution after registration.
- No generic support for nested objects, union types, conditional schemas, dictionaries, or arbitrary JSON-schema vocabulary. Those schemas require an explicit typed UI/validator adapter.
- No automatic deletion of historical database records, configurations, schedules, occurrences, runs, or audit events when an agent folder disappears.

## Design and data flow

```text
explicit package catalog / installed app\agents immediate children
    -> bounded manifest + configuration.schema.json parsing
    -> contained non-reparse executable + SHA-256 validation
    -> conservative AgentDefinition upsert/disable + append-only audit

typed built-in adapter -------------------------------+
limited schema catalog -> generic server validator ---+-> immutable configuration revision
unsupported schema -----------------------------------+-> typed-adapter-required state

Runner occurrence
    -> per-agent manifest (legacy global fallback)
    -> executable hash/identity check
    -> versioned JSONL protocol + artifacts + persisted summary
```

The scanner never walks recursively and never executes discovered content. New definitions are disabled. Updates preserve existing configuration, schedules, enabled state, and history. Missing or invalid previously registered package folders are disabled and audited, not erased. Generic field metadata is parsed into server-owned records; Razor never renders schema-provided HTML or scripts.

## Milestones

1. [completed] Add the living plan and implement bounded configuration-schema contracts, validation, and filesystem catalog.
2. [completed] Implement safe installed-agent scanning, per-agent manifest integrity lookup, Runner command, audit behavior, and tests.
3. [completed] Add the generic configuration editor while preserving Founder Scout and Wake Remote typed adapters.
4. [completed] Add the template, Sample Business Agent, manifests/schema/sample configuration, solution wiring, and meaningful tests.
5. [completed] Replace hard-coded publish composition with the explicit agent catalog; wire installer registration and sample opt-in.
6. [completed] Add extension documentation and architecture rationale, execute scaffold/register/configure/schedule/Runner/disable smoke, and run all required validation.
7. [completed] Update `docs/PROJECT_STATE.md` with verified results and exact next-prompt status.

## Detailed steps

1. Define the supported schema subset in Application, including safe field metadata, default document construction, secret-reference fields, unsupported-complex-schema reporting, and authoritative validation errors compatible with immutable configuration saves.
2. Load schemas only from the resolved installed agent root, with size/depth/path/reparse bounds. Compose the catalog-backed validator after typed validators so built-in behavior remains unchanged.
3. Scan immediate child directories only. Require `manifest.json`, `configuration.schema.json`, and the manifest command executable; validate identity, folder name, declared commands, semantic versions, session requirements, containment, reparse points, and executable hash before persistence.
4. Add `scan-agents` to Runner. Return a deterministic JSON result and a nonzero status when candidates are invalid. Installation invokes it after migrations. Registration never schedules or runs an agent.
5. Extend the configuration page with primitive/enum/simple-array inputs and a separate secret-store action. Keep schema strings Razor-encoded, reject unsupported fields, rebuild typed JSON server-side, and run the same authoritative validator on save.
6. Create the template and checked-in sample from the same conventions. The sample performs only local, bounded folder metadata reporting and writes under assigned data/artifact roots.
7. Add an explicit package-agent JSON catalog. Publish built-ins by default and the sample only with `-IncludeSampleAgent`; copy each package's manifest/schema alongside its executable.
8. Document the full extension process, security boundaries, compatibility/versioning policy, custom adapter route, packaging opt-in, install/update/remove semantics, and release checklist.

## Progress

- 2026-08-31: Read repository instructions, canonical handoff, planning rules, architecture, relevant SDK/runner/management/packaging ADRs, Stage 17 prompt, current manifests, persistence, Runner, configuration page, packaging scripts, tests, and project conventions.
- 2026-08-31: Confirmed Stage 17 needs no central database migration and that upsert can preserve existing agent state. Identified the hard-coded bootstrap, global-only runtime manifest lookup, two-adapter UI, and two-agent publish script as the integration seams.
- 2026-08-31: Added the constrained schema catalog/validator, conservative immediate-child package scanner, `scan-agents` Runner command, package-local manifest integrity lookup, safe generic editor/secret actions, and focused persistence/Host/Runner coverage.
- 2026-08-31: Added the repository-local `hba-agent` template, optional Domain modifier, checked-in Sample Business Agent, explicit `packaging/agents.json`, sample publish flag, extension guide, ADR-0014, and installation/repair/smoke discovery hooks.
- 2026-08-31: Scaffolded and independently built/tested `InvoiceMonitor` in a temporary root; the generated four tests passed. The optional Domain-project scaffold also built and tested successfully.
- 2026-08-31: The real Sample Business Agent Runner integration registered disabled, saved immutable configuration, created/validated a manual schedule, launched the real child, and verified protocol events, hashes, summary, artifact, and revisions. Scanner integration verified conservative removal/disable with configuration retained.
- 2026-08-31: Required restore and Release build passed with 0 warnings/errors. The complete solution run passed 290 tests with 0 failures and one intentional opt-in browser-fixture skip; after one final non-object validation regression test was added, its focused four-test set passed. Final format verification passed.
- 2026-08-31: Production-default publish passed with 1,182 files and 563,275,078 bytes and excluded the sample. Sample-enabled publish passed with 1,395 files and 644,768,340 bytes and contained the expected executable/manifest/schema. Per the user's request to keep remaining validation small, a separate rendered-browser pass was not repeated; the Host integration suite covered the generic page, encoding, and secret non-echo behavior.

## Decisions

- Use a repository-local `dotnet new` template and a `--platform-root` string parameter so generated project references work both in the normal `agents/<Name>` location and isolated smoke roots.
- Keep built-in typed configuration validators/editors authoritative. Generic schema support is an opt-in fallback for simple schemas, not a replacement for domain validation.
- Use `format: hba-secret-reference` for opaque secret-reference fields. Configuration stores only the reference; the Host uses `ISecretStore` for the value and never reads it into the page.
- Prefer installed per-agent `manifest.json` for execution integrity and retain global `<id>.agent-manifest.json` only as a compatibility fallback.
- Disable missing/invalid packages and preserve their records. This provides uninstall visibility and recovery without cascading data deletion.
- Treat the checked-in sample as developer/operator evidence. Production publication excludes it unless an explicit flag is supplied.

## Validation

Required commands:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Stage-specific validation:

- Install the local template in an isolated custom hive and scaffold `InvoiceMonitor` with the exact Stage 17 command shape.
- Build and test the generated projects.
- Publish/copy the generated agent into a temporary configured `AgentDirectory`; run `scan-agents`; confirm it is registered disabled and audit evidence exists.
- Save a valid generic configuration, create/enable a schedule through production application services, enable the agent explicitly, and execute a real occurrence through Runner.
- Inspect persisted protocol events, summary, artifact metadata/hash, configuration revision/hash, executable hash/version, and audit history.
- Remove or invalidate the package, rescan, and confirm conservative disable with historical configuration/schedules/runs retained.
- Validate every explicit package manifest/schema and test sample-excluded and sample-included package composition.

Actual results are recorded above and in `docs/PROJECT_STATE.md`.

## Recovery and rollback

- This stage adds no EF migration. Rolling back source changes leaves existing database rows intact.
- Scanner reconciliation is conservative: it disables definitions rather than deleting histories. Restoring a valid package and rescanning can make it manageable again; explicit user enablement is still required for a newly installed definition.
- Template smoke and Runner proof use unique bounded temporary directories. Cleanup will validate the resolved root before removal.
- Publishing remains staging-first. The explicit catalog prevents accidental inclusion of arbitrary directories.

## Remaining risks and follow-up

- JSON Schema is deliberately a small supported subset. Complex schemas remain fully valid installation metadata but require a code-owned adapter before configuration can be edited.
- A malicious executable inside a package approved for the configured agent directory is not sandboxed; the scanner prevents traversal and records hashes but does not establish publisher trust or code signing.
- Prompt 17 is the last prompt currently present in `prompts/`; there is no checked-in next prompt unless repository state changes.
