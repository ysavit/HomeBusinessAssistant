# ADR-0020: Simple Scout console entry point

- Status: Accepted
- Date: 2026-10-01

## Context

The Host and Runner provide durable scheduling, audit, and a web UI, but these layers made the first founder search hard to understand and troubleshoot. The user requested one small console flow that searches, stores, analyzes, and exposes the local database.

## Decision

Add `FounderScout.SimpleCli` as an explicitly interactive local entry point. It invokes the existing independently executable agent command implementation with the same validated execution-input envelope and uses the existing Founder Scout SQLite schema and result service. A bounded `run` command performs capture/screening before targeting the candidates touched by that scan for AI; separate `scan`, `analyze`, `list`, and `db-path` commands support recovery and inspection. The default new-profile and AI batch limit is five, with one model request at a time. Authentication stays in a dedicated Chrome profile. The console reads the current-user protected key or a process environment key and uses the existing private temporary input manager, which cleans up after each command.

The console does not create central scheduled occurrences or central run history. Its live progress and terminal summary are printed directly, and Founder Scout domain data remains in `founders.db`. It must not run concurrently with a Host Scout occurrence using the same browser profile. It does not send invitations.

## Consequences

The simple path shares data with the larger application and adds no migration. Captures survive AI provider failures. Existing platform run history does not include console invocations, so the console is for explicit interactive use, not scheduled execution. The user can inspect candidates through `list` or a read-only SQLite connection. OpenAI rate/quota limits remain external and are reported clearly.
