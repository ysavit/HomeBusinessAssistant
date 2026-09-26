# Execution Plans

Use an ExecPlan for a stage that changes more than one project, introduces a process boundary, changes a database schema, integrates Windows APIs, or requires a multi-step migration.

Create plans under:

```text
docs/exec-plans/stage-XX-short-name.md
```

An ExecPlan is a living implementation document. A developer with only the current repository and the plan must be able to resume the work.

## Required sections

### Purpose and user-visible outcome

State the concrete capability delivered by the stage and how a user verifies it.

### Current repository state

Record relevant projects, files, contracts, migrations, tests, and known gaps discovered during inspection. Do not repeat speculative architecture as fact.

### Scope and non-goals

List what this stage implements and what is deliberately deferred.

### Design and data flow

Describe process boundaries, dependency direction, state transitions, persistent data, security boundaries, failure handling, and cleanup. Include a compact text diagram when useful.

### Milestones

Break implementation into observable milestones. Each milestone must leave the repository buildable or identify the smallest expected temporary break and how it will be resolved immediately.

### Detailed steps

Name the files/projects expected to change, the behavior to add, migration strategy, and tests. Update this section when discoveries alter the approach.

### Progress

Maintain checkboxes with timestamps or concise status notes. Update after each meaningful milestone.

### Decisions

Record significant decisions, alternatives considered, and why the selected approach fits V1.

### Validation

List exact commands, test scenarios, expected output, and manual checks. Record actual results when completed.

### Recovery and rollback

Explain how to recover from failed migrations, interrupted runs, partial files, task-registration failures, or incompatible configuration.

### Remaining risks and follow-up

Record known limitations that are genuinely deferred and point to the stage that addresses them.

## Planning rules

- Read the entire relevant code path before writing the plan.
- Keep the plan self-contained and current.
- Prefer a small proof of concept when a Windows API, browser behavior, or package choice is uncertain.
- Do not use the plan as a reason to stop before implementation. Continue through code, tests, and validation unless explicitly asked for planning only.
- Do not hide changed assumptions. Update the plan and decision log.
- Do not mark a milestone complete until the behavior and its tests exist.

## Synchronization with project state

At the start of a stage, reconcile the plan's **Current repository state** with `docs/PROJECT_STATE.md` and direct repository inspection. At the end of a stage:

- update the ExecPlan with actual milestone and validation results;
- update `docs/PROJECT_STATE.md` with the concise cross-session handoff;
- keep detailed design/implementation reasoning in the ExecPlan rather than duplicating it in project state;
- identify partial completion, blockers, manual verification, and the exact next prompt honestly.

An ExecPlan may describe work in progress. `docs/PROJECT_STATE.md` must describe only the latest verified repository state.
