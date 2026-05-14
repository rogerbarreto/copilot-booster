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
