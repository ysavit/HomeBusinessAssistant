# ADR-0018: Founder Scout code-owned simple mode

- Status: Accepted; focused AI settings amended by ADR-0019
- Date: 2026-09-25

## Context

Founder Scout already has durable configuration, onboarding, browser-account, discovery, processing, evaluation, and result-management capabilities. The combined first-run and specialized onboarding workflows, Windows Negotiate browser authentication, editable settings, optional AI provider setup, and agent activation created too much friction for the immediate goal: sign in to Startup School and run a conservative local founder scan.

Secrets and browser authentication state cannot safely be hardcoded. Startup School authentication must still be completed by the user in the dedicated headed browser profile.

## Decision

The original AI-disabled, display-only settings decisions below describe the initial simple-mode increment. ADR-0019 later adds a narrow explicit OpenAI settings and analysis surface without changing capture-first discovery or the code-owned safety policy.

Compile Founder Scout simple mode on for the current product increment.

- `FounderScoutDefaults.CreateConfiguration()` is the source of truth for safe operational settings.
- Host startup promotes that document through the immutable configuration service, enables Founder Scout for explicit manual execution, and reconciles one `startup-school-primary` browser account plus one `startup-school-default` discovery segment.
- Startup reconciliation preserves authentication health, timestamps, enforcement pauses, and discovery counters.
- Deep AI evaluation is disabled, so no provider key is required or accepted by the simple-mode UI.
- Live browser work uses the installed system Chrome channel. Authentication may traverse the official YC account host, but an authenticated marker is accepted only after the browser returns to the Startup School application host.
- The Founder Scout settings page is display-only. The specialized Founder Scout onboarding route redirects to that page, and crafted configuration/secret/setup/enablement changes are rejected.
- The visible product surface is temporarily limited to Founder Scout Overview and Candidates. Overview exposes one explicit `Start Founder Scout` action and a validated 1–60 second per-profile delay.
- Overview and Candidates render the durable active/terminal run state and poll every three seconds only while a run is active, so the owner can see discovery progress without a separate operations surface.
- The `start` command reuses a healthy dedicated browser session. When the session is missing or discovery proves it expired, it opens the official site for manual authentication and retries discovery once in the same Runner occurrence.
- The versioned Startup School adapter enters through `/cofounder-matching/candidate/next`, accepts only validated stable candidate routes (including the current nested candidate route), rejects navigation tabs, and may use the final browser URL as the identity when profile markup has no self-link.
- Discovery commits each captured profile before best-effort deterministic screening. `start` never invokes deep AI analysis, even when a provider is configured; a screening or AI-boundary failure cannot delete an already captured candidate.
- The command does not accept a password, create schedules, or start work at Host launch.
- Host management pages do not require Windows Negotiate while simple mode is active. Kestrel remains restricted to exact IPv4 loopback, exact Host and unsafe-Origin validation remain active, CSP remains active, mutations remain antiforgery protected, and no CORS policy is enabled.

## Consequences

The initial path has one non-secret per-run delay field and needs no API key. Retrieved candidates become visible and their count updates during execution without requiring a score or evaluation. Runtime records remain durable, while a code change plus rebuild is required to alter code-owned settings. Each changed code document creates a normal immutable configuration revision.

Removing per-user HTTP authentication broadens the local trust boundary: another local process capable of reaching loopback and obtaining an antiforgery token can use the UI. This is an explicit temporary usability tradeoff for a single-user workstation, not authorization for external binding. Remote UI access remains unsupported.

The previous onboarding implementation remains in source for later reactivation, but it cannot mutate Founder Scout while simple mode is enabled. No password, cookie, session export, API key, or invitation-send capability is added.

## Alternatives considered

### Keep the Stage 20 wizard and Windows Negotiate

Rejected for the current increment because it preserves the exact setup and access friction blocking first use.

### Hardcode credentials or browser state

Rejected because it would violate the secret, privacy, and browser-session boundaries and would not reliably survive site authentication changes.

### Automatically start discovery at Host launch

Rejected because live site access must remain an explicit user action and enforcement signals require visible, account-scoped handling.
