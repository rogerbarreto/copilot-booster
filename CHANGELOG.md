# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.14.0] - 2026-02-26

### Added

- **Configurable session tabs** — replace hardcoded Active/Archived with user-defined tabs (up to 10). Manage tabs in Settings with Add, Rename, Remove, and reorder (Up/Down) buttons. Sessions can be moved between tabs via the right-click "Move to" submenu.
- **Stable default tab identity** — new `DefaultTab` setting tracks the default tab by name, surviving renames and reorder operations. Legacy `IsArchived` sessions auto-migrate to an "Archived" tab.
- **Tab reordering** — Up/Down buttons in Session Tabs settings to change tab display order.
- **Dynamic Open Files submenu** — "Open Files" is now a submenu listing all user files in the session folder (plan.md, files/ subfolder) with shell-associated icons. Top item opens the session folder in Explorer. Reserved Copilot files (events.jsonl, workspace.yaml, session.db) and folders (rewind-snapshots, checkpoints) are excluded.
- **Running-first default sort** — sessions with active processes are automatically sorted to the top. Configurable via Settings with three modes: Running first (default), Last updated, Alias/Name.
- **Test-first bug fix workflow** — added to copilot-instructions.md: all bug fixes require a failing test before the fix is applied.

### Fixed

- **Session list ordering** — new sessions now respect the current sort/filter instead of appearing at the end of the list.
- **Context menu targeting** — right-clicking a session row now correctly selects it before showing the context menu.
- **Grid empty on startup** — fixed grid not displaying on any tab after initial load by falling back to TabPages[0] when the TabControl handle isn't created yet.
- **Tab rebuild crash** — fixed NullReferenceException when saving settings after adding a new tab (TabPages.Clear fires SelectedIndexChanged with null SelectedTab).
- **Selection sync after refresh** — CurrentCell now stays in sync with the user's selection after the 3-second auto-refresh re-sorts rows.
- **Multi-selection lost on refresh** — fixed CurrentCell assignment clearing multi-selection in FullRowSelect mode by setting it before marking additional rows.
- **Scroll position reset on refresh** — grid scroll position is now preserved across the auto-refresh cycle.
- **Default tab leak on reorder** — untagged sessions no longer leak to the wrong tab when tabs are reordered.

### Changed

- **Context menu layout** — Open Files moved to top, Delete Session moved to last position with separator.
- **Max session tabs** — increased from 5 to 10.
- **Code organization** — extracted context menu handlers from MainForm into MainForm.ContextMenu.cs (partial class).

## [0.13.4] - 2026-02-25

### Added

- **Workspace from existing branch** — create a new workspace by checking out an existing local or remote branch via `git worktree`, with automatic local tracking branch creation.
- **Configurable workspaces directory** — new setting to choose where workspaces are created instead of using the default `_workspaces` folder next to the repo.
- **Auto-trust workspace directories** — session working directories are now automatically passed as `--add-dir` to the Copilot CLI, preventing repeated "trust this folder" prompts.
- **Duplicate settings guard** — adding an allowed tool or directory that already exists in the list is silently ignored.

### Fixed

- **Edge collection crash** — fixed `Collection was modified` exception in `CheckEdgeTabChanges` by snapshotting the tracked workspaces dictionary before enumeration.
- **Bell notification crash** — fixed `Balloon tip text must have a non-empty value` when session name is null or empty.
- **Installer missing files** — `session.html` and `copilot.ico` are now correctly included in the publish output for the installer.
- **Workspace folder naming** — sanitized folder names now coalesce special characters into single dashes instead of stripping them silently.

## [0.13.3] - 2026-02-20

### Added

- **Per-session metadata file** — session.html now reads name/alias and version from `sessions/{id}/metadata.js` instead of relying on UIA title updates.
- **Server-driven poll intervals** — signal and metadata poll intervals are dictated by the app via the script files, not hardcoded in HTML.
- **1-hour background update check** — periodically checks GitHub Releases for new versions without manual interaction.
- **About dialog instant update button** — if an update is already detected, the About button shows "⬆ Update to vX.Y.Z" immediately on open.

### Fixed

- **Bell notification alias** — tray balloon now shows the session alias (if set) instead of always using the session summary, including the watcher-based notification path.
- **Signal/metadata split** — separated fast-changing signals (3s) from slow-changing metadata (60s) into distinct files to reduce I/O.
- **Polling resilience** — script load errors now reschedule the next poll instead of silently stopping.
- **beforeunload guard** — explicit flag-based add/remove prevents redundant listener stacking.

## [0.13.2] - 2026-02-20

### Added

- **Edge unsaved tab detection** — detects tab changes via SHA256 hash of tab titles and shows an "unsaved changes" card in the session anchor page with a save button.
- **Save signal via title suffix** — clicking "Save Tabs" in the anchor page sets a `::Save` title suffix detected by UIA to trigger automatic save.
- **Signal file polling** — session.html polls a `session-signals.js` file written by the app to show/hide the unsaved card and spinner.
- **Before-unload guard** — warns before closing an anchor tab with unsaved changes.
- **Auto-baseline on Edge open** — creates a title hash baseline when an Edge workspace is first opened.

### Fixed

- **Bell notification name** — tray balloon now shows the session alias (if set) instead of always using the session summary.

## [0.13.1] - 2026-02-19

### Fixed

- **Edge tab traversal** — fixed tab save selecting in-page HTML elements (e.g., GitHub PR tabs) instead of browser tabs. Uses the anchor tab's `SelectionContainer` to identify real browser tabs.
- **Tab switch waiting** — replaced fixed delays with polling `IsSelected` state for reliable tab switching.
- **`--allow-tool` / `--add-dir` flags** — fixed argument format from `--allow-tool=X` to `--allow-tool X` so the Copilot CLI actually receives them.
- **IDE settings live reload** — changing IDEs or file patterns in Settings now takes effect immediately without restarting.
- **Session rename grid refresh** — renaming a session alias now always refreshes the grid, preventing archived items from appearing in the active list.
- **Duplicate URL deduplication** — saving Edge tabs now deduplicates URLs (case-insensitive).

### Added

- **Edge tab restore without empty tab** — when saved tabs exist, skips opening a blank new tab on Edge workspace launch.
- **Session rename via UIA** — renaming a session updates the Edge anchor tab title in-place without spawning a new tab or process.
- **"Update Edge tab on rename" setting** — optional setting (default: off) to control whether renaming a session updates the Edge anchor tab.
- **"Start New Session" context menu** — added to the existing sessions grid context menu.
- **New Session dialog improvements** — play (▶) column for quick launch, "Add Directory" button replaces "Create", icons on all context menu items.
- **IDE file pattern info** — ℹ️ tooltip in Add/Edit IDE dialog explaining the optional file pattern feature.
- **Settings save toast** — "✅ Settings saved successfully" notification on save.

## [0.13.0] - 2026-02-18

### Added

- **Delete instant removal** — deleting a session instantly removes it from the list without requiring a full refresh.
- **Multi-select sessions** — hold Ctrl to select individual sessions or Shift for range selection. Multi-select supports batch Pin/Unpin and Archive/Unarchive via context menu; other actions are greyed out.
- **Edge tab save/restore** — Edge browser tabs can be saved per session via the "Save Edge State" context menu button. Opening Edge for a session restores all previously saved tabs.
- **Per-session state** — each session now has its own state directory under `%APPDATA%\CopilotBooster\sessions\{id}\` for storing Edge tabs and other session-specific data.
- **Unified window handle persistence** — all tracked window handles (IDE, Explorer, Edge) are persisted in a single cache file and survive app restarts. Stale handles are automatically pruned on load.
- **Events.jsonl content-based detection** — Copilot CLI working/idle status is now detected by parsing the last event in `events.jsonl` (assistant turns, tool requests, ask_user). Replaces the old file-watcher approach.
- **Bell notifications** — sessions that finish work show a 🔔 bell icon and red-highlighted row. Windows toast notifications pop up with the session name. Bell state persists across app restarts.
- **Direct terminal launch** — terminals now launch via `wt.exe` (Windows Terminal) with `cmd.exe` fallback, for faster startup.
- **Duplicate CLI prevention** — opening a session that already has a Copilot CLI running focuses the existing window instead of spawning a new one.
- **IDE file pattern matching** — configure file patterns per IDE (e.g., `*.sln;*.slnx`) in Settings. The context menu shows a sub-menu with matched project files for quick opening.
- **IDE Search settings tab** — new "IDE Search" tab in Settings to manage directories excluded from file pattern search (node_modules, bin, obj, etc.).
- **Context menu icons** — all context menu items now have icons extracted from system shell resources (shell32.dll, imageres.dll) and IDE executables.
- **Open Files** — context menu option to open the session's files folder (`~/.copilot/session-state/{id}/files`).
- **Open Copilot Plan.md** — context menu option to open a session's plan file (visible only when it exists).
- **Settings tab tooltips** — all settings tabs have info labels (ℹ️) and hover tooltips explaining their purpose.
- **Running session sort priority** — sessions with active processes are sorted to the top of the list.

### Changed

- **Unified IDE sub-menus** — IDE context menu items are now always sub-menus containing CWD and Repo Root folders (merged when identical), plus matched project files when a pattern is configured.
- **Context menu reorganized** — session operations (Pin, Archive, Delete) moved to top section after Open/Edit for better grouping.
- **Brighter bell colors** — notification row backgrounds are stronger in both dark and light themes for better visibility. Selected bell rows use an even more prominent color.
- **Darker grid borders** — light theme grid borders now use `ControlDark` to match header borders.
- **ListView hover highlight** — owner-drawn ListViews now show a hover effect in light theme.
- **ListView foreground fix** — fixed white text appearing in light theme ListViews due to inherited dark-mode colors.
- **Toast notifications** — use app icon via `AppUserModelID` on Start Menu shortcuts; bell emoji in title, session name in body.
- **Staleness threshold** — events.jsonl files older than 30 minutes are treated as unknown status, preventing false bells on old sessions.
- **Renamed "Open Artifacts"** to **"Open Files"**.

### Fixed

- **False bells on startup** — stale cache entries are filtered on load; only working sessions are suppressed during startup seeding.
- **Bell-to-working transition** — selected row background color is properly reset when a bell session starts working again.
- **Concurrent modification crash** — session list in `ActiveStatusTracker.Refresh()` is now snapshot-copied to prevent collection modification during enumeration.
- **VSCode Insiders icon** — IDE paths with embedded quotes are now trimmed before icon extraction.
- **Toast icon missing** — added `AppUserModelID: CopilotBooster` to installer shortcuts so toast notifications show the app icon.

## [0.12.0] - 2026-02-17

### Added

- **Archived sessions** — sessions can now be archived via right-click context menu, moving them to a separate "Archived" tab for a cleaner active list. Unarchive from the same menu.
- **Pinned sessions** — pin sessions to keep them at the top of the list regardless of column sorting. Configurable sort order for pinned items (last updated or alias name) in Settings.
- **Session Files folder** — right-click "Open Files Folder" opens a dedicated Explorer window per session (`~/.CopilotBooster/{sessionId}/Files`), with HWND tracking for focus management.
- **Open Plan.md** — right-click context menu option to open a session's `plan.md` file directly (shown only when the file exists).
- **Open CWD in Explorer** — right-click to open the session's working directory in Explorer (untracked).
- **Search debounce** — search input now waits 500ms after the last keystroke before filtering, reducing UI churn during typing.
- **Settings gear button** — ⚙ button in the toolbar for quick access to Settings dialog.
- **About dialog** — accessible from Settings, shows app logo, version, creator, GitHub links, and a Check for Updates button.
- **Max active sessions** — configurable limit (default 50, 0 = unlimited) in Settings.
- **New Session dialog** — "New Session" is now a button with a modal directory picker dialog including Create/Cancel buttons.
- **Settings as modal dialog** — Settings moved from a tab to a standalone modal dialog.
- **Explorer in Running column** — tracked Explorer windows now appear in the "Running" column with click-to-focus support.
- **STA task scheduler** — Edge UI Automation scans now run on a dedicated background STA thread instead of blocking the UI thread.

### Changed

- **Column renamed** — "Activity" column renamed to "Running" for clarity.
- **Tab layout** — replaced 3-tab layout (Sessions/New Session/Settings) with a single-panel sessions view and sub-tabs (Active/Archived) with counts.
- **Async refresh** — all `RefreshActiveStatus` calls now run on background threads, keeping the UI responsive.
- **Archive/pin operations** — use lightweight row removal instead of full grid repopulate for instant visual feedback.

### Fixed

- **ListBox item clipping** — fixed descenders (`g`, `y`) and underscores being cut off in all owner-drawn ListBoxes by setting proper `ItemHeight`.
- **Dialog TopMost** — Settings, New Session, and About dialogs now inherit `TopMost` from the main form when AlwaysOnTop is enabled.
- **About logo quality** — uses embedded high-res PNG (722×714) instead of low-res icon bitmap conversion.

## [0.11.0] - 2026-02-16

### Added

- **Edge session names** — Edge anchor tabs now display the session name (alias or summary) in the tab title and page content, making it easy to identify which browser window belongs to which session.
- **Live Edge name updates** — changing a session alias automatically updates the Edge tab title via `hashchange` navigation, no need to close and reopen.
- **Edge new-tab on open** — opening an Edge workspace now automatically opens a fresh new tab alongside the session anchor tab, so your browsing doesn't overwrite the tracker tab.
- **Session.html dark mode** — the anchor tab page now supports light and dark themes via `prefers-color-scheme`, with a warning banner reminding users not to close the tracking tab.
- **Session list ordering preserved** — refreshing the session grid no longer resets row order; existing positions are maintained while new sessions are appended.
- **Update banner theming** — the update-available link label now uses theme-aware colors (light blue in dark mode, dark blue in light mode).

### Fixed

- **Edge not working in installed version** — the Inno Setup installer was missing `session.html` and `copilot.ico`, causing Edge workspace open to silently fail. Both files are now included.
- **Spaces in session names showing as `+`** — switched from `WebUtility.UrlEncode` to `Uri.EscapeDataString` for proper `%20` encoding in URL hash fragments.

## [0.10.0] - 2026-02-16

### Added

- **Session aliases** — sessions now have a stable alias field separate from the Copilot CLI's dynamic session name. Aliases persist across name changes and are shown in the session list with a tooltip displaying the current name.
- **Auto-hide on focus** — clicking to focus a session automatically minimizes tracked windows (terminals, IDEs, Edge) from other sessions, keeping your desktop clean. Enabled by default; configurable in Settings.
- **Always on top** — new setting to keep the CopilotBooster window above all other windows.
- **Configurable log level** — set `"logLevel": "Debug"` in `launcher-settings.json` to enable performance profiling and diagnostic output.

### Fixed

- **IDE tracking lost when opening .sln** — Visual Studio windows are now re-captured by process ID when the window handle changes (e.g., opening a solution file).
- **Edge windows not minimized** — auto-hide now correctly includes Edge workspace windows.
- **Collection modified during enumeration** — fixed a race condition between the background refresh thread and UI focus actions.

### Improved

- **93× faster first load** — replaced per-session Edge workspace probing (84 seconds for 90 sessions) with a single bulk UI Automation scan (~1 second).
- **Git status caching** — git repository checks are now cached for the app lifetime, eliminating redundant filesystem walks on every refresh cycle.
- **Migrated to `ILogger`** — replaced custom `LogService` with `Microsoft.Extensions.Logging.ILogger` for structured logging with proper log levels.

## [0.9.0] - 2026-02-15

### Added

- **Dark/light/system theme support** — new theme dropdown in Settings with System (default), Light, and Dark options. Persisted across restarts. Changing theme restarts the app with confirmation.
- **Session soft delete** — right-click a session → "Delete Session" with confirmation dialog. Soft-deletes by renaming `workspace.yaml` to `workspace-deleted.yaml`, preserving all artifacts for recovery.
- **Custom-styled tabs** — owner-drawn tabs in light mode with better contrast between selected and unselected states.
- **Themed DataGridView headers** — custom-painted column headers with sort glyphs, column borders, and proper dark/light colors.
- **Themed selection highlights** — consistent blue selection colors across all grids, lists, and listviews in both themes.
- **Panel-as-border TextBox styling** — all text inputs wrapped with themed border panels for consistent appearance.
- **Session status icons** — animated blue spinner for working sessions and static red bell for idle/waiting sessions, rendered as image icons in a new Status column.
- **Toast notifications** — Windows balloon notifications via the system tray icon when a Copilot CLI session finishes work and is ready for interaction. Click the notification to focus the terminal. Configurable on/off in Settings.
- **Bell row highlighting** — sessions waiting for input get a soft red background color for visual distinction.
- **Focus-click bell dismissal** — clicking to focus a session suppresses its bell until it transitions to working again.
- **Startup suppression** — existing idle sessions don't trigger false bell notifications when the app launches.

### Changed

- **Architecture refactoring** — decoupled business logic from UI with new service classes (`SessionInteractionManager`, `BellNotificationService`, `WorkspaceCreationService`, `SessionRefreshCoordinator`). All visual classes renamed with `Visuals` suffix. MainForm reduced from ~1412 to ~940 lines.
- **Non-blocking installer** — `install.ps1` no longer waits for the application to close before returning.

## [0.8.1] - 2026-02-15

### Added

- **Open in Explorer** — right-click a directory in the New Session tab to open it in Windows Explorer.
- **Open Terminal** — right-click a directory in the New Session tab to launch a terminal at that path (without session tracking).

## [0.8.0] - 2026-02-14

### Added

- **System tray icon** — the app now lives in the system tray with a context menu (Show, Settings, Quit). Closing the window minimizes to tray instead of exiting; only "Quit" from the tray menu exits the application.
- **AppData migration** — CopilotBooster state files (settings, caches, logs) moved from `~/.copilot/` to `%APPDATA%\CopilotBooster\`. Existing files are migrated automatically on first startup.
- **Session start event** — new sessions now write an `events.jsonl` with a `session.start` event, matching the format expected by Copilot CLI.
- **Release process documentation** — added full release checklist to `.github/copilot-instructions.md`.

### Fixed

- **Session creation** — replaced broken SDK-based session creation with direct `workspace.yaml` + `events.jsonl` file creation. The `id` field required by Copilot CLI is now always present.
- **JumpList after rename** — set `AppUserModelID` on the process so Windows associates the JumpList with the correct taskbar button after the CopilotApp→CopilotBooster rename.
- **Grid refresh after session creation** — the session list now auto-refreshes after creating a new session.

### Changed

- **Settings UI** — removed Move Up/Move Down buttons from Allowed Tools and Directories lists.

### Removed

- **GitHub.Copilot.SDK dependency** — replaced with direct file creation for session management.

## [0.7.1] - 2026-02-14

### Fixed

- **IDE focus always went to Visual Studio** — IDE windows are now tracked by cached window handle (HWND) instead of title substring matching, fixing the collision where "Visual Studio" matched both VS and VS Code.
- **IDE tracking lost on app restart** — IDE window handles are now persisted in `~/.copilot/ide-cache.json` and re-validated on startup, so IDE instances survive app restarts.
- **Duplicate IDE instances** — clicking "Open in IDE" for a session that already has that IDE open now focuses the existing window instead of launching a new instance.

## [0.7.0] - 2026-02-14

### Changed

- **Renamed to Copilot Booster** — the project identity is now `CopilotBooster` across namespaces, assemblies, executables, installer, AppData paths, and all documentation.
- **New Session tab revamp** — replaced the bottom button bar with a right-click context menu matching the Existing Sessions tab pattern. All actions (New Session, New Workspace, Add/Remove Directory) are accessible from the context menu.
- **Session name prompts** — creating a new session or workspace now prompts for a session name before launch.
- **Default tab is Existing Sessions** — clicking the taskbar icon opens the Existing Sessions tab; the jump list "New Copilot Session" opens the New Session tab.
- **Improved README pitch** — rewrote the introduction to highlight parallel agent productivity and session isolation.

### Added

- **GitHub Copilot SDK integration** — sessions are created programmatically via `GitHub.Copilot.SDK` with working directory and name support.
- **Pinned directories** — manually added directories persist in `~/.copilot/pinned-directories.json` and appear in the New Session tab even with zero sessions.
- **Loading overlay** — the New Session tab shows "Loading directories..." while data is being fetched.
- **New app icon** — auto-cropped multi-resolution icon from the new logo (16–256px).
- **`--new-session` CLI flag** — opens the New Session tab directly; used by the jump list.

### Fixed

- **Workspace menu visibility** — "Open as New Copilot Session Workspace" now correctly appears only for Git-enabled directories.

## [0.6.4] - 2026-02-14

### Fixed

- **Session name not applied on resume** — editing a session name now updates both `name` and `summary` fields in workspace.yaml, so the Copilot CLI picks up the renamed session.
- **Multiple app instances allowed** — replaced process-name detection with a named Mutex to reliably prevent multiple MainForm windows from opening simultaneously.
- **UI freeze when loading sessions** — moved session data loading and active status refresh off the UI thread to prevent the waiting cursor freeze on startup.

## [0.6.3] - 2026-02-14

### Changed

- **Release notes** — GitHub Releases now show only the current version's changelog instead of the entire history.
- **README download links** — added prominent download buttons at the top of the README for quick access to the installer and portable ZIP.

## [0.6.2] - 2026-02-14

### Fixed

- **Copilot CLI active tracking with dynamic titles** — Copilot CLI changes the terminal title while working (e.g., `🤖 Fixing emoji prefix` instead of the session name). Active tracking now strips leading emoji prefixes and caches window handles so sessions stay active even when the title changes dynamically.
- 8 new unit tests for emoji stripping (112 total).

## [0.6.1] - 2026-02-14

### Changed

- **Right-click context menu** — replaced the bottom "Open ▾" button with a right-click context menu on session rows. All actions (Open Session, Edit Session, Open Terminal, IDE, Edge) are now accessible via right-click.
- **Loading overlay** — the Existing Sessions tab shows a "Loading sessions..." indicator while session data is being fetched on startup.

### Fixed

- **Edge browser launch** — resolved Edge executable path from Windows registry (`App Paths`) instead of relying on PATH, with fallback to common install locations and `microsoft-edge:` protocol handler.

## [0.6.0] - 2026-02-14

### Added

- **Copilot CLI detection** — scans open windows for Copilot CLI terminals by matching session summaries. Detects multiple instances of the same session with numbered labels (e.g., "Copilot CLI #1", "#2").
- **HWND-based window focus** — clicking an Active link now focuses the exact window handle, fixing a bug where duplicate Copilot CLI titles always focused the first match.
- **Direct IDE launch from context menu** — replaced the IDE picker dialog with direct "Open in {IDE} (CWD)" and "Open in {IDE} (Repo Root)" items in the Open dropdown.
- **Session summary live sync** — detects when session names change externally (e.g., from Copilot CLI) and updates the list automatically.
- **Auto-refresh on new sessions** — the Existing Sessions list refreshes when new sessions appear.

### Changed

- **SOLID refactoring** — extracted MainForm (1658 → 860 lines) into focused components: `SessionDataService`, `ActiveStatusTracker`, `SessionGridVisuals`, `SettingsVisuals`, `NewSessionTabBuilder`.
- **Constructor decomposition** — 700-line constructor split into 10 well-named builder methods.
- **Async I/O** — all file/process scanning runs on background threads via `Task.Run()` to prevent UI freezes during startup and refresh.
- **Auto-fit CWD column** — column width adjusts to content (capped at 300px). Window width increased to 1000px.
- **Hand cursor** — shows only when hovering over clickable Active column link text.
- 104 total tests.

## [0.5.0] - 2026-02-13

### Added

- **Terminal cache across restarts** — active terminal sessions are now cached in `~/.copilot/terminal-cache.json`. When the app restarts, it re-discovers still-running terminals and restores their "Active" status instead of losing track of them.
- **TerminalCacheService** — new service that persists terminal PIDs on launch, validates them on startup, and garbage-collects dead entries automatically.
- 7 new unit tests for `TerminalCacheService` (101 total).

## [0.4.0] - 2026-02-13

### Added

- **Edit session** — right-click any session in the Existing Sessions list to open an "Edit" context menu. The Edit Session dialog lets you rename the session summary and change the working directory (with a folder browser).
- **SessionEditorVisuals** — new modal dialog with session name and CWD fields, including a Browse button that defaults to the current working directory.
- **SessionService.UpdateSession()** — new method to persist session edits back to `workspace.yaml`, preserving all other fields.
- 5 new unit tests for `UpdateSession` (99 total).

### Changed

- **Default tab** — the app now opens on "Existing Sessions" by default instead of "New Session".

## [0.1.1] - 2026-02-12

### Fixed

- **Session list showing incomplete results** — sessions without a summary were silently dropped; now uses the folder name as a fallback display title.
- **Search scope** — search now queries across all sessions (cached), not just the visible 50.
- **Refresh button** — now reloads the full session cache before updating the display.

## [0.1.0] - 2026-02-12

### Added

- **Git workspace creation** — create isolated [git worktrees](https://git-scm.com/docs/git-worktree) from the New Session tab ("Create Workspace" button) or the Existing Sessions dropdown ("Open as New Session Workspace"). Each workspace gets its own branch and directory under `%APPDATA%\CopilotBooster\Workspaces\`.
- **Git column in directory picker** — the New Session tab now shows a "Git" column indicating whether each directory is inside a Git repository (including worktrees).
- **Session count column** — directory picker column renamed to "# Sessions created" for clarity.
- **Git indicator on sessions** — sessions with Git-enabled working directories show "- Git" in the date column of the Existing Sessions tab.
- **Open Session dropdown** — split button with "Open as New Session" and "Open as New Session Workspace" options.
- **GitService** — new service for branch listing, current branch detection, and worktree creation.
- 72 new unit tests for `GitService`.

### Changed

- **Tab order** — "New Session" is now the first (default) tab, followed by "Existing Sessions" and "Settings".
- **Worktree detection** — `FindGitRoot` now detects git worktrees (`.git` file) in addition to standard `.git` directories.
- Updated README with new screenshots and documentation for all v0.1.0 features.

## [0.0.3] - 2026-02-10

### Added

- **Session search** — search box in the Existing Sessions tab filters sessions as you type, matching title/summary first and falling back to metadata (cwd, session id).
- 10 new unit tests for search functionality.

## [0.0.2] - 2026-02-06

### Added

- **Window focus for active sessions** — clicking an active session in the jump list now focuses the existing terminal window instead of launching a duplicate.
- **Unit test suite** — 72 xUnit tests covering all testable business logic (models, services, argument parsing).
- **Test gate in release pipeline** — builds, format checks, and tests must pass before publishing.
- **XML documentation** on all public and internal members.
- **`.editorconfig`** adopted from microsoft/agent-framework with project-specific relaxations.
- **`dotnet format` verification** in the CI pipeline as a pre-release check.

### Changed

- **SOLID architecture refactor** — extracted services (`SessionService`, `PidRegistryService`, `WindowFocusService`, `LogService`, `CopilotLocator`, `JumpListService`) from monolithic `Program.cs`.
- **Singleton MainForm** — prevents duplicate UI windows; signals existing instance to switch tabs.
- Expanded single-line conditional and try-catch blocks to multi-line for readability.
- Moved `RuntimeIdentifier` from project file to publish-time only (`-r win-x64`), fixing MSIL/AMD64 architecture mismatch warning.
- Added `ExcludeFromCodeCoverage` to UI forms and P/Invoke-heavy code.

### Fixed

- **Window focus** — uses `keybd_event` (Alt key) trick to bypass Windows `SetForegroundWindow` restrictions; matches terminal window by cmd.exe process ID instead of window title.
- Jump list no longer requires custom `AppUserModelID`; keeps form visible for taskbar integration.

## [0.0.1] - 2026-02-04

### Added

- Initial release of Copilot App — a Windows taskbar companion for GitHub Copilot CLI.
- Taskbar-pinnable launcher with custom icon.
- Jump list integration with active and recent sessions.
- Session resume via `--resume` flag.
- Working directory picker for new sessions.
- Settings UI with configurable allowed tools, directories, and default work directory.
- IDE picker integration (VS Code, Rider, Visual Studio).
- PID registry for tracking active launcher instances.
- Install script (`install.ps1`) for automated setup.
- MIT license.
- GitHub Actions release workflow with `.zip` artifact publishing.

[0.10.0]: https://github.com/rogerbarreto/copilot-booster/compare/v0.9.0...v0.10.0
[0.8.0]: https://github.com/rogerbarreto/copilot-booster/compare/v0.7.1...v0.8.0
[0.6.3]: https://github.com/rogerbarreto/copilot-booster/compare/v0.6.2...v0.6.3
[0.6.2]: https://github.com/rogerbarreto/copilot-booster/compare/v0.6.1...v0.6.2
[0.6.1]: https://github.com/rogerbarreto/copilot-booster/compare/v0.6.0...v0.6.1
[0.6.0]: https://github.com/rogerbarreto/copilot-booster/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/rogerbarreto/copilot-booster/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/rogerbarreto/copilot-booster/compare/v0.3.0...v0.4.0
[0.1.1]: https://github.com/rogerbarreto/copilot-booster/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/rogerbarreto/copilot-booster/compare/v0.0.3...v0.1.0
[0.0.3]: https://github.com/rogerbarreto/copilot-booster/compare/v0.0.2...v0.0.3
[0.0.2]: https://github.com/rogerbarreto/copilot-booster/compare/v0.0.1...v0.0.2
[0.0.1]: https://github.com/rogerbarreto/copilot-booster/releases/tag/v0.0.1
