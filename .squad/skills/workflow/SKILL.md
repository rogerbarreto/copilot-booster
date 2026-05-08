# Workflow Pipeline — Squad Skill

> Structured, lane-based agent orchestration for predictable development workflows.

## Domain

orchestration, workflow, pipeline, TDD, code-review

## Confidence

🟡 MEDIUM — Validated in design, pending multi-session production use.

## Source

manual — Designed from Squad SDK routing primitives and coordinator spawn protocol.

## Overview

This skill enables **deterministic multi-agent pipelines** on top of Squad's existing
coordinator. Instead of free-form routing (coordinator decides who runs next), the
workflow defines lanes with explicit sequencing, parallelism, gates, and human checkpoints.

**No code changes to Squad required.** The workflow is injected via the session prompt.
The coordinator reads the pipeline definition, spawns agents per the lane graph, and
uses structured `WORKFLOW_RESULT` tags from agent output to decide branching.

## Pipeline Notation

```
SYMBOL                  MEANING
───────────────────────────────────────────────────────────────────
→                       Sequential: wait for left to finish, then run right
[ A | B ]               Fan-out: run A and B in parallel (background)
GATE(condition?)        Conditional branch evaluated from WORKFLOW_RESULT
  → YES: ...            Branch taken when PASS/YES/APPROVED
  → NO: ...             Branch taken when FAIL/NO/REJECTED
HITL(question)          Pause lane — ask user, resume on answer
LANE N [parallel]       Starts immediately, runs independently
LANE N [after: X, Y]    Waits for lanes X and Y to complete before starting
```

## How It Works

### Execution Model

1. **Lanes = parallelism.** Lanes marked `[parallel]` start simultaneously.
   Their first step spawns as `mode: "background"` in the same tool-calling turn.

2. **Steps within a lane = sequential.** Each step runs `mode: "sync"`.
   The next step starts only after the current one returns `WORKFLOW_RESULT`.

3. **Cross-lane deps `[after: X, Y]`.** The lane waits until ALL dependency
   lanes reach their final PASS before spawning its first step.

4. **Fan-out `[ A | B ]`.** Both agents spawn as `mode: "background"`.
   The lane advances when ALL fan-out agents return PASS.

### Handoff Protocol

Every agent receives a `WORKFLOW CONTEXT` block appended to their spawn prompt:

```
WORKFLOW CONTEXT:
You are Lane {L}, Step {S} in a workflow pipeline.
Full pipeline:
{entire WORKFLOW DEFINITION}

Your lane: {this lane's definition}
Predecessor output: "{summary of previous step's result}"
Cross-lane context: "{summaries from completed dependency lanes}"

AFTER completing your work, end your response with EXACTLY:
---
WORKFLOW_RESULT: PASS | FAIL | NEEDS_REVISION
WORKFLOW_SUMMARY: {one-line summary of outcome}
WORKFLOW_NEXT: {who runs next per the pipeline, or LANE_COMPLETE}
WORKFLOW_ARTIFACTS: {files created/modified}
---
```

### Gate Evaluation

The coordinator reads `WORKFLOW_RESULT` and follows the pipeline branch:
- `PASS` / `YES` / `APPROVED` → YES branch or advance to next step
- `FAIL` / `NO` / `REJECTED` → NO branch or loop back
- `NEEDS_REVISION` → treated as FAIL with expectation of retry

### Loop Detection

Track execution count per step per lane. If any step runs > 3 times, STOP:
```
⚠️ Loop in Lane {L} at @{agent}. Ran {count} times. Last: {result}.
```
Ask user: continue / skip step / abort lane / abort all.

### HITL Gates

When a lane reaches `HITL(question)`, that lane pauses. Other lanes continue.
The user is presented the agent's output and the question. Lane resumes on answer.

### Lane Status Board

After each step completes, the coordinator prints:
```
WORKFLOW STATUS
───────────────────────────────────────────
Lane 1: ✅ @neo(PASS) → ⏸️ HITL(waiting for user)
Lane 2: ✅ @tank(PASS) → 🔄 @oracle(running)
Lane 3: ✅ @tank(PASS) → ✅ @oracle(PASS)
Lane 4: ⏳ waiting for lanes 2, 3
Lane 5: ⏳ waiting for lane 4
Lane 6: ⏳ waiting for lane 5
───────────────────────────────────────────
```

## Default Pipeline — copilot-booster

The standard TDD development workflow for this repo:

```
LANE 1 [parallel]: @neo(review scope & architecture) → HITL(approve design?)
LANE 2 [parallel]: @tank(write unit tests TDD RED) → @oracle(validate test quality & SOLID)
LANE 3 [parallel]: @tank(write integration tests TDD RED) → @oracle(validate test quality)
LANE 4 [after: 1, 2, 3]: @trinity(implement services GREEN) → @neo(code review)
LANE 5 [after: 4]: @morpheus(implement UI) → @oracle(UI/code quality review) → [@trinity(fix issues) | @tank(E2E tests)]
LANE 6 [after: 5]: @tank(run Playwright E2E) → @oracle(validate E2E) → @morpheus(final UX sign-off)
```

### Agent Mapping

| Lane | Agent | Role in Pipeline | Mode |
|------|-------|-----------------|------|
| 1 | Neo | Architecture review, scope approval | sync → HITL |
| 2 | Tank → Oracle | Unit tests (RED), then quality validation | sync chain |
| 3 | Tank → Oracle | Integration tests (RED), then quality validation | sync chain |
| 4 | Trinity → Neo | Service implementation (GREEN), then code review | sync chain |
| 5 | Morpheus → Oracle → [Trinity \| Tank] | UI implementation, quality review, parallel fixes/E2E | sync → fan-out |
| 6 | Tank → Oracle → Morpheus | Playwright E2E, validation, final UX sign-off | sync chain |

### Build/Test Commands

Agents should use these for validation:
- **Build:** `dotnet build --tl:off`
- **Unit tests:** `dotnet test tests/CopilotBooster.Tests.csproj --tl:off`
- **Integration tests:** `dotnet test tests/CopilotBooster.IntegrationTests.csproj --tl:off`
- **Format check:** `dotnet format --verify-no-changes`

## When to Use

- Feature development requiring architecture review, TDD, and UI work
- Bug fixes where you want tests before implementation (TDD RED-GREEN)
- Any task where multiple agents touch shared systems (services + forms + tests)

## When NOT to Use

- Quick one-agent fixes (use free-form routing instead)
- Documentation-only changes (route directly to Niobe)
- Release management (route directly to Neo)

## Anti-Patterns

- Don't modify agent charters to embed workflow logic — keep workflows session-scoped
- Don't skip the HITL gate in Lane 1 — architecture approval prevents rework
- Don't run Lane 4 before Lanes 2+3 — tests must exist before implementation (TDD)

## Tools

| Tool | Description | When |
|------|-------------|------|
| `squad_route` | Hand off to next agent in pipeline | Agent uses this to explicitly route follow-up work |
| `squad_decide` | Record workflow gate decisions | When a GATE evaluation produces an architectural decision |
| `squad_memory` | Record learnings from workflow execution | When an agent discovers patterns useful for future workflows |
