# ADR-0019: Founder Scout focused OpenAI settings

- Status: Accepted
- Date: 2026-09-30

## Context

ADR-0018 deliberately reduced Founder Scout to capture-first discovery with a display-only configuration. That made stored profiles reliable and easy to retrieve, but it left the already implemented evaluation pipeline inaccessible: users could not supply a standard OpenAI Platform key, add their local founder context, or explicitly evaluate existing candidates from the focused UI.

The API cannot read a user's ChatGPT Memory or prior ChatGPT conversations. API credentials are secrets and cannot be placed in immutable configuration, logs, HTML, or source. Discovery must continue to work when the provider is unavailable.

## Decision

Add `/FounderScout/Settings` as the only editable Founder Scout settings surface in simple mode.

- Keep browser, discovery, source, screening, privacy, retention, and invitation-safety policy code-owned.
- Preserve only a bounded OpenAI model identifier, analysis batch size, and optional local founder context when Host startup promotes the simple-mode configuration.
- Fix the simple-mode provider to the standard OpenAI Responses API through the existing official OpenAI .NET SDK adapter. Azure AI Foundry remains available in the broader pipeline but is not exposed by this focused page.
- Store the API key only through `ISecretStore` under the existing opaque Founder Scout secret reference. The Razor page may query existence, set a replacement, or delete it, but must never receive or display its value.
- Treat owner-pasted ChatGPT text as explicit local persona context. Do not claim or attempt automatic access to ChatGPT Memory or conversations.
- Keep **Start Founder Scout** capture-and-screen only. AI evaluation occurs only after the owner confirms **Analyze candidates now** and its provider-cost warning.
- On that explicit action, atomically queue a bounded set of active candidates that have a current snapshot and matching deterministic screening but no prior evaluation, including previously monitored or filtered candidates. Then dispatch a durable manual `analyze --phase deep` occurrence through the central Runner.
- Show analysis run progress through the existing persisted run/event projection. Preserve captured candidates when provider or prompt processing fails.
- Do not add automatic scheduling, Host-start analysis, invitation sending, or a database migration.

## Consequences

Founder Scout can now produce AI evidence, scores, summaries, and human-review-only drafts from already captured candidates using a normal OpenAI Platform key. Non-secret changes remain immutable and auditable; the key remains current-user protected and non-echoing. Copied context participates in the persona hash, so a changed context naturally invalidates evaluation reuse.

The local owner must manage API project access, billing, and model availability. Explicitly queued candidates remain durable if Runner dispatch or provider execution fails and can be retried through the existing bounded analysis policy. The Settings page expands the loopback local trust surface for a sensitive write, so existing exact-loopback, Host/Origin, CSP, antiforgery, and no-CORS controls remain mandatory.

## Amendment: candidate-level refresh and analysis

The explicit **Analyze candidates now** action now lives on Candidates. A separate **Analyze** action on each row dispatches `analyze-candidate` through Runner. That command refreshes the selected source profile using its existing dedicated account, commits the capture, screens exactly the new snapshot when one is created, and claims only the selected candidate for AI evaluation. Explicit repeat evaluations receive a unique run-scoped input hash while retaining the ordinary behavior-input hash for future change detection. Both actions retain antiforgery and provider-cost confirmation. The Candidates page shows active phase progress, the latest run summary, and the latest AI run summary even when a later discovery run completed. Settings remains limited to model, context, batch size, and protected-key management. No migration or automated invitation behavior is introduced.

## Alternatives considered

### Re-enable the complete Stage 20 onboarding wizard

Rejected for the immediate simple-mode workflow because it restores unrelated setup steps and configuration complexity.

### Read ChatGPT Memory or reuse ChatGPT browser sessions

Rejected because the API does not provide that account context and browser-session extraction would violate the product's secret and browser boundaries.

### Run AI automatically after discovery

Rejected because provider use has cost and failure modes, and capture reliability must remain independent from AI availability.
