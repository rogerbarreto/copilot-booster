# Morpheus — History

## Core Context

- **Project:** A C# WinForms (.NET 10) desktop application that enhances GitHub Copilot workflow with session management, IDE integration, and GitHub tracking.
- **Role:** UI Dev
- **Joined:** 2026-03-15T15:50:59.675Z

## Learnings

<!-- Append learnings below -->

### 2026-05-10 Dialog-Return Struct Pattern (WorkspaceCreatorVisuals)

* Replaced the `(string, string?, string?)?` tuple return from `ShowWorkspaceCreator` with `WorkspaceCreatorResult?` struct. Plain `internal struct` (not record) — chosen deliberately; only promote if value-equality ever needed.
* `WorkspaceGitHubLink` holds `Owner`, `Repo`, and a `GitHubTrackedItem` — enough for callers to call `AddItem`, `GetItemUrl`, and seed Edge tabs without any re-fetch.
* Both `Task.Run` validation blocks (PR and Issue) in `WorkspaceCreatorVisuals` dispose `JsonDocument` with `using var doc` inside the lambda — **all JSON extraction must complete before the lambda returns**. New fields (state, draft, author, merged→effectiveState, updatedAt, owner, repo for PR; state, stateReason, author, labels, updatedAt, owner, repo for Issue) were all extracted inside the lambda and returned via extended value tuples.
* `fetchedPrGitHubLink` and `fetchedIssueGitHubLink` are closure-captured nullable structs built on successful validation; set to `null` in their respective `Reset*` functions.
* GitHub URL is no longer computed or stored in the dialog result — callers use `GitHubLinkService.GetItemUrl(link.Owner, link.Repo, link.Item)` at the call site.
* **Helper-dedupe skipped (Phase 5):** The two caller blocks differ in `CreateSessionAsync` source-dir arg and the `dialog.Close()` call present only in `MainForm.cs`. The inner seeding sequence is identical but extracting it alone would add indirection for minimal gain.
* `MainForm.ContextMenu.cs` needed `using Microsoft.Extensions.Logging;` added — it wasn't imported despite `Program.Logger` being used via the shared partial class.

### 2026-05-10 Warp Terminal Pane Focus

* No UI changes in this iteration (R2 pane focus is a service-only feature).
* Trinity wired WarpPaneFocuser into ActiveStatusTracker with _warpPaneFocuser and _sessionDisplayNameProvider seams.
* FocusCopilotHost branches on HostKindLabel == "Warp": reads session display name, calls focuser, logs outcome.
* On mismatch: logs warning, focuses warp.exe window as courtesy fallback.
* Non-Warp hosts unchanged (Windows Terminal, Console, etc.).
* Fallback window focus maintains booster usability even when no Warp pane matches (safety net for title mismatches, session renames).

### 2026-05-10 Smart GitHub URL input pattern — Team awareness

* Neo shipped `GitHubLinkService.TryParseIssueOrPrUrl` parser + smart input wiring in AddPrForm, AddIssueForm, WorkspaceCreatorVisuals.
* Pattern: bare positive integer first, then full HTTPS/scheme-less GitHub URL parsing. Rejects `http://`, non-github hosts, `/pulls`, extra segments, non-positive numbers.
* **WorkspaceCreatorVisuals dual-panel design:** PR and Issue panels have separate validation states. When URL type differs from visible panel (e.g., Issue URL in PR panel), do NOT flip radio buttons (preserves user stability), but DO route creation by URL type so PR URLs fetch PR refs and Issue URLs create issue-style branches.
* Skill documented at `.squad/skills/smart-github-url-input/SKILL.md` for future form enhancements.

### 2026-05-09 Settings AI model dropdown

* Settings AI model selection uses a strict `ComboBoxStyle.DropDownList`, initially seeded with `"(default — let Copilot decide)"` so the form remains usable before async model discovery completes.
* Unknown saved model ids are preserved by appending a display-only `" (custom)"` suffix; saving strips the suffix and maps the default sentinel back to `""`.
* WinForms async fetches should keep a form-owned `CancellationTokenSource`, marshal UI updates with `BeginInvoke`, and cancel/dispose the CTS from `Dispose(bool)`.

### 2026-05-08 Issue #20 GitHub cell spinner and cancel region

* GitHub cell reserves a cell-relative top-right `16x16` status region through `SessionGridVisuals.GetStatusIconRegion(Rectangle cellBounds)`. The region consumes clicks even when idle, so the PR and issue strip does not receive corner clicks.
* `SessionGridVisuals` owns one shared `System.Windows.Forms.Timer` for GitHub status animation. It starts on visible `DetectionStatus.Running`, advances an 8-frame spinner, invalidates only running GitHub cells with `InvalidateCell`, and stops when no visible row is running.
* Tooltip routing is two-region. Corner plus non-idle state shows `"Detecting GitHub link... click to cancel."`; every other GitHub-cell position falls through to the existing PR and issue tooltip.
* `IConfirmDialog` lives in `src/Services/IConfirmDialog.cs`. Production `MessageBoxConfirmDialog` uses `MessageBoxButtons.YesNo` and appends `Yes = Stop` plus `No = Keep running` to the body.
* Tank seams exposed on `SessionGridVisuals`: `HandleGitHubCellClick(int rowIndex, Point clickPos, Rectangle cellBounds)`, static `GetStatusIconRegion(Rectangle cellBounds)`, and `IsSpinnerVisibleForSession(string sid)`.

### 2026-05-08 Issue #19 AI context menu gating

* Wired `ExistingSessionsVisuals.BuildGitHubAiMenuItem` to render the AI leaf through `GetEvaluatedAiMenuItem(sid, cwd)` so the item calls `AiDetectionService.EvaluateMenuState(sid, cwd)` each time the row context menu is rebuilt.
* Added `GetEvaluatedAiMenuItem(string sid, string? cwd)` as Tank's internal test seam. It returns the configured leaf item with `Enabled` and `ToolTipText` already applied.
* Kept the existing `ContextMenuStrip.Opening` build pattern rather than adding `DropDownOpening`, because the GitHub submenu is rebuilt on each right click.
* Enabled `ShowItemToolTips` on the row context menu and AI submenu dropdown. The enabled state maps to an empty tooltip through `AiDetectionTooltips.For(state)`.
* Wrapped menu state evaluation defensively. Unexpected exceptions render the leaf disabled with `AI auto-detect unavailable`.

### 2026-05-08 Issue #17 AI context menu wiring

* `ExistingSessionsVisuals.BuildGridContextMenu()` owns the session row context menu. The GitHub group parent is the local `ToolStripMenuItem menuGitHub`, populated inside `gridContextMenu.Opening`.
* Added nested path `GitHub` > `AI` > `Auto Detect GitHub Issue and PR` via internal `BuildGitHubAiMenuItem(string sid)`. Tank can call this helper and `PerformClick()` the leaf to verify `OnAiAutoDetect` receives the session id.
* `MainForm.ContextMenu.cs` subscribes `OnAiAutoDetect` and starts `AiDetectionService.StartDetectionAsync(sid)`. `MainForm` listens for detection leaving Running and calls `RequestRefresh(sessionId: sid, trackingChanged: true)` on the UI thread.
* No GitHub cell rendering or gating lives in this slice. Keep spinner, status icons, disabled states and tooltips in later slices.

## Team Updates from Other Sessions

### From Trinity (2026-05-08 Issue #17)

- Trinity completed `AiDetectionService` with public contract: `StartDetectionAsync(sid)`, `CancelDetection(sid)`, `TryGetState(sid)` overloads, `DetectionStateChanged` event. Constructor signature fixed for Morpheus to accept `GitHubApiService`, `IProcessRunner`, CWD resolver func, toast sink func, optional polling service, and root directories. Service API locked; no breaking changes expected.

### From Tank (2026-05-08 Issue #17)

- Tank verified menu wiring via E2E grid test. `BuildGitHubAiMenuItem(string sid)` is `internal` and accessible to Tank's integration tests via `InternalsVisibleTo`. Grid GitHub cell updates on `DetectionStateChanged` event and UI refresh. All 99/104 integration tests pass.

### From Cross-Agent Session (2026-05-09 Issue #15 Refinement)

- **Trinity's path removal:** Removed `AiDetectionSettings.CopilotPath` field entirely. `AiDetectionService` now resolves copilot exe dynamically via `CopilotLocator.FindCopilotExe()` at detection start time. SettingsForm path-row UI can now be removed by Morpheus (path is auto-discovered, no longer user-configurable).
- **Trinity's models service:** `CopilotModelsService` with cache-first + stale-fallback design. Auth: `gh auth token` directly into `Authorization: <token>`. API: `GET https://api.githubcopilot.com/models`. Cache: `%LOCALAPPDATA%\CopilotBooster\models-cache.json` with 24h TTL. Fallback: fresh cache → API → stale cache → hardcoded. Cancellation: `OperationCanceledException` rethrown (not converted to fallback). All service tests passing (11/11).
- **Tank's dropdown UI testing:** Form-owned `CancellationTokenSource` for async fetch. Marshal combo rebuilds via `BeginInvoke` on completion. Cancel and dispose CTS from `Form.Dispose(bool)` to prevent orphaned tasks. Test pattern: construct form in `using`, reflect `_modelFetchCts` and cancel immediately before assertions. All 5 tests passing.

- **2026-05-03 — All-green integration test directive (UI impact):** User grilling exposed that accepting environmental baseline failures (13 reds in current IT suite) violated standing release policy. New directive: all tests green at all times. Tests must either self-bootstrap environment or skip explicitly. This affects Phase 5+ UI integration tests that depend on Playwright or local-only environments — fixtures must auto-install or skip-traits must be honored by runner. No more ceremonial baseline-comparison workflows.

### 2026-03-15 — Issue #12 UI Design (Worktree Creation Dialog)

- **WorkspaceCreatorVisuals.cs** is a 1252-line static class that builds the entire worktree creation dialog programmatically in `ShowWorkspaceCreator()`. No Designer file — all layout is manual coordinate-based.
- The form uses `FormBorderStyle.FixedDialog` with dynamic height changes via `RelayoutControls()`. Four radio-button modes (Existing Branch, New Branch, PR, Issue) each rearrange controls and resize the form.
- Dark mode detection uses `Application.IsDarkModeEnabled` throughout all Forms. Standard pattern: ternary for colors, e.g., `Application.IsDarkModeEnabled ? Color.FromArgb(0x11, 0x11, 0x11) : SystemColors.Window`.
- Only PR mode uses `await Task.Run()` for async creation. Issue, New Branch, and Existing Branch modes call `WorkspaceCreationService` synchronously, blocking the UI thread.
- `GitService.RunGit` has its own timeout (default 10s, 60s for fetch). Returns `"Git command timed out."` on timeout. No `CancellationToken` support yet.
- WinForms ProgressBar doesn't support dark theming natively — it uses the system accent color. Acceptable for this project.
- Found **10 user-facing strings** to rename (not 9 as originally estimated) — the four error messages at lines 1156, 1199, 1224, 1242 were undercounted.
- Settings form has `"Workspaces Dir:"` which refers to the directory setting, not the creation feature — should NOT be renamed.

### 2026-03-15 — Phase 4: String Renames (Workspace → Worktree)

- Completed all 10 user-facing string renames across 3 files: `WorkspaceCreatorVisuals.cs` (8 strings), `NewSessionVisuals.cs` (1 string), `ExistingSessionsVisuals.cs` (1 string).
- No class names, method names, variable names, or file names were changed — only string literals visible to users.
- The 4 identical `"Failed to create workspace:"` error messages required surrounding context in the edit to disambiguate each occurrence.
- Main project builds clean after changes. Pre-existing test errors (RunGitAsync not yet implemented, Playwright missing) are unrelated.

### 2026-03-15 — Phase 3: Wire Async Creation Modes + FormClosing Guard

- Wired all 4 creation modes (PR, Issue, New Branch, Existing Branch) to use async service methods (`CreateWorkspaceFromPrAsync`, `CreateWorkspaceAsync`, `CreateWorkspaceFromExistingBranchAsync`).
- PR mode: Removed `Task.Run` wrapper — the service method is now truly async. Added `isCreating` flag.
- Issue, New Branch, Existing Branch modes: Converted from synchronous `CreateWorkspace`/`CreateWorkspaceFromExistingBranch` calls to async equivalents.
- Added `isCreating` bool flag and `FormClosing` guard that prevents the user from closing the form via the X button while creation is in progress.
- Pattern: `isCreating = true` before await, `isCreating = false` after await (before any MessageBox or form.Close). Button disabled + "Creating..." text during operation.
- New Branch and Existing Branch error paths now restore button state (`btnCreate.Enabled = true; btnCreate.Text = "Create"`) — previously they had no restore on error.
- All awaits use `.ConfigureAwait(true)` per WinForms UI thread convention.
- Main project builds clean. Pre-existing Playwright integration test error remains unrelated.

### 2026-05-08 Issue #21 Settings AI tab

* Settings UI keeps the existing left TreeView plus right category panel pattern. Added top-level `AI` category instead of a WinForms `TabControl`.
* AI fields bind through `LauncherSettings.AiDetection`: enabled check box, timeout seconds `NumericUpDown`, confidence threshold `NumericUpDown` with two decimals, Copilot CLI path picker, optional model textbox.
* Copilot path validation runs on focus loss and save. Non-empty missing files show the inline red `File not found` label, red wrapper border, and tooltip. Save is blocked until valid.
* Probe cache invalidation hook lives in `SettingsForm` save path. `MainForm` passes `CopilotProbe` into the dialog. Changed `CopilotPath` calls `InvalidateCache()` after settings save and before close.
* Tank seams: `GetCurrentAiDetectionFormState()` reads controls into a fresh `AiDetectionSettings`; `LoadAiDetectionFromSettings(AiDetectionSettings s)` marshals to UI thread and loads/clamps control values.

### 2026-05-08 Issue #21 ICopilotProbe and Func<AiDetectionSettings> injection

* Trinity implemented `ICopilotProbe` at `src/Services/ICopilotProbe.cs` with lazy `--version` probe and in-memory cache. Cache is invalidated when CopilotPath changes.
* AiDetectionService constructor now accepts `Func<AiDetectionSettings> getSettings` instead of holding settings. This enables per-detection-run configuration without settings-changed-mid-detection races.
* Func is called at detection start to capture point-in-time snapshot. If user changes settings while detection runs, next detection sees new values; in-flight detections unaffected.
* SettingsForm passes `copilotProbe.InvalidateCache()` hook to `OnCopilotPathChanged` event path, ensuring cache is fresh when settings are saved.
### 2026-05-08 Issue #22 AI detection cell undecided and error UI

* Added cached `GitHubIconRenderer.GetQuestionIcon()` and `GetWarningIcon()` for the `16x16` GitHub cell corner status region. `?` uses `PendingYellow`; warning triangle uses `ClosedRed`.
* Added `IMessageBox` plus production `MessageBoxAdapter` in `src/Services/IMessageBox.cs`. `MainForm` owns the production instance and passes it to `SessionGridVisuals` beside `IConfirmDialog`.
* `SessionGridVisuals.GetCornerIconForSession(sid)` returns the current rendered corner bitmap for Tank without pixel comparing cells.
* Corner click routing now keeps Running cancel behavior, shows undecided or error details through `IMessageBox`, then calls `_aiDetection.Reset(sid)` so `DetectionStateChanged` clears the icon.
* Tooltip routing now calls `AiDetectionTooltips.ForUndecided(...)` and `ForFailure(...)`; only Running rows keep the shared animation timer active.