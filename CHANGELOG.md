# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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

- **SOLID refactoring** — extracted MainForm (1658 → 860 lines) into focused components: `SessionDataService`, `ActiveStatusTracker`, `SessionGridController`, `SettingsTabBuilder`, `NewSessionTabBuilder`.
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
- **SessionEditorForm** — new modal dialog with session name and CWD fields, including a Browse button that defaults to the current working directory.
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
