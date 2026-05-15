# Tank — History

## Learnings

<!-- Append learnings below -->

- **2026-05-17: Architectural lesson - caches of file-content derivatives can lie:** The shipped `_latestCwdBySessionId` cache held a stale cwd from events.jsonl that was written BEFORE Copilot CLI restarted the session. workspace.yaml had the fresh cwd (CLI-authoritative on restart), but the cache clobbered it. Roger issued ULTRA directive 2026-05-15T19:45:00Z: NO derivative caches of session-state files. Either invalidate on EVERY file change (complex, error-prone) or eliminate (simple, correct). He chose eliminate. Pattern: for files that change unpredictably and where "most recent" matters across multiple sources, read fresh per refresh rather than cache. Watchers are triggers, not cache updaters. The prior `tank-overlay-clobber-red.md` approach (cache-with-invalidation) was REJECTED; `tank-no-cache-red.md` enforces the cache-free architecture.

- **2026-05-17: Performance pattern - tail-read for latest entry in append-only logs:** events.jsonl can reach 8MB+. Finding the latest hook.start by loading the entire file is unacceptable. Solution: scan backwards from EOF, stop at first hook.start (or session.start fallback), read at most 50KB. This works because we only need the LATEST entry, not a full scan. Enforced by test with GC.GetTotalAllocatedBytes guard (<50KB budget) and Stopwatch (<50ms budget). Pattern applies to any append-only log where "latest entry of type X" is the query.

- **2026-05-17: Nested-quote YAML parsing pitfall (BUG B):** Copilot CLI wrote `name: '"Hosted Agents V2 Questions"'` (single quotes wrapping double-quoted text). Parser did `Trim('"')` which only strips double quotes, leaving `'"Hosted Agents V2 Questions"'` with literal single quotes in UI. Fix: `Trim('"', '\'')` strips BOTH quote wrapper types. Trim iterates until no target chars remain at ends, so nested quotes like `'"text"'` are fully stripped. Pattern: parsers for user-facing text from external tools must handle multiple quoting conventions. Don't assume one wrapper style.

- **2026-05-14: WI-3 editor CWD save regression tests:** Neo Q3 chose no production seam for the WinForms context-menu lambda, so Tank pinned the adjacent source contract in `tests/Forms/MainFormContextMenuEditorSaveTests.cs`: no `UpdateSessionCwd`, no in-memory `session.Cwd` or `session.Folder` mutation, no `CWD` grid-cell update, and alias `SetAlias` remains. RED could not be reproduced in this worktree because WI-3 source removal was already present before the test landed; build and unit tests are green.


- **2026-05-14: WI-2 CWD fallback RED tests:** `tests/Services/CopilotLogWatcherServiceTests.cs` now pins parser purity and caller fallback ordering. Trinity needs a caller-side `ResolveCwdAfterParse` style seam that accepts parsed CWD, PID, and injectable `Func<int, string?>` PEB probe so Tank can fake PEB without depending on live Win32 process state.

- **Team update 2026-05-14 — CWD feature complete:** All 15 RED/GREEN tests (11 WI-2 + 4 WI-3 regression) passed. Oracle approved WI-2 design and Trinity's implementation. WI-3 closed-as-absorbed per Neo Q3. Build clean, full suite green (939 unit, 155 integration, 0 failed). Feature closed.

* **2026-05-14: WI-LiveCwd-1 RED tests:** Added `tests/Services/EventsJournalServiceCwdTests.cs` with parser coverage for hook `data.input.cwd`, session.start fallback, latest hook wins, malformed input, truncated final line, and watcher notification. RED is correct: build is clean, unit run fails 7 tests because `EventsJournalService` does not yet expose `ExtractLatestCwd(TextReader)`, a test root constructor, or `LatestCwdChanged`.

- **Team update 2026-05-16 — Live CWD from events.jsonl:** Tank wrote 7 RED tests, Oracle approved all seams, Trinity implemented parser + event + wired MainForm. All tests GREEN. Read-only directive enforced: production reads only, tests use temp dirs. Backward compat preserved (parameterless ctor chains to new string overload). Full suite green (946 unit, 155 integration). Feature WI-LiveCwd-1 closed.

- **2026-05-16: Live CWD reload pipeline RED test:** Added `tests/Integration/EventsJournalLiveCwdPipelineTests.cs` with `LiveCwdSurvivesLoadSessions_AfterEventsJsonlChange_SessionListReflectsLiveCwd`. Production reload call chain is `MainForm.OnLatestCwdChanged` → `RequestRefresh(..., dataChanged: true)` → `MainForm.OnDebouncedRefreshAsync` → `SessionRefreshCoordinator.LoadSessions` → `SessionService.LoadNamedSessions`. Prior tests missed this because they proved `ExtractLatestCwd` and `LatestCwdChanged` in isolation, but never exercised the reload that rehydrates brand new `NamedSession` objects from stale `workspace.yaml`. RED confirmed: expected `D:\new`, actual `D:\old`.

- **2026-05-17: ULTRA directive reversal — from cache-with-overlay to cache-free architecture:** Roger issued ULTRA 2026-05-15T19:45:00Z ruling the overlay + cache design (commit 5cbd35a) fundamentally broken. Root cause: overlay cached a derivative of file content; that derivative drifted from source. No invalidation strategy can save a cache of a file's content — either invalidate on every file change (complexity disaster) or eliminate (simple, correct). Tank wrote entirely new RED suite (31 tests, 19 honest failures) enforcing cache-free architecture: no `_latestCwdBySessionId`, no `ApplyLiveCwdOverlay`, no persistence. Instead: `ExtractLatestCwdFromTail` helper, `ResolveSessionCwd` seam that reads both files fresh and picks the most recent by mtime (yaml wins ties). All prior overlay tests (`LiveCwdOverlaySeamTests`, `LiveCwdOverlaySeamGapCoverageTests`, integration pipeline) deleted as obsolete. New tests: 14 behavioral (cwd resolution, tail-read, quote parsing), 11 source-contract guards (forbid cache/overlay/callsites). Dozer added 6 behavioral gaps, Oracle binary-approved, Switch implemented, all 976 unit tests GREEN.

---

## 2026-05-16 — Live CWD Overlay Production Seam (RED)

**Context:** Roger reported live CWD reactivity broken. MainForm.OnLatestCwdChanged updates cached session CWD, then triggers refresh via OnDebouncedRefreshAsync. The refresh calls LoadSessions which re-reads workspace.yaml and creates brand new NamedSession instances, discarding the just-applied live CWD. Prior integration test `EventsJournalLiveCwdPipelineTests` used a LOCAL helper `ApplyLiveCwdOverlay` defined inside the test class, proving the overlay IDEA works but not exercising production code. Roger rule: "Is not acceptable that we can't mimick what the application is doing Live in our tests."

**Task:** Write strong RED test that exercises a production seam MainForm will call AFTER LoadSessions and BEFORE ApplySessionStates.

**Design Choice:** Option A — extract production seam `EventsJournalService.ApplyLiveCwdOverlay(sessions, journal)`. Clean API with clear responsibility. No WinForms scaffolding, no STA threading, no reflection on internals (uses InternalsVisibleTo).

**Seam Signature:**
```csharp
internal static void ApplyLiveCwdOverlay(
    IReadOnlyList<NamedSession> sessions,
    EventsJournalService journal)
```

**Deliverables:**

1. **New test file:** `tests/Services/LiveCwdOverlaySeamTests.cs`
   - 3 behavior tests (overlay when differs, no change when matches, no change when no live CWD)
   - 1 source-contract guard (asserts MainForm.OnDebouncedRefreshAsync contains "ApplyLiveCwdOverlay" call)

2. **Integration test update:** Removed inline `foreach` helper from `EventsJournalLiveCwdPipelineTests.ProductionRefreshPipeline_AfterLiveCwdChange_OverlaysLiveCwdOntoStaleWorkspaceYaml`. Now calls production seam directly.

3. **Decision file:** `.squad/decisions/inbox/tank-livecwd-prod-red.md`

**RED Evidence:**
- Build command: `dotnet build tests/CopilotBooster.Tests.csproj -c Release --tl:off`
- Exit code: 1 (FAILED as expected)
- 3 compile errors: `EventsJournalService does not contain a definition for 'ApplyLiveCwdOverlay'`
- Reason: Seam doesn't exist yet. This is compile-time RED (acceptable per charter).

**Key Learning:** When test references a missing production API, compile error IS the RED proof. No need to stub/mock first. The test signature defines the contract Trinity must implement. Source-contract guard (4th test) will compile once seam exists but fail at runtime if MainForm doesn't wire it — catching "seam exists but unused" bugs.

**Status:** RED ✅ — Passed to Oracle for gate review.

