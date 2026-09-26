# ADR-0010: Founder Scout Responses provider and deterministic validation boundary

- Status: Accepted
- Date: 2026-08-30
- Stage: 12
- Owners: Home Business Assistant

## Context

Founder Scout needs strict structured evaluation from configurable OpenAI or Azure OpenAI deployments without coupling application scoring to SDK types or trusting model arithmetic. Provider keys are stored through the central Windows-protected secret store, while the independently executable agent receives one private Runner-owned execution input.

## Decision drivers

- Both configured providers must support the same strict JSON Schema contract.
- Provider/network details and secret material must remain outside Domain and Application.
- Every scoring and invitation fact must be grounded in the protected-attribute-free Stage 11 input.
- Provider failure, malformed structure, unsupported evidence, and draft validation require distinct bounded handling.
- Configuration, prompt, persona, scorecard, schema, and provider-policy changes must invalidate cache deterministically.

## Decision

Use the official stable `OpenAI` .NET SDK Responses client behind `IFounderEvaluationModelClient` for OpenAI and Azure OpenAI. Infrastructure configures the SDK over an `IHttpClientFactory` transport. OpenAI uses its v1 Responses endpoint. Azure uses the configured resource endpoint normalized to `/openai/v1/`, sends the configured deployment as `model`, and uses the v1 route's implicit versioning. The schema keeps optional `apiVersion` metadata for a future focused compatibility adapter, but the current v1 adapter rejects a non-empty value instead of silently ignoring or inventing one.

Runner resolves opaque configuration references through `ISecretStore` and writes only resolved values that exist into its current-user private execution-input envelope. The agent reads the value by its opaque reference; neither the reference-to-value map nor provider headers enter logs, database rows, protocol events, artifacts, or summaries. Existing cleanup removes the private file after the child exits.

The provider requests `text.format` strict JSON Schema and `store=false`, extracts only the bounded output text, and maps authentication/configuration/not-found, throttling, timeout/network, 5xx, refusal/incomplete, and invalid-output cases into application-owned failure categories. Model output is deserialized with unknown members disallowed and then checked for exact key sets, bounds, grounding, protected markers, and numeric claims.

C# calculates every total, penalty, confidence aggregate, activity bucket, priority, and recommendation. The behavior-complete input hash excludes correlation IDs and secrets. One repair allowance is shared by malformed/evidence/draft validation; transient provider failures use at most three total attempts.

## Alternatives considered

### Azure.AI.OpenAI 2.1 for Azure and another client for OpenAI

The locally available stable Azure package predates the current Responses API surface, would split equivalent provider behavior across APIs, and does not provide the selected strict Responses contract. It was rejected.

### A handwritten REST adapter

A focused adapter was viable, but current official .NET SDK documentation supports Responses strict schema for the Azure v1 endpoint as well as OpenAI. Duplicating request/response protocol parsing was not justified.

### Trust provider totals or persist unvalidated output

This would allow arithmetic drift and fabricated evidence to affect ranking. It was rejected; invalid output is never a completed ranked evaluation.

## Consequences

### Positive

- One provider-neutral application path serves both configured endpoints.
- Strict schema plus deterministic post-validation produces bounded, grounded persistence.
- Secret resolution stays in the central protected-storage boundary and private process handoff.
- Cache invalidation and score arithmetic are reproducible without a provider call.

### Negative / tradeoffs

- The SDK Responses namespace may carry a preview diagnostic even in a stable package; the suppression is isolated to the Infrastructure adapter and documented by tests.
- Provider/region/model combinations still require opt-in live verification.
- Conservative evidence matching can require manual review for legitimate paraphrases.

## Follow-up work

- Stage 13 should render provider/version/validation metadata without exposing raw secret or request headers.
- A future Entra ID credential option can be added as a new secret/authentication policy without changing Application contracts.
