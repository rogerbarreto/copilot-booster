# Morpheus — History

## Core Context

- **Project:** A C# WinForms (.NET 10) desktop application that enhances GitHub Copilot workflow with session management, IDE integration, and GitHub tracking.
- **Role:** UI Dev
- **Joined:** 2026-03-15T15:50:59.675Z

## Learnings

<!-- Append learnings below -->

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
