# ADR-0001: Agent contract ownership and event discrimination

- Status: Accepted
- Date: 2026-08-29
- Stage: 01
- Owners: Home Business Assistant

## Context

The runner and independently executable agents need one compile-time and wire-level definition of agent identifiers, manifest policy, invocation context, exit codes, and JSONL events. The baseline architecture identifies `HomeBusinessAssistant.AgentSdk` as the shared process contract but does not specify whether platform identifiers should be duplicated there or how `System.Text.Json` should select polymorphic event payloads while retaining unknown future event types.

Using serializer runtime type names would couple the wire format to .NET namespaces and permit unsafe or accidental type activation. Using only untyped JSON would preserve compatibility but remove compile-time payload guarantees. Duplicating `AgentId`, run IDs, or `ConcurrencyPolicy` in the SDK would introduce conversion and equality boundaries before persistence is added.

## Decision drivers

- One definition of platform identities and persisted policy semantics.
- An inward clean-architecture dependency direction.
- A stable language-neutral JSON contract that does not contain CLR type names.
- Strict known-payload validation with graceful forward compatibility.
- No ASP.NET Core, EF Core, Windows, or third-party serialization dependency in the SDK.

## Decision

`HomeBusinessAssistant.Domain` owns `AgentId`, `AgentVersion`, `AgentRunId`, `OccurrenceId`, and the stable platform status/policy enums. `HomeBusinessAssistant.AgentSdk` takes an inward project reference on Domain and owns manifests, command parsing, exit codes, JSONL event contracts, serialization, validation, writing, and the minimal agent execution session. Runner and agent executables continue to depend on the SDK.

Protocol 1.0 uses an explicit lower-case `type` discriminator selected by an allow-listed switch in `AgentEventSerializer`. Known discriminators deserialize to sealed typed event records. An unrecognized discriminator deserializes to `UnknownAgentEvent` and retains its `payload` as a cloned `JsonElement`. Serialization always uses the event's runtime type from the SDK allow list; arbitrary runtime type-name metadata is never accepted or emitted.

JSON contract enums use documented stable member names and reject numeric enum JSON. Domain enum members also have explicit numeric values for future persistence mapping.

## Alternatives considered

### Duplicate process-facing identifier and policy types in AgentSdk

This would keep the SDK project independent of Domain but require mappings at every runner, manifest, and persistence boundary. Those duplicate types could drift in validation, equality, or numeric policy meaning. It was not selected.

### System.Text.Json attribute-based derived-type registration

Attribute registration is concise for a closed hierarchy, but its configured unknown-derived-type behavior does not by itself provide the required typed `UnknownAgentEvent` carrying raw payload JSON. It also distributes compatibility metadata across the model. It was not selected.

### Embed CLR type names in JSON

This would make deserialization convenient but would leak implementation names into a language-neutral process protocol and increase unsafe type-resolution risk. It was rejected.

## Consequences

### Positive

- Runner and agents compare and serialize the same validated identifiers.
- The protocol discriminator and payload mapping are auditable in one place.
- Future minor-version event types do not crash the current reader and can be retained for diagnostics.
- The wire format is independent of assembly and namespace names.

### Negative / tradeoffs

- `AgentSdk` now has one project reference rather than being a completely standalone assembly.
- Every new known event type requires an explicit model, serializer switch entry, validator branch, documentation, and tests.
- Unknown top-level extension properties are tolerated but only the unknown event's payload is preserved by the current model.

### Risks and mitigations

- A new event could be added to only part of the mapping — round-trip tests cover every documented discriminator and future changes must extend that matrix.
- Domain changes could affect the process contract — explicit numeric enum tests and value-object validation tests make changes visible.
- Raw future payloads could contain sensitive material — size limits still apply, and callers must redact before emitting any payload.

## Compatibility with baseline architecture

This clarifies the supplied clean-architecture direction. The SDK depends inward on pure Domain contracts; Domain does not depend on the SDK or any outer layer. It preserves the process boundaries and stdout/stderr rules in `AGENTS.md` and `docs/architecture.md`. The architecture document is updated with the exact dependency and the canonical protocol guide.

## Implementation and migration

No data migration is required in Stage 01. `HomeBusinessAssistant.AgentSdk.csproj` references `HomeBusinessAssistant.Domain.csproj`. `AgentEventSerializer`, `AgentEventValidator`, `AgentEventWriter`, and their tests implement the selected mapping. Later persistence stages must use the documented stable enum values or an explicit stable string converter rather than implicit enum ordinals.

## Validation

- Architecture tests enforce the `AgentSdk -> Domain` reference and absence of cycles.
- Domain tests enforce identifier/version validation and explicit enum values.
- SDK tests round-trip all ten known events and preserve an unknown event payload.
- Writer tests cover concurrent sequencing, one-object-per-line output, fake time, flushing, cancellation, and structured post-completion errors.
- Founder Scout and Wake Remote tests deserialize their complete `protocol-demo` streams.

## Follow-up work

- Stage 02 will define persistence conversions for identifiers and enums.
- Stage 04 will add runner-side streaming ingestion, lifecycle checks across multiple records, and authoritative process supervision.
- A future protocol minor version must extend the allow list without changing existing discriminator meanings.
