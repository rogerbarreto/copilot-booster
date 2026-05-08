# Workflow Session Prompt — copilot-booster

> Copy everything below the --- line and paste as your first message in a Squad session.

---

I need to run a **workflow session**. Agents execute in defined lanes —
some parallel, some sequential, some with cross-lane dependencies.
Each agent's output determines the next step in its lane.

Read `.squad/skills/workflow/SKILL.md` for the full protocol before proceeding.

### WORKFLOW DEFINITION

```
LANE 1 [parallel]: @neo(review scope & architecture) → HITL(approve design?)
LANE 2 [parallel]: @tank(write unit tests TDD RED) → @oracle(validate test quality & SOLID)
LANE 3 [parallel]: @tank(write integration tests TDD RED) → @oracle(validate test quality)
LANE 4 [after: 1, 2, 3]: @trinity(implement services GREEN — make tests pass) → @neo(code review)
LANE 5 [after: 4]: @morpheus(implement UI) → @oracle(UI/code quality review) → [@trinity(fix issues) | @tank(E2E tests)]
LANE 6 [after: 5]: @tank(run Playwright E2E) → @oracle(validate E2E) → @morpheus(final UX sign-off)
```

### RULES FOR THIS SESSION

**Execution model:**

1. **Lanes are the unit of parallelism.** Lanes marked `[parallel]` start
   simultaneously — spawn their first step as `mode: "background"` in
   the SAME tool-calling turn. Lanes marked `[after: X, Y]` wait until
   lanes X and Y both reach their final PASS before starting.

2. **Steps within a lane are sequential.** Within a lane, each step runs
   in `mode: "sync"` — the next step starts only after the current one
   completes and returns WORKFLOW_RESULT.

3. **Fan-out `[ A | B ]` within a lane.** When a step produces a fan-out,
   spawn both agents as `mode: "background"` in the same turn. The lane
   advances once ALL fan-out agents return PASS. If ANY returns FAIL,
   the lane follows the GATE/NO branch (or stops if none defined).

**Handoff protocol:**

4. **Workflow context.** When spawning each agent, append this block to
   their spawn prompt AFTER the task and charter:

   ```
   WORKFLOW CONTEXT:
   You are Lane {L}, Step {S} in a workflow pipeline.
   Full pipeline:
   LANE 1 [parallel]: @neo(review scope & architecture) → HITL(approve design?)
   LANE 2 [parallel]: @tank(write unit tests TDD RED) → @oracle(validate test quality & SOLID)
   LANE 3 [parallel]: @tank(write integration tests TDD RED) → @oracle(validate test quality)
   LANE 4 [after: 1, 2, 3]: @trinity(implement services GREEN) → @neo(code review)
   LANE 5 [after: 4]: @morpheus(implement UI) → @oracle(review) → [@trinity(fix) | @tank(E2E)]
   LANE 6 [after: 5]: @tank(Playwright E2E) → @oracle(validate) → @morpheus(sign-off)

   Your lane: {this lane's definition}
   Predecessor output: "{summary of previous step's result in this lane}"
   Cross-lane context: "{summaries from completed dependency lanes, if any}"

   AFTER completing your work, end your response with EXACTLY:
   ---
   WORKFLOW_RESULT: PASS | FAIL | NEEDS_REVISION
   WORKFLOW_SUMMARY: {one-line summary of outcome}
   WORKFLOW_NEXT: {who runs next per the pipeline, or LANE_COMPLETE}
   WORKFLOW_ARTIFACTS: {list of files created/modified, if any}
   ---
   ```

**Control flow:**

5. **Gate evaluation.** Read WORKFLOW_RESULT from the completed step:
   - PASS / YES / APPROVED → follow YES branch or advance to next step
   - FAIL / NO / REJECTED → follow NO branch or loop back
   - NEEDS_REVISION → treat as FAIL with the expectation of a retry

6. **HITL gates.** When a lane reaches HITL(question), STOP that lane,
   present the agent's output to the user, and ask the question.
   Other lanes continue running. Resume the lane when the user responds.

7. **Loop detection.** Track execution count per step per lane.
   If any step runs > 3 times:
   "⚠️ Loop in Lane {L} at @{agent}. Ran {count} times. Last: {result}."
   Ask user: continue / skip step / abort lane / abort all.

**Observability:**

8. **Lane status board.** After each step completes, print the full board:

   ```
   WORKFLOW STATUS
   ───────────────────────────────────────────
   Lane 1: ✅ @neo(PASS) → ⏸️ HITL(waiting for user)
   Lane 2: ✅ @tank(PASS) → 🔄 @oracle(running)
   Lane 3: ✅ @tank(PASS) → ✅ @oracle(PASS)
   Lane 4: ⏳ waiting for lanes 1, 2, 3
   Lane 5: ⏳ waiting for lane 4
   Lane 6: ⏳ waiting for lane 5
   ───────────────────────────────────────────
   ```

9. **Completion.** When ALL lanes reach their final PASS (or are
   explicitly skipped by user), print:
   ```
   🏁 Workflow complete. {N} lanes, {M} total steps executed.
   ```
   Then spawn Scribe to log the full workflow to orchestration-log.

10. **Agent charters still apply.** Each agent works per their charter.
    Workflow context is ADDITIONAL, not a replacement. Inline charters
    as usual per Standard Spawn Template.

**Build/test validation commands for agents:**
- Build: `dotnet build --tl:off`
- Unit tests: `dotnet test tests/CopilotBooster.Tests.csproj --tl:off`
- Integration tests: `dotnet test tests/CopilotBooster.IntegrationTests.csproj --tl:off`
- Format: `dotnet format --verify-no-changes`

### START

Begin all [parallel] lanes now. The initial input for this workflow is:

{DESCRIBE YOUR FEATURE, BUG FIX, OR TASK HERE}
