

---

## Archived: 2026-05-14T15-01-25Z

# Tank — History

## Core Context

- **Project:** A C# WinForms (.NET 10) desktop application that enhances GitHub Copilot workflow with session management, IDE integration, and GitHub tracking.
- **Role:** Tester
- **Joined:** 2026-03-15T15:50:59.676Z

### Key Testing Principles (distilled from learnings)

- **All-Green Directive (Roger, 2026-05-03):** All integration tests must be GREEN at all times — no tolerance for environmental baseline failures. Tests that fail due to missing setup are TEST BUGS requiring self-bootstrapping (fixture installs) or explicit skip traits `[Trait("Category", "LocalOnly")]` or `[Trait("Category", "RequiresInteractiveDesktop")]`.
- **Determinism over documentation:** When tests are flaky (e.g., race conditions, timing assumptions), fix the test design, not the baseline. Example: `AiDetectTreeKillIntegrationTests` flake fixed via temp-file handshake PID protocol instead of by-name process discovery.
- **Test structure preferences:** xUnit v3 async facts (`[Fact] public async Task`), `[Theory]` with `[InlineData]` for parameterized cases, `IDisposable` with temp dirs for file-based tests, stub classes (no mocking framework) for seams, `[LocalOnlyFact]` + `[Trait("Category", "LocalOnly")]` for real-copilot/desktop-bound tests.
- **Seam patterns:** Tests expose service internals via internal interfaces (`IProcessRunner`, `IConfirmDialog`, `IMessageBox`, `ICopilotProbe`) and `Func<T>` parameter injection (e.g., `Func<string, string?>` CWD resolver, `Func<AiDetectionSettings>` settings getter). Avoid refactoring code purely for testability; use reflection pins when seams would bloat production.
- **Integration test serialization:** Desktop tests (WinEvent hooks, WinForms timers, terminal interaction) go in serialized xUnit collections `[Collection(WindowEventHookCollection.Name)]` to prevent race conditions.
- **Performance bounds:** Synthetic tests (large file parsing, concurrency stress) use explicit `[Fact(Skip = "LocalOnly")]` to skip CI but validate locally. Regression tests capture max memory/iteration budgets and assert compliance.

## Learnings

<!-- Append learnings below -->

- **STANDING RULE (2026-05-13): All-Green Test Suite Required** — Pre-existing test failures are NOT acceptable. The team may not declare work "done" while ANY test is failing, even if pre-dating the current change. Whoever lands work meeting a red suite must either (a) fix the pre-existing failure as part of delivery, or (b) escalate with analysis + plan before claiming completion. "Unrelated" is not sufficient. This is binding release policy: the project ships only with a fully green suite.

- **2026-05-14: Update-source git integration fixture:** `GitTestRepo` builds a source repo, bare remote, and local clone, then pushes commits after clone so the local ref is behind. Use it for end-to-end git update tests through `GitService.RunGitAsync`, not raw process shelling. Edge cases covered: checked-out branch fallback must fetch the remote-tracking ref before creating from `origin/<branch>`; no-upstream local branches remain at local tip after fallback fetch; bogus remotes return a surfaced error.

- **2026-05-03 — All-green integration directive:** All integration tests must be GREEN at all times. No tolerance for environmental baseline failures. Tests that fail due to missing setup (e.g., Playwright not installed locally) are TEST BUGS requiring self-bootstrapping (fixture installs) or explicit skip traits. This supersedes any prior decision to "document pain" or "tolerate named-diff baseline". Baseline red count (13 tests) must be reduced to zero via fixture auto-install or skip traits, not accepted as "normal".
- **Async test pattern:**xUnit v3 supports `[Fact] public async Task` natively — no special runner needed. Use `.ConfigureAwait(false)` in tests since they don't run on a UI thread.
- **ImplicitUsings covers Tasks/Threading:** The test project has `<ImplicitUsings>enable</ImplicitUsings>`, so `System.Threading.Tasks` and `System.Threading` are available without explicit imports.
- **GitResult record struct:** Agreed design uses `readonly record struct GitResult(int ExitCode, string Stdout, string Stderr)` — tests reference `.ExitCode`, `.Stdout`, `.Stderr` properties.
- **RunGitAsync signature:** `RunGitAsync(string repoPath, string arguments, IProgress<string>? stderrProgress = null, CancellationToken cancellationToken = default)` — named parameter `cancellationToken:` needed when skipping `stderrProgress`.
- **CreateWorktreeAsync mirrors sync:** Returns `Task<(bool success, string error)>` matching the existing `CreateWorktree` return type.
- **Worktree branch-already-checked-out test:** Passing `"main"` as both branchName and baseBranch triggers git's "already checked out" error — reliable way to test the failure path.
- **Existing test helper `InitBareGitRepo()`:** Creates a real git repo with one commit on `main` in a random temp subdirectory. Reuse for all git integration tests.
- **Tests are proactive (pre-implementation):** These 5 async tests won't compile until Trinity lands `RunGitAsync`, `GitResult`, and `CreateWorktreeAsync` in `GitService.cs`. They're ready to validate the implementation once it arrives.
- **Phase 1+2 test patterns (0.21.0):** Established 5 test files (59 tests total) for Copilot Host discovery foundations. Used `[Theory]` with `[InlineData]` for classifier tests, `IDisposable` with temp dirs for file-based tests (FirstUserMessageExtractor, SessionNameOverride). FakeProcessTree fixture pattern for IProcessTreeProvider allows in-memory process tree testing without Win32 APIs. SessionNameOverride returns nullable record — access properties directly (`entry.Name`) not via `.Value`. CopilotHostResolver constructor is `(IProcessTreeProvider, int ownPid)` and Resolve takes single arg `(int copilotPid)`.
- **Resolution chain testing (0.21.0 Round 2):** LoadNamedSessions now accepts 5 optional parameters (sessionStateDir, pidRegistryFile?, sessionStateFile?, aliasFile?, overrideFile?). Test pattern: create temp dir with per-test alias/override JSON files, use Guid.NewGuid().ToString() session IDs to avoid collisions, verify chain precedence: alias > ws.summary > override > folder > "(no summary)". Trinity-C implemented the chain in parallel; tests passed on first run.
- **WinForms HWND fixture for cache tests (0.21.0 Round 2):** Integration tests needing a real HWND: create `Form` with `Opacity = 0`, call `.Show()` (required for `IsWindowVisible`), use `.Handle` property. Must dispose in `IDisposable.Dispose()`. `WindowFocusService.IsWindowAlive` checks `IsWindow(hwnd) && IsWindowVisible(hwnd)` — handle creation alone isn't enough.
- **GetWindowProcessId PID-revalidation pattern (0.21.0 Round 2):** Cache Load validates copilot-host entries by comparing `WindowFocusService.GetWindowProcessId(hwnd)` against stored `HostPid`. Mismatch = PID recycled, entry dropped. Test fake-PID rejection: save entry with `HostPid=999999`, actual HWND owned by current process → Load drops it.
- **ActiveStatusTracker host tests (0.21.0 Round 3):** Unit tests for `_copilotHosts` dictionary scaffolding: `SetCopilotHost`/`GetCopilotHost`/`RemoveCopilotHost` + projection into `BuildActiveText` + event firing (`CopilotHostResolved`, `CopilotHostRemoved`). Idempotency tests verify no-op on identical re-set. Dedup tests confirm HWND-based deduplication prevents double-projection. All 10 tests green; total unit test count now 647.
- **Integration test fixture reuse (0.21.0 Round 3):** Reused `FakeProcessTree` fixture pattern from `CopilotHostResolverTests.cs` in integration tests that need controlled host-tree scenarios. Real process spawning for validation (e.g., `powershell.exe -NoProfile -Command Start-Sleep 30`) provides end-to-end confirmation that resolver walks real Win32 process trees correctly.
- **WinForms title-change resilience test (0.21.0 Round 3):** Created Form, saved host entry to cache with `Text = "Copilot CLI - {sid}"`, changed `Text` mid-test, reloaded cache. HWND-based cache entry survives title changes (proof that Host path fixes the fragile title-pattern bug). `WindowFocusService.IsWindowAlive` still returns true after title change.
- **External session workspace.yaml tests (0.21.0 Round 3):** Tests validate that external sessions do NOT write `summary:` line to workspace.yaml (ADR-0001). Instead of calling private `CopilotLogWatcherService.CreateWorkspaceYamlFromPid`, tests create minimal workspace.yaml directly with `id:` and `cwd:` lines, then assert `summary:` and `:Copilot` placeholders are absent. Also test public `CopilotLogWatcherService` methods: `ExtractPidFromFilename` and `TryParseLogContent`.
- **Deferred name resolution tests (0.21.0 Round 3):** Tests prove unresolved → resolved transition: `SessionNameOverrideService.Set` with `ResolvedFromUserMessage=false` → `FirstUserMessageExtractor.Extract` → `BoosterResolvedNameFormatter.Format` (strips code fences, collapses whitespace, truncates to 32+ellipsis) → `SessionNameOverrideService.Set` with `ResolvedFromUserMessage=true`. Truncation produces 33-char strings (32 + "…"), not 32. Whitespace collapse regex `\s+` → single space handles newlines, tabs, runs of spaces.
- **Integration failure classification (0.21.0 Round 4):** Inventory reds as 🟢 Playwright/environment (`PlaywrightException` or missing executable), 🟡 LocalOnly trait tests, or 🔴 real regressions. LocalOnly tests must skip in the default `dotnet run --project tests\\CopilotBooster.IntegrationTests.csproj -c Release` run; no baseline reds are tolerated.
- **Playwright auto-bootstrap (0.21.0 Round 4):** Use an xUnit collection fixture to probe `Playwright.CreateAsync().Chromium.LaunchAsync()`, run `Microsoft.Playwright.Program.Main(["install", "chromium"])` once per process on missing-browser errors, then continue. If install cannot complete, skip cleanly instead of red-barring.
- **External log parser test shape (0.21.0 Round 4):** `CopilotLogWatcherService.TryParseLogContent` expects telemetry JSON with `kind: "session_start"`, `session_id`, and optional `context.cwd`; raw top-level `cwd` is not a valid production log shape.
- **Integration project isolation (0.21.0 Round 4):** `tests/Directory.Build.props` gives `CopilotBooster.IntegrationTests` its own `obj-integration\\` path; otherwise solution restore can overwrite shared `tests/obj/project.assets.json` and drop Playwright references during `dotnet build`.
- **Window-hook integration stability (0.21.0 Round 4):** Real WinEvent/terminal tests can race other desktop tests. Put only the unstable class in a non-parallel xUnit collection to keep the full IT run near 29s instead of disabling all integration parallelism.
- **Playwright self-bootstrap pattern (0.21.0 Round 4):** Local IT runs auto-install chromium via xUnit collection fixture. Probe `Playwright.CreateAsync().Chromium.LaunchAsync()` once per test process; on missing browser call `Microsoft.Playwright.Program.Main(["install", "chromium"])`. First run 36s (with install), cached 29s, vs. 39s pre-bootstrap with 13 reds. Fixture is transparent — CI's existing `playwright install chromium` release step makes the fixture a fast probe.
- **wt.exe multi-pane live IT pattern (0.21.0 Round 5b):** Keep the test `[LocalOnlyFact]` plus `[Trait("Category", "LocalOnly")]`; the default run skips it and release `-notrait "Category=LocalOnly"` filters it out entirely. Preflight checks must skip (not fail) when `wt.exe`, interactive desktop, or `copilot --deny-url` support is missing.
- **--deny-url process discovery (0.21.0 Round 5b):** Use a per-pane PowerShell wrapper that starts `copilot --deny-url=<guid>`, then queries `Win32_Process.CommandLine` for that GUID and writes the real Copilot PID to a marker file. Map PID → session via `~/.copilot/logs/process-*-<pid>.log` and `CopilotLogWatcherService.TryParseLogContent`.
- **Windows Terminal automation gotchas (0.21.0 Round 5b):** Launch WT directly with semicolon-separated commands in `ProcessStartInfo.Arguments`; no PowerShell escaping of `;` is needed because we do not invoke a shell. Use `--title` + `--suppressApplicationTitle`, append deterministic `user.message` events, then re-resolve host entries so pane matching can use Booster-resolved labels.
- **2026-05-08: AI detect E2E grid wiring:** For AI detection grid tests, create a real `DataGridView`, `ActiveStatusTracker`, and `SessionGridVisuals`; set `GetGitHubValue` to the same compact `PR#N` format as MainForm; subscribe to `AiDetectionService.DetectionStateChanged` and call `tracker.IncrementalRefresh(sessions)` plus `visuals.UpdateGridIncremental(snapshot)` so the GitHub cell re-renders after `GitHubTrackingService.AddItem` writes disk state.
- **FakeProcessRunner contract:** Shared fake lives at `tests/Integration/TestTools/FakeProcessRunner.cs`. It implements `IProcessRunner.RunAsync(string fileName, IReadOnlyList<string> args, string cwd, int timeoutSeconds, CancellationToken ct)`, returns a canned `ProcessResult`, and records fileName, args, cwd, timeout for exact invocation assertions.
- **AI detection tests use explicit CWD resolver:** When testing `AiDetectionService` with a temp session-state root, use the constructor overload that accepts `Func<string,string?> getSessionCwd`; the default workspace reader uses `Program.SessionStateDir`.

- **2026-05-08 — Issue #18 strict AI validator matrix:** `tests/Services/AiResponseParserTests.cs` now covers pure JSON rejection, schema field/type/range failures, mixed valid plus invalid all-fail behavior, empty success, ordered success, top-3 confidence truncation, and inclusive confidence bounds against `AiParseResult`.
- **2026-05-08 — Issue #18 failure classification unit tests:** `tests/Services/AiDetectionServiceTests.cs` validates `TryGetState(sid).FailureClass` for `ProcessSpawn`, `Timeout`, user cancel with null failure, `ProcessFailure`, `MalformedJson`, `SchemaViolation`, and `NoCandidates`.
- **2026-05-08 — Issue #18 failure E2E harness:** `tests/Integration/AiDetectFailureIntegrationTests.cs` adds one STA integration test per failure class using real `DataGridView`, `ActiveStatusTracker`, `SessionGridVisuals`, `AiDetectionService`, `GitHubTrackingService`, fake `GitHubApiService`, and `FakeProcessRunner`.
- **2026-05-08 — Shared logging and process fakes:** `FakeProcessRunner` supports `SetResult` and `ThrowOnNextCall(Exception)` for process boundary cases. `tests/Integration/TestTools/CapturingLogger.cs` captures `ILogger` level/message/exception so future AI slices can assert warning vs error without scraping log files.
- **2026-05-08 — Issue #20 IConfirmDialog seam for slice #22:** Morpheus created `IConfirmDialog` interface at `src/Services/IConfirmDialog.cs` for cancel confirmation tests. Production uses `MessageBoxConfirmDialog` with `MessageBox.Show(YesNo)` appending button labels to body. Tank will fake this interface in slice #22 tests.
- **2026-05-08 — Issue #21 ICopilotProbe location and Func<AiDetectionSettings> injection:** Trinity implemented `ICopilotProbe` at `src/Services/ICopilotProbe.cs` with lazy `--version` probe and in-memory cache invalidation. AiDetectionService now accepts `Func<AiDetectionSettings> getSettings` in its constructor instead of holding settings directly. This allows per-detection-run configuration and prevents settings-changed-mid-detection conflicts. The getter is called at detection start time to capture a point-in-time snapshot; if user changes settings while detection runs, next detection picks up the new settings without affecting in-flight runs.
- **2026-05-08 — Issue #22 IMessageBox seam for click-to-dismiss flow:** Morpheus created `IMessageBox` interface at `src/Services/IMessageBox.cs` for click-to-dismiss confirmation tests. Production uses `MessageBoxWrapper` with `MessageBox.Show(OKCancel)`. Sibling to Issue #20 `IConfirmDialog`. Tank fakes this interface in slice #22 integration tests for dismiss dialog scenarios (confirm dismiss or keep detecting).

- **2026-05-10 — Warp R2 Unit Tests (12 test cases):** Wrote WarpPaneFocuserTests.cs with stub-based infrastructure (no mocking framework). 12 edge-case tests: NoMainWindow, AlreadyOnTargetTab, TitleMatchIsCaseInsensitive, MatchOnSecondTab, MatchOnThirdTab, NoMatch_CyclesBackToOriginal, HitsIterationCap, DuplicateTitlesAcrossPanes_FirstMatchWins, EmptyExpectedTitle, NullExpectedTitle, FocusHwndFails, TitleReaderReturnsEmptyDuringProbe. Test infrastructure: StubTitleReader (Queue<string> script), StubKeyboardSender (call counter), StubPaneFocusClock (record). Contract discovery: focusHwnd failure is prerequisite; focuser returns false immediately (no tab probing if window focus fails). Result: 12/12 green (total unit count now 851).

- **2026-05-10 — Warp R2 Live Integration Tests (3 LocalOnly tests):** Wrote WarpPaneFocusLiveTests.cs with restore-on-teardown via IDisposable. 3 tests marked [LocalOnlyStaFact], skip cleanly when Roger's live Warp unavailable: FocusKnownPane_LandsOnExpectedTab (captures pane titles, targets second, resets, verifies success), FocusUnknownPane_RestoresOriginal (fake title, asserts failure + restore), FocusAlreadyOnTarget_NoTabSwitch (current title as target). LiveWarpScenario.Detect() probes for warp.exe + copilot.exe descendant via CIM Win32_Process.ParentProcessId. Tests serialize via [Collection(WindowEventHookCollection.Name)] (existing pattern). Added System.Management 10.0.1 to IntegrationTests.csproj for CIM queries. Verified all 3 live tests pass against Roger's live Hi 1 / Hi 2 with COPILOT_BOOSTER_RUN_LOCALONLY=1. Integration test count now 141 (3 new Warp LocalOnly tests skip in default run). Documented conversion path to deterministic spawn automation in test code (out of scope for R2 per Roger).
- **2026-05-10 — Bug B session-pid liveness test suite:** Created 5 test files for Bug B (stale session-pid mapping fix). Trinity has ALREADY implemented the fix (SessionPidLivenessValidator + 8-arg ActiveStatusTracker constructor + session-aware IsCopilotHostActive). Tests verify: (1) pure DateTime liveness invariant including Roger's 8-hour-stale scenario, (2) T1 watcher gate rejection, (3) session-aware eviction of existing stale hosts via ReprojectActiveCopilotHosts, (4) discrimination between fresh/stale sessions, (5) TryFocusCopilotCli focus path. **Status:** 9 of 10 tests PASS; 1 test (TryFocusCopilotCli_StaleSession_DoesNotFocus) FAILS, revealing that the focus callback is invoked even when the session is stale — Trinity needs to investigate the TryFocusCopilotCli Priority 1 path. Key learning: HandleExternalSessionDiscovered calls SessionPidLivenessValidator.IsLive directly (real-FS overload), NOT the injected _isSessionLiveForCopilotPid callback; the callback is only used in IsCopilotHostActive.

- **2026-05-10 — GitHub URL parser unit tests (14 tests):** GitHubLinkServiceParseUrlTests.cs validates `TryParseIssueOrPrUrl` parser edge cases: bare positive integers, HTTPS URLs, scheme-less URLs, GitHub-only hosts, `/issues/` and `/pull/` paths, exact number boundaries (0 and negative reject), owner/repo case-insensitive matching, extra segments rejection, `/pulls` vs `/pull` strictness. Test infrastructure: fact + inline data tests, nullable outparam assertion pattern. Result: 14/14 green (unit test count now 651 total).

- **2026-05-10 — Memory regression tests for streaming log parser (Trinity's fix):** Created `tests/Services/CopilotLogWatcherStreamingTests.cs` with 4 tests proving Trinity's streaming `TryParseLogContent(TextReader)` overload prevents 4GB bloat. Tests: (1) Parity test — streaming vs array overload produce identical output for typical/resume/no-cwd/empty/dupe fixtures. (2) Allocation budget — 50 MB synthetic log allocates < 25 MB (uses `GC.GetTotalAllocatedBytes(precise: true)` snapshot pattern). (3) Gen-2 GC ceiling — 50 MB log triggers ≤ 1 gen-2 collection (catches LOH thrashing). (4) Real-world fixture — 5 KB sanitized log with /resume parses 2 sessions correctly. Tests 2+3 marked `[Fact(Skip="LocalOnly...")]` + `[Trait("Category","LocalOnly")]` — too expensive for CI (50 MB file generation), Roger runs manually before release. Trinity's streaming overload already shipped; array overload now wraps streaming via `StringReader`. All tests GREEN (LocalOnly skipped in default run). **Key learning:** `GC.GetTotalAllocatedBytes(precise: true)` pattern for memory regression tests: force GC (`GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true); GC.WaitForPendingFinalizers(); GC.Collect();`), snapshot before/after, assert delta < budget. `GC.CollectionCount(2)` for gen-2 promotion ceiling. LocalOnly heavy disk tests use `[Fact(Skip="...")]` not `[LocalOnlyFact]` (attribute lives in IntegrationTests namespace). Synthetic 50 MB log generation: write in 8 KB chunks with `StreamWriter` to avoid bloating test process; scatter ~10 valid session lines among ~500K filler lines. Use `Path.GetTempPath()` for temp files (NOT /tmp forbidden path).

- **2026-05-09 — Issue #15 refinement — CopilotModelsService tests (11 tests):** Comprehensive test matrix for cache-first + stale-fallback service: cache-hit (fresh), cache-miss (API call + write), stale-cache fallback (API failure + return stale), network error (return hardcoded), cancellation (OperationCanceledException rethrown), TTL expiry (force refresh), concurrent fetch (no corruption), null-models (API returns no valid models), empty-models (API returns empty array), network recovery (transient error then success), and fast-path reuse. LocalAppData isolation pattern documented in `.squad/skills/localappdata-test-isolation/SKILL.md` for future use. All 11 tests passing.

## Cross-agent update — Warp focuser shipped

**Win32 INPUT cbSize Layout Tests (2026-05-10):** Added 4 Win32InputLayoutTests pinning `sizeof(INPUT)` to 40 bytes (x64) / 28 bytes (x86) to prevent regression of Win32KeyboardSender's Marshal.SizeOf mismatch. Tests use `[StructLayout(LayoutKind.Sequential)]` validation and dynamic arch detection. Regression guard for Trinity's canonical Win32Input.cs refactor. Latent WT bug in WindowsTerminalPaneGateway.cs will need similar regression tests when migrated.

- **2026-05-09 — Issue #15 refinement — SettingsForm async dropdown tests (5 tests):** Validated dropdown population on form load, selected model persistence round-trip, fetch cancellation when form disposed, empty model list error handling, and dropdown selection workflow. Pattern: construct form in `using`, reflect `_modelFetchCts` and cancel immediately before assertions to avoid race with background fetch. All 5 tests passing.

- **2026-05-09 — Issue #15 refinement — Build/test gate + flake fixes:** (a) Diagnosed CS0579 duplicate-attribute build error: integration project's default `**\*.cs` glob was picking up generated AssemblyInfo from unclean obj/bin in simulator test-tools directories. Cleaned all obj/bin, error resolved. (b) Fixed `AiDetectTreeKillIntegrationTests` flake: by-name process discovery (`SnapshotProcessIds("PING")`) captured unrelated ping.exe processes during 10s test window. Replaced with temp-file handshake protocol: parent writes `$PID` and child `$p.Id` to file; test tracks only those two PIDs. Added `$ErrorActionPreference='Stop'` + `$null` guard to fail fast on Start-Process anomalies. Verified 10/10 deterministic, avg 1s per run. Final: format clean, build clean, 786/786 unit passing, 134/134 integration passing (11 LocalOnly skipped).

- **2026-05-09 — Issue #15 AI detect startup regression test (RED):** Wrote `Startup_ExistingCopilotProcesses_ShowAsActiveAfterBoosterLaunchAsync` in `tests/Integration/WindowsTerminalMultiPaneE2ETests.cs` to reproduce Roger's shipped regression — copilot.exe sessions existing BEFORE booster starts never show ACTIVE. Test launches Windows Terminal + copilot --resume, waits for JSONL file on disk (deterministic polling with 10s total budget), THEN constructs `ActiveStatusTracker` + `LoadNamedSessions` using production startup path. NO manual `HandleExternalSessionDiscovered` call. Asserts `snapshot.ActiveTextBySessionId` contains "Copilot CLI". This assertion MUST FAIL on current HEAD (v0.22.0, commit f189115) because `CopilotLogWatcherService` only watches FileSystemWatcher events, never scans existing files. Pre-existing sessions never populate `_copilotHosts` dictionary. Trinity is implementing the rescan fix in parallel; this test will turn GREEN after the fix ships. LocalOnly test with `[Trait("Category", "LocalOnly")]` plus deterministic 30s copilot-detection budget and 10s JSONL-polling budget (no flaky `Thread.Sleep`). File: `tests/Integration/WindowsTerminalMultiPaneE2ETests.cs:93-230`.

- **2026-05-10 — Warp host shell-skip regression tests (7 tests, all GREEN):** Wrote `tests/Services/CopilotHostResolverShellSkipTests.cs` to cover Option A shell-skip semantics after Niobe's root-cause analysis (pwsh.exe with hidden conhost HWND intercepts Warp focus). Trinity had ALREADY implemented the fix before test writing began (`CopilotHostResolver.Resolve` now caches `shellFallback` and skips shell wrappers via `IsShellWrapper("PowerShell" | "Command Prompt" | "Console")`). Tests prove: (1) pwsh→Warp returns Warp not pwsh, (2) cmd→WindowsTerminal returns WT not cmd, (3) pwsh→WezTerm returns WezTerm, (4) standalone pwsh returns pwsh as fallback, (5) multi-shell depth (pwsh→cmd→Warp) returns Warp, (6) direct terminal (no shell) still works, (7) zero-HWND shell already skipped (regression guard). FakeProcessTree reused from `CopilotHostResolverTests.cs`. All 7 tests GREEN on first run (Trinity's fix complete). Total unit count: 865 (858 existing + 7 new). Key learning: Trinity's implementation matches Niobe's Option A spec exactly (lines 76-114 in `CopilotHostResolver.cs`): cache first shell with HWND, continue walking, return non-shell ancestor if found, else return shellFallback. These are now regression tests ensuring the shell-skip logic doesn't break in future refactors.

## Team Updates from Other Sessions

- **2026-05-08 — Collection grouping + parallel scheduling drift:** Adding tests to `WindowEventHookCollection` to serialize window-hook and IDE-tracking tests can shift parallel xUnit scheduling. This shift may expose latent races in NON-collection tests that were passing by accident under different scheduling. Monitor for this pattern in future slices: if test stability improves after adding collection attributes, check whether other tests are now racing. A fix may require adding those other tests to the same collection, not backing out the stabilization.
### From Trinity (2026-05-08 Issue #17)

- Trinity implemented `AiDetectionService` with full state machine (Idle → Pending → Running → Complete/Failed). Public contract: `StartDetectionAsync(sid)`, `CancelDetection(sid)`, `TryGetState(sid)` overloads, `DetectionStateChanged(sid, oldStatus, newStatus)` event. Process runner abstracted via `IProcessRunner` interface (paired with `FakeProcessRunner` for tests). Prompt builder and response parser are internal service classes. All 667 unit tests pass.

### From Morpheus (2026-05-08 Issue #17)

- Morpheus wired menu nesting (GitHub > AI > Auto Detect) and context menu integration. `OnAiAutoDetect` handler starts detection service and listens for state changes. Grid refresh triggered by `DetectionStateChanged` event. `BuildGitHubAiMenuItem(string sid)` exposed `internal` for Tank's E2E verification via `InternalsVisibleTo`. Menu structure enables future GitHub feature growth without pollution.

- **2026-05-08 — Issue #19 repo resolution and menu gating tests:** Added resolver matrix coverage in `tests/Services/GitServiceTests.cs`, menu-state gating coverage in `tests/Services/AiDetectionServiceTests.cs`, and five-row E2E menu gating in `tests/Integration/AiDetectMenuGatingIntegrationTests.cs`. `CreateGitRepo(string remoteName, string remoteUrl)` helpers live in both unit test files plus the E2E fixture; all create temp repos and clean through fixture disposal. Fork-parent resolution is unit-covered with Trinity's `GH_PATH` seam by pointing to a fake `gh` script that returns `upstream/repo`; no LocalOnly network test was needed. Real WinEvent integration tests that use `WindowEventHookService` now share `WindowEventHookCollection` to preserve the all-green integration bar under parallel runs.
- **2026-05-08 — Issue #20 cancel pipeline tests:** Extended `tests/Services/AiDetectionServiceTests.cs` with `CancelDetection_RunningDetectionCancelsTokenAndReturnsIdleWithoutFailureAsync` and `Dispose_RunningDetectionsCancelsAllTokensAndReturnsSessionsToIdleAsync`. The controlled runner records the exact `CancellationToken`, blocks on a per-call `TaskCompletionSource`, completes with `WasKilled=true`, and verifies `FailureClass` stays null.
- **2026-05-08 — Issue #20 spinner and cancel integration tests:** Added `tests/Integration/AiDetectSpinnerCancelIntegrationTests.cs` for real `DataGridView` plus `SessionGridVisuals` plus `AiDetectionService` plus blocking `FakeProcessRunner` plus fake `IConfirmDialog`. Covered spinner visible on running, spinner cleared after terminal state, timer start/stop via private `_spinnerTimer.Enabled`, confirm Stop cancellation, confirm Keep running, and non-corner fallthrough to `OnGitHubColumnClick`.
- **2026-05-08 — Issue #20 tree-kill integration test:** Added `tests/Integration/AiDetectTreeKillIntegrationTests.cs`. Fixture command is `powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$p = Start-Process ping -ArgumentList '-n','60','127.0.0.1' -PassThru; Start-Sleep -Seconds 60"`; test snapshots new `powershell` and `PING` PIDs, cancels `ProcessRunner`, then asserts all diffed parent and child PIDs exited. Cleanup kills only captured PIDs.
- **2026-05-08 — Issue #20 spinner accessor pattern:** Tests use Morpheus seams `SessionGridVisuals.IsSpinnerVisibleForSession(sid)`, `SessionGridVisuals.GetStatusIconRegion(cellBounds)`, and `SessionGridVisuals.HandleGitHubCellClick(rowIndex, clickPos, cellBounds)`. Timer assertion reads private `_spinnerTimer.Enabled` because no public timer accessor exists.
- **2026-05-08 — Issue #20 collection note:** `AiDetectSpinnerCancelIntegrationTests` is in `WindowEventHookCollection` even though it does not start hooks, because full-run parallelism plus WinForms timer `Application.DoEvents()` left the shared timer enabled intermittently. Serializing with existing UI hook tests kept the full integration run green without broad assembly-level serialization.
- **2026-05-08 — Issue #21 settings tests:** Added `LauncherSettings.AiDetection` JSON default, old-file, direct settings round-trip, and custom launcher round-trip coverage. Added `SettingsForm` AI form-state tests using Morpheus' `LoadAiDetectionFromSettings` and `GetCurrentAiDetectionFormState`; direct control mutation uses private-field reflection only because the exposed test seam is model-level.
- **2026-05-08 — Issue #21 probe test approach:** `CopilotProbeTests` exercise the production cache with an internal probe delegate seam for deterministic call counts, plus a real nonexistent-path probe for binary-not-found false-without-throw. This avoids depending on local Copilot or Git binaries for cache semantics.
- **2026-05-08 — Issue #21 settings integration scope:** Added `AiDetectSettingsIntegrationTests` with real `ExistingSessionsVisuals` grid, `AiDetectionService`, writable settings object, fake `IProcessRunner`, and fake `ICopilotProbe`. Covered disabled menu tooltip, probe-unavailable tooltip, custom timeout/model/path propagation, and in-flight settings snapshot vs next-run pickup. No `WindowEventHookCollection` was needed; full integration run stayed green.
- **2026-05-08 — Issue #22 AI undecided, error, reset tests:** Extended `tests/Services/AiDetectionServiceTests.cs` with low-confidence Undecided, AllAlreadyLinked, partial dedup toast, Error failure transitions, Reset, running reset no-op, and manual add reset coverage. Added `tests/Services/AiDetectionTooltipsTests.cs` for failure and undecided tooltip strings.
- **2026-05-08 — Issue #22 icon dismiss integration tests:** Added `tests/Integration/AiDetectIconsAndDismissIntegrationTests.cs` under `WindowEventHookCollection`. Harness uses real `DataGridView`, real `SessionGridVisuals`, `FakeProcessRunner`, fake `IMessageBox`, and `GetCornerIconForSession` to cover `?`, `!`, click-to-message, Reset clear, manual add clear, and non-corner fallthrough.
- **2026-05-08 — Issue #22 IMessageBox fake:** Test fake lives inside `AiDetectIconsAndDismissIntegrationTests.RecordingMessageBox`; production seam is `IMessageBox` with `MessageBoxAdapter`. Assert `Body` for exact texts, then verify `AiDetectionService.Reset` leaves `GetCornerIconForSession(sid)` null.
- **2026-05-08 — Issue #22 partial-dedup toast pattern:** Unit assertions cover `✅ AI added PR #123 (already linked: Issue #99)`, `✅ AI added PR #123 + Issue #456 (already linked: PR #42 + Issue #99)`, and unchanged all-new toast `✅ AI added PR #1 + Issue #2 to session`.
- **2026-05-08 — Issue #23 real copilot LocalOnly pattern:** `AiDetectRealCopilotIntegrationTests` picks the first `~/.copilot/session-state/*` folder with `workspace.yaml` plus non-empty `events.jsonl`, skips if cwd is missing or not a GitHub repo, copies the fixture under a temp root with a fresh session id, runs production `ProcessRunner` plus real `GitHubApiService`, and cleans both the temp copy and `SessionStateService` tracking folder.
- **2026-05-08 — Issue #23 concurrency E2E pattern:** `AiDetectConcurrencyIntegrationTests` uses one shared per-session blocking `IProcessRunner`, five session rows, and real `AiDetectionService` to assert all sessions reach `Running` with all five process invocations started before any result is released. Release completions one at a time and assert each session reaches `Idle` while later sessions remain `Running`.
- **2026-05-08 — Issue #23 docs and release wrap-up:** README section `AI auto-detect GitHub issue / PR` sits after GitHub issue workspace creation and documents menu path, Settings → AI fields, spinner, `?`, `!`, cancel, and LocalOnly contributor flow. Version bump strategy for this user-facing feature was minor, `0.21.0` to `0.22.0`, updated in `src/CopilotBooster.csproj` and `installer.iss` with a matching top CHANGELOG entry.

- **2026-05-09 — Copilot path removal unit tests:** Updated unit tests after `AiDetectionSettings.CopilotPath` removal. Launcher/settings-form tests now assert only enabled, timeout, confidence, and model fields; `AiDetectionServiceTests` and the settings integration assertion now expect `CopilotLocator.FindCopilotExe()` at the runner boundary; `CopilotProbeTests` covers the parameterless locator-backed constructor.


- **2026-05-09 — CopilotModelsService cache test isolation:** Added service tests that set `LOCALAPPDATA` to a per-test temp directory before constructing `CopilotModelsService`, then restore the original environment value and delete the temp tree in `Dispose`. Because Windows .NET `GetFolderPath(LocalApplicationData)` does not follow a process-level env override reliably, the service now resolves `%LOCALAPPDATA%` from `Environment.GetEnvironmentVariable("LOCALAPPDATA")` first and falls back to `GetFolderPath` for production robustness.
- **2026-05-09 — SettingsForm model combo tests:** `SettingsForm` starts the model fetch in its constructor, so form tests cancel the private `_modelFetchCts` immediately after construction before asserting synchronous combo state. Save-path tests find the local Save button recursively and invoke its protected `OnClick` via reflection because `PerformClick()` does not fire reliably on an unshown form.

- **2026-05-09 — Final validation blocked at unit command:** Final gate ran `dotnet format` successfully with no file changes, but format reported unfixable IDE1006 fixer messages. The required unit command `dotnet run --project tests\CopilotBooster.Tests.csproj -c Release --tl:off` failed immediately with `unknown option: --tl:off` before executing tests, so validation stopped before integration tests/build.

- **2026-05-09 — Final validation blocked at integration build:** After rerunning with corrected `dotnet run` commands, unit tests passed 786/786. Integration test command `dotnet run --project tests\CopilotBooster.IntegrationTests.csproj -c Release` failed during build with duplicate generated assembly attributes from `tests\obj-integration\Release` (`TargetFrameworkAttribute`, assembly metadata, `TargetPlatformAttribute`), so validation stopped before solution build.




- **2026-05-09 — Integration duplicate attributes persisted after clean:** Removed `tests\obj-integration` and `tests\bin-integration`, then reran `dotnet run --project tests\CopilotBooster.IntegrationTests.csproj -c Release`. Build still failed with CS0579 duplicates for generated `TargetFrameworkAttribute`, assembly metadata attributes, and `TargetPlatformAttribute` in `tests\obj-integration\Release`, so this is not just stale integration obj/bin cache.

- **2026-05-09 — Integration retry still blocked after coordinator clean:** Retried `dotnet run --project tests\CopilotBooster.IntegrationTests.csproj -c Release` after reported cleanup of `tests\obj`, `tests\bin`, `tests\obj-integration`, and `tests\bin-integration`. Build still fails before tests run with CS0579 duplicate generated attributes in `tests\obj-integration\Release`, so final build was not run.

- **2026-05-09 — Final validation blocked by integration assertion:** After simulator obj/bin cleanup, integration tests built and ran, but `AiDetectIntegrationTests.Ai_auto_detect_happy_path_adds_pr_to_tracking_data_and_renders_in_cell_and_emits_toast` failed. It expected runner executable `"copilot"` but actual was the resolved WinGet Copilot path under `C:\Users\roger\AppData\Local\Microsoft\WinGet\Packages\...`; integration total was 134 with 1 failed and 11 LocalOnly skipped, so solution build was not run.

## Learnings — 2026-05-09: Copilot Probe Timeout Bug (RED Tests)

### Context
Root-caused bug where CopilotProbe.IsCopilotAvailable() returns false even when copilot.exe is installed and working. The issue: ProbeVersion uses Process.Start with RedirectStandardOutput=true, then WaitForExit(5000). The process prints version text but doesn't exit within 5s — likely because an auto-update subprocess inherits the stdout handle and keeps the pipe open. Probe kills the process and caches false result.

### Test Strategy (TDD RED First)
Wrote three layers of tests that document the bug and expected fix:

1. **Integration Tests (LocalOnly):**  
   - IsCopilotAvailable_WithRealWingetCopilotExe_ReturnsTrue — may pass/fail due to timing, documents expected behavior  
   - CopilotLocator_WithWingetInstall_FindsValidPath — confirms locator works, issue is in probe  
   - ProbeVersion_DirectReproduction_DocumentsTimeout (SKIPPED) — explicit repro for manual verification

2. **Unit Tests (Deterministic):**  
   - IsCopilotAvailable_WhenProbeFunctionReturnsFalse_PreviouslyTrappedRealInstalls — documents OLD bug behavior  
   - IsCopilotAvailable_WhenLocatorReturnsExistingPath_ReturnsTrue (SKIPPED) — awaits Trinity's fix, tests NEW behavior  
   - IsCopilotAvailable_PathChangesFromNonExistentToExistent_InvalidatesAndReturnsTrue — ensures fix doesn't break cache

3. **Menu State Regression:**  
   - EvaluateMenuState_ProbeReturnsTrue_DoesNotReturnCopilotUnavailable — full pipeline test  
   - EvaluateMenuState_ProbeReturnsFalse_ReturnsCopilotUnavailable — legitimate unavailable case

### Key Learnings

1. **Flaky bugs require creative test strategies:** When a bug doesn't reproduce 100% deterministically (due to timing variance), write tests that:
   - Document the EXPECTED behavior (assert true when it SHOULD be true)  
   - Skip the flaky repro test but keep it for manual verification  
   - Use unit-level mocks to guarantee the failure path is exercised

2. **Process stdout redirection pitfalls:** When spawning processes with RedirectStandardOutput=true and calling WaitForExit(timeout), be aware:
   - If the process spawns a child that inherits the stdout handle, the parent may never "exit" from the redirector's perspective  
   - The pipe stays open even after the parent writes its output and terminates  
   - WinGet-installed CLI tools are particularly susceptible (auto-update subprocesses)  
   - **Solution:** Either don't redirect stdout, or use async ReadLineAsync() with independent timeout, or don't execute the binary at all (file-existence check)

3. **TDD with external dependencies:** For integration tests that rely on external binaries:
   - Use [LocalOnlyFact] + [Trait("Category", "LocalOnly")] pattern  
   - Gate with environment variable (COPILOT_BOOSTER_RUN_LOCALONLY=1)  
   - Accept some level of flakiness in exchange for real-world coverage  
   - Supplement with deterministic unit tests using mocks/fakes

4. **Documenting RED tests for teammates:** When writing tests for someone else to fix:
   - Create a decision doc (.squad/decisions/inbox/) with clear contract  
   - Mark SKIPPED tests that await the fix with explanation  
   - Include both "documents OLD behavior" and "tests NEW behavior" tests  
   - Provide explicit repro script (PowerShell) for manual verification

### Test Infrastructure Patterns

- **TempDirectory helper:** Created CreateTempDirectory() + IDisposable pattern in CopilotProbeTests for managing temp files during tests  
- **LocalOnly gate:** Used existing LocalOnlyTestGate + LocalOnlyFactAttribute for integration tests that need real copilot.exe  
- **Deterministic unit tests:** Leveraged existing Func<string, bool> injection point in CopilotProbe constructor to mock probe outcomes

### Reusable Skill Candidate?

The "process-probe-stdout-trap" pattern (don't use RedirectStandardOutput + WaitForExit(timeout) for CLI tools that might spawn background processes) is reusable across projects. If this pattern recurs, extract to .squad/skills/process-probe-stdout-trap/SKILL.md.

---
**Next:** Trinity implements fix → unskip IsCopilotAvailable_WhenLocatorReturnsExistingPath_ReturnsTrue → verify all tests GREEN → verify on Roger's machine (menu no longer greyed out).

## Learnings — 2026-05-09: Real CLI v1.0.44 Log Fixtures (Fixture Refresh)

### Context
The unit tests in `tests/Services/CopilotLogWatcherServiceTests.cs` used synthetic fixtures with `"kind": "session_start"` JSON. **Real copilot CLI v1.0.44 never emits that kind** — the fixtures baked in a stale assumption. Tests passed against a parser that would fail in production.

### Real CLI v1.0.44 Log Shape
Harvested from Roger's `~/.copilot/logs/process-*.log`:

1. **Telemetry blocks use diverse `kind` values:**
   - `"kind": "cli_ready"` — main startup telemetry with session_id, client metadata, feature flags
   - `"kind": "allow_all_enabled"` — yolo mode telemetry
   - `"kind": "session_resume"` — resuming existing session
   - `"kind": "memory_usage"` — periodic stats
   - **NEVER** `"kind": "session_start"` in CLI v1.0.44

2. **INFO line patterns for session discovery:**
   - `[INFO] Workspace initialized: <uuid> (checkpoints: N)`
   - `[INFO] Registering foreground session: <uuid>`
   - These provide regex fallback when telemetry blocks are incomplete

3. **Telemetry block structure (verbatim from CLI v1.0.44):**
   ```json
   {
     "kind": "cli_ready",
     "properties": { "copilot_pid": "74528", "engagement_id": "..." },
     "metrics": { "startup_duration_ms": 979 },
     "session_id": "ba62613b-7f04-46bc-9c1e-778b12616687",
     "features": { ... },
     "created_at": "2026-05-09T10:59:34.127Z",
     "copilot_tracking_id": "...",
     "client": { "cli_version": "1.0.44", ... }
   }
   ```

### Fixture Refresh Strategy
1. **Verbatim pinned fixture:** `RealisticLogContent` constant contains a **real** CLI v1.0.44 log slice with comment:  
   `// Real CLI v1.0.44 telemetry shape — DO NOT modify without re-harvesting from a current copilot log`

2. **Updated all synthetic fixtures:** Replaced `"kind": "session_start"` with `"kind": "cli_ready"` or other real kinds

3. **Added 7 new tests for Trinity's new parser:**
   - `TryParseLogContent_ExtractsSessionId_FromAnyTelemetryKind` — accepts ANY kind value
   - `TryParseLogContent_ExtractsSessionId_FromInfoRegisteringForegroundSessionLine` — regex fallback
   - `TryParseLogContent_ExtractsSessionId_FromInfoWorkspaceInitializedLine` — regex fallback
   - `TryParseLogContent_ReturnsFirstSessionId_WhenMultipleTelemetryBlocksPresent` — first-match behavior
   - `TryParseLogContent_ReturnsNull_WhenNoSessionIdAnywhere` — neither telemetry nor INFO
   - `TryParseLogContent_ToleratesTruncatedFinalJsonBlock_AndStillReturnsEarlierSessionId` — mid-stream tolerance
   - `TryParseLogContent_RejectsMalformedSessionId` — malformed GUID handling

4. **Integration test updated:** `tests/Integration/ExternalSessionDiscoveredIntegrationTests.cs` changed `"kind": "session_start"` to `"kind": "cli_ready"`

### Test Status
12 tests initially RED (expected) because Trinity's parser still required `kind: "session_start"`. Once Trinity shipped the new parser that:
- Accepts ANY telemetry block with `session_id` field (regardless of `kind`)
- Has regex fallback for `[INFO] Registering foreground session:` and `[INFO] Workspace initialized:` lines

...tests went GREEN after fixing 9 malformed placeholder GUIDs.

**UPDATE 2026-05-09 15:32:** Trinity's parser delivered. After fixing 9 malformed placeholder GUIDs to valid 36-char GUID shapes (e.g., `aaaa-bbbb-cccc-dddd` → `aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee`), **all 801 unit tests PASS** (1 skipped). Trinity's parser correctly:
- Validates GUID format (rejects malformed IDs like `not-a-valid-guid-shape`)
- Accepts any telemetry `kind` value
- Returns the **last** valid session_id when multiple blocks present (sequential processing with overwrite)

### Why Pin to Real Logs?
1. **Detect silent format drift:** If copilot CLI changes its log format in a future version, the verbatim fixture will catch it
2. **Prevent false confidence:** Tests that pass against synthetic fixtures but fail against real logs are worse than no tests
3. **Document third-party contract:** The verbatim fixture serves as living documentation of what copilot CLI actually emits

### Reusable Skill
Created `.squad/skills/real-log-pinned-fixtures/SKILL.md` documenting the pattern: always pin at least ONE fixture to a verbatim slice of real third-party-tool output to catch silent format drift.

## Learnings — 2026-05-09: Empty Session Title RED Tests

### Context
Bug reported by Roger: newly-created sessions show EMPTY "Session" column in the grid. Older sessions (May 4) render correctly, but today's sessions (May 9) show blank.

### Root Cause
Trinity diagnosed the bug in SessionService.cs line 357 (original code):
```csharp
displaySummary = string.IsNullOrWhiteSpace(folder) ? "(no summary)" : "";
```

When a session has:
- No `summary:` field in workspace.yaml
- No entry in session-name-overrides.json
- A valid `cwd` with a folder name (e.g., "C:\repo\example")

The fallback chain landed on empty string `""` → blank display in grid.

### Test Strategy (RED First)

Wrote 4 tests in `tests/Services/SessionServiceTests.cs` to cover the fallback scenarios:

1. **LoadNamedSessions_NoSummaryNoOverride_FallbackProducesNonEmptyDisplayName (RED → GREEN)**  
   Core test that documents the bug. Creates a session with `cwd` but no summary/override. Asserts the Summary must be non-empty.  
   - RED (original code): `session.Summary = ''` → test fails  
   - GREEN (Trinity's fix): `session.Summary = 'Session 11111111'` → test passes

2. **LoadNamedSessions_WorkspaceSummary_WinsOverOverride (GREEN → GREEN)**  
   Regression guard: workspace.yaml summary must always win over override entries. Passes today and after fix.

3. **LoadNamedSessions_NoSummary_UsesOverride (GREEN → GREEN)**  
   Confirms the fallback chain uses override when no workspace summary exists. Passes today and after fix.

4. **LoadNamedSessions_PlaceholderUpgrade_UsesResolvedMessage (GREEN → GREEN)**  
   Tests the placeholder→resolved transition: session starts with `("cli placeholder", false)`, then receives first user message, override is upgraded to `(formatted_message, true)`. Exercises the full upgrade pipeline.

### Trinity's Fix (Uncommitted)

Trinity changed line 357-358 to:
```csharp
// Fallback: use first 8 chars of session ID as deterministic display name
displaySummary = id.Length >= 8 ? $"Session {id.Substring(0, 8)}" : $"Session {id}";
```

This ensures sessions without summary/override always show a deterministic, non-empty display name based on the session ID prefix.

### Key Learnings

1. **Session name resolution chain architecture:**  
   - Priority: alias > workspace.yaml summary > override sidecar > **fallback**  
   - Tests now cover all four layers plus the placeholder upgrade flow  
   - The fallback MUST produce a non-empty display name (contract for Trinity's implementation)

2. **RED test verification process:**  
   - Used `git stash` to temporarily revert Trinity's uncommitted fix  
   - Ran test to confirm RED failure with exact empty-string assertion  
   - Restored fix with `git stash pop`, confirmed GREEN  
   - This proves the test correctly captures the bug and validates the fix

3. **Fallback display name contract:**  
   Test asserts `!string.IsNullOrWhiteSpace(session.Summary)` without prescribing the exact format.  
   Trinity chose "Session <first-8-chars>" — test remains green for any non-empty deterministic format.

4. **Test isolation with temp directories:**  
   SessionServiceTests follows the existing pattern from LoadNamedSessionsTests:  
   - Constructor creates temp dir: `Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())`  
   - Dispose cleans up: `try { Directory.Delete(_tempDir, true); } catch { }`  
   - Each test creates its own session subdirectories with minimal workspace.yaml fixtures

5. **Override model structure:**  
   `SessionNameOverride` is a record: `record SessionNameOverride(string Name, bool ResolvedFromUserMessage)`  
   - Used in placeholder upgrade test to simulate the CLI→resolved transition  
   - Tests write/read JSON manually via SessionNameOverrideService.Save/Load

### Test Contract for Trinity

**What the test asserts (minimal contract):**
- When `workspace.yaml` has no `summary:` field AND session-name-overrides.json has no entry for that session ID, the `LoadNamedSessions` return must have `!string.IsNullOrWhiteSpace(session.Summary)`

**What the test does NOT prescribe:**
- Exact wording ("Session <id>" vs "(unnamed)" vs "Untitled" — Trinity chooses)  
- Format details (Trinity chose first-8-chars of ID for brevity)  
- Whether fallback uses session ID, timestamp, folder name, or a constant

**Implementation freedom:** As long as the Summary is non-empty and deterministic, the test passes.

### Reusable Pattern

**Fallback-chain coverage pattern:**  
When testing priority-chain resolution (e.g., alias > summary > override > fallback):
1. Write one test per chain layer (e.g., "workspace summary wins over override")
2. Write one test for the default-fallback case (asserts non-empty but flexible on format)
3. Write regression guards for each transition (e.g., placeholder upgrade)

This pattern applies to any service with multi-source resolution: DNS, config files, environment variables, etc.

---
**Status:** All 4 tests written. Test 1 confirmed RED on original code (empty string), GREEN after Trinity's fix (Session 11111111). Tests ready for commit alongside Trinity's SessionService changes.

## Learnings

### Baseline Tests Breaking After Domain Changes

**Context:** Trinity shipped Bug B's session-liveness gate in ActiveStatusTracker. The new 8-arg constructor added isSessionLiveForCopilotPid validator with a default implementation that queries real filesystem state (events.jsonl + process start time). Three baseline tests in ActiveStatusTrackerHostTests.cs broke because they:
- Used fake PIDs and session IDs
- Called the old 3-arg constructor which now delegates through to the real validator
- Had no fake filesystem state to match their fake test data

**Fix pattern:**
1. Updated CreateTracker helper (line 826) to use the 8-arg constructor with (_, _) => true for session liveness
2. Updated FocusActiveProcess_WindowsTerminalHostWithRuntimeId_FocusesParentBeforePaneSelection test's direct constructor call to add the 8th parameter
3. This restored the original test behavior: permissive validation so the tests exercise their actual logic (title matching, host rebinding, focus sequencing)

**Result:** All 834 unit tests pass. The 3 failing baseline tests now construct with an always-true validator, maintaining test isolation.

**Pattern for future breaks:**
- When domain code adds validators with filesystem/process dependencies, baseline tests need explicit mocks/fakes
- Check if there's a shared test helper (like CreateTracker) that can inject the fake once for many tests
- Reference implementation: IsCopilotHostActivePidReuseTests.cs already used the 7-arg form cleanly

**Team impact:** This unblocks Trinity's parallel fix to Tank's TryFocusCopilotCli_StaleSession_DoesNotFocus test, which also broke from the same validator change.


- **2026-05-10 — Warp R2 tests (stub-based unit + LocalOnly live integration):** Created `tests/Services/WarpPaneFocuserTests.cs` with 12 pure unit tests using stubs (no mocking framework) for `IWindowTitleReader`, `IKeyboardSender`, `IPaneFocusClock`. Covered all edge cases: no main window, already-on-target, case-insensitive match, iteration cap, cycle-back detection, empty/null title handling, focusHwnd failure. Created `tests/Integration/WarpPaneFocusLiveTests.cs` with 3 LocalOnly live tests against Roger's actual Warp sessions. LiveWarpScenario helper detects warp.exe + descendent copilot.exe at runtime, captures original title, always restores on teardown. Tests NEVER kill/close processes or send destructive keys (only Ctrl+Tab). Win32WindowTitleReader, Win32KeyboardSender, SystemPaneFocusClock provide real Win32 interop (EnumWindows, GetWindowThreadProcessId, GetWindowText, keybd_event). WindowEventHookCollection serialization pattern reused from existing terminal tests. LocalOnly tests skip cleanly when Roger's live scenario is absent (no warp.exe + copilot.exe detected).
- **2026-05-10 — LocalOnly restore-on-teardown pattern:** LiveWarpScenario uses IDisposable to guarantee restoration: capture original active tab title in constructor, restore it in Dispose() via repeated Ctrl+Tab cycling up to 30 iterations. Test teardown always calls Dispose() even on assertion failure (try-finally). This pattern is reusable for any terminal-host live test that must leave the user's session state unchanged.
- **2026-05-10 — Restore-on-teardown flake risk:** If the tab cycle loop (up to 30×150ms = 4.5s) doesn't restore the original title, the user's screen is left in a different state. This can happen if: (1) Warp tabs close/change mid-test, (2) title-read timing races wgpu rendering, (3) pane order changes. Mitigations: LocalOnly tests are opt-in (Roger's explicit authorization), iteration cap prevents infinite loops, failures are visible (user sees wrong tab active after test).
- **2026-05-10 — System.Management for parent PID lookup:** Integration tests added `System.Management` package reference (v10.0.1) to query `Win32_Process.ParentProcessId` via CIM. This is the standard pattern for non-admin PID ancestry checks on Windows. Alternative (`NtQueryInformationProcess` via PEB) requires pinvoke and unsafe code — CIM is cleaner for test-only usage.
- **2026-05-10 — Tail-read parity and byte-boundary tests:** `CopilotLogTailReadTests` should synthesize exact Copilot CLI telemetry with timestamp-first header (`2026-... [INFO] [Telemetry] cli.telemetry:`), root-level `session_id`, and `context.cwd`. Boundary tests are strongest when `maxTailBytes` is computed so `length - maxTailBytes` lands inside a telemetry JSON block before a complete target block; assert the boundary session is absent and the target is the only result. Parser-shape gotcha: only `cwd` lives under `context`; nesting `session_id` under `context` is an invalid fixture.

- **2026-05-10 — Warp multi-tab E2E test infrastructure (REVISED - single-instance fix):** Created `WarpMultiTabE2ETests.cs` mirroring the WindowsTerminal E2E pattern. **CRITICAL FINDING:** Warp is a SINGLE-INSTANCE app. `Process.Start("warp.exe")` when Warp is running signals the existing instance (opens new window), then launcher exits immediately. Original implementation leaked 3 windows and couldn't clean up. **FIX:** (1) Pre-flight check: if any warp.exe exists, return null → test skips (cannot spawn safely without disrupting Roger's live session); (2) PID tracking: poll `Process.GetProcessesByName("warp")` after spawn to capture NEW PID only; (3) HWND tracking for WM_CLOSE cleanup; (4) Dispose sends WM_CLOSE first (graceful), waits 2s, then `Kill(entireProcessTree:true)` ONLY on tracked PIDs (never pre-existing); (5) `AttachThreadInput` + `GetForegroundWindow` verification ensures keystrokes target correct window; (6) `CleanupWarpTestWindows.ps1` helper detects orphaned test windows (title pattern `TAB-\d+-[a-f0-9]{8}`) for manual cleanup. Key discoveries: (1) Warp's NewTab binding on Windows is `Ctrl+Shift+T` (not Ctrl+T), confirmed from `bindings.rs` line 269; (2) Tab switching via `Ctrl+PageDown` / `Ctrl+PageUp` (ActivateNextTab/ActivatePreviousTab); (3) Tab title determination: Opted for echo-command approach (`echo "TAB-{n}-{guid8}"`) rather than programmatic rename — Warp reflects running commands in the window title, making it visible to `GetWindowText`; (4) Warp cold-start timing is critical — spawned instance requires 3-5s initial sleep before main window is reliably queryable. Tests skip correctly when Warp is running (Roger's environment). Pitfall: Focus-stealing during keybd_event delivery can mis-route keystrokes if test host loses focus mid-execution — tests are fragile to external window activations. WarpMultiTabE2ETests.cs mirroring the WindowsTerminal E2E pattern. Key discoveries: (1) Warp's NewTab binding on Windows is Ctrl+Shift+T (not Ctrl+T), confirmed from indings.rs line 269; (2) Tab switching via Ctrl+PageDown / Ctrl+PageUp (ActivateNextTab/ActivatePreviousTab); (3) Tab title determination: Opted for echo-command approach (cho "TAB-{n}-{guid8}") rather than programmatic rename — Warp reflects running commands in the window title, making it visible to GetWindowText; (4) Warp cold-start timing is critical — spawned instance requires 3-5s initial sleep before main window is reliably queryable; (5) Process cleanup via Process.Kill(entireProcessTree: true) ensures no warp.exe leaks after test disposal. Tests pass reliably with env var COPILOT_BOOSTER_RUN_LOCALONLY=1, skip correctly without it. Pitfall: Focus-stealing during keybd_event delivery can mis-route keystrokes if test host loses focus mid-execution — tests are fragile to external window activations.
- **2026-05-10 — Auto-link PR/Issue session creation tests (Phase 6):** Wrote 9 tests across 2 new files for the auto-link feature (Morpheus + Trinity implementation).
  - 	ests/Services/GitHubLinkServiceGetItemUrlTests.cs (5 tests): Cover GetItemUrl dispatch for PR type, Issue type, mixed-case "PR" (case-insensitive IsPr), and byte-equality with GetPrUrl/GetIssueUrl. Pure logic — no fakes needed.
  - 	ests/Forms/WorkspaceCreatorResultTests.cs (4 tests): (1) null GitHubLink — fields set, HasValue false; (2) populated WorkspaceGitHubLink — all fields round-trip including Item.Author/HeadBranch; (3) default struct — WorktreePath null, GitHubLink absent; (4) Tier 2 Option B reflection pin — asserts ShowWorkspaceCreator returns exactly Nullable<WorkspaceCreatorResult> via reflection so any future signature break is caught at test time.
  - **Tier 2 decision:** Chose Option B (reflection contract pin) over Option A (MainForm mock) because GitHubTrackingService has no interface, AddItem is called as a static-style class method, and the seeding lambda is deeply embedded in MainForm event wiring. Refactoring for a unit test would require extracting an interface or restructuring MainForm — disproportionate for a lambda guard. Option B is cheap, honest, and catches the most likely regression (signature change). Documented as follow-up opportunity if GitHubTrackingService ever grows an interface seam.
  - **Tier 3:** Skipped — no existing integration test exercises the create-session flow end-to-end; introducing one would require full MainForm fixture scaffolding (out of scope for this phase).
  - All 884 unit tests pass (0 failed, 2 skipped — pre-existing LocalOnly/awaiting-fix skips).

- **2026-05-10 — RequiresInteractiveDesktop integration trait:** Tests that spawn real terminal/IDE windows or depend on global WinEvent hooks (`EVENT_OBJECT_NAMECHANGE`, create/destroy/foreground) are marked `[Trait("Category", "RequiresInteractiveDesktop")]`. Rationale: GitHub-hosted `windows-latest` runners cannot reliably deliver WinEvent hook notifications for externally launched GUI processes (`cmd.exe`, `mspaint.exe`, IdeSimVS, etc.), even though those tests remain valuable locally. CI must filter both skip categories with: `dotnet run --project tests/CopilotBooster.IntegrationTests.csproj -c Release -- -notrait "Category=LocalOnly" -notrait "Category=RequiresInteractiveDesktop"`. Prefer class-level traits when every test in the class has the same interactive desktop dependency; use method-level traits for mixed classes or classes with existing LocalOnly tests that should remain untouched.
- **2026-05-11 — Flaky test: AiDetectSpinnerCancelIntegrationTests.Spinner_StartsWhenDetectionRunsAndStopsWhenDetectionEndsAsync:** Test failed once during v0.22.0 release (Neo's first turn, multi-turn halt), then passed in isolation and on full-suite re-run (Coordinator verified green). Likely race condition or timing assumption. This is a STABILIZATION CANDIDATE for a future cycle — investigate root cause (spinner lifecycle timing, event ordering, or WinForms control state assumptions). Pattern mirrors historical `AiDetectTreeKillIntegrationTests` flake (fixed via determinism upgrade). High-priority for Tank to debug if flake reoccurs.

---

## Team update 2026-05-14 — CWD TDD Recovery Complete

**TDD Loop Directive (Roger Barreto):** All implementation work follows RED-first cycle: Tank writes test, Oracle reviews test, Trinity/Morpheus implements GREEN, Oracle reviews impl, then next item. Tests reviewed BEFORE implementation begins. Implementation reviewed BEFORE next work item. This cadence is now locked for the entire CWD feature.

**WI-1 (Win32ProcessCwd helper, Trinity) — CLOSED:** 6 tests backfilled (3 unit + 3 integration), all RED-sensitive. Oracle reviewed and passed. Implementation uses SafeProcessHandle for proper lifecycle, caches on (pid, processStartTime) to prevent PID recycling bugs. One advisory: self-process CWD reading limitation documented (PEB probe may fail for current process). Production ready.

**WI-4 (Session editor read-only, Morpheus) — CLOSED:** 3 contract tests backfilled, verify return type changed from tuple to string?. Oracle reviewed and passed. Alias field stays editable (user-controlled label). CWD and Session Name read-only with copy-to-clipboard icons. Return type changed to string? (Alias only). Form title "Session Details". Visual behavior verified manually. Production ready.

**Pre-existing failure resolved:** LoadNamedSessionsTests.LoadNamedSessions_QuotedEmptySummary_TreatsAsEmpty now passes (resolved separately). Full suite: 930 unit tests, 0 failures, 2 skips (expected LocalOnly).

**WI-2 and WI-3 unblocked:** Both ready for Tank RED-first cycle under same TDD discipline.

**Standing all-green rule (decisions.md):** Reinforced. No work may claim completion while any test fails, even if pre-existing. Whoever lands code meeting a red suite must fix or escalate. This is binding release policy.
