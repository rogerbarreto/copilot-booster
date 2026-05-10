## 2026-05-10: Log Tail-Read Pattern for T0 Startup and File Watcher Events

**Date:** 2026-05-10  
**Status:** Delivered  
**Contributors:** Trinity (implementation), Tank (test validation)

### Decision: Tail-Read Last 256 KB Aligned to Newline

When reading Copilot process logs for session parsing:

1. **T0 Startup (`RescanExistingSessions`):** Tail-read the last 256 KB of each log aligned to a newline boundary instead of reading the full file.
2. **File Watcher Events (`TryProcessLogFile`):** Use the same tail-read pattern on `Changed` events instead of full-file reads.

**Rationale:** Full-file reads of 100s-of-MB active Copilot logs create multi-GB transient allocations. T0 only needs the latest `session_id` per PID; older log entries are irrelevant.

**Implementation:** `TryParseLogTail(string logPath, int maxTailBytes = 256*1024, string? fallbackCwd = null)` in `src/Services/CopilotLogWatcherService.cs`. Handles:
- Files smaller than 256 KB → read entire file (full parity with prior behavior)
- Large files → seek to `Math.Max(0, length - maxTailBytes)`, align to next newline, parse from that point
- Malformed/missing files → graceful fallback

**Impact:**
- `RescanExistingSessions`: 79,000 ms → ~1,800 ms (**47× faster**)
- Working set after 60s: 7,800 MB → 125 MB (**~60× lower**)
- Stale session bindings: 256 → 4

---

## 2026-05-10: Warp Terminal Pane Focus — R2 Probe-and-Match Strategy

**Date:** 2026-05-10  
**Status:** Delivered  
**Contributors:** Roger (approval + verification), Trinity (WarpPaneFocuser implementation), Tank (unit + integration tests), Coordinator (verification)  

### Context: Warp Terminal Challenge

Warp (v0.2026.05.06 stable) shares a single `warp.exe` PID across all tabs/panes and renders UI with Winit + wgpu, exposing **zero UIA automation elements**. The booster's "Copilot CLI" link could not map `copilot.exe` PID → Warp pane UUID because:

- SQLite stores pane UUIDs but no PID linkage; cwd/shell are identical across panes
- PEB does not carry `WARP_PANE_UUID` env var (verified by reading PEB of 4 Warp shells)
- Process-time correlation (pwsh.StartTime ↔ pane ordering) is heuristic, breaks on tab close/reorder — **rejected** per Roger's explicit guidance against silent-mis-focus failure modes
- `warp://session/<uuid>` deep link (PR #9655 merged 2026-05-06) is useless without UUID mapping

**Key fact verified 2026-05-10 (non-admin):** `GetWindowText(warp_main_hwnd)` returns the **active pane title** (e.g., `'Hi 1'`). Confirmed on PID 132268.

### Decision: R2 Probe-and-Match (Active Tab Cycling)

When focus dispatcher needs to focus a Copilot CLI session in Warp:

1. Locate `warp.exe` main HWND (visible window with non-empty title)
2. Read current pane title via `GetWindowText`; if matches session display name → **done**
3. Else send Ctrl+Tab to advance pane; sleep ~150ms; re-read; match
4. If we cycle back to original title without match → **return false** (no live tab hosts this session)
5. Hard iteration cap = 30 to handle edge cases (titles mutating live, panes closing)

**Invasiveness:** R2 is visible (tabs flash as they cycle). Roger approved: "clicking in the link in the copilot booster happens already outside of warp, we could disrupt it".

**Determinism:** No heuristic failure modes. Either matches or provably no match.

### Implementation: Trinity — WarpPaneFocuser Service

Created 7 new files with 3-seam architecture for testability:

**Interfaces:**
- `IWindowTitleReader` — find main HWND, read title
- `IKeyboardSender` — send Ctrl+Tab
- `IPaneFocusClock` — abstract timing

**Core Logic (WarpPaneFocuser.cs):**
- Testable (no [ExcludeFromCodeCoverage])
- TryFocusPane(int processId, string expectedTitle) → bool
- Null/empty title → false immediately
- Win32 apis abstracted via seams

**Concrete Implementations:**
- `Win32WindowTitleReader` — EnumWindows + IsWindowVisible + GetWindowText via LibraryImport
- `Win32KeyboardSender` — SendInput API with INPUT structs for Ctrl+Tab sequence
- `SystemPaneFocusClock` — Thread.Sleep

All concrete classes marked [ExcludeFromCodeCoverage] (thin P/Invoke wrappers).

### Wiring: Trinity — ActiveStatusTracker Integration

Added two seams to maintain backward compatibility with 9 existing constructor overloads:
- `Func<int, string, bool> _warpPaneFocuser` — default constructs WarpPaneFocuser
- `Func<string, string?> _sessionDisplayNameProvider` — default reads SessionInfo.Summary

Final (10th) constructor chains all overloads with defaults. No existing tests modified.

**FocusCopilotHost branch on Warp:**
1. Call `_sessionDisplayNameProvider(sessionId)` → expectedTitle
2. Call `_warpPaneFocuser(hostInfo.HostPid, expectedTitle)`
3. On match → log success, return
4. On failure → log warning ("no Warp pane matched..."), focus warp.exe window as **fallback**, return

Non-Warp hosts (Windows Terminal, Console, etc.) unchanged.

### Testing: Tank — 12 Unit Tests + 3 LocalOnly Live Integration Tests

**Unit Tests (WarpPaneFocuserTests.cs):**
All edge cases covered, no mocking framework, stub classes only:
1. NoMainWindow → false
2. AlreadyOnTargetTab → true (no cycle)
3. TitleMatchIsCaseInsensitive
4. MatchOnSecondTab (1× Ctrl+Tab)
5. MatchOnThirdTab (2× Ctrl+Tab)
6. NoMatch_CyclesBackToOriginal → false
7. HitsIterationCap → false
8. DuplicateTitlesAcrossPanes_FirstMatchWins
9. EmptyExpectedTitle → false
10. NullExpectedTitle → false
11. FocusHwndFails → false
12. TitleReaderReturnsEmptyDuringProbe

**Live Integration Tests (WarpPaneFocusLiveTests.cs):**
Marked `[LocalOnlyStaFact]`, skip cleanly when Roger's live Warp scenario unavailable:
1. FocusKnownPane_LandsOnExpectedTab — capture all pane titles, target second, restore, verify
2. FocusUnknownPane_RestoresOriginal — fake title, assert failure + restore
3. FocusAlreadyOnTarget_NoTabSwitch — current title as target

**Test Infrastructure:**
- `StubTitleReader` (Queue<string> script), `StubKeyboardSender` (count), `StubPaneFocusClock` (record)
- `LiveWarpScenario.Detect()` probes for warp.exe + copilot.exe descendant (CIM `Win32_Process.ParentProcessId`)
- `IDisposable` restore-on-teardown (guaranteed cleanup on assertion failure)
- `[Collection(WindowEventHookCollection.Name)]` serializes live tests (existing pattern)
- Added `System.Management 10.0.1` to IntegrationTests.csproj for CIM queries

### Verification: Coordinator

- **dotnet format** — clean
- **dotnet build src/CopilotBooster.csproj -c Release --tl:off** — SUCCESS (0 warn, 0 err)
- **Unit tests:** 851 total (+28 from 823 baseline, 1 pre-existing skip)
- **Integration tests:** 141 total (3 new Warp live tests skip without COPILOT_BOOSTER_RUN_LOCALONLY)
- **Live test verification:** All 3 LocalOnly Warp live tests pass against Roger's live Hi 1 / Hi 2

### Deferred (Not R2)

- `warp://session/<uuid>` deep link integration (needs upstream WARP_PANE_UUID env var)
- Multi-window Warp cycling (v1 targets single warp.exe window)
- Deterministic spawn automation (LocalOnly→CI conversion path documented in test code; out of scope per Roger)

### Known Limitations (v1)

- Multi-window Warp: EnumWindows picks FIRST visible warp.exe window with non-empty title; may focus wrong window if multiple exist (rare)
- expectedTitle source: relies on SessionInfo.Summary; if session not yet started, Summary empty → no match

---

## 2026-05-09: Issue #15 (refinement) — Auto-resolve Copilot CLI path + Dynamic model dropdown

**Date:** 2026-05-09  
**Status:** Delivered  
**Contributors:** Niobe (API validation), Trinity (path removal + models service), Tank (tests + flake fix), Morpheus (Settings UI), Niobe (changelog/README)

### Niobe: Copilot Models API Authentication Flow

**Decision:** Use gh auth token (standard GitHub PAT) directly as Authorization: <token> header to call GET https://api.githubcopilot.com/models.

**Rationale:**
- The initial plan assumed gh api /copilot_internal/v2/token would return a special Copilot token (it returns 404; endpoint does not exist)
- Niobe verified in live session: gh auth token returns a standard GitHub PAT in gho_* format
- Both Authorization:  and Authorization: Bearer  work; neither prefix is technically required but Bearer is conventional

**Response Shape:**
`json
{
  "object": "list",
  "data": [
    {
      "id": "gpt-4o",
      "name": "GPT-4o",
      "vendor": "OpenAI",
      "model_picker_enabled": true,
      ...
    }
  ]
}
`

**Design consequences:**
- CopilotModelsService calls gh auth token to fetch bearer token, then GET https://api.githubcopilot.com/models
- Parses data[].id, filters out 	ext-embedding-* models (embedding-only, not usable with copilot CLI)
- Cache path: %LOCALAPPDATA%\CopilotBooster\models-cache.json with 24h TTL
- Fallback ordering: Fresh cache → Fetch API → Stale cache → Hardcoded list (from copilot help config)

**Confidence:** HIGH (✅ live tested, ✅ 35 models returned, ✅ consistent schema, ✅ verified against community Copilot plugins)

### Trinity: Copilot Path Removal

**Decision:** Remove CopilotPath property from AiDetectionSettings and MainForm. Use parameterless CopilotProbe constructor as production default, delegating to CopilotLocator.FindCopilotExe() at service boundary.

**Rationale:**
- Settings no longer carry path (path is auto-resolved at detection time)
- MainForm no longer owns path configuration
- Tests retain existing Func<string> constructor for deterministic probes
- AiDetectionService resolves the executable dynamically through CopilotLocator

**Consequence:**
- UI path row removed entirely
- Services are path-agnostic
- Detection always uses current auto-resolved copilot exe location
- Backward compatibility: existing settings.json transparently migrated

### Trinity: CopilotModelsService Implementation

**Design:**
`csharp
internal sealed class CopilotModelsService
{
    internal Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default);
}
`

**Cache schema:**
`json
{
  "fetchedAt": "2026-05-09T10:12:39.5480000Z",
  "models": ["claude-sonnet-4.6", "gpt-5.5"]
}
`

**Fallback ordering:**
1. Fresh cache (< 24h): return immediately
2. Stale cache (≥ 24h): fetch from API, cache new, return fresh
3. API failure + stale cache exists: return stale (graceful degradation)
4. No usable cache: return hardcoded list

**Non-obvious choices:**
- Auth header: Authorization: <token> without Bearer prefix (verified working form)
- Editor-Version header: copilot-booster/<assemblyVersion> for identification
- Parse failures treated like fetch failures (Morpheus can call safely without error surface)
- Stale-cache-over-hardcoded rule preserves user's recently working model list across transient outages

**Thread-safe:** ConcurrentDictionary with Task.FromResult isolation; no explicit locking needed

**Cancellation:** OperationCanceledException rethrown (not converted to fallback); allows UI form disposal to cancel pending fetch

### Tank: CopilotModelsService Unit Tests

**Pattern:** 11 comprehensive tests covering:
- Cache hits, cache misses, stale fallback
- Network errors, cancellation, TTL expiry
- Concurrent fetch, null/empty models, fast-path reuse
- Network recovery after transient failure

**LocalAppData Isolation Skill:** Tests use isolated temp directories per run; cleaned up after each test. Pattern documented in .squad/skills/localappdata-test-isolation/SKILL.md for future use.

**Result:** 11/11 tests passing. No flakes.

### Tank: Copilot Path Test Pattern

**Pattern:** For AiDetectionService path assertions after path removal, use RecordingProcessRunner.Calls and assert RecordedProcessCall.FileName equals CopilotLocator.FindCopilotExe().

**Rationale:** Verifies service boundary receives auto-resolved executable path production uses, without adding new seams or reintroducing path settings.

**Updated test files:** AiDetectionServiceTests, CopilotProbeTests, LauncherSettingsTests. All 786/786 unit tests passing.

### Morpheus: Settings Model Dropdown

**Decision:** Replace path row + free-text model box with strict ComboBox (DropDownStyle.DropDownList).

**UI Shape:**
- Removes CopilotPath row entirely
- Model control: ComboBox, label at x=4, input at x=220, width 300
- First item: sentinel "(default — let Copilot decide)"
- Unknown saved ids appended as <id> (custom) to preserve user choice even if API doesn't return it

**Save Mapping:**
- Sentinel → persist ""
- Plain model id → persist verbatim
- <id> (custom) → strip suffix, persist <id>

**Lifecycle:**
- Form.Shown triggers CopilotModelsService.GetModelsAsync() with form-owned CancellationTokenSource
- Completion checks IsDisposed/cancellation, marshals combo rebuild through BeginInvoke
- Form.Dispose cancels CTS to avoid orphaned background tasks

**Skill:** "WinForms async cancel on dispose" pattern documented in .squad/skills/winforms-async-cancel-on-dispose/SKILL.md

### Tank: SettingsForm Async Dropdown Tests

**Pattern:** 5 tests validating:
- Combo population on form load
- Selected model persistence
- Fetch cancellation on form dispose
- Empty/error model list handling
- Dropdown selection workflow

**Result:** 5/5 tests passing.

### Tank: Format-Build-Test Gate + Flakes Fixed

**Build Error:** CS0579 duplicate-attribute — obj/bin leaking AssemblyInfo into integration project's default **\*.cs glob. Fixed by cleaning all obj/bin directories in simulator test tools.

**Test Flake:** AiDetectTreeKillIntegrationTests capturing unrelated ping.exe processes via by-name discovery during 10s window. Fixed by replacing with temp-file handshake protocol: parent writes $PID and child $p.Id to file; test tracks only those two PIDs. Verified 10/10 deterministic, avg 1s per run. Added $ErrorActionPreference='Stop' + $null guard to fail fast on Start-Process anomalies.

**Final State:**
- Format clean: dotnet format applied
- Build clean: 0 errors
- Unit tests: 786/786 passing
- Integration tests: 134/134 passing (11 LocalOnly skipped)
- No flakes or environmental reds

### Niobe: CHANGELOG + README Updates

**CHANGELOG.md (0.22.0 § Changed):**
- Added 3 bullet points under 0.22.0 § Changed documenting:
  - Auto-resolved Copilot CLI path (path no longer configurable in Settings)
  - Dynamic model dropdown (fetched from GitHub Copilot models API; fallback to hardcoded list)
  - Cache-first + stale-fallback resilience

**README.md:**
- Settings table updated: CopilotPath row removed, model now described as "auto-discovered from GitHub Copilot"
- Feature description updated to reflect path auto-discovery
- No version bump (per user constraint: refinements only, 0.22.0 locked)

**Format:** Keep a Changelog convention; focused on user-facing changes

### Integration + Test Results

**Unit tests:** 786/786 passing (7 new tests for CopilotModelsService, 5 new tests for SettingsForm dropdown, existing tests updated for path removal)

**Integration tests:** 134/134 passing, 11 LocalOnly skipped (per all-green directive)

**Build:** 0 errors, 0 warnings, format clean

**Status:** Delivered end-to-end. All agents completed. Coordinator applied final build/flake fixes. Ready for squad documentation merge.

---
# Squad Decisions

## 2026-05-09: Issue #23 — LocalOnly Real-Copilot Test + Concurrency E2E + Docs + Version Bump

**Date:** 2026-05-09  
**Status:** Delivered  
**Contributors:** Tank (real-copilot LocalOnly test, concurrency E2E, test patterns)

### Tank: LocalOnly Real-Copilot Integration Test Guarding

**Issue:** #23

Real `copilot -p` integration tests must guard against missing fixture session, non-repo CWD, and auth failures.

**Pattern:**
- `[LocalOnlyFact]` + `[Trait("Category", "LocalOnly")]` markers
- Default runs and CI skip unless `COPILOT_BOOSTER_RUN_LOCALONLY=1` environment variable is set
- Local runs still skip when fixture session does not exist, fixture CWD is not a GitHub repo, or `copilot spawn/auth` fails with `ProcessSpawn` or `ProcessFailure`
- Test copies session-state fixture to temp root, uses fresh session ID, deletes temp root and SessionStateService tracking folder after run
- No red bars in CI; developers opt-in locally with env var

**Consequence:**
- CI remains green (LocalOnly tests skip cleanly with [Trait])
- Local testing can validate real copilot integration without breaking default runs
- Clear auth guard signal: developers know why the test was skipped
- Aligns with "All-Green Integration Directive" (Roger Barreto, Issue #20)

### Tank: Concurrency E2E Test (Parallelism=5)

**Issue:** #23

Concurrent real `copilot -p` detection spawning validates no queueing, race conditions, or shared-state collisions.

**Pattern:**
- Task.WhenAll(5 parallel detection tasks)
- All 5 spawn real copilot CLI simultaneously
- All 5 must succeed and return correct GitHub issue/PR detection
- Validates concurrent IProcessRunner execution and state isolation

**Consequence:**
- Regression protection against future queueing bugs
- Confidence that AiDetectionService handles parallel invocation
- E2E validation of full detection pipeline under load

### Integration + Documentation

**Deliverables:**
- README.md: New section documenting "AI auto-detect GitHub issue / PR" feature (Right-click → GitHub → AI → Auto Detect GitHub Issue and PR; configurable in Settings → AI)
- CHANGELOG.md: Entry under `[0.22.0] - 2026-05-08` Added section consolidating all 7 PRs/slices (#17–#23) under one feature statement
- Version bump: 0.22.0 (already bumped in src/CopilotBooster.csproj and installer.iss)

**Test Results:**
- Unit tests: 768/768 pass
- Integration tests: 134/134 pass, 11 LocalOnly skipped
- Build: 0 errors, clean format
- All-green integration bar maintained per Roger's directive

### Cumulative Pipeline (#17–#23)

7 PRs/slices complete. Feature "AI auto-detect GitHub issue / PR for sessions" delivered end-to-end:
- #17: Foundation state machine
- #18: GH API integration
- #19: UI cell click dispatch
- #20: Spinner + cancel + JobObject tree-kill
- #21: Icon + HTML scraping
- #22: Settings UI
- #23: LocalOnly test + concurrency + docs + version bump

**Status:** Production ready. All tests passing. Code formatted. Version 0.22.0. Awaiting human-driven release (PR → tag → sign).

## 2026-05-08: Issue #20 — Cell Spinner + Click-to-Cancel + JobObject Tree-Kill

**Date:** 2026-05-08  
**Status:** Delivered  
**Contributors:** Trinity (ProcessRunner JobObject), Morpheus (UI spinner + confirm dialog), Tank (state machine + tree-kill tests)

### Trinity: JobObject Tree-Kill in ProcessRunner

**Issue:** #20  

Production `ProcessRunner` owns process tree termination with a raw Win32 Job Object.

**Pattern:**
- Create Job Object
- Set `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` through `JOBOBJECT_EXTENDED_LIMIT_INFORMATION`
- Start child process
- Assign child handle to the job immediately after `Process.Start()`
- On linked cancellation or timeout, call `TerminateJobObject(job, 1)`
- Always close the job handle in `finally`

**Service contract:**
- `IProcessRunner.RunAsync(...)` signature unchanged
- `ProcessResult.WasKilled == true` signals cancellation or timeout tree termination
- `AiDetectionService` classifies `WasKilled && cts.IsCancellationRequested` as `cancelled`, not a failure
- `DetectionStateChanged(sid, Running, Idle)` remains the spinner clear signal for Morpheus

### Morpheus: IConfirmDialog Seam for Cancel Confirmation

**Issue:** #20  

Use `src/Forms/IConfirmDialog.cs` interface for the cancel confirmation seam.

**Shape:**
```csharp
internal interface IConfirmDialog
{
    bool Confirm(string title, string body, string yesLabel, string noLabel);
}
```

**Production implementation:** `MessageBoxConfirmDialog` wraps `MessageBox.Show(YesNo)`. Since native WinForms cannot customize button text, message appends:
```
Yes = Stop
No = Keep running
```

**Consequences:**
- Tank can fake `IConfirmDialog` and assert title, body, and labels
- `SessionGridVisuals.HandleGitHubCellClick(...)` stays testable without showing UI
- Custom Form can replace `MessageBoxConfirmDialog` later if exact button text becomes mandatory

**Deliverables:**
- Top-right 16x16 corner spinner region in SessionGridVisuals
- ONE shared `System.Windows.Forms.Timer` for all running rows
- Click-to-cancel with confirm dialog
- Spinner-frame icon rendering in GitHubIconRenderer
- Test seams: `HandleGitHubCellClick`, `GetStatusIconRegion`, `IsSpinnerVisibleForSession`

### Tank: Spinner Test Serialization Decision

**Issue:** #20  

`AiDetectSpinnerCancelIntegrationTests` belongs to `WindowEventHookCollection` serialized subset.

**Why:** The class does not start `WindowEventHookService`, but uses WinForms timer state plus `Application.DoEvents()` inside the same desktop test process. In full integration runs, parallel execution intermittently left `_spinnerTimer.Enabled` true after the service returned to idle. Serialization preserves the all-green integration bar.

**Scope:** Only this spinner/cancel class was added. No global integration parallelism change.

**Deliverables:**
- Cancel state transition tests
- Dispose-cancels-all test
- Spinner state machine tests
- Confirm/fallthrough E2E integration tests
- Real tree-kill test with PowerShell child process spawning and PID diff capture

## 2026-05-03: Round 3 Completion — Copilot Host Discovery Phase 3 + All-Green Integration Directive

**Date:** 2026-05-03  
**Status:** Complete  
**Contributors:** Trinity (E-Triggers, Cleanup, F-UIA), Tank (Round 3 tests), Roger Barreto (grilling + directive)

### User Directive — All Integration Tests Must Be Green

**By:** Roger Barreto  
**Precedence:** Supersedes prior decisions to tolerate environmental baseline failures

**The Directive:**
All integration tests must be GREEN. Period. There is no acceptable baseline of failing integration tests — locally OR in CI. Tests that fail today because of environmental gaps (e.g., Playwright not installed locally) are TEST BUGS to be fixed, not "known baseline failures" to document and tolerate. Either:
1. The test self-bootstraps its environment (e.g., `playwright install chromium` runs as IT collection fixture/setup), OR
2. The test is explicitly skipped with a `[Trait("Category", ...)]` filter that the runner honors both locally and in CI (no red bar from missing setup).

**Why:** The user's standing release rule ("all tests must pass before any release — both unit tests and integration tests") was being violated by the team accepting environmental reds as a normal-state baseline. The grilling exposed that this acceptance had become a process smell: inventing baseline-comparison ceremony to interpret test-output noise that shouldn't exist in the first place. Restoring the "binary green" bar is simpler, more honest, and matches the user's actual policy.

**Supersedes:** Any prior decision to "document local Playwright pain in TROUBLESHOOTING.md", "maintain a tolerance list of named-diff baseline failures", or "compare test output against main to interpret failures". The right answer was always "auto-install Playwright (option c from earlier triage) or mark tests skippable so the local run is also green."

### User Directive — Zero Warnings Policy

**By:** Roger Barreto  
**Status:** Ongoing

There should be no warnings at all in the CI pipeline. All warnings must be addressed without excuse — they fail the pipeline.

### Phase 3 Delivery Summary

| Task | Owner | Status | Tests | Notes |
|------|-------|--------|-------|-------|
| T1/T2/T4/T5 Trigger Wiring | Trinity | ✅ | 647 unit | External discovery, PID registration, FullRefresh, window eviction, focus migration |
| GUID Fallback Cleanup | Trinity | ✅ | Build clean | ADR-0001 compliance, no placeholder writes |
| Deferred Name Resolution | Trinity | ✅ | Integration | First user.message extraction, sidecar update on change |
| UIA Gateway (Windows Terminal) | Trinity | ✅ | 250ms time-box | SelectionItemPattern → InvokePattern fallback |
| Test Coverage (Unit + IT) | Tank | ✅ | 10 unit + 20 IT | Host dict scaffolding + Phase 5 integration paths |

**Unit Tests:** 647/647 pass (was 637, +10)  
**Integration Tests:** 104 total with 13 pre-existing baseline reds (Playwright browser + LocalOnly skips) — now superseded by all-green directive.  
**Build:** 0 warnings, 0 errors across all 3 projects  
**Format:** `dotnet format --verify-no-changes` clean

### Phase 3 Architecture — Trinity E-Triggers

**File:** `.squad/decisions/inbox/trinity-0210-phase3-triggers.md`

5 distinct triggers populate and maintain `_copilotHosts` dictionary:

1. **T1 (External Session Discovery):** `CopilotLogWatcherService.ExternalSessionDiscovered` signature changed from `Action<string>` to `Action<string, int>` (sessionId + copilotPid). Handler calls `_hostResolver.Resolve(copilotPid)` and writes Booster-Resolved Name placeholder via sidecar.
2. **T2 (Internal PID Registration):** `PidRegistryService.CopilotPidRegisteredStatic` static event with dedup logic (`s_lastCopilotPidByLauncherPidStatic` dict). Static event needed because `Program.cs` calls `UpdatePidSessionId` statically.
3. **T4 (FullRefresh Re-Resolution):** `ActiveStatusTracker.FullRefresh` iterates `SessionService.GetActiveSessions()` after cleanup, re-resolving hosts for sessions with `CopilotPid > 0` where host is missing or dead.
4. **T5 (Window Destruction):** `WindowEventHookService.WindowDestroyed` wired to `ActiveStatusTracker.HandleWindowDestroyed` for HWND-based host eviction.
5. **Focus Migration:** `TryFocusCopilotCli` and `FocusActiveProcess` check `_copilotHosts` first (Priority 1: direct HWND), then legacy title-scan (Priority 2), then PID-based fallback (Priority 3).

**Key Design Decisions:**
- Short-circuit HWND-alive checks in T2 and T4 to avoid redundant re-resolution
- Placeholder format: `"{HostProcessName}:Copilot"` (unresolved state, replaced by deferred path)
- Focus dedup: `FocusActiveProcess` skips duplicate HWND insertion when host already added
- Legacy paths preserved as safety net (no regression risk for older sessions)

### Phase 3 Architecture — Trinity Cleanup

**File:** `.squad/decisions/inbox/trinity-0210-cleanup-summary-and-deferred.md`

Two components establish ADR-0001 compliance and deferred name resolution:

1. **Drop GUID Fallback:** `CopilotLogWatcherService.CreateWorkspaceYamlFromPid` no longer writes `workspace.yaml.summary` when window title resolution fails. External sessions now create workspace.yaml with NO `summary:` field (vs. previous GUID fallback).
2. **Deferred Resolution:** `EventsJournalService.TryResolveBoosterName` hook on `OnFileChanged` extracts first user.message content from events.jsonl and updates sidecar when content is available. Short-circuited once `ResolvedFromUserMessage == true` to avoid redundant extraction.

**New Event:** `BoosterResolvedNameUpdated(sessionId)` for UI refresh signals.

**Contract:**
- `CreateWorkspaceYamlFromPid` NEVER writes GUID to `workspace.yaml.summary`
- `TryResolveBoosterName` runs on FileSystemWatcher thread — subscribers must marshal to UI
- Resolution happens at most once per session (idempotent after first success)

### Phase 4 Architecture — Trinity F-UIA

**File:** `.squad/decisions/inbox/trinity-0210-uia-gateway-impl.md`

`WindowsTerminalPaneGateway` implements `IWindowsTerminalPaneGateway` via `System.Windows.Automation` to enable pane-level focus for Windows Terminal.

**Key Design Decisions:**
- **`<UseWPF>true</UseWPF>` enabled:** Direct `<Reference Include="UIAutomationClient" />` approach failed in .NET 10 with assembly resolution errors. UseWPF transitively provides assemblies from Desktop shared framework.
- **250ms time-box:** `Stopwatch` elapsed check inside `foreach (AutomationElement tabItem)` loop. Breaks early if budget exceeded.
- **Pattern preference:** Try `SelectionItemPattern.Select()` first, fall back to `InvokePattern.Invoke()`.
- **Exception strategy:** Top-level try/catch logs warning and returns empty list. Per-action try/catch for pattern invocations (swallows `ElementNotAvailableException` and `InvalidOperationException`).

**Consequence:** `System.Windows.Automation` is now available throughout the codebase. Tests can fake `IWindowsTerminalPaneGateway` without touching real UIA. Adding pane focus for other hosts (tmux, Warp) requires implementing a new gateway behind the same interface pattern.

### Round 3 Test Coverage — Tank

**File:** `.squad/decisions/inbox/tank-0210-round3-tests.md`

30 new tests (10 unit + 20 integration) for Phase 3 scaffolding and Phase 5 integration:

**Unit Tests (10 total):**
- `ActiveStatusTrackerHostTests.cs`: Round-trip, idempotency, updates, removal, projection, unprojection, dedup, event data verification

**Integration Tests (20 total):**
- `InternalSessionHostResolutionIntegrationTests.cs`: Real process tree, PID skip, deep nesting, host classification
- `InternalSessionTitleChangesIntegrationTests.cs`: HWND-based host path robustness against title changes (proves fix for the hardest existing bug)
- `ExternalSessionWorkspaceYamlTests.cs`: No `summary:` writes (ADR-0001 compliance)
- `DeferredNameResolutionIntegrationTests.cs`: Placeholder → resolved name transition

**Test Count Growth:** 637 unit → 647 unit (+10), 84 IT → 104 IT (+20)

### Round 4 Integration Tests All-Green Mandate — Tank

**Date:** 2026-05-03  
**Status:** Complete  
**Implementation:** Tank (Round 4)  
**Directive:** Enforce Roger Barreto's all-green mandate across all integration tests

**Problem:** Round 3 ended with 13 baseline-red integration tests (Playwright browser not installed locally, LocalOnly tests). These were documented as "known environmental failures" — a process smell. The directive supersedes tolerance: ALL tests must be GREEN, period. Either tests self-bootstrap their environment or skip cleanly.

**Solution:**

1. **Playwright auto-bootstrap fixture:**
   - New xUnit collection: `PlaywrightBootstrap`
   - Collection fixture probes `Playwright.CreateAsync().Chromium.LaunchAsync()`
   - On missing browser: auto-calls `Microsoft.Playwright.Program.Main(["install", "chromium"])`
   - On install failure: skips cleanly (xUnit skip mechanism, no red bar)
   - Timing: 36s first run (with install), 29s cached, vs. 39s pre-bootstrap inventory with 13 reds

2. **LocalOnly conditional skip policy:**
   - Custom attributes: `LocalOnlyFactAttribute`, `LocalOnlyStaFactAttribute`
   - Skip unless `COPILOT_BOOSTER_RUN_LOCALONLY=1` or `true` is set
   - Trait-only approach fails; attribute-based skip is reliable in in-process xUnit runner

3. **Test-shape corrections:**
   - `CopilotLogWatcherService.TryParseLogContent` expects `kind: "session_start"`, `session_id`, `context.cwd` (not raw top-level `cwd`)
   - Deferred name resolution code-fence test corrected to exercise production formatter contract (not invented trailing-fence stripping)

4. **Build isolation and stability:**
   - `tests/Directory.Build.props` gives `CopilotBooster.IntegrationTests` its own `obj-integration/` intermediate path
   - Prevents solution-level restore/build from racing with unit test project and dropping Playwright references
   - `RunningAppsGridDetectionTests` assigned to non-parallel xUnit collection (uses global WinEvent hooks and real windows)
   - Collection narrowing keeps full IT run at ~29s instead of disabling all parallelism

**Outcome:**

| Metric | Result |
|--------|--------|
| Integration Tests Passed | 99 / 104 |
| Integration Tests Skipped | 5 / 104 |
| Integration Tests Failed | 0 / 104 ✅ |
| Unit Tests | 647 / 647 ✅ |
| Build (dotnet build --tl:off) | 0 warnings, 0 errors ✅ |
| Format (dotnet format --verify-no-changes) | Clean ✅ |
| Cached IT Runtime | 29.247s |

**Consequence:** Directive satisfied. Binary green bar restored. Local `dotnet run --project tests/CopilotBooster.IntegrationTests.csproj -c Release` produces zero failures. All tests either pass or skip cleanly.


---

## Issue #17: Tracer End-to-End Happy Path (AI Auto-Detect GitHub Link)

**Date:** 2026-05-08  
**Status:** Complete  
**Contributors:** Trinity (Services Dev), Morpheus (UI Dev), Tank (Tester)

### Service Contract — Trinity

**Decision:** `AiDetectionService` owns the service orchestration for slice #17 and exposes only service-safe hooks.

- **Process boundary:** `IProcessRunner.RunAsync(...)` returning `ProcessResult`
- **State event:** `DetectionStateChanged(string sid, DetectionStatus oldStatus, DetectionStatus newStatus)`
- **Query:** `TryGetState(string sid)` plus out-param overload
- **Toast:** Injected `Action<string>` sink, no new toast interface
- **CWD:** Injected `Func<string,string?>`, so MainForm uses cached sessions while tests use fixture roots

**Deferred to later slices:**
- Strict schema validation and failure classes → slice #18
- Proper upstream then origin repo resolution → slice #19
- JobObject process tree kill and visual status cells → slices #20 and #22
- Settings-backed timeout, threshold, binary path, and model → slice #21

**Outcome:** Public surface stable. Service API locked before Morpheus and Tank implementation. All 667 unit tests pass, 99/104 integration tests pass.

### Shared Test Double Location — Tank

**Decision:** `FakeProcessRunner` lives at `tests/Integration/TestTools/FakeProcessRunner.cs` and is shared by AI detection slices.

**Contract:**
```csharp
RunAsync(string fileName, IReadOnlyList<string> args, string cwd, int timeoutSeconds, CancellationToken ct)
```

**Usage for exact process boundary assertions:**
- `fileName == "copilot"`
- args contain the rendered prompt and required flags
- cwd equals the session cwd
- timeout equals 300 for slice 17

**Rationale:** Later slices can reuse the same fake for malformed JSON, timeout, cancellation, and real CLI boundary tests without cloning runner logic.

**Outcome:** Tank implemented FakeProcessRunner. Trinity and Morpheus verified contracts pass. All integrations green.

### Windows Terminal Multi-Pane Discovery and Focus — Trinity (Phase 4)

**Date:** 2026-05-03  
**Status:** Implemented  
**Decision:** Carry the UIA tab runtime id as part of `CopilotHostInfo` and treat WT session identity as `(parent HWND, pane runtime id)`.

**Changes:**
- Cache WT panes by `(parent HWND, pane runtime id)`, not parent HWND alone
- Keep host projections from being removed by parent HWND title churn
- On focus, if the host is WT and a runtime id is known, call `IWindowsTerminalPaneGateway.FocusPane(parentHwnd, runtimeId)`
- `FocusPane` selects the UIA tab item with `SelectionItemPattern.Select()` and falls back to `InvokePattern.Invoke()`

**Consequences:**
- Non-WT hosts keep the existing one-host-per-HWND behavior
- WT focus remains degradable: if runtime-id selection fails, parent window is foregrounded and title/process matching remains as fallback
- Live LocalOnly E2E now uses separate WT tabs and asserts both active-grid discovery and selected-tab focus

**Outcome:** All 647 unit tests pass. Trinity-H learnings recorded in history.md.

---

## Issue #18: Strict Response Validator + 6 Failure Classes for AI Auto-Detect

**Date:** 2026-05-08  
**Status:** Complete  
**Contributors:** Trinity (Services Dev), Tank (Tester)  

### Strict Parser Architecture — Trinity

**Decision:** `AiResponseParser.Parse` returns discriminated union `AiParseResult` instead of candidate list.

```csharp
internal abstract record AiParseResult
{
    internal sealed record Success(IReadOnlyList<AiCandidate> Candidates) : AiParseResult;
    internal sealed record Failure(AiFailureClass Class, string Reason) : AiParseResult;
}
```

**Strict Validation Rules:**
- Rejects empty stdout, prose, markdown fences, non-object roots
- Requires `candidates` field (array, not null)
- Rejects candidate non-objects, missing required fields, wrong JSON types
- Enforces case-sensitive type values: `issue` or `pr` only
- Enforces non-positive item ids and PR numbers
- Enforces confidence within inclusive [0.0, 1.0] range
- Accepts up to 3 valid candidates; sorts by confidence descending (array-order tiebreak), truncates excess

**Parser Contract:**
- Empty stdout is malformed (no success object)
- Empty candidates array is parse success (zero candidates, no reason)
- Non-empty success with 1–3 candidates returns `Success(candidates)`
- Any violation returns `Failure(class, reason)` where class is `MalformedJson` (JSON parse) or `SchemaViolation` (shape/type/range)

**Why:** Deterministic classification allows downstream service to route failures to appropriate handling without nested if/catch chains. Empty success (zero candidates) is distinct from schema violation so `AiDetectionService` can classify it as `NoCandidates` (not parser error).

### 6-Class Failure Classifier — Trinity

**Decision:** `AiFailureClass` enum lives in `src/Services/` with six deterministic failure classes:

```
Timeout
ProcessSpawn
ProcessFailure
MalformedJson
SchemaViolation
NoCandidates
```

**Classifier Routing in `AiDetectionService`:**

1. **Process spawn:** Try-catch wrapper around `IProcessRunner.RunAsync` start; classify start exceptions (missing binary, access denied) as `ProcessSpawn`.
2. **Timeout:** If process was killed AND `!ct.IsCancellationRequested`, classify as `Timeout`. If killed after user cancel, leave `FailureClass` null (not a failure).
3. **Process exit:** On nonzero exit (before parsing stdout), classify as `ProcessFailure`.
4. **Parser results:** Exit code zero → call `AiResponseParser.Parse(stdout)`:
   - `Failure(MalformedJson, ...)` → `FailureClass = MalformedJson`
   - `Failure(SchemaViolation, ...)` → `FailureClass = SchemaViolation`
   - `Success([])` (empty) → `FailureClass = NoCandidates`
   - `Success(candidates)` (non-empty) → proceed to threshold/apply logic

**Logging Contract:**

- **Start log:** `session_id`, `resolved_owner_repo`, `configured_timeout_seconds`
- **Debug logs:** `exact_prompt_sent`, `raw_stdout`, `raw_stderr` (no redaction)
- **End log:** `outcome`, `failure_class`, `reason`, `exit_code`, `candidate_count`, `top_confidence`, `applied_items`, `duration_ms`
- **Log levels:** `Timeout` and `NoCandidates` use WARNING; `MalformedJson`, `SchemaViolation`, `ProcessSpawn`, `ProcessFailure` use ERROR

**Observation Point:**
- `DetectionState.FailureClass` is nullable `AiFailureClass?`
- Tests and UI observe via `TryGetState(sid).FailureClass` (no new event arg)

**Why:** Centralizing failure classification in the service (not in parser, not in runner) allows parser to focus on JSON purity and service to own orchestration semantics. Six classes are sufficient for slice #18; future slices (timeout settings, retry policy, etc.) will extend logging and thresholds without new classes.

### Test Infrastructure Extensions — Tank

**Decision:** Extend shared test doubles for AI detection slices.

**FakeProcessRunner Extensions:**
- Already implements `IProcessRunner.RunAsync(fileName, args, cwd, timeoutSeconds, ct)`
- Add `SetResult(ProcessResult result)` for per-test canned outcomes
- Add `ThrowOnNextCall(Exception ex)` for process spawn failure paths

**CapturingLogger (New):**
- Lives at `tests/Integration/TestTools/CapturingLogger.cs`
- Implements `ILogger` to capture level/message/exception tuples
- Used by E2E failure-class tests for log-level assertions without file scraping

**Why:** FakeProcessRunner is already the shared copilot process boundary fake; extending it for canned results and exception support avoids test cloning. CapturingLogger is faster and more deterministic than scraping app log files for assertion.

**Outcome:**
- `tests/Services/AiResponseParserTests.cs` — ~28 strict-violation rows
- `tests/Services/AiDetectionServiceTests.cs` — classifier unit tests for all six classes
- `tests/Integration/AiDetectFailureIntegrationTests.cs` — six E2E tests (one per class)
- `tests/Integration/TestTools/FakeProcessRunner.cs` — extended with SetResult/ThrowOnNextCall
- `tests/Integration/TestTools/CapturingLogger.cs` — ILogger capture

---

## Issue #19: Repo Resolution + AI Menu Preconditions Gating

**Date:** 2026-05-08  
**Status:** Complete  
**Contributors:** Trinity (Services Dev), Morpheus (UI Dev), Tank (Tester), Coordinator (test stabilization)  

### Fork Parent Resolver via GH_PATH Environment — Trinity

**Decision:** Use `GH_PATH` environment variable override for fork parent lookup instead of adding an `IForkResolver` interface.

**Rationale:** Issue #19 needs fork parent detection via `gh repo view owner/repo --json parent --jq .parent.nameWithOwner`. The lookup must be best-effort, bounded to 5 seconds, and must not throw from repo resolution.

**Contract:**
- Production uses `gh` from PATH
- Tests can set `GH_PATH` to a fake executable
- If `gh` fails, times out, or returns no parent, resolution falls back to the remote repo
- No new dependency or service interface is needed

**Outcome:** GitService.TryResolveGitHubRepo implements full HTTPS/SSH/fork-parent chain with timeout protection.

### Window Event Hook Collection Serialization — Tank

**Decision:** Any integration test that starts `WindowEventHookService` belongs in `WindowEventHookCollection`.

**Why:** WinEvent hooks and real window title changes race when multiple classes run in parallel. The all-green integration directive needs these tests serialized, not retried.

**Applied To:**
- `TerminalTitleDetectionIntegrationTests`
- `WindowEventHookIntegrationTests`
- `TimerRefreshAfterFormCloseTests`

**Side Effect Discovered:** Collection-grouping changes shifted parallel test scheduling and exposed latent race in `IdeTrackingIntegrationTests.E2E_IdeWithFolderPath_ProcessKilled_NoDestroyEvent_GridMustClear`. Coordinator added collection attribute to serialize IDE tests alongside window-hook tests, resolving the race.

**Outcome:** All 118 integration tests green (10 LocalOnly skipped per policy). No races or flakes.

### AiMenuState Enum and Preconditions Gating — Trinity

**Decision:** `AiMenuState` enum gates menu enable/disable + tooltip messaging for AI auto-detect menu item.

```csharp
enum AiMenuState
{
    Unavailable,        // Service error or not initialized
    NoSession,          // No active session
    RepoNotFound,       // Repo resolution failed
    TelemetryBlocked,   // AI telemetry disabled in settings
    Ready               // All preconditions met, menu enabled
}
```

**Preconditions:**
1. Service initialized and healthy
2. Active session exists with valid CWD
3. GitService.TryResolveGitHubRepo succeeds
4. AI telemetry enabled (prior-tracking-data trust chain)

**UI Contract:**
- `ExistingSessionsVisuals.GetEvaluatedAiMenuItem()` returns internal accessor for Tank tests
- Menu DropDownOpening event triggers `AiDetectionService.EvaluateMenuState(sessionId)`
- Tooltip messaging via `AiDetectionTooltips` constants per state

**Outcome:** All menu states tested and integrated. UI wiring complete.

---

## Issue #22: Undecided + Error Icons + Dedup Variant + Partial-Dedup Toast

**Date:** 2026-05-08  
**Status:** Delivered  
**Contributors:** Trinity (Services Dev), Morpheus (UI Dev), Tank (Tester)

### Undecided Outcome and UndecidedReason Enum — Trinity

**Decision:** Extend `AiDetectionService` state machine with `DetectionStatus.Undecided` outcome and `UndecidedReason` enum.

```csharp
enum UndecidedReason
{
    LowConfidence,      // Candidates exist but below configured threshold
    AllAlreadyLinked    // All candidates already linked (dedup variant)
}
```

**Rationale:**
- LowConfidence occurs when parser returns valid candidates but none exceed the confidence threshold.
- AllAlreadyLinked is the partial-dedup variant: candidates exist and pass threshold, but all are already linked.
- Undecided is NOT a failure — it's an outcome requiring no action (no apply, no error icon fallback).
- Contrasts with Success (apply all new + dedup) and Failure (error icon, logs).

**Surface:**
- `DetectionState.UndecidedReason` property (nullable when status is not Undecided)
- `AiDetectionService.Reset(string sessionId)` clears detection state for manual add/issue workflows
- `AiDetectionTooltips.ForUndecided(UndecidedReason reason)` routes reason-specific tooltip text

### TopCandidates Projection — Trinity

**Decision:** Parser returns all valid candidates in input order; service projects only top-3 for UI display.

**Rationale:**
- Partial-dedup tests require full candidate list to classify all duplicates.
- UI only needs top-3 for low-confidence reason display.
- Apply logic evaluates all above-threshold candidates, not just top-3.

**Contract:**
- `AiResponseParser` preserves every valid candidate sorted by confidence descending.
- `DetectionState.TopCandidates` returns up to 3 candidates for grid icon tooltip.
- Service dedup logic and apply logic use full candidate list internally.

### Error Icon and Fallback Tracking — Trinity + Morpheus

**Decision:** When detection encounters any `AiFailureClass`, track error state for icon fallback rendering.

**Rationale:**
- Error state distinct from Running (no spinner) and Undecided (no error icon).
- Morpheus renders `!` (error) icon in corner when status is Error.
- Icon region reuses Issue #20 corner placement and click dispatch.

**Surface:**
- `DetectionState.Status == Error` when any failure class occurs
- `AiDetectionService.TryGetState(sid)` reports error status
- Morpheus queries status to render appropriate icon

### IMessageBox Seam for Dismiss Flow — Morpheus + Tank

**Decision:** New `IMessageBox` interface for click-to-dismiss confirmation dialog (sibling to Issue #20 `IConfirmDialog`).

**Shape:**
```csharp
internal interface IMessageBox
{
    bool Show(string title, string message, string okLabel, string cancelLabel);
}
```

**Production implementation:** `MessageBoxWrapper` wraps `MessageBox.Show(OKCancel)`.

**Rationale:**
- Tank can fake the dialog for integration tests without blocking on WinForms message boxes.
- Parallel to `IConfirmDialog` pattern (cancel confirmation).
- Allows testing click-to-dismiss flow in grid rendering tests.

**Outcome:**
- Corner click on Undecided/Error state shows confirmation: "Dismiss and stop detecting?"
- User confirms dismiss → `AiDetectionService.Reset(sessionId)` → grid refreshes
- User cancels → state unchanged, detection continues

### Partial-Dedup Toast Pattern — Trinity + Morpheus

**Decision:** On AllAlreadyLinked outcome, create one toast listing all duplicate issue links.

**Toast Format:** `"X issue link(s) already exist: #123, #456, ..."`

**Rationale:**
- User sees all duplicates in one notification (better UX than silent no-op).
- Toast sink already wired in Issue #17; no new interface needed.
- Service creates toast, MainForm displays it per existing pattern.

**When Toast Appears:**
- Detection completes with Undecided{AllAlreadyLinked}
- All candidates resolved and found linked
- Toast lists every duplicate by issue number
- No toast when Undecided{LowConfidence} (no linking action taken)

### Grid Cell Rendering and Click Dispatch — Morpheus + Tank

**Decision:** Grid renders state-dependent icons in corner region (reuses Issue #20 spinner corner).

**Icon Set:**
- Running: 8-frame spinner (Issue #20)
- Undecided: `?` icon (new, cached in `GitHubIconRenderer`)
- Error: `!` icon (new, cached in `GitHubIconRenderer`)
- Idle/Success: no corner icon

**Click Routing:**
- Corner click on Undecided/Error: trigger dismiss dialog via `IMessageBox`
- Corner click on other states: no-op (not clickable when not Undecided/Error)
- Non-corner GitHub cell click: fall through to PR/issue strip handler (unchanged)

**Tooltip Routing:**
- Corner icon region: "Detecting... click to dismiss" (Undecided) or "Detection error" (Error)
- PR/issue strip: existing PR/issue tooltip
- Both sourced from `AiDetectionTooltips` helpers

### Parser Candidate Retention — Tank inbox (decision in first commit)

**Decision:** `AiResponseParser` preserves every valid candidate in input order instead of truncating to 3.

**Rationale:** Partial-dedup tests require full candidate list to apply multiple links while also reporting duplicates.

**Outcome:** `DetectionState.TopCandidates` projects top-3 for UI; full list available for service dedup/apply logic.

### OutcomeKind.NoCandidatesVariant — Trinity inbox (decision in first commit)

**Decision:** Use `DetectionState.OutcomeKind.NoCandidatesVariant` for all-already-linked dedup variant instead of adding a seventh `AiFailureClass`.

**Rationale:** All-already-linked is not an error; it uses undecided visual path and has `UndecidedReason.AllAlreadyLinked`, preserving six failure classes from Issue #18.

---

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction

