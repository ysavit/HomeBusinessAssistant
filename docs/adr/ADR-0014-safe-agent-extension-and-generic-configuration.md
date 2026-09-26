# ADR-0014: Safe agent extension and generic configuration

- Status: Accepted
- Date: 2026-08-31
- Stage: 17
- Owners: Home Business Assistant

## Context

The first release has a stable out-of-process SDK boundary, but built-in database seed, UI adapters, and packaging composition name Founder Scout and Wake Remote explicitly. Copying one of those agents would not establish safe discovery, default-disabled registration, schema validation, or a bounded UI path. Loading arbitrary assemblies into Host would also collapse the existing failure and trust boundaries.

## Decision drivers

- Preserve Runner-supervised independent processes and clean dependency direction.
- Prevent arbitrary directory traversal, recursive discovery, package execution during registration, and silent auto-run.
- Preserve configuration/schedule/run history across upgrades and removal.
- Provide useful configuration UI coverage without turning JSON Schema into an executable extension surface.
- Keep publishing explicit and reviewable.

## Decision

Agents remain folder-deployed independent executables. `scan-agents` inspects only immediate children of the explicitly configured agent directory. It requires directory/manifest identity, valid versioned manifest and configuration schema, safe contained non-reparse executable paths, bounded files, and SHA-256 readback. It never launches content. New definitions are disabled; valid updates preserve user state; invalid or missing packages disable but do not delete persisted history and create append-only audit evidence.

Runtime integrity prefers the package-local `manifest.json` and retains the existing global manifest naming as a compatibility fallback.

The platform supports a deliberately small JSON Schema subset for primitive fields, simple arrays, enums, bounds, defaults, descriptions, and opaque `hba-secret-reference` strings. Server-side parsing and validation are authoritative; Razor renders only platform-owned controls and encoded text. Unsupported complex schemas require a code-owned typed validator/editor adapter. Existing Founder Scout and Wake Remote typed adapters remain authoritative.

Source packaging uses the checked-in `packaging/agents.json` allow list. The Sample Business Agent is not included unless an explicit flag opts in. Registration does not create schedules or occurrences and never enables or runs a new agent.

## Alternatives considered

### In-process plugin assembly loading

Rejected because it gives extension code Host/Runner lifetime and privileges, weakens failure isolation, complicates dependency/version loading, and contradicts the established process protocol.

### Recursive convention-only source or install scanning

Rejected because unrelated or attacker-controlled directories could become package candidates and because package composition would no longer be reviewable.

### Fully dynamic JSON Schema UI

Rejected because arbitrary/complex schema vocabulary creates ambiguous validation and rendering behavior. The limited subset covers common settings while complex domain configuration stays typed.

### Delete definitions on package removal

Rejected because schedules, configurations, runs, artifacts, and audit history are operational evidence. Conservative disable supports diagnosis and recovery.

## Consequences

### Positive

- New agents have one tested scaffold and registration contract.
- Installation/update/removal remains observable and reversible at the definition level.
- Generic configuration is useful without admitting HTML/script/schema-code execution.
- Existing releases and built-in manifests remain compatible.

### Negative / tradeoffs

- Complex configuration requires platform code changes for a typed adapter.
- Folder packages are integrity-checked but not sandboxed or publisher-trusted.
- The package catalog and generated project references require deliberate maintenance.

### Risks and mitigations

- A malicious executable is placed in an approved package root — registration never executes it, new agents remain disabled, runs record hashes, and operators must control the per-user app directory. Future signing policy can add publisher trust.
- Repeated scans of missing packages create repeated removal audit rows — scans are explicit install/repair operations, not a high-frequency loop; audit remains bounded by operator actions.
- Schema regex causes expensive validation — patterns and input are bounded and evaluation uses a fixed timeout.

## Compatibility with baseline architecture

This preserves AGENTS.md process, Runner, persistence, secrets, and UI rules. It extends `docs/architecture.md` with a generic extension path and does not add a process, database, migration, distributed messaging, or in-process plugin system.

## Implementation and migration

Stage 17 adds the schema contracts/catalog/validator, safe registry scanner, Runner command, package-local integrity lookup, generic Razor editor, template, sample, explicit package catalog, and installer/repair scan. No EF migration is required. Existing records and global manifests remain valid.

## Validation

- Parser/validator and scanner persistence tests.
- Sample configuration, path, protocol, artifact, and cancellation tests.
- Real Runner child-process integration with persisted event/artifact/configuration/executable hashes.
- Generated Invoice Monitor restore/build/test smoke in an isolated template hive.
- Generic UI integration and rendered desktop/narrow browser QA.
- Sample-excluded/default and sample-included package validation.

## Follow-up work

- Publisher signing or a manifest publisher-trust policy may be added in a future stage.
- A future agent with complex settings must add and test a typed adapter rather than widening the generic vocabulary casually.
