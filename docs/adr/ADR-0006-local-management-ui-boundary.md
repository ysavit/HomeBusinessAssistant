# ADR-0006: Local management UI query and command boundary

- Status: Accepted
- Date: 2026-08-30

## Context

The Stage 08 loopback UI needs bounded views spanning installed agents, immutable configuration revisions, schedules, occurrences, runs, events, metrics, artifacts, audit, wake state, and database health. Existing focused repositories are deliberately write-oriented and should not become a generic repository or leak EF entities into Razor Page models. Mutations must preserve the existing scheduler, Runner, wake, power, audit, secret, and persistence boundaries.

Configuration editing adds two specific risks. A form can overwrite a revision promoted after the page was loaded, and a secret entry can accidentally be rendered or copied into canonical configuration JSON. Run output and artifact metadata are also untrusted and must not become HTML or arbitrary filesystem access.

## Decision

- Add one Application-owned `IManagementQueryService` with bounded, stable, task-specific read models. Infrastructure implements it with short-lived `AsNoTracking` EF queries, server-side filters, stable ordering, and explicit page-size limits.
- Add an Application-owned `IManagementCommandService` that composes existing focused repositories and use cases. Schedule mutations validate and reconcile schedule plus wake state; manual and retry actions create durable occurrences and dispatch only the central Runner abstraction.
- Keep Razor Page models thin. They translate typed forms to versioned domain/application records, invoke the management boundary, and use Post/Redirect/Get for successful mutations.
- Keep immutable canonical JSON as the durable agent-configuration contract, but expose typed Wake Remote and Founder Scout forms rather than a raw JSON editor. Every edit carries the displayed revision number and hash; the transactional save rejects a stale pair.
- Handle secret entry in a separate action. The page can ask only whether the opaque secret reference exists and can never retrieve or round-trip its value.
- Render agent/audit/protocol text through Razor encoding. Artifact downloads first prove that metadata belongs to the requested run, then rely on the root-confined artifact store and attachment disposition.
- Do not expose destructive schedule-history deletion in V1. Disable or pause retains occurrences, runs, configuration identities, and audit evidence.

## Consequences

- UI queries may join several central tables without weakening write invariants or introducing a generic repository.
- An agent-specific typed adapter is required whenever a new configuration shape is made editable.
- Management actions remain locally auditable and consistent with tray/background behavior, while SQLite claims and leases remain authoritative for races.
- The local Host still has no user-account system and is intentionally bound only to exact IPv4 loopback. Antiforgery, CSP, safe text rendering, and DPAPI protect different threat boundaries; none turns same-user code execution into a security boundary.
- Founder Scout candidate/results pages remain separate Stage 13 work even though its baseline operational configuration is editable now.
