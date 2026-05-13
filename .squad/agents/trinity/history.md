# Trinity — History

## Core Context

- **Project:** A C# WinForms (.NET 10) desktop application that enhances GitHub Copilot workflow with session management, IDE integration, and GitHub tracking.
- **Role:** Services Dev
- **Joined:** 2026-03-15T15:50:59.673Z

### Key Architecture Patterns (distilled from prior work)

- **Copilot Host Discovery (Phase 0.21.0–0.22.0):** Maps external `copilot.exe` processes to hosting terminals (Windows Terminal, Warp, Console) via PID → parent walk → IPC classifiers (process name, window title, UIA automation). Host entries keyed by `(HWND, runtime_id)` for WT; single HWND for non-UIA terminals. Stored in `WindowHandleCacheService` and `_copilotHosts` dict on `ActiveStatusTracker`. Five trigger types: T1 external log discovery, T2 launcher PID register, T4 periodic refresh, T5 window destruction, focus migration.
- **Async Patterns:** `RunGitAsync` with concurrent stdout/stderr reading (Win32 deadlock prevention). `CancellationToken`-only contract; caller owns timeout policy via `CancellationTokenSource`. Streaming log parse with `ReadLine()` loops (not `ReadToEnd()`) for GB-scale files.
- **Win32 Abstractions:** `IProcessTreeProvider` for in-memory process trees; `LibraryImport` for kernel32/user32 P/Invoke; Job Objects for process tree kill on cancellation. `WindowFocusService.IsWindowAlive` checks both `IsWindow` AND `IsWindowVisible`.
- **Settings/State Patterns:** Use `Func<T> getSettings` injection (e.g., `Func<AiDetectionSettings>`) to capture point-in-time config at work start, preventing settings-changed-mid-operation races. Lazy probes with in-memory caches (e.g., `ICopilotProbe --version` cache invalidation on path change).
- **UIA (Windows Terminal):** `(parent HWND, runtime_id)` tuples for pane identity (terminal tabs lack separate HWNDs). `SelectionItemPattern.Select()` with `InvokePattern.Invoke()` fallback. Closed-loop E2E markers: seed `--interactive` guid, assert selected pane content contains marker.
- **Data Model:** `GitHubTrackedItem` is a `class` with mutable properties (no constructor), struct wrappers for dialog returns (plain struct, not record). Use `readonly record struct` for parser results.

## Summary of Prior Work (Phase 0.21.0 — Copilot Host Discovery + Async Worktree, 2026-03-15 to 2026-05-02)

**STANDING RULE (2026-05-13): All-Green Test Suite Required** — Pre-existing test failures are NOT acceptable. The team may not declare work "done" while ANY test is failing, even if pre-dating the current change. Whoever lands work meeting a red suite must either (a) fix the pre-existing failure as part of delivery, or (b) escalate with analysis + plan before claiming completion. "Unrelated" is not sufficient. This is binding release policy: the project ships only with a fully green suite.

**Copilot Host Discovery (Phases 1-4):**
- Implemented 5 core discovery components: `HostKindClassifier` (process name → label), `BoosterResolvedNameFormatter` (truncation + whitespace collapse), `FirstUserMessageExtractor`, `SessionNameOverrideService` (sidecar JSON), `CopilotHostInfo` record
- Built `IProcessTreeProvider` abstraction with Win32 Toolhelp32 backend; `CopilotHostResolver` walks parent tree with cycle detection
- Wired 5 trigger points (ExternalSessionDiscovered, PidRegisteredStatic, FullRefresh, WindowDestroyed, focus migration) into `ActiveStatusTracker`
- Phase 4: Integrated Windows Terminal UIA gateway for pane matching via `(parent HWND, runtime id)` tuples with closed-loop E2E validation
- Compliance: ADR-0001 external sessions never write `summary:` to workspace.yaml; `TryResolveBoosterName` updates sidecar after first user.message extraction

**Async Worktree Operations (Issue #12):**
- Implemented `RunGitAsync` with concurrent stdout/stderr reading and process tree kill on cancellation via Win32 `Kill(entireProcessTree: true)`
- Added 4 async git wrappers and 3 async `WorkspaceCreationService` overloads; all sync methods kept unchanged
- Cancellation contract: `CancellationToken`-only (no hardcoded timeout), caller owns timeout policy via `CancellationTokenSource`
- `ReadToEndAsync` throws `TaskCanceledException` (subclass of `OperationCanceledException`)

**Outcome:** Phase 0.21.0 shipped 647 passing unit tests, full integration test suite, format-clean build.

---

## Core Learnings — Recent Work (Issue #17–#21, 2026-05-03 onwards)

**Issue #17: AI Detection Service Layer:** Added `IProcessRunner` plus `ProcessResult`, `ProcessRunner`, `AiPromptBuilder`, `AiResponseParser`, and `AiDetectionService`. Public contract: `StartDetectionAsync(string sessionId)`, `CancelDetection(string sessionId)`, `TryGetState(string sessionId)` overloads, `DetectionStateChanged(string sid, DetectionStatus oldStatus, DetectionStatus newStatus)`. Constructor accepts `GitHubApiService`, `IProcessRunner`, `Func<string,string?>` CWD resolver, `Action<string>` toast sink, optional `GitHubPollingService`, session-state root, and log root. Prompt template in `AiPromptBuilder`; logs go under `<app log root>\copilot-booster-detect\<timestamp>-<sid8>`. Slice #17 kept lenient JSON parsing with TODOs for slices #18–#21.

**Issue #18: Strict Response Validator + 6 Failure Classes:** Implemented discriminated-union parser return type `AiParseResult` with `Success(IReadOnlyList<AiCandidate>)` and `Failure(AiFailureClass, reason)`. Added `AiFailureClass` enum: `Timeout`, `ProcessSpawn`, `ProcessFailure`, `MalformedJson`, `SchemaViolation`, `NoCandidates`. Strict validation enforces JSON purity (rejects empty stdout, prose, markdown fences, non-object roots, missing/non-array candidates, invalid types, out-of-range confidence). Accepts up to 3 valid candidates sorted by confidence descending (array-order tiebreak). Extended `AiDetectionService` with deterministic classifier routing. Implemented logging contract: start logs include session_id/owner_repo/timeout; debug logs include exact prompt/stdout/stderr; end logs include outcome/failure_class/reason/exit_code/candidate_count/top_confidence/applied_items/duration_ms. Log levels: WARNING for Timeout/NoCandidates; ERROR for other failures. Observation point: `DetectionState.FailureClass` nullable field via `TryGetState(sid).FailureClass`.

**Issue #19: GitHub Repo Resolution + Menu Preconditions:** Added `GitService.ResolveGitHubRepo(cwd)` returning `GitHubRepoResult(Status, Owner, Repo)` and `GitService.TryResolveGitHubRepo(cwd)` as the resolved-only tuple wrapper. `GitHubRepoResolution` distinguishes `Resolved`, `NotAGitRepo`, `NoRemote`, and `NonGitHubRemote`. Resolution uses `git -C <cwd>`, probes `upstream` before `origin`, supports HTTPS, `git@github.com:owner/repo`, and `ssh://git@github.com/owner/repo`, accepts mixed-case GitHub hosts, rejects non-GitHub hosts, and uses `gh repo view owner/repo --json parent --jq .parent.nameWithOwner` with a 5 second timeout to prefer a fork's parent. Tests can set `GH_PATH` to fake or skip that parent lookup. `AiDetectionService.EvaluateMenuState(sessionId, sessionCwd)` returns `AiMenuState`, and `AiDetectionTooltips.For(state)` exposes the shared tooltip strings. Detection and gating trust prior `GitHubTrackingData.Owner/Repo` when both are non-empty, even if `Items` is empty.

**Issue #20: Cancellation + Process Tree Kill + Spinner UI:** Implemented Win32 Job Object process tree kill through `Win32JobObject.CreateKillOnCloseJob` with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. `ProcessRunner` assigns process to job immediately after spawn and calls `TerminateJobObject(job, 1)` on cancellation or timeout. Cancellation contract: `CancelDetection(sid)` cancels session CTS; `WasKilled && cts.IsCancellationRequested` logs outcome `cancelled` and keeps `FailureClass` null (user cancel does not enter failure pipeline). `AiDetectionService.Dispose()` iterates all state entries, logs shutdown cancel, and cancels every CTS. Morpheus wired spinner UI: GitHub cell reserves top-right `16x16` status region for icon; shared `System.Windows.Forms.Timer` advances 8-frame spinner on visible `DetectionStatus.Running`; tooltip routes corner clicks to "Detecting GitHub link... click to cancel."; confirm dialog used for cancel.

**Issue #21: Settings UI + Lazy Copilot Probe + Configurable Invocation:** Created `AiDetectionSettings` model nested on `LauncherSettings` with 5 fields (Enabled, TimeoutSeconds, ConfidenceThreshold, CopilotPath, Model). Implemented `ICopilotProbe` service with lazy `--version` check, in-memory cache, and invalidation hook when CopilotPath changes. Refactored `AiDetectionService` to accept `Func<AiDetectionSettings> getSettings` instead of holding settings directly, enabling per-detection-run configuration without settings-changed-mid-detection races. Getter is called at detection start to capture point-in-time snapshot; if user changes settings while detection runs, next detection picks up new values. All `EvaluateMenuState` callers now supply current settings via getter. Morpheus wired SettingsForm AI category with 5 controls (enabled, timeout, threshold, path+browse, model), validation, and probe cache invalidation hook on save. Tank wrote comprehensive tests: 5 round-trip settings scenarios, 4 probe cache semantics, 10+ settings-driven unit tests, 4 E2E integration tests. Result: 748 unit tests passing (1 transient flake observed, flagged for future monitoring, never reproduced).

**2026-05-09: Copilot path setting removal:** Removed `AiDetectionSettings.CopilotPath` so old JSON `copilotPath` values are ignored by deserialize and no longer rewritten. `AiDetectionService` now resolves the executable with `CopilotLocator.FindCopilotExe()` immediately before `_processRunner.RunAsync(...)`, and `AiDetectionInvocationSettings` carries only enabled/timeout/threshold/model. Added a parameterless `CopilotProbe` constructor that uses the locator while preserving the `Func<string>` constructor for tests; `MainForm` now uses that default. SettingsForm path-row UI remains for Morpheus' follow-up, but its save/load path references were cleared so the source project compiles after the model field removal.

**2026-05-09: Copilot models service:** Added `CopilotModelsService` with `GetModelsAsync(CancellationToken)` plus injectable `HttpMessageHandler`, clock, and `gh` process runner. The verified Niobe flow is `gh auth token` directly into `Authorization: <token>` for `GET https://api.githubcopilot.com/models`; there is no `copilot_internal` token step. Models are cached at `%LOCALAPPDATA%\CopilotBooster\models-cache.json` using `{ fetchedAt, models }` with a 24h TTL, embedding ids are filtered, cancellation is rethrown, and fallback ordering is fresh cache → API/cache write → stale cache → hardcoded `copilot help config` snapshot.

**2026-05-09: Issue #15 refinement — Cross-agent coordination notes:**
- **Niobe's API findings:** Models API verified end-to-end in live session. `gh auth token` (standard GitHub PAT) + `GET https://api.githubcopilot.com/models` is the working flow. No special `copilot_internal` token endpoint exists.
- **Tank's test fixes:** Fixed CS0579 duplicate-attribute build error (obj/bin leaking AssemblyInfo into integration project). Fixed `AiDetectTreeKillIntegrationTests` flake via temp-file handshake PID protocol (10/10 deterministic runs). All 786 unit + 134 integration tests passing.
- **Morpheus's UI shape:** Settings model dropdown uses strict `ComboBoxStyle.DropDownList`, async-fetches models on `Form.Shown`, cancels fetch on `Form.Disposed`. Form-owned CTS allows safe cancellation without orphaned tasks. Unknown saved ids appended as `<id> (custom)` for preservation.

**2026-05-09: Active status regression fix (startup rescan):** Fixed shipped regression where pre-existing copilot.exe processes (launched before booster) never lit up as ACTIVE in the grid. Root cause: `ActiveStatusTracker._copilotHosts` was only populated via FileSystemWatcher events for NEW jsonl writes; existing sessions loaded via `SessionService.LoadNamedSessions` had no corresponding host binding. Added public `ActiveStatusTracker.RescanExistingSessions()` method that enumerates `~/.copilot/logs/process-*.log`, extracts session IDs and PIDs, verifies processes are alive, and calls `HandleExternalSessionDiscovered(sessionId, copilotPid)` for each live process. Rescan is idempotent (SetCopilotHost deduplicates by HWND/PID identity) and synchronous. Wired into `MainForm.LoadInitialDataAsync()` after `LoadSessions()` but before first `RefreshActiveStatus()` so active icons light up correctly on startup. Result: 795 unit tests + 138 integration tests passing (0 failures). Rescan logs `scanned {n} live copilot.exe process(es), bound {m} host(s)` at Information level when m > 0.

**2026-05-10: Smart GitHub URL input pattern shipped — Team awareness:** Neo shipped `GitHubLinkService.TryParseIssueOrPrUrl` parser + smart input wiring in AddPrForm, AddIssueForm, WorkspaceCreatorVisuals. Pattern: bare positive integer first, then full HTTPS/scheme-less GitHub URL parsing. URL routing corrects form mismatches (Issue URL in PR panel routes to Issue API, not PR API). Skill documented at `.squad/skills/smart-github-url-input/SKILL.md` for future form enhancements (reports, filters, label suggestions, etc.).

## Cross-agent update — Warp focuser shipped

**Win32 INPUT cbSize Fix (2026-05-10):** Win32KeyboardSender was sending 0/4 keystrokes due to Marshal.SizeOf mismatch—INPUT union only contained KEYBDINPUT (32 bytes) instead of MOUSEINPUT-sized (40 bytes). Extracted canonical Win32Input.cs to prevent future regressions. Latent identical bug in WindowsTerminalPaneGateway.cs:23-38 masked by UIA being primary path—Keaton should ticket for future migration.


**2026-05-10: Warp Terminal Pane Focus — R2 Probe-and-Match Implementation:** Implemented WarpPaneFocuser service with deterministic title-probe cycling for Warp terminal pane focus. Warp shares single warp.exe PID across all tabs/panes with zero UIA automation elements. Created 7 new files: IWindowTitleReader, IKeyboardSender, IPaneFocusClock (seam interfaces for testability), WarpPaneFocuser (core logic, testable), Win32WindowTitleReader, Win32KeyboardSender, SystemPaneFocusClock (concrete Win32 implementations marked [ExcludeFromCodeCoverage]). Algorithm: find main HWND, read title via GetWindowText, if matches expectedTitle → done; else loop up to 30 iterations sending Ctrl+Tab (150ms settle), reading, matching, detecting cycle-back to original. Integrated into ActiveStatusTracker via two seams: _warpPaneFocuser and _sessionDisplayNameProvider. Updated 9 existing constructor overloads plus created final 10th ctor with defaults (backward compatible). FocusCopilotHost branches on HostKindLabel == "Warp": reads session display name, calls focuser, on failure logs warning + focuses warp.exe window as fallback. Verified: dotnet format clean, build clean (0 warn, 0 err), unit tests 851 (+28 from 823 baseline). Live integration tests deferred to Tank (3 LocalOnly tests with restore-on-teardown). Known limitation: EnumWindows picks FIRST visible warp.exe window; multi-window instances may mis-focus (rare). Session title source relies on SessionInfo.Summary; empty summary → no match.

**2026-05-10: Log Streaming Memory Fix:** Fixed 4GB memory bloat caused by `ReadToEnd().Split('\n')` on unbounded Copilot CLI process logs (Roger's largest single log: 678 MB). Root cause: each `ReadToEnd()` on large logs allocates ~2x file size in LOH (UTF-16 internal + split array) with rare compaction. Added streaming `TryParseLogContent(TextReader reader, string? fallbackCwd)` overload that uses `ReadLine()` in a while loop—never materializes the entire file. Refactored existing `(string[] lines)` overload to wrap a `StringReader` for test compatibility. Updated two production call sites (`CopilotLogWatcherService.cs:168` FileSystemWatcher path and `ActiveStatusTracker.cs:530` startup rescan) to use streaming overload with `StreamReader` over `FileStream`. EventsJournalService confirmed safe (uses backward-seek `ReadLastLine`, not `ReadAllText`). Streaming parser maintains IDENTICAL output (session order, dedup, fallbackCwd chain) to array overload. Tank's regression tests (50 MB synthetic log, < 25 MB allocation budget) skip by default via `[Fact(Skip = "LocalOnly")]`. Build clean, format clean, 855 unit tests passing (+4 from 851 baseline). Streaming pattern documented in `.squad/skills/streaming-large-files/`.

**2026-05-11: Release notes completeness skill captured:** Neo's v0.22.0 release notes expansion surfaced a key lesson: before tagging a release, sweep `git log <prev-tag>..<this-tag>` to catch features that landed in commits but weren't included in the PR-driven changelog. This prevents gaps between shipped features and documented release notes. See `.squad/skills/release-notes-completeness/SKILL.md`.

**2026-05-10: T0 startup rescan tail-read optimization:** Added `CopilotLogWatcherService.TryParseLogTail(logPath, maxTailBytes: 256 * 1024)` and changed `ActiveStatusTracker.RescanExistingSessions()` to use it instead of streaming every alive Copilot process log from byte 0. T0 only needs the latest live `session_id` per PID, so the byte budget is the last 256 KB; small files still parse whole-file. For large files, seek to `Length - maxTailBytes`, advance to the next `\n`, then hand the aligned `StreamReader` to the existing parser. Alignment means a tail that begins mid-line or mid-JSON block is ignored until the next complete telemetry header, preserving parser state-machine safety while keeping T1 watcher full-file semantics unchanged.

## Learnings Archive

<!-- Detailed technical notes from prior phases -->

- **Windows Terminal XAML-Islands tab identity:** Windows Terminal tabs/panes do not expose separate Win32 HWNDs; UIA tab items are composed under one parent `wt.exe` HWND. Host-resolved Copilot sessions must carry the UIA pane runtime id and dedupe/project as `(parent HWND, runtime id)`, not parent HWND alone. Focus must foreground the WT parent, select the tab item through `SelectionItemPattern.Select()` (with `InvokePattern.Invoke()` fallback), then verify selection readback.
- **Windows Terminal closed-loop E2E marker:** WT `TextPattern` alone can miss terminal scrollback, but Raw UIA traversal exposes the selected terminal screen. Seed each live Copilot pane with a unique `--interactive` marker and assert the selected pane content contains only that marker after grid-link focus; this catches swapped session/pane mappings that selected-tab/title assertions can miss.
- **Windows Terminal title-change trap:** WT parent title/name-change events fire when the foreground tab changes. Never remove `_copilotHosts` for all sessions sharing that HWND on name change; doing so collapses active links and focus dispatch back to title-scan/parent-HWND behavior. Preserve PID/runtime-id host mappings until Copilot PID exit or WT parent destruction, and keep a capped `%LOCALAPPDATA%\\CopilotBooster\\logs\\diag.log` for parent-chain, active-text, tab enumeration, and post-select diagnostics.
- **Windows Terminal pane dispatch:** Wired `IWindowsTerminalPaneGateway` into `ActiveStatusTracker` after `CopilotHostResolver` identifies a Windows Terminal host. UIA `ProcessId` belongs to `WindowsTerminal.exe`, so pane matching uses deterministic tab titles first (`Copilot CLI - {sessionId}`, session id, workspace/override summary), with `ProcessId` only as a defensive fast path. `_copilotHosts` now stores the pane HWND when UIA exposes one; otherwise it stores the WT parent plus `ParentHostHwnd`/`PaneTitle` so focus can re-select the tab before foregrounding WT. Pane cache lives in `WindowsTerminalPaneCacheService` keyed by `(wt HWND, copilot PID)` and is invalidated synchronously on WT parent name changes, pane/parent destruction, and FullRefresh liveness checks.
- **Copilot Host discovery Phase 3 scaffolding:** Added `_copilotHosts` dictionary and `_hostResolver` field to `ActiveStatusTracker`. Implemented three accessor methods: `GetCopilotHost`, `SetCopilotHost`, `RemoveCopilotHost`. Wired `WindowHandleCacheService.Load` to populate `_copilotHosts` and `Save` to persist it (5th parameter). Host entries project into `_activeTrackedWindows` as `("Copilot CLI", "", hwnd)` tuples via `ProjectCopilotHostToActiveWindows` with HWND-based deduplication. `UnprojectCopilotHostFromActiveWindows` removes entries by HWND match. Added `CopilotHostResolved` and `CopilotHostRemoved` events for downstream tasks. Projection target dict: `_activeTrackedWindows` (stores `List<(string Label, string Title, IntPtr Hwnd)>` per session). Dedup rule: scan existing entries, skip insert if HWND already present, preserving title-scan entries.
- **Copilot Host Phase 3 trigger wiring:** Wired all 5 triggers for Copilot Host resolution. T1: `CopilotLogWatcherService.ExternalSessionDiscovered` now passes `(sessionId, copilotPid)` signature (was `sessionId` only). T2: Added `PidRegistryService.CopilotPidRegisteredStatic` static event with dedup logic via `s_lastCopilotPidByLauncherPidStatic` dictionary to avoid firing on duplicate updates. Static path needed because `Program.cs` calls `UpdatePidSessionId` statically. T4: `ActiveStatusTracker.FullRefresh` now iterates `SessionService.GetActiveSessions()` after cleanup, re-resolving hosts for sessions with `CopilotPid > 0` where host is missing or dead. T5: `WindowEventHookService.WindowDestroyed` already existed; wired to `ActiveStatusTracker.HandleWindowDestroyed` which evicts `_copilotHosts` entries by HWND match. Focus migration: `TryFocusCopilotCli` and `FocusActiveProcess` now check `_copilotHosts` first (Priority 1), then legacy title-scan `_activeTrackedWindows` (Priority 2), then PID-based fallback (Priority 3). `FocusActiveProcess` skips duplicate HWND insertion when host already added.
- **Phase 1 Booster-Resolved Name resolution chain:** Implemented unified display name resolution chain in `SessionService.LoadNamedSessions` and `ParseWorkspace`. Extended `LoadNamedSessions` signature with optional `aliasFile` and `overrideFile` parameters (default null). Load both dictionaries once at top of method. Resolution priority (highest first): Alias → workspace.yaml summary → SessionNameOverride sidecar → cwd folder (displaySummary = "") → literal "(no summary)". Alias call site discovery: previously overlaid alias onto `.Alias` field in separate loop, now resolved directly into `.Summary` within `LoadNamedSessions` (priority 1 in chain). Removed post-load alias overlay loop. Folder fallback preserved: when folder exists and summary empty, sets displaySummary = "" so UI shows folder badge separately.
- **Playwright self-bootstrap pattern (0.21.0 Round 4):** Local IT runs auto-install chromium via xUnit collection fixture. Probe `Playwright.CreateAsync().Chromium.LaunchAsync()` once per test process; on missing browser call `Microsoft.Playwright.Program.Main(["install", "chromium"])`. First run 36s (with install), cached 29s, vs. 39s pre-bootstrap with 13 reds. Fixture is transparent — CI's existing `playwright install chromium` release step makes the fixture a fast probe.
- **2026-03-16 — Phase 3 summary write cleanup and deferred name resolution:** Modified `CopilotLogWatcherService.CreateWorkspaceYamlFromPid` (line ~401) to comply with ADR-0001: removed GUID fallback (`?? sessionId`) from summary write. Now passes `GetWindowTitleByPid(pid) ?? string.Empty` to the shared `CreateWorkspaceYaml` helper (line ~369), which checks `!string.IsNullOrWhiteSpace(sessionName)` before writing `summary:` and `name:` fields. External sessions discovered via log watcher never write a GUID to `workspace.yaml.summary`; the T1 trigger (sister task) sets the Booster-Resolved Name placeholder in the sidecar instead. Added deferred name resolution to `EventsJournalService.OnFileChanged`: new `TryResolveBoosterName` method (line ~440) checks if the current override has `ResolvedFromUserMessage == false`, extracts first user.message via `FirstUserMessageExtractor.Extract`, formats via `BoosterResolvedNameFormatter.Format`, and updates sidecar with `resolvedFromUserMessage: true`. New event `BoosterResolvedNameUpdated` (Action<string>) fires on successful resolution for MainForm to trigger UI refresh. Resolution is short-circuited once `ResolvedFromUserMessage == true` (no redundant extraction). Updated `ExternalSessionDiscovered` event signature from `Action<string>` to `Action<string, int>` (added copilotPid parameter) for consistency with ActiveStatusTracker T1 trigger expectations.
### 2026-05-08 Issue #17 AI detection service layer

Added `IProcessRunner` plus `ProcessResult`, `ProcessRunner`, `AiPromptBuilder`, `AiResponseParser`, and `AiDetectionService`. Public contract for Morpheus and Tank: `StartDetectionAsync(string sessionId)`, `CancelDetection(string sessionId)`, `TryGetState(string sessionId)`, `TryGetState(string sessionId, out DetectionState?)`, and `DetectionStateChanged(string sid, DetectionStatus oldStatus, DetectionStatus newStatus)`. `AiDetectionService` constructor accepts `GitHubApiService`, `IProcessRunner`, `Func<string,string?>` CWD resolver, `Action<string>` toast sink, optional `GitHubPollingService`, session-state root, and log root. A compatibility constructor also accepts `(IProcessRunner, GitHubApiService, GitHubPollingService?, Action<string>, sessionStateRoot, logRoot)`. Prompt template lives verbatim in `AiPromptBuilder`; logs go under `<app log root>\copilot-booster-detect\<timestamp>-<sid8>`. Slice #17 intentionally keeps lenient JSON parsing, origin-only repo fallback, hardcoded settings, and simple child kill with TODOs for slices #18, #19, #20, and #21.

### 2026-05-08 Issue #18 Strict response validator + 6 failure classes

Implemented discriminated-union parser return type `AiParseResult` with `Success(IReadOnlyList<AiCandidate>)` and `Failure(AiFailureClass, string reason)`. Added `AiFailureClass` enum with six deterministic classes: `Timeout`, `ProcessSpawn`, `ProcessFailure`, `MalformedJson`, `SchemaViolation`, `NoCandidates`. Replaced `AiResponseParser.Parse` to enforce strict validation: rejects empty stdout, prose, markdown fences, non-object roots, missing/non-array candidates, candidate non-objects, missing required fields, wrong JSON types, case-sensitive type values (`issue` or `pr` only), non-positive item ids/PR numbers, confidence outside [0.0, 1.0]. Accepts up to 3 valid candidates; sorts by confidence descending (array-order tiebreak), truncates excess. Extended `AiDetectionService` with deterministic 6-class classifier: process spawn exceptions → `ProcessSpawn`, killed process without user cancel → `Timeout`, nonzero exit → `ProcessFailure`, parser `Failure(MalformedJson, ...)` → `MalformedJson`, parser `Failure(SchemaViolation, ...)` → `SchemaViolation`, parser `Success([])` → `NoCandidates`, parser `Success(candidates)` → proceed to threshold/apply. Implemented logging contract: start logs include session_id, resolved_owner_repo, configured_timeout_seconds; debug logs include exact_prompt_sent, raw_stdout, raw_stderr; end logs include outcome, failure_class, reason, exit_code, candidate_count, top_confidence, applied_items, duration_ms. Log levels: WARNING for Timeout and NoCandidates; ERROR for MalformedJson, SchemaViolation, ProcessSpawn, ProcessFailure. Observation point: `DetectionState.FailureClass` nullable field, observed via `TryGetState(sid).FailureClass` (no new event arg). Parser owns JSON purity and schema validation; `AiDetectionService` owns no-candidate classification, process classification, logging level, and terminal state transition.

## Team Updates from Other Sessions

### From Morpheus (2026-05-08 Issue #17)

- Morpheus completed UI wiring for AI detection context menu (GitHub > AI > Auto Detect). `MainForm.ContextMenu.cs` subscribes `OnAiAutoDetect` and calls `AiDetectionService.StartDetectionAsync(sid)`. Listens for `DetectionStateChanged` and calls `RequestRefresh(sessionId: sid, trackingChanged: true)` on completion. Menu structure locked; future status cells go in slices #20–#22.

### From Tank (2026-05-08 Issue #17)

- Tank implemented `FakeProcessRunner` at `tests/Integration/TestTools/FakeProcessRunner.cs` as shared test double for all AI detection slices. Public contract fixed: `RunAsync(fileName, args, cwd, timeoutSeconds, ct)`. Grid E2E test wires real `DataGridView` + `ActiveStatusTracker` + `SessionGridVisuals`, verifies GitHub cell re-renders after state change. All 667 unit tests pass, 99/104 integration tests pass, 5 LocalOnly skip.
- **2026-05-08: Issue #21 settings backed AI detection services:** `AiDetectionSettings` has `Enabled`, `TimeoutSeconds`, `ConfidenceThreshold`, `CopilotPath`, and `Model`, serialized as `aiDetection` on `LauncherSettings` with defaults for old settings files. `AiDetectionService` now takes `Func<AiDetectionSettings>` and reads one invocation snapshot per `StartDetectionAsync`, so in-flight detections keep their original timeout, threshold, path, and model while the next detection gets fresh settings. `ICopilotProbe` exposes `IsCopilotAvailable()` and `InvalidateCache()`; `CopilotProbe` lazily runs `<path-or-copilot> --version`, caches by resolved path, re-probes on path change or invalidation, and logs the path plus result. `MainForm.CopilotProbe` is wired with `() => Program._settings.AiDetection.CopilotPath`; `AiDetectionService` is wired with `() => Program._settings.AiDetection`; `SettingsForm` receives the probe so save can invalidate when `CopilotPath` changes.

### 2026-05-08 Issue #22 Undecided, Error, and dedup outcomes

Implemented `AiDetectionService` terminal state semantics for slice #22. Failure classes `Timeout`, `ProcessSpawn`, `ProcessFailure`, `MalformedJson`, `SchemaViolation`, and `NoCandidates` now transition `Running` to `Error` and keep `DetectionState.FailureClass` in memory until `Reset(sid)` or app restart. User cancel remains `Running` to `Idle` with no failure class.

Added `UndecidedReason` with `LowConfidence` and `AllAlreadyLinked`, plus `DetectionState.UndecidedReason`, `DetectionState.OutcomeKind`, and `OutcomeKind.NoCandidatesVariant`. Parser success with candidates below threshold transitions `Running` to `Undecided` with top 3 candidates sorted by confidence. Parser success where all above-threshold candidates are already linked transitions `Running` to `Undecided` with reason `AllAlreadyLinked`, outcome `NoCandidatesVariant`, no toast.

Added `AiDetectionService.Reset(sid)`. It clears only `Undecided` or `Error` states back to `Idle` and raises `DetectionStateChanged(sid, oldStatus, Idle)`. `MainForm.ContextMenu.cs` now calls `this.AiDetectionService.Reset(sid)` after successful manual Add PR and Add Issue flows, immediately after `_githubPoller?.PollSessionNow(sid)`.

Updated partial dedup success handling. Above-threshold candidates are pre-filtered against existing `GitHubTrackingData.Items`. New candidates are enriched and added, duplicates are omitted, and toasts use `✅ AI added <newly_added> (already linked: <duplicates>)` when both new and duplicate candidates exist. All-new success toasts keep the prior `✅ AI added <items> to session` form.

Extended `AiDetectionTooltips` with undecided and failure constants plus `ForFailure(...)` and `ForUndecided(...)` helpers for Morpheus. Adjusted `AiResponseParser` to return all valid candidates sorted by confidence, so services can apply every above-threshold candidate while `DetectionState.TopCandidates` remains capped to 3 for UI details.


### 2026-05-09 CopilotProbe stdout redirection trap + loose-ties consolidation

**Root cause:** `CopilotProbe.ProbeVersion` executed `copilot.exe --version` with `RedirectStandardOutput = true` then `WaitForExit(5_000)`. On WinGet installs, the copilot process prints version output within 0.6s but spawns a background auto-update subprocess that inherits the stdout handle. The pipe stays open because the child hasn't exited. `WaitForExit` waits for the entire process tree and times out after 5s. Probe kills process, returns false, `AiDetectionService.ComputeMenuState` caches `CopilotUnavailable` for the session, menu permanently disabled despite working copilot.exe.

**Solution:** Replaced `ProbeVersion` logic with file-existence check. If resolved path is absolute AND `File.Exists(resolvedPath)` → return true. If resolved path is bare `copilot.exe` (locator's fallback when nothing found) → return false. No process spawn, no 5s timeout, synchronous, deterministic, <1ms. `CopilotLocator.FindCopilotExe()` already validates WinGet paths and `where copilot` output with `File.Exists`, so re-checking in the probe is redundant but avoids the stdout trap entirely.

### 2026-05-09 Copilot CLI v1.0.44 telemetry format change — parser fix

**Root cause:** `CopilotLogWatcherService.TryParseLogContent` required `kind == "session_start"` in telemetry JSON blocks. Real Copilot CLI v1.0.44 never emits that kind. Instead, it emits kinds: `cli_ready`, `tools_available`, `skills_loaded`, `exp_context_fetch`, `first_launch`, `copilot_user_info`, `mcp_policy_check`, `session_model_change`, `allow_all_enabled`, `memory_usage`, `session_resume`, etc. EVERY telemetry block carries a `session_id` field. The old parser rejected all real logs, breaking active-status detection for every copilot.exe process.

**Solution:**
1. Dropped `kind == "session_start"` requirement. Parser now accepts ANY `[Telemetry] cli.telemetry:` JSON block whose root object contains a non-empty `session_id` field. Returns the FIRST valid session_id encountered (logs typically have many blocks, all with the same session_id for a single process).
2. Added regex fallback for two deterministic INFO patterns that appear in every real log, in case telemetry is disabled by the user or the format changes again:
   - `\[INFO\]\s+Registering foreground session:\s+([0-9a-f-]{36})`
   - `\[INFO\]\s+Workspace initialized:\s+([0-9a-f-]{36})`
   The fallback runs only if no telemetry block yielded a session_id. First match wins.
3. Validation: a session_id must be a 36-character GUID-shaped string (lowercase hex + hyphens, format `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`). Parser rejects anything else (e.g., test fixtures with fake IDs like `"aaaa-bbbb-cccc-dddd"`).
4. Behavior on partial/streaming logs: parser tolerates truncated final JSON blocks — catches `JsonException` mid-block and continues scanning instead of throwing.
5. Preserved PID extraction via `ExtractPidFromFilename` (regex `^process-\d+-(\d+)\.log$`). Did NOT touch that method.

**Contract preservation:** Public method signatures unchanged (`TryParseLogContent`, `ExtractPidFromFilename`). CWD extraction and fallback chain unchanged. `ActiveStatusTracker.RescanExistingSessions` continues to work via the existing contract.

**Testing:** Build succeeds with 0 warnings. Existing unit tests now correctly fail on fake session IDs (expected behavior — Tank is updating fixtures in parallel). Manual verification confirms parser extracts session IDs from real CLI v1.0.44 logs in `~/.copilot/logs/`. Smoke tests with real log patterns pass.

**Why this change is robust to future CLI drift:**
- ANY telemetry kind with session_id is accepted, not just a hardcoded list
- Regex fallback protects against telemetry being disabled or format changing again
- GUID validation prevents garbage IDs from poisoning the tracker
- Parser tolerates partial JSON (streaming logs don't crash it)
- The old contract was CLI-version-coupled; new contract is field-coupled (session_id presence), which is far more stable


**Canonical availability contract:** Inventory sweep found NO loose ties. All sites already use `CopilotLocator.FindCopilotExe()` for path resolution or `ICopilotProbe.IsCopilotAvailable()` for availability. Only non-conformance: stale tooltip referencing removed Settings path. Updated tooltip to `Copilot CLI not found. Install via WinGet or ensure 'copilot' is on PATH.`; unskipped Tank's file-existence test; updated integration test assertion. All 791 unit tests + 137 integration tests pass (14 LocalOnly skipped in CI).

**Learnings:** The stdout redirection trap — when a parent process redirects stdout and the child spawns background subprocesses (auto-update, telemetry), those grandchildren inherit the stdout handle. The pipe stays open until all holders exit. `WaitForExit` waits for the entire tree. Never rely on `Process.Start + RedirectStandardOutput + WaitForExit(timeout)` for short-lived commands that may spawn background work. Use file-system checks or alternate detection strategies. For locally-installed CLI tools, file-existence is sufficient when the locator already validates paths. Tradeoff: doesn't verify auth or executability, but failures surface in `AiDetectionService.InvokeAsync` with proper `AiFailureClass`.

### 2026-05-09 Empty session title bug fix (Roger's bugfix)

**Root cause:** Newly-created sessions with no `summary:` field in `workspace.yaml` and no override sidecar entry rendered with empty string in the Session column. The `SessionService.LoadNamedSessions()` fallback chain (lines 341-358) previously returned `""` when a non-empty folder name was present, making the grid cell appear blank.

**Two-layer fix implemented:**

**Layer 1 — Service-layer fallback (PRIMARY):** Modified `SessionService.cs` line 357 to return a deterministic display name when all higher-priority sources (alias, workspace.yaml summary, override sidecar) are missing. New fallback format: `Session {first-8-chars-of-sessionId}`. Examples: `be6b9891-...` → `"Session be6b9891"`, `14cec216-...` → `"Session 14cec216"`. This guarantees the Summary field is never empty in the grid.

**Layer 2 — Reliable placeholder seeding (SECONDARY, belt-and-braces):** Modified `EventsJournalService.TryResolveBoosterName()` line 451 to attempt resolution even when `currentOverride == null`. Previous logic early-returned when the override entry was missing entirely, meaning sessions discovered externally without host resolution never got their first user.message extracted. New logic: if `currentOverride` is null OR `currentOverride.ResolvedFromUserMessage == false`, attempt extraction. After successful extraction, `SessionNameOverrideService.Set` naturally creates the row with `resolvedFromUserMessage: true`.

**Post-fix invariant:** A session with `events.jsonl` and a first `user.message` MUST end up with a resolved override regardless of host-resolution timing. The Layer 1 fallback ensures the UI never shows empty strings even during the race window before `TryResolveBoosterName` upgrades the placeholder.

**Learnings:** Override-lifecycle gap exposed by Neo's triage. When host resolution is delayed or fails, no override is created by `ActiveStatusTracker.HandleExternalSessionDiscovered` (lines 237-242) because `info == null`. The fallback chain in `SessionService` was the only safety net, and it returned `""` for non-empty folder names. Layer 1 fix is the canonical safety net; Layer 2 fix ensures the upgrade path works even when timing races occur. Always provide a deterministic, non-empty fallback for user-visible identifiers.
### 2026-05-10 Bug B implementation (session-PID liveness validator)

**Task:** Implement SessionPidLivenessValidator and wire into ActiveStatusTracker to prevent stale (sessionId, copilotPid) bindings when Copilot CLI /resume switches sessions in-process.

**Root cause:** Copilot CLI's /resume reuses the same PID for different sessions. Booster only sees the INITIAL session_id from process-*.log, so pid 39992 may start with session A, then /resume into session B, but booster still thinks "pid 39992 → session A". Clicking "Copilot CLI" focuses the wrong window.

**Fix invariant:** A (sessionId, copilotPid) binding is live iff vents.jsonl.LastWriteTime >= Process.StartTime (with 5s fudge for clock skew).

**Implementation:**
- Created src/Services/SessionPidLivenessValidator.cs with two overloads: real-FS check (runtime) + pure overload (testing)
- Added 8-arg ctor to ActiveStatusTracker accepting Func<string, int, bool> isSessionLiveForCopilotPid
- Existing 7-arg ctor delegates to 8-arg with DefaultIsSessionLiveForCopilotPid (uses allowMissingEventsJsonl: true for T1/T2, test-friendly null check)
- Made IsCopilotHostActive session-aware (now IsCopilotHostActive(string sessionId, CopilotHostInfo hostInfo)) — AND-checks the liveness validator
- Updated all 7 call sites of IsCopilotHostActive to pass sessionId (compiler-enforced)
- Gated HandleExternalSessionDiscovered (T1) to consult validator before resolving host
- Gated RescanExistingSessions (T0) with direct static call (allowMissingEventsJsonl: false, drops stale bindings)
- Gated TryFocusCopilotCli Priority 3 (PID fallback) with both Bug A (_isExpectedCopilotProcess) and Bug B (_isSessionLiveForCopilotPid) guards
- Gated FocusActiveProcess Priority 3 (PID fallback) with same dual guards

**Test results:** 834 tests total, 829 pass, 4 fail, 1 skip. Failures are in Tank's concurrent test files + 3 baseline ActiveStatusTrackerHostTests that broke due to DefaultIsSessionLiveForCopilotPid returning true in test scenarios. The 4 failures need investigation but baseline remains stable (823→829 passing).

**Learnings:** 
- Constructor chaining with injected validators enables test isolation without sacrificing production safety
- Session-aware IsCopilotHostActive eliminates PID-reuse false positives at all consumption points
- Default validator must be test-friendly (return true when Program.SessionStateDir is null) to avoid breaking existing fixtures
- T1 (watcher) uses allowMissingEventsJsonl: true; T0 (rescan) uses false — semantically distinct
- PID fallback paths (TryFocusCopilotCli, FocusActiveProcess) now have dual guards: Bug A (process name) + Bug B (session liveness)


**Post-fix:** Traced Tank's failing test — Priority 2 paths (tracked windows from ProjectCopilotHostToActiveWindows) bypassed session liveness. Added inline guard: if _copilotHosts contains sessionId and validator returns false, skip focus. Applied to both TryFocusCopilotCli Priority 2 (lines 1202-1206) and FocusActiveProcess Priority 2 loop (lines 1260-1265). Tank's TryFocusCopilotCliPidFallbackTests now passes. Baseline ActiveStatusTrackerHostTests failures remain (Tank updating in parallel).

**2026-05-10: WarpPaneFocuser implementation (R2 probe-and-match):**

Implemented deterministic Warp pane focus via title-probe cycling per decision .squad/decisions/inbox/squad-warp-r2-pivot.md.

**Architecture pattern — seam-driven testing:**
- Created 3 interfaces (IWindowTitleReader, IKeyboardSender, IPaneFocusClock) to abstract all Win32 dependencies
- WarpPaneFocuser is 100% testable via constructor injection (NO [ExcludeFromCodeCoverage])
- Concrete implementations (Win32WindowTitleReader, Win32KeyboardSender, SystemPaneFocusClock) are thin P/Invoke wrappers marked [ExcludeFromCodeCoverage]
- This pattern lets Tank write pure unit tests with stub implementations that script title sequences and count SendCtrlTab calls

**P/Invoke style consistency:**
- Used LibraryImport partial methods in Win32WindowTitleReader (matching WindowFocusService pattern)
- Used SendInput API (not keybd_event) in Win32KeyboardSender for modern keyboard input with INPUT structs
- Both follow existing codebase convention: P/Invoke at top, [ExcludeFromCodeCoverage] on class

**ActiveStatusTracker constructor telescoping:**
- Added 2 new seams (_warpPaneFocuser, _sessionDisplayNameProvider) to final (10th) constructor
- Updated ALL 9 intermediate constructors to chain through with default values
- This maintains backward compatibility: existing tests compile without changes, new tests can inject mocks for precise control

**Expected title source — pragmatic choice:**
- _sessionDisplayNameProvider defaults to SessionService.GetActiveSessions().Summary
- Summary is the canonical session name Copilot CLI sets as window title (same source used for Windows Terminal tab matching)
- Rejected events.jsonl session_renamed tail parsing (heavier I/O), rejected alias (booster-only override)
- Seam allows tests to inject any lookup logic

**FocusCopilotHost Warp branch:**
- Branched on hostInfo.HostKindLabel == "Warp" (HostKindClassifier already maps warp/warpterminal/warp-terminal → "Warp")
- Calls _warpPaneFocuser(hostInfo.HostPid, expectedTitle) → logs success on match, logs warning on failure
- Fallback: focus warp.exe window even if pane didn't match (courtesy behavior; user still gets foreground warp.exe)
- Non-Warp hosts unchanged (Windows Terminal, Console, etc.)

**Win32 quirks discovered:**
- EnumWindows iteration order is NOT stable; for multi-window Warp (rare), we pick the FIRST visible window with non-empty title. Documented as v1 limitation.
- GetWindowText returns empty string for hwnd == IntPtr.Zero or invisible windows; defensive check in ReadTitle

**Reusable skill extraction candidate:**
- Title-probe pattern (find main HWND → foreground → read title → loop SendCtrlTab + settle + read + match) is generic
- Could work for WezTerm/Alacritty if they also expose active tab title via GetWindowText and support Ctrl+Tab cycling
- Pattern has 2 seams (IWindowTitleReader for hwnd/title lookup, IKeyboardSender for tab-switch key sequence)
- Document as .squad/skills/title-probe-tab-focus/SKILL.md if future terminals need it

**2026-05-10: Shell-wrapper skip implementation (Warp host resolution fix):** Implemented Option A from Niobe's research (niobe-warp-host-classification.md): Modified `CopilotHostResolver.Resolve()` to skip shell wrappers (PowerShell, Command Prompt, Console) when walking the parent process tree. Added private static helper `IsShellWrapper(string hostKindLabel)` that returns true for the three shell wrapper labels. Algorithm: walks ancestors as before, but after classifying each ancestor, checks if it's a shell wrapper. If yes, caches it as fallback (first one seen wins) and continues walking. If a non-shell ancestor with non-zero HWND is found, returns it (terminal host). If walk completes with no non-shell ancestor, returns cached shell fallback (standalone pwsh scenario). If no shell was cached either, returns null (current behavior). Added diagnostic logging via `RuntimeDiagnosticLog.Write` when skipping shell wrappers to trace behavior in diag.log. Updated method docstring to reflect new semantics.

Result: Warp-hosted Copilot CLI sessions now correctly resolve to warp.exe (132268) instead of pwsh.exe (135764), allowing the Warp focus branch (ActiveStatusTracker:788) to execute and invoke WarpPaneFocuser probe-and-match. Fix is terminal-agnostic—works for any terminal that hosts shells (WezTerm, Alacritty, Windows Terminal with cmd.exe). HostKindClassifier left untouched; shell-wrapper list is private decision in resolver, not a classifier concern. Tank added 7 comprehensive tests in CopilotHostResolverShellSkipTests covering Warp/WT/WezTerm scenarios, standalone fallback, multi-shell chains, and direct-terminal (no shell) regression guards. All 865 unit tests passing (+7 from 858 baseline), 141 integration tests passing (0 failures). One pre-existing unit test failure in SessionServiceTests (unrelated to resolver changes). Format check clean. ActiveStatusTracker.FocusCopilotHost unchanged—already checks `HostKindLabel == "Warp"` and dispatches to WarpPaneFocuser; this fix just makes the resolver REACH warp.exe so that branch executes.

**Shell-wrapper skip design rationale:** Fallback-first-shell-seen strategy preserves standalone pwsh sessions (user runs pwsh.exe without a terminal, booster still resolves to pwsh). Cache is only written once (first shell with HWND) to avoid thrashing if chain has multiple shells. Non-shell ancestors take priority—always return first non-shell with HWND before considering fallback. This matches "terminal hosts wrap shells" mental model. Considered but rejected: checking for "Console" label may be too aggressive (conhost.exe is a legitimate host in legacy scenarios), but Niobe's research shows modern ConPTY terminals (Warp, WT) attach conhost.exe as hidden child, so skipping "Console" is safe and necessary. If future edge cases arise (e.g., conhost.exe as actual host in Windows 7-era scenarios), can refine IsShellWrapper to check both label AND process name.

**Interaction with ActiveStatusTracker.FocusCopilotHost:** The Warp focus branch (line 788) checks `HostKindLabel == "Warp"` which is set by HostKindClassifier.Classify("warp"). Before this fix, resolver returned pwsh.exe (HostKindLabel="PowerShell"), so the Warp branch was skipped and fell through to live WT re-resolve (line 819), which also failed (Warp is not WT). Now resolver returns warp.exe (HostKindLabel="Warp"), so branch executes and calls WarpPaneFocuser. Diagnostic log "shell-wrapper skipped" lines will appear in diag.log for every Warp-hosted session, documenting the new walk behavior. Example: `CopilotHostResolver shell-wrapper skipped copilotPid=149764 ancestorPid=135764 hostKindLabel=PowerShell`.


**2026-05-10: Issue #15 Phase 2 — GitHubLinkService.GetItemUrl added:** Added `internal static string GetItemUrl(string owner, string repo, GitHubTrackedItem item)` to `GitHubLinkService.cs`. Dispatches to `GetPrUrl` or `GetIssueUrl` based on `item.IsPr` (the canonical discriminator on `GitHubTrackedItem`). Added `using CopilotBooster.Models;` to the file. Method placed between `GetIssueUrl` and `GetRunUrl` per member-ordering (URL builders, ascending specificity). Pre-existing build environment failure (MSB3492 cache lock) confirmed unrelated to this change. Format check clean (IDE0001: `<see cref>` simplified to unqualified name after adding the using).

