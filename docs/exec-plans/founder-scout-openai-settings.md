# Founder Scout OpenAI settings and explicit analysis

## Purpose and user-visible outcome

Add one focused Founder Scout Settings page where the local owner can select an OpenAI Responses model, paste non-secret founder context copied from a classic ChatGPT conversation, protect an OpenAI Platform API key, and explicitly start AI evaluation for stored candidates. Discovery remains capture-first and independent from provider availability.

## Current repository state

- Founder Scout simple mode currently promotes a complete code-owned configuration at every Host startup.
- Deep evaluation already exists behind the official OpenAI .NET Responses client and accepts secrets only through the Runner's private resolved-secret envelope.
- The visible simple-mode UI currently exposes Overview and Candidates only; the legacy Agent configuration page is read-only and rejects secrets.
- The `start` command intentionally captures and screens without invoking AI.
- Existing stored candidates can be Monitor, FilteredOut, or ManualReview and therefore require an explicit first-analysis queue action before `analyze --phase deep` can claim them.

## Scope and non-goals

Implement a minimal OpenAI-only settings surface, immutable non-secret configuration revisions, DPAPI-backed write-only key mutation, local copied context, explicit bounded queueing, and Runner-backed deep analysis. Do not import ChatGPT Memory or chats, add Azure setup to this simplified page, expose keys, change capture-first Start, auto-run analysis after discovery, add invitation sending, or add a database migration.

## Design and data flow

```text
Founder Scout Settings
  -> save model + copied context -> immutable assistant.db configuration revision
  -> protect API key -> DPAPI CurrentUser secret file (never read by Razor)
  -> Analyze candidates
       -> queue bounded screened/unanalyzed rows in founders.db
       -> create durable manual `analyze --phase deep` occurrence
       -> Runner resolves key into private execution input
       -> FounderScout official OpenAI Responses adapter
       -> strict validation + deterministic C# scores + persisted result/drafts
```

Simple-mode startup continues to restore safe code-owned discovery/browser policy but preserves the focused OpenAI model, batch, and copied-context fields. The provider is fixed to OpenAI with the official default endpoint. The copied ChatGPT text is local persona context; it is not ChatGPT account memory or conversation access.

## Milestones

- [x] Preserve a bounded editable OpenAI subset across simple-mode startup and enable analysis in defaults.
- [x] Add a focused settings page and visible Founder Scout Settings tab.
- [x] Add separate protected API-key set/delete actions with no echo.
- [x] Add explicit bounded queueing and Runner dispatch for stored candidates.
- [x] Add regression tests for configuration/context validation, queueing, secret-free rendering, and antiforgery.
- [x] Run standard validation and rendered desktop/narrow QA.
- [x] Update `docs/PROJECT_STATE.md` with verified results.

## Detailed steps

1. Extend the typed founder persona with optional bounded multiline additional context so it participates in the existing persona hash, cache invalidation, and model payload.
2. Change safe simple defaults to enabled OpenAI evaluation with a current lower-cost structured-output model while keeping discovery free of AI calls.
3. Merge only the supported OpenAI subset from the current revision during startup; retain code ownership of browser, source, screening, privacy, and invitation safety policy.
4. Add `/FounderScout/Settings` with model, batch size, copied context, key existence/set/delete, readiness, and an explicit charged-provider confirmation.
5. Add a focused results command that queues only active candidates with a current snapshot, matching screening evidence, and no prior evaluation.
6. Dispatch deep analysis only through the existing durable manual-occurrence and central Runner boundary.

## Decisions

- OpenAI Platform API keys are supported directly; Azure AI Foundry is not required on this simplified page.
- API keys remain outside configuration/SQLite and are never redisplayed.
- ChatGPT Memory is not imported. User-pasted text becomes versioned local context and is included in the existing strict evaluation request.
- Start remains capture-and-screen only. Provider failures cannot prevent candidate storage.
- Existing deterministically filtered/monitor/manual-review candidates may be queued only by the explicit owner action on this page.

## Validation

Run from the repository root:

```powershell
dotnet restore
dotnet build HomeBusinessAssistant.sln -c Release --no-restore
dotnet test HomeBusinessAssistant.sln -c Release --no-build
dotnet format HomeBusinessAssistant.sln --verify-no-changes --no-restore
```

Also verify the Settings page at desktop and narrow widths, zero secret echo, antiforgery rejection, immutable save behavior, queue persistence, and a fake-provider Runner analysis. A real OpenAI call remains opt-in and must not be claimed without an owner-provided protected key and explicit action.

Verified on 2026-09-30:

- Restore completed with all 29 projects up to date.
- Release build completed with zero warnings and zero errors.
- Full solution tests passed 338 with zero failures; three interactive Host fixtures remained intentionally skipped and the live-provider test remained explicit/opt-in.
- Format verification exited zero.
- Real temporary SQLite coverage proved bounded first-analysis queueing; evaluation tests proved local context is included in the provider payload.
- Repository Playwright validated immutable save, a real temporary DPAPI key round trip with no echo, antiforgery, analysis readiness, no overflow, and zero unexpected browser errors at 1440x1000 and 390x844. Browser-plugin control was unavailable, so the documented fallback was used.
- The updated per-user Founder Scout package scanned valid and a real installed-package diagnostic completed with exit code zero.
- Release Host live, Overview, and Settings routes returned HTTP 200 on `127.0.0.1:5180`. No live OpenAI request was made.

## Recovery and rollback

Configuration revisions are immutable. Removing the focused merge and page stops future edits; an earlier revision can be promoted through the existing configuration service. Deleting the protected key does not affect candidates or revisions. Queued candidates remain durable and can be analyzed later; provider failure releases them according to the existing bounded retry/attention policy.

## Remaining risks and follow-up

- Model availability depends on the owner's OpenAI API project and billing.
- Pasted context quality is user-controlled; strict grounding, protected-attribute exclusion, and C# arithmetic remain authoritative.
- Live OpenAI compatibility is not validated unless the user explicitly runs the action with a real key.
- Pause/resume remains unavailable in protocol 1.0; Stop uses the existing Runner cancellation path.
