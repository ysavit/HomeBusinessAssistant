# ADR-0020: Simple Scout console entry point

- Status: Accepted
- Date: 2026-10-01

## Context

The Host and Runner provide durable scheduling, audit, and a web UI, but these layers made the first founder search hard to understand and troubleshoot. The user requested one small console flow that searches, stores, analyzes, and exposes the local database.

## Decision

Add `FounderScout.SimpleCli` as an explicitly interactive local entry point. It invokes the existing independently executable agent command implementation with the same validated execution-input envelope and uses the existing Founder Scout SQLite schema and result service. A bounded `run` command performs capture/screening before targeting the candidates touched by that scan for AI; `list` shows persisted candidate summaries and the configured database path. Local `appsettings.json` holds the database path, OpenAI key, model, delay, founder context, per-run limit, and daily limit; the file is ignored by Git. The new-profile cap is 50 per run, with one model request at a time. A provider failure leaves capture complete and pending AI work saved. Authentication stays in a dedicated Chrome profile. The console uses the existing private temporary input manager, which cleans up after each command.

The console does not create central scheduled occurrences or central run history. Its live progress and terminal summary are printed directly, and Founder Scout domain data remains in `founders.db`. It must not run concurrently with a Host Scout occurrence using the same browser profile. It does not send invitations.

## Consequences

The simple path shares data with the larger application and adds no migration. Captures survive AI provider failures. Existing platform run history does not include console invocations, so the console is for explicit interactive use, not scheduled execution. `list` opens the existing database through a read-only SQLite connection without bootstrap migrations, and the user can also inspect it with a SQLite client. The owner's evaluation brief is code-owned for new configurations and included in the example config; existing Host revisions preserve their saved context until changed through Settings. OpenAI rate/quota limits remain external and are reported clearly.
