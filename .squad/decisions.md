# Team Decisions

## STANDING RULE: All-Green Test Suite Required (2026-05-13)

**Date:** 2026-05-13
**By:** Roger Barreto (Copilot directive)
**Status:** BINDING

Pre-existing test failures are NOT acceptable. The team may not declare work "done" while ANY test in the suite is failing, even if the failure pre-dates the current change. Whoever lands work that meets a red suite must either:
1. Fix the pre-existing failure as part of their delivery, OR
2. Escalate to the coordinator with a clear analysis of the failure and a plan, before claiming completion.

"Unrelated" is not a sufficient justification on its own. This is a standing release policy: the project ships only with a fully green suite.

## CWD Fallback Chain & Editor Read-Only — Feature Complete (2026-05-14)

**Date:** 2026-05-14  
**Feature Lead:** Trinity  
**Contributors:** Tank, Oracle, Morpheus, Neo  
**Status:** CLOSED

### Work Item Summary

| WI | Owner | Title | Verdict | Notes |
|----|-------|-------|---------|-------|
| WI-1 | Trinity | Win32ProcessCwd helper | DONE | SafeProcessHandle, 64-bit PEB probe, reusable pattern |
| WI-2 | Trinity | CWD fallback chain reorder | **APPROVED** | Parser purified (→ ""), seam at caller (TryProcessLogFile), all normalizations applied |
| WI-3 | Trinity | Editor CWD write removal | **CLOSED-AS-ABSORBED** | Removals already in place; 4 source-contract regression guards protect against re-introduction |
| WI-4 | Morpheus | SessionEditorVisuals read-only conversion | DONE | Name/CWD read-only, Alias editable per Neo Q1 |
| WI-5/6/7 | Tank | Unit + integration tests + validation | DONE | 11 WI-2 tests, 4 WI-3 regression guards, all green |

### Tank: WI-2 RED Tests

Wrote 11 RED tests exposing pure parser UserProfile fallback and missing `ResolveCwdAfterParse` seam:
- Tests 1-8: Expect `""` when no CWD sources exist; parser currently returns `C:\Users\rbarreto`
- Test 9: Confirms seam method signature via reflection
- Tests 10-11: Test caller-site fallback chain (PEB → DefaultWorkDir → empty)

All failures are legitimate RED. Build: 0 warnings, 0 errors. Unit suite: 939 total, 11 failing as expected.

### Tank: WI-3 Regression Guards

Wrote 4 source-contract tests in `MainFormContextMenuEditorSaveTests.cs`:
- Asserts `SessionService.UpdateSessionCwd` absent from context menu editor save
- Asserts session CWD and folder not mutated on save
- Asserts grid CWD cell not updated
- Asserts `SessionAliasService.SetAlias` still intact

Per Neo Q3 ruling: no seam extraction solely for testing. Source-contract assertions serve as regression guards. Implementation was absorbed into WI-4 before TDD directive; deviation accepted since tests serve intended purpose and forcing revert-redo wastes time.

### Oracle: TDD Gate Review (Tank WI-2/WI-3)

**WI-2 Verdict: APPROVED — Trinity may proceed**

RED verification via code reasoning:
1. Tests 1-8 fail because `TryParseLogContent` (lines 385-389) has `Environment.GetFolderPath(UserProfile)` fallback. Parser returns `C:\Users\rbarreto`, tests expect `""`. Correct RED.
2. Test 9 fails because `ResolveCwdAfterParse` seam does not exist yet. Correct RED.
3. Tests 10-11 fail because `TryProcessLogFile` does not yet apply caller-side fallback chain. Correct RED.

**Seam Design Review:** Tank's signature is correct. Recommended adjustments:
1. Normalize `parsedCwd` as unresolved if empty, whitespace, or equals UserProfile
2. Guard PEB null/whitespace results
3. Validate `DefaultWorkDir` is absolute (not relative)
4. Defensive try/catch around `getProcessCwd(pid)`

No blocking false-positive risks. Tests will correctly fail buggy implementations.

**WI-3 Verdict: CLOSED-AS-ABSORBED**

Source inspection shows all WI-3 removals already in place:
- `SessionService.UpdateSessionCwd` — not present
- `session.Cwd` assignment — not present
- `session.Folder` assignment — not present
- `row.Cells["CWD"].Value` assignment — not present
- `SessionAliasService.SetAlias` — preserved (line 44)

Tank's 4 regression guard tests are valid. TDD deviation accepted (implementation preceded tests, but tests serve their purpose).

### Trinity: WI-2 GREEN Implementation

**Parser Purification:**
- Removed `Environment.GetFolderPath(UserProfile)` from `TryParseLogContent` fallback chain
- Final fallback now `?? string.Empty` (line 389)
- `TryParseLogContent` remains pure static parser

**Seam Addition:**
- Added `internal static string ResolveCwdAfterParse(string parsedCwd, int pid, Func<int, string?> getProcessCwd)` (lines 455-488)
- Applied all Oracle normalizations:
  - Lines 457-463: Check if parsedCwd is empty, whitespace, or equals UserProfile (unresolved)
  - Lines 475-478: Guard PEB null/whitespace results
  - Line 482: `Path.IsPathRooted(defaultWorkDir)` validates absolute path
  - Lines 466-473: Defensive try/catch around `getProcessCwd(pid)`
- Wired seam before `CreateWorkspaceYamlFromPid` (lines 221-222)

**Deviation: `s_gitRepoCache` → `ConcurrentDictionary`**
- Changed `SessionService.s_gitRepoCache` from `Dictionary` to `ConcurrentDictionary`
- xUnit runs tests in parallel by default; `Dictionary` is not thread-safe
- Parallel test runs exposed nondeterministic failure in `LoadNamedSessions`
- `ConcurrentDictionary` is drop-in replacement, semantically identical
- Correct fix per all-green standing rule

**Test & Build Results:**
- Unit tests: 939 total, 0 failed, 2 skipped
- Integration tests: 155 total, 0 failed, 22 skipped
- Build: `dotnet build --tl:off` — 0 warnings, 0 errors
- Format: `dotnet format` — exit code 0
- Cosmetic changes: docnet format BOM additions, unused using removals (no scope creep)

### Oracle: Trinity WI-2 Implementation Review

**Verdict: ✅ APPROVE — WI-2 CLOSED**

**Spec Compliance Verification:**
| Requirement | Status | Evidence |
|-------------|--------|----------|
| Remove UserProfile fallback from `TryParseLogContent` | ✅ | Line 389: fallback now `?? string.Empty` |
| Add `ResolveCwdAfterParse(string, int, Func<int,string?>)` | ✅ | Lines 455–488 |
| Normalize parsedCwd as unresolved | ✅ | Lines 457–463 |
| Guard PEB null/whitespace | ✅ | Lines 475–478 |
| Validate DefaultWorkDir absolute | ✅ | Line 482: `Path.IsPathRooted` |
| Defensive try/catch | ✅ | Lines 466–473 |
| Seam wired before CreateWorkspaceYamlFromPid | ✅ | Lines 221–222 |

**Deviation Assessment:** `s_gitRepoCache` → `ConcurrentDictionary` is the correct fix for parallel test race. Accepted.

**File Change Audit:** CopilotLogWatcherService.cs in scope (WI-2 implementation). SessionService.cs deviation accepted. Extra files (SessionEditorVisuals.cs, Win32ProcessCwd.cs, tests) show cosmetic dotnet format changes only (BOM, unused using removal). No scope creep.

**Final Verdict:** All spec requirements met. SOLID principles preserved. Deviation justified. Extra-file changes are cosmetic `dotnet format` outputs only. WI-2 complete. Feature ready for closure.

---

## Live CWD from events.jsonl — Read-Only Watcher Extension (2026-05-16)

**Date:** 2026-05-16  
**Feature:** WI-LiveCwd-1 (Extract live session CWD from Copilot CLI events.jsonl)  
**Contributors:** Tank, Oracle, Trinity  
**Status:** CLOSED

### Overview

Extract the latest session CWD from the Copilot CLI's append-only events.jsonl file without writing to it. Emit a `LatestCwdChanged` event when CWD changes. The existing `EventsJournalService` watcher triggers the extraction; no new FileSystemWatcher required.

### Read-Only Directive (BINDING)

**2026-05-14T20-09-51Z: User directive — Roger Barreto**

The booster must NEVER modify any Copilot CLI session file. `events.jsonl`, `workspace.yaml`, and every other file under `~/.copilot/session-state/<sid>/` are READ-ONLY. The booster may parse and extract data from them but must never write, append, rename, delete, or otherwise mutate them. (The exception is the existing `CreateWorkspaceYamlFromPid` path for externally-discovered sessions whose folder lacks a workspace.yaml; that is creation of a missing file, not mutation of an existing one. All other files: read-only.)

**Rationale:** Copilot CLI owns these files; any mutation by the booster risks corrupting the upstream session, racing with the CLI, or being silently overwritten.

**Verification (Production):**
- `EventsJournalService.ExtractLatestCwd(TextReader)` — pure static parser, no I/O
- `EventsJournalService.LatestCwdChanged` event — notification only, no writes
- `EventsJournalService` constructor — watches session root, reads only via `FileShare.ReadWrite`
- `MainForm.OnLatestCwdChanged` — in-memory update only, no file I/O

**Verification (Tests):**
- Test fixtures create temp directory under `AppContext.BaseDirectory`, NOT `~/.copilot/session-state/`
- Watcher integration test writes to temp directory only
- No production Copilot session files touched

### Tank: WI-LiveCwd-1 RED Tests

**File:** `tests/Services/EventsJournalServiceCwdTests.cs`

7 RED tests exposing missing seams:

1. `ExtractLatestCwd_HookEventWithInputCwd_ReturnsHookCwd` — Parser must extract `hook.start.data.input.cwd`
2. `ExtractLatestCwd_OnlySessionStart_ReturnsContextCwd` — Parser must fallback to `session.start.data.context.cwd`
3. `ExtractLatestCwd_MultipleHooksWithDifferentCwds_ReturnsLatest` — Latest valid hook cwd wins
4. `ExtractLatestCwd_EmptyOrMalformedFile_ReturnsNull` (theory, 2 rows) — Handle empty string and non-JSON input
5. `ExtractLatestCwd_TruncatedFinalLine_StillReturnsLatestValidCwd` — Skip truncated final line, return previous valid cwd
6. `EventsJournalService_RaisesCwdChangedEvent_WhenLatestCwdChangesAcrossWatcherFire` — Service must raise event on cwd change

**RED Verdict:** ✅ All 7 tests fail for correct reasons:
- 6 tests fail at `Assert.NotNull(method)` because `static ExtractLatestCwd(TextReader)` does not exist
- 1 test fails at `Assert.NotNull(ctor)` because `EventsJournalService(string sessionsDir)` does not exist

**Validation:**
- Build: 0 warnings, 0 errors
- Unit suite: 946 total, 7 failing (as expected), 0 unintended failures

### Oracle: TDD Gate Review (Tank RED)

**Verdict: ✅ APPROVED — Trinity may proceed**

**RED Verification:** All 7 tests fail because the seams don't exist, not due to test bugs. Code reasoning against `EventsJournalService.cs` confirms:
- No method named `ExtractLatestCwd` exists
- No constructor accepting `string sessionsDir` exists (only implicit parameterless default)
- No event named `LatestCwdChanged` exists

**Test Design Quality:**
- JSON path accuracy: Tank's fixtures use documented Copilot CLI event schema (`session.start.data.context.cwd`, `hook.start.data.input.cwd`). Approved.
- Truncated final line scenario: Realistic (watcher fires mid-write). Parser correctly skips incomplete final line and returns last valid cwd. Approved.
- Malformed input coverage: Theory covers empty string and non-JSON content. Implicitly tests fallback-to-null behavior. Approved.

**Seam Design Sign-Off:**

| Seam | Signature | Notes | Status |
|------|-----------|-------|--------|
| Parser | `internal static string? ExtractLatestCwd(TextReader reader)` | Returns latest hook cwd, falls back to session.start cwd, returns null if no valid cwd | ✅ APPROVED |
| Event | `internal event Action<string, string>? LatestCwdChanged` | Args: `(sessionId, latestCwd)`. Minimal signature: notify that new cwd is available. | ✅ APPROVED |
| Constructor | `internal EventsJournalService(string sessionsDir)` | Lets tests watch test-controlled session root. **BACKWARD COMPATIBILITY REQUIRED:** Parameterless ctor must exist and chain to new overload. | ✅ APPROVED w/ requirement |

**Backward Compatibility Requirement:**
Current instantiation in `ActiveStatusTracker.cs:46` uses `new EventsJournalService()` (parameterless). Trinity MUST implement:
```csharp
internal EventsJournalService() : this(s_copilotSessionsDir) { }
internal EventsJournalService(string sessionsDir) { ... }
```

### Trinity: WI-LiveCwd-1 GREEN Implementation

**Files Modified:**
- `src/Services/EventsJournalService.cs` — Added parser, event, constructors, watcher integration
- `src/Forms/MainForm.cs` — Wired `LatestCwdChanged` event to UI refresh
- `tests/Services/EventsJournalServiceCwdTests.cs` — Test file (created by Tank)
- `.squad/skills/append-only-jsonl-field-extraction.md` — Skill documentation (created by Trinity)

**Implementation Summary:**

1. **Parser (`ExtractLatestCwd(TextReader reader)`):**
   - Scans lines in file order
   - Ignores empty, invalid JSON, and truncated lines
   - Uses `session.start.data.context.cwd` as fallback
   - Uses `hook.start.data.input.cwd` as latest (wins over fallback)
   - Returns null when no valid cwd exists
   - Never writes to events.jsonl

2. **Event (`LatestCwdChanged`):**
   - Fires from `OnWatcherChanged` callback
   - Passes sessionId and latest cwd to handler
   - Suppressed during startup cache priming via `SuppressEvents` flag

3. **Constructors:**
   - Parameterless `internal EventsJournalService()` chains to string overload (backward compat)
   - `internal EventsJournalService(string sessionsDir)` stores sessionDir for watching

4. **UI Wiring (MainForm.cs):**
   - `OnLatestCwdChanged` handler receives event from watcher thread
   - Marshals to UI thread via `BeginInvoke`
   - Updates in-memory `_cachedSessions[sessionId].Cwd` and `.Folder`
   - Calls `RequestRefresh(sessionId, dataChanged: true)` to refresh grid

**Test Results:**
- Unit: 946 total, 0 failed, 2 skipped
- Integration: 155 total, 0 failed, 22 skipped
- Build: 0 warnings, 0 errors
- Format: clean

**Deviations:**
- Test helper uses reflection `GetAddMethod(nonPublic: true)` to subscribe to internal event. This is the standard .NET pattern when event's add accessor is non-public (required by Oracle seam design). Isolated to test code only.

### Oracle: WI-LiveCwd-1 Implementation Review

**Verdict: ✅ APPROVE — WI-LiveCwd-1 CLOSED**

**Spec Compliance Matrix:**
| Requirement | Status | Evidence |
|-------------|--------|----------|
| `ExtractLatestCwd(TextReader)` exists | ✅ | Line 271: exact signature |
| Scans lines sequentially | ✅ | Lines 276–313: `while ((line = reader.ReadLine()) != null)` |
| Skips malformed/truncated lines | ✅ | Lines 278–280, 310–312: null checks + `catch (JsonException)` |
| Prefers `hook.start.data.input.cwd` | ✅ | Line 315: `return latestHookCwd ?? sessionStartCwd` |
| Returns null when nothing valid | ✅ | Both variables default to null |
| `LatestCwdChanged` event | ✅ | Line 52: exact signature |
| `EventsJournalService(string sessionsDir)` | ✅ | Lines 75–78 |
| Parameterless ctor preserved | ✅ | Lines 70–73: chains to string overload |
| File reads use `FileShare.ReadWrite` | ✅ | Line 327 |
| MainForm wiring + thread marshalling | ✅ | Lines 1785–1795: `BeginInvoke`, session update, refresh |

**SOLID & Code Quality:**
- SRP: Parser pure (no I/O), service focused on watching
- Member ordering: statics, privates, then methods
- `this.` prefix consistent throughout
- `ConcurrentDictionary` with `OrdinalIgnoreCase` for Windows path semantics
- Suppress flow honored: events suppressed during startup
- Defensive error handling: try/catch around JSON parse and I/O

**Read-Only Directive (BINDING):**
- Production code only reads, no writes/appends/deletes
- Tests use isolated temp directories
- Compliant.

**UI Thread Safety:**
- Event fires from FileSystemWatcher thread
- `BeginInvoke` marshals to UI thread
- `IsHandleCreated` guard prevents posting to dead message queue
- Matches existing pattern in codebase

**Test Reflection Deviation:**
- `GetAddMethod(nonPublic: true)` required because event is internal
- Standard .NET pattern, necessary deviation
- Isolated to test code only
- Accepted.

**Final Results:**
- Unit tests: 946 total, 0 failed
- Integration tests: 155 total, 0 failed
- Build: clean (0 warn, 0 err)
- Format: clean
- Skill document: correctly captures append-only JSONL extraction pattern

---

## Summary: Live CWD Feature Complete

**Red-Green-Review Cycle:** ✅ CLOSED

- **Tank RED:** 7 tests, all failing for correct reasons
- **Oracle Review 1:** All seams approved, backward compat requirement mandated
- **Trinity GREEN:** All 7 tests passing, backward compat preserved, read-only compliant
- **Oracle Review 2:** Implementation verified, seam signatures exact, SOLID standards met, reflection deviation accepted

**Test Suite:** All green (946 unit, 155 integration)

**Outcome:** Ready for git commit and release. No further action required.

---

## ULTRA DIRECTIVE: Gaps are Never Acceptable (2026-05-15T18-50-00Z)

**By:** Roger Barreto (via Copilot)

**What:** "GAPS IS NEVER ACCEPTABLE." Any reviewer verdict that includes "GAPS", "WITH GAPS", "APPROVE WITH GAPS", or any equivalent qualifier is treated as a REJECT. Reviewer Rejection Lockout applies in full: the original author of the rejected artifact is locked out and a different agent must own the revision.

**Scope:** RED tests, GREEN implementations, design reviews, and all other artifacts subject to quality gates.

**Rationale:** User directive enforcing all-or-nothing quality gates. The analytical-diversity peer reviewer (e.g., Dozer) surfaces gaps; once surfaced they must be closed by a different agent. No "good enough" makes it past the gate.

**Operational Consequences:**
1. Reviewers must use either APPROVE or REJECT. "APPROVE WITH GAPS" verdicts are abolished.
2. When gaps are found, reviewer issues REJECT with gap list, remedy, and required revision owner (must be different agent).
3. Coordinator must refuse to spawn the locked-out author for revision.
4. Charter updates required for all reviewer agents to remove "WITH GAPS" language.

**Retroactive Scope:** Does not invalidate work that landed before 2026-05-15T18-50Z. Any verdict after this timestamp must conform.

---

## Live CWD Overlay Survives LoadSessions Reload (2026-05-16)

**Date:** 2026-05-16  
**Feature:** Apply live CWD overlay after LoadSessions to prevent stale workspace.yaml data from overwriting live CWD on every refresh tick  
**Contributors:** Tank (RED), Dozer (peer + impl review), Oracle (gates), Trinity (first attempt, locked out), Switch (lockout relief, final GREEN)  
**Status:** COMPLETE & APPROVED  

### Overview

Copilot CLI updates events.jsonl when `/cwd` switches but does not update workspace.yaml. The live CWD feature extracts the latest session CWD from events.jsonl (committed earlier). However, MainForm's refresh loop calls `SessionService.LoadSessions()` which rebuilds `NamedSession` instances fresh from stale workspace.yaml, silently overwriting the live CWD that had just been applied via `OnLatestCwdChanged`. 

Solution: Wire `EventsJournalService.ApplyLiveCwdOverlay(sessions, journal)` at BOTH MainForm callsites that consume LoadSessions output:
1. `OnDebouncedRefreshAsync` (data-only refresh when a live event arrives)
2. `RefreshBackgroundCoreAsync` (full-refresh timer every 45s)

Without both callsites, the live CWD would be silently restored to stale workspace.yaml on the full-refresh tick.

### Red-Green-Review Pipeline

#### Tank: RED Tests (LiveCwdOverlaySeamTests.cs)

**Date:** 2026-05-16

4 RED tests exposing missing production seam:

1. `ApplyLiveCwdOverlay_WhenLiveCwdDiffersFromWorkspaceYaml_OverlaysLiveCwd` — Core happy path
2. `ApplyLiveCwdOverlay_WhenLiveCwdMatchesWorkspaceYaml_NoChange` — Idempotency guard
3. `ApplyLiveCwdOverlay_WhenSessionHasNoLiveCwd_NoChange` — Empty cache edge case
4. `ProductionCallsite_MainFormOnDebouncedRefreshAsync_CallsApplyLiveCwdOverlay` — Source-contract substring guard

**Build Status:** Compile-time RED. `EventsJournalService.ApplyLiveCwdOverlay` does not exist. Tests reference missing seam.

#### Dozer: Peer Review with Gap-Coverage Tests (LiveCwdOverlaySeamGapCoverageTests.cs)

**Date:** 2026-05-16

**Verdict:** APPROVE WITH GAPS (pre-ULTRA DIRECTIVE)

Added 3 gap-coverage tests capturing missing edge cases:

1. `ApplyLiveCwdOverlay_WhenLiveCwdDiffersOnlyByCase_PreservesWorkspaceCwdAndFolder` — Behavioral test for Windows case-insensitive path handling (OrdinalIgnoreCase comparison required)
2. `ApplyLiveCwdOverlay_WhenMultipleSessionsHaveMixedLiveCwdStates_OverlaysEachIndependently` — Verifies N sessions processed without short-circuiting
3. `ProductionCallsite_MainFormOnDebouncedRefreshAsync_CallsOverlayBeforeCachingAndApplyingStates` — Ordered source-contract guard requiring exact sequence: `LoadSessions()` → `ApplyLiveCwdOverlay()` → `_cachedSessions = sessions` → `ApplySessionStates()`

**Gap Analysis:**
- Tank's substring guard too loose (could pass on comment or dead branch)
- Dozer's ordered guard binding: asserts exact 4-step sequence in order
- Path edge case: `TrimEnd('\\')` only trims backslashes, leaving forward-slash paths empty in Folder computation
- **Missing gap (to be discovered later):** `RefreshBackgroundCoreAsync` also calls `LoadSessions` but lacks overlay call. This full-refresh path (every 45s) silently restores stale CWD.

#### Oracle: RED Gate Review

**Date:** 2026-05-16

**Verdict:** ✅ APPROVE

**Seam Signature (BINDING for Trinity):**
```csharp
internal static void ApplyLiveCwdOverlay(
    IReadOnlyList<NamedSession> sessions,
    EventsJournalService journal)
```

**Seam Requirements:**
- Owner: `EventsJournalService` (static method)
- Use `TryGetLatestCwd(sessionId, out cwd)` to access journal cache (must be added)
- Case-insensitive comparison: `StringComparison.OrdinalIgnoreCase` required
- Folder computation: `Path.GetFileName(liveCwd.TrimEnd('\\'))` (per Tank's design)
- No `ArgumentNullException` throws; empty input should no-op
- No logging; pure in-memory mutation

**Source-Contract Guard (Binding):** Dozer's ordered guard is the standard. Asserts 4 exact substrings in sequence with local variable names fixed (`sessions`, `this._eventsJournal`).

**Marching Orders:** Trinity may proceed GREEN. Must match binding seam signature exactly. Must wire callsite between `LoadSessions()` and `_cachedSessions` assignment.

#### Trinity: GREEN Implementation (First Attempt) — LOCKED OUT

**Date:** 2026-05-16

Implemented seam per Oracle's binding contract. Added `TryGetLatestCwd` and `ApplyLiveCwdOverlay` to `EventsJournalService.cs`. Wired callsite in `MainForm.OnDebouncedRefreshAsync`.

**Test Results:** All 7 tests passed (4 Tank + 3 Dozer). 956 unit tests pass total.

**Gap 1 (BLOCKING):** `RefreshBackgroundCoreAsync` missing overlay call. This method is called by the full-refresh timer every 45 seconds. Trinity wired only the data-only refresh path (`OnDebouncedRefreshAsync`), leaving the full-refresh path vulnerable to stale `workspace.yaml` restoration.

**Gap 2:** Folder computation with forward-slash. When Copilot CLI emits paths like `D:/repo/work/agent-framework/` (forward slashes), `Path.GetFileName(liveCwd.TrimEnd('\\'))` leaves the trailing forward slash intact, and `GetFileName` on Windows returns empty string for forward-slash-terminated paths. Result: empty Folder display name.

**Verdict:** ❌ REJECTED per ULTRA DIRECTIVE (2026-05-15T18-50-00Z). Gaps = REJECT. Trinity locked out. Switch (new Services Dev agent) hired as lockout relief.

#### Dozer: Implementation Review (Trinity's Attempt)

**Date:** 2026-05-16

Reviewed Trinity's implementation and captured gap analysis:

1. **Gap 1:** RefreshBackgroundCoreAsync reloads sessions without overlay. Full-refresh timer (every 45s) overwrites live CWD with stale workspace.yaml. Added `ProductionCallsite_RefreshBackgroundCoreAsync_CallsOverlayBeforeCachingAndApplyingStates` test to verify the missing callsite.

2. **Gap 2:** Trailing forward-slash case not handled. Added `ApplyLiveCwdOverlay_WhenLiveCwdHasTrailingForwardSlash_ComputesFolderFromLastSegment` test expecting `D:/repo/work/agent-framework/` → Folder = `agent-framework`.

**Verdict:** APPROVE WITH GAPS (legacy pre-ULTRA). Both gaps documented for next implementer.

#### Oracle: Implementation Gate (Trinity's Attempt) — Missed Gaps

**Date:** 2026-05-16

**Verdict:** ✅ APPROVE

Verified Trinity's implementation matches binding contract exactly. All tests passed. Suite green. No scope creep. Oracle did not re-verify against Dozer's new gap-find tests. Oracle's verdict is superseded by ULTRA DIRECTIVE lockout: gaps found = REJECT regardless of Oracle's approval.

#### Switch: GREEN Implementation (Lockout Relief)

**Date:** 2026-05-16

Trinity locked out per ULTRA DIRECTIVE. Switch (claude-sonnet-4.5, new Services Dev) hired as relief to re-implement from scratch.

**Implementation:**
1. Added `internal bool TryGetLatestCwd(string sessionId, out string cwd)` to `EventsJournalService.cs` (lines 188–198)
2. Added `internal static void ApplyLiveCwdOverlay(IReadOnlyList<NamedSession> sessions, EventsJournalService journal)` to `EventsJournalService.cs` (lines 205–224)
   - Case-insensitive comparison: `StringComparison.OrdinalIgnoreCase`
   - **Forward-slash trim FIXED:** `TrimEnd('\\', '/')` trims both backslash and forward slash
3. Added `private readonly EventsJournalService _eventsJournal;` field to `MainForm.cs`
4. **Wired BOTH callsites:**
   - `OnDebouncedRefreshAsync` (data-only path, line 1752)
   - **RefreshBackgroundCoreAsync** (full-refresh path, line 1608) — **The gap Trinity missed**

**Test Results:**
- Tank's 4 tests: ✅ All pass
- Dozer's 5 gap tests: ✅ All pass (including forward-slash and RefreshBackgroundCoreAsync tests)
- EventsJournalServiceCwdTests: ✅ 8 pass
- Full suite: ✅ 958 total, 0 failed, 2 skipped

**Deviations:** None. Exact match to Oracle's binding contract. Both gaps closed.

#### Oracle: Binary Gate Review (Switch GREEN)

**Date:** 2026-05-16

**Directive:** ULTRA DIRECTIVE (2026-05-15T18-50-00Z) — binary verdicts only. APPROVE or REJECT.

**Verdict:** ✅ APPROVE

- All 9 targeted tests pass (4 Tank + 5 Dozer)
- Full suite 958/958 green
- Both MainForm callsites correctly ordered
- Forward-slash trim in place
- SOLID principles upheld
- No scope creep
- `dotnet format` clean

Trinity → Scribe pipeline may proceed.

#### Dozer: Binary Review (Switch GREEN)

**Date:** 2026-05-16

**Directive:** ULTRA DIRECTIVE (2026-05-15T18-50-00Z) — binary verdicts only.

**Verdict:** ✅ APPROVE

All prior gaps confirmed closed:
- Forward-slash trim: `TrimEnd('\\', '/')` implemented
- RefreshBackgroundCoreAsync: Callsite present at line 1608, correctly ordered
- Case-sensitivity: `OrdinalIgnoreCase` in place
- Multi-session: Correct independent iteration

No new gaps found. Production code path verified GREEN.

### Files Modified

**Production:**
- `src/Services/EventsJournalService.cs` — Added `TryGetLatestCwd` + `ApplyLiveCwdOverlay` seam
- `src/Forms/MainForm.cs` — Added `_eventsJournal` field + callsites in both refresh paths

**Tests:**
- `tests/Services/LiveCwdOverlaySeamTests.cs` — Tank's 4 RED tests (new)
- `tests/Services/LiveCwdOverlaySeamGapCoverageTests.cs` — Dozer's 5 gap tests (new)

### Verification

- **Build:** `dotnet build src/ --tl:off` — 0 warnings, 0 errors
- **Format:** `dotnet format` — exit code 0
- **Unit Tests:** 958 total, 0 errors, 0 failed, 2 skipped (LocalOnly, CopilotProbe pending)
- **Targeted Classes:** LiveCwdOverlaySeamTests (4 pass), LiveCwdOverlaySeamGapCoverageTests (5 pass), EventsJournalServiceCwdTests (8 pass)

### Key Learnings (ULTRA DIRECTIVE Impact)

1. **Gaps trigger REJECT** — Finding gaps forces original author out, requires different agent for revision
2. **Binary verdicts enforce rigor** — "APPROVE WITH GAPS" eliminated; no half-measures past gates
3. **Lockout relief pattern** — When lockout applies, hire different agent to re-solve; Trinity locked, Switch hired
4. **Two-agent gap finding** — Peer reviewer (Dozer) + implementation reviewer (Oracle) both added tests; Oracle's gate review alone missed gap
5. **Source-contract guards are enforcement** — Ordered guard forced both callsites to be wired (Trinity missed RefreshBackgroundCoreAsync; Dozer's test caught it)

### Outcome

Feature complete. Live CWD overlay now survives both data-only refresh (OnDebouncedRefreshAsync) and full-refresh timer (RefreshBackgroundCoreAsync). Stale workspace.yaml data no longer overwrites live CWD. All gaps closed. Ready for commit.
