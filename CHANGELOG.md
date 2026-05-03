# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.21.1] - 2026-05-03

### Fixed

- **Windows Terminal multi-tab discovery** — Copilot sessions in separate WT tabs that share one parent `wt.exe` HWND now remain independently active in the booster grid by carrying the UIA pane runtime id alongside the parent HWND, preferring process-tree pane-root correlation over mutable tab-title matching.
- **Windows Terminal tab focus** — clicking a WT-hosted Copilot CLI link now foregrounds the WT parent, selects the cached UIA tab item with `SelectionItemPattern.Select()` / `InvokePattern.Invoke()`, and verifies the selected tab/readback title before treating focus as successful.
- **Windows Terminal live E2E coverage** — the LocalOnly WT regression now clicks the actual grid link for each session and verifies the selected UIA tab runtime id, tab name, WT window title, and UIA-visible in-pane marker after focus so swapped session/pane mappings fail the test.
- **Windows Terminal title-bind regression** — WT tab title/name-change events no longer evict PID-resolved Copilot hosts or collapse both grid links onto the foreground tab; active CLI links are computed from the live Copilot PID and cached pane runtime id.
- **Windows Terminal focus diagnostics and fallback** — WT host resolution/focus now writes capped runtime diagnostics to `%LOCALAPPDATA%\CopilotBooster\logs\diag.log` and falls back to verified Ctrl+Tab/Ctrl+Shift+Tab navigation when UIA tab selection does not stick.

## [0.21.0] - 2026-05-03

### Added

- **Copilot Host discovery** — every Copilot session now tracks the host process that spawned it (Windows Terminal, conhost, third-party terminals, IDE-hosted shells). Click-to-focus migrates to the correct host window even when the Copilot CLI itself has no visible window of its own.
- **Windows Terminal multi-pane focus** — when multiple Copilot sessions run in different panes of the same `wt.exe` window, clicking a session in the booster focuses the *exact* pane via UI Automation (`SelectionItemPattern` / `InvokePattern`), not just the parent window. Pane handles are cached per WT window with automatic invalidation on tab/pane structure changes.
- **Deferred session naming** — sessions that first appear with a GUID-only name (typical for fresh externally-spawned sessions) are auto-renamed when their `events.jsonl` records the first user message. The resolved name flows through the same booster-resolved-name pipeline used by internally-launched sessions.
- **External session host resolution** — sessions detected via the Copilot log watcher (`~/.copilot/logs/`) now resolve their host process at discovery time, not on first focus, eliminating the "click and nothing happens" first-time delay.
- **Live `wt.exe` multi-pane integration test** — local-only test that spins up Windows Terminal with multiple Copilot panes (each tagged via `copilot --deny-url=<guid>`), validates that the booster shows them as distinct sessions with the correct booster-resolved names, and verifies pane-precise focus dispatch.

### Changed

- **`workspace.yaml.summary` no longer holds GUIDs** — externally-discovered sessions write an empty summary on creation; the booster-resolved name is computed from `events.jsonl` content and stored as a sidecar (ADR-0001 compliance). No migration is performed on existing sessions — GUIDs may be legitimate names in rare cases.
- **Focus migration priority order** — first try the cached host HWND (Priority 1), fall back to legacy title-scan (Priority 2), then PID-based heuristics (Priority 3). Avoids the previous behaviour of always landing on whichever window happened to match a brittle title pattern.
- **`UseWPF` is now enabled** in the main project to access `System.Windows.Automation` for pane enumeration. No WPF UI is shipped — only the automation namespace is consumed.

### Fixed

- **Stale window-handle cache** when a Windows Terminal tab is closed/moved — the cache now invalidates on `WindowDestroyed` events for both the parent and the cached pane HWND, plus on WT name-changed events that signal a tab structure change.
- **Integration tests run reliably on local machines** — Playwright tests now auto-install the chromium browser on first run via a self-bootstrap fixture; CI continues to install in `release.yml` as before. Tests requiring an interactive desktop session are now marked `[Trait("Category", "LocalOnly")]` and skip cleanly in CI rather than producing tolerated red runs.

## [0.20.1] - 2026-04-14

### Added

- **Edge tracking integration tests** — validates that "Open in Edge" correctly registers the Edge workspace in the session tracker and builds the expected session URL.

## [0.20.0] - 2026-04-09

### Added

- **Welcome popup** — on startup, shows a thank-you message and requests a GitHub star. Authenticated users can star directly from the popup; unauthenticated users are directed to the repo page in their browser. Includes a "Don't show again" checkbox.
- **`HasGhCli` and `IsAuthenticated` properties** — cached detection of `gh` CLI availability and authentication status, used by the welcome popup and available for future features.
- **`IsRepoStarredAsync` / `StarRepoAsync`** — check and set GitHub star status via `gh api` (primary) or HTTP with PAT (fallback).
- **HTML scraping extracts rich PR metadata** — author, head branch, head SHA, state (open/merged/closed), merged-by, and updated-at are now extracted from embedded JSON in GitHub HTML pages.
- **HTML scraping for CI check runs** — the `/pull/N/checks` page is scraped for check run names, conclusions, and job IDs, removing the dependency on `gh api` for CI status.

### Fixed

- **Issue/PR check "not found" bug** — the "Create New Worktree" and "New Session" dialogs no longer fail to find issues/PRs on public repositories when the GitHub API rate limit is exhausted.
- **IDE processes tied to CopilotBooster lifecycle** — IDEs now launch as fully detached processes with immediate handle disposal, preventing crashes when CopilotBooster exits.
- **Process handle leaks** — `OpenInIde` and `LaunchIde` now return PIDs and dispose `Process` handles internally.

### Changed

- **Eliminated all `api.github.com` usage** — GitHub data access now uses HTML scraping of `github.com` pages (primary, never rate-limited) with `gh api` CLI as fallback for private repos or richer data.
- **Forms use centralized `GitHubApiService`** — the "Create New Worktree" and "New Session" dialogs now use the shared `GitHubApiService` instead of inline HTTP calls.
- **Injectable process runner** — `GitHubApiService` accepts an optional process runner delegate for unit test faking.

### Removed

- **Direct `api.github.com` HTTP calls** — the internal `GetAsync`/`TryGetAsync`/`CreateRequest` HTTP infrastructure and manual token management have been removed from `GitHubApiService`.

## [0.19.3] - 2026-04-08

### Fixed

- **Issue/PR check "not found" bug** — the "Create New Worktree" and "New Session" dialogs no longer fail to find issues/PRs on public repositories when GitHub API rate limit is exhausted. The previous unauthenticated `api.github.com` calls (60 req/hour) would silently fail and report "Issue not found" or "PR not found".

### Changed

- **Eliminated all `api.github.com` usage** — GitHub data access now uses HTML scraping of `github.com` pages (primary, never rate-limited) with `gh api` CLI as fallback for private repos or richer data. No more unauthenticated API calls anywhere in the codebase.
- **HTML scraping extracts state and state_reason** — closed issues, merged PRs, and "not planned" status are now correctly detected from embedded metadata in GitHub HTML pages.
- **Forms use centralized `GitHubApiService`** — the "Create New Worktree" and "New Session" dialogs now use the shared `GitHubApiService` instead of inline HTTP calls, ensuring consistent error handling across the app.

### Removed

- **Direct `api.github.com` HTTP calls** — the internal `GetAsync`/`TryGetAsync`/`CreateRequest` HTTP infrastructure and manual token management have been removed from `GitHubApiService`.

## [0.19.2] - 2026-03-18

### Fixed

- **GitHub links open in default browser** — reverted broken Edge session browser integration for PR/Issue/CI links. Clicking GitHub column items now opens in the OS default browser reliably.

### Removed

- **"Open GitHub links in session Edge browser"** setting removed from Settings → GitHub (was non-functional).

## [0.19.1] - 2026-03-17

### Fixed

- **Worktree creation timeout** — `git worktree add` on large repositories no longer times out after 10 seconds. The operation now runs to completion without being killed. (#12)
- **Async worktree creation** — all 4 worktree creation modes (PR, Issue, New Branch, Existing Branch) now run asynchronously, preventing UI freezes during creation.
- **FormClosing guard** — added guard to prevent accidentally closing the worktree creation dialog during an operation.

### Changed

- **Workspace → Worktree rename** — renamed user-facing "Workspace" labels to "Worktree" in the creation dialog, menus, and error messages for consistency with git terminology. (#12)

### Added

- **External Copilot session discovery** — sessions started outside of Copilot Booster are now automatically detected via log file monitoring (`~/.copilot/logs/`). A `workspace.yaml` is auto-created so the session appears in the UI immediately.

## [0.19.0] - 2026-03-15

### Added

- **GitHub Integration Column** — new "GitHub" column in the session grid for tracking PRs and Issues per session.
  - **Add PR / Add Issue** dialogs with API validation, remote selection, and "Discover from Branch" auto-detection.
  - **PR/Issue icons** with GitHub state colors (green=open, red=closed, purple=merged, gray=draft/not-planned).
  - **Pipeline CI overlay** (top-left, blue ✓ / red ✗) and **approval overlay** (bottom-right, green ✓ + count) on PR icons.
  - **Red notification dot** when new activity is detected on tracked items.
  - **GitHub submenu** in context menu: Add PR, Add Issue, per-item Show CI Jobs / Open in Browser / Remove.
  - **CI Information Form** — lists all check runs (PR Checks + Merge Queue Checks) with log viewer, search, and Open in Browser buttons.
  - **Background polling** with exponential backoff (30s/5min/30min) and immediate poll on startup.
  - **Toast and tray notifications** when tracked PRs/Issues have new activity.
  - **Cascading auth**: unauthenticated → `gh` CLI token → PAT, with HTML fallback for rate-limited/SAML-blocked repos.
- **Track Active Session** — focusing a tracked window (IDE, Terminal, Edge) auto-selects the session row and switches tabs. Configurable in Settings → General.
- **Win+Alt+C Window Pin** — press Win+Alt+C to pin any window to a session via click-to-pin with crosshair cursor and confirmation dialog.
- **GitHub settings section** in Settings with "Open GitHub links in session Edge browser" toggle.

### Fixed

- **Fork-based PR discovery** — "Discover from Branch" now finds PRs from forks by scanning all open PRs when the `head` filter misses.
- **SAML-blocked repos** — HTML page fallback when API returns 403 for SAML-enforced orgs (e.g., microsoft/).
- **Selected remote stored correctly** — owner/repo from user-selected remote is stored, not the first remote.
- **Dark mode row contrast** — unselected rows are near-black (#111111), selected rows are dark blue (#384659).
- **Edge session URL opening** — focuses workspace window before launching URL so new tab opens in correct browser profile.
- **CI form buttons visible** — moved button panel to form level so it's not hidden behind the log panel.
- **CI form log line breaks** — normalized `\n` to `\r\n` for proper WinForms TextBox display.
- **CI form app icon** — shows Copilot Booster icon.
- **Job log fetch** — tries unauthenticated first for public repos.
- **Closed issue colors** — "not_planned" issues show gray, "completed" show purple. StateReason backfilled for old data.
- **Test native handle exhaustion** — tests explicitly dispose SessionGrid/SessionTabs to prevent IndexOutOfRangeException.
- **Removed obsolete `UpdateEdgeTabOnRename` setting** (replaced by session.js).

## [0.18.4] - 2026-03-10

### Fixed

- **Ignore .lock files in context counts** — Copilot CLI `inuse.*.lock` files are now filtered out so they don't appear as context files.

## [0.18.3] - 2026-03-09

### Fixed

- **Context menus no longer jump to adjacent monitors** — on multi-screen setups, right-click menus now open on the correct monitor.
- **Fix crash: Win32Exception "Error creating window handle"** — WinEvent hook callbacks could fire after the form's window handle was destroyed during closing, causing `RequestRefresh()` to restart the debounce timer on a dead form. Added `IsDisposed`/`IsHandleCreated` guard, moved hook/timer cleanup before handle destruction, and added `_stopped` flag to `WindowEventHookService`.

## [0.18.2] - 2026-03-09

### Fixed

- **Terminal/Copilot CLI detection is now immediate** — windows are detected as soon as they open via `EVENT_OBJECT_NAMECHANGE` and `EVENT_SYSTEM_FOREGROUND` hooks, instead of waiting for the 45-second full refresh.
- **IDE tracking survives Visual Studio splash→main window transition** — when VS destroys its splash screen and creates the main window, the HWND is recaptured by PID without relying on window titles.
- **IDE close is detected immediately** — closing Visual Studio or VS Code Insiders now clears the "Running" column right away instead of leaving stale entries.
- **Single-process IDE tracking (VS Code pattern)** — multiple sessions using the same IDE host process are tracked independently; closing one session's window does not affect others.
- **IDE tracking never relies on window titles** — all detection uses PID matching and HWND association from the session that launched the IDE.

### Added

- **E2E integration tests for Terminal/CLI grid detection** — 5 tests using real `wt.exe` and `TerminalLauncherService` paths with proper cleanup.
- **E2E integration tests for IDE lifecycle** — full open/close/reopen matrix across multiple sessions using `mspaint.exe` as an IDE stand-in.
- **IDE simulators (IdeSimVS, IdeSimVSCode)** — standalone test tools that mimic VS splash→main transition and VS Code single-instance host behavior, with random window titles to prevent title-based tracking.
- **VS simulator SLN and folder modes** — distinct tests for opening .sln files vs folders, matching observed VS behavior.
- **Real Visual Studio integration test** — E2E test using actual `devenv.exe` with `LocalOnly` trait for local-only execution.
- **`IDE2000` enforced** — multiple blank lines now flagged as error in `.editorconfig`.

## [0.18.1] - 2026-03-08

### Fixed

- **Edge tab count updates instantly after save** — the session grid now refreshes the tab count column immediately after saving or clearing Edge tabs, instead of requiring a manual refresh.
- **Version display moved to session.html** — version is now stamped directly into `session.html` at startup instead of being injected via `metadata.js`, preventing unnecessary polling.
- **Terminal launcher tests no longer leave orphan tabs** — invalid-directory tests now verify method existence via reflection instead of launching actual terminal processes.

### Added

- **Integration tests for save→count flow** — new unit and E2E tests verify that the Edge tab count in the session grid matches the persisted tab count immediately after save.

## [0.18.0] - 2026-03-07

### Added

- **Reactive window tracking architecture** — replaced the 3-second polling loop with event-driven monitoring using `SetWinEventHook`. Window title changes, visibility, and destruction events are now detected instantly via OS-level callbacks, dramatically reducing CPU usage.
- **Process exit watcher** — tracks terminal and IDE process lifecycles via exit callbacks instead of periodic liveness checks.
- **FileSystemWatcher for session discovery** — `workspace.yaml` changes are detected in real time, eliminating the need to poll for new or removed sessions.
- **FileSystemWatcher for session file counts** — file additions and deletions within session directories are tracked reactively.
- **Edge save detection via title hook** — clicking Save in the session page triggers a `::Save` title signal detected by the window event hook, replacing the previous polling-based approach.
- **Save signal debounce** — prevents duplicate save processing when title change events fire multiple times within a 2-second window.
- **Integration test project** — new xUnit v3 + Playwright test suite covering FileSystemWatcher events, terminal title detection, process tracking, and full E2E save-button flow.
- **Automated code signing pipeline** — private self-hosted runner workflow that signs both the portable EXE and installer with Certum code signing certificate, with automated TOTP authentication for SimplySign Desktop.

### Fixed

- **Save preserves previously saved tabs** — when only the session tab is open, clicking Save no longer clears previously saved Edge tabs from `edge-tabs.json`.
- **Edge tab detection** — fresh STA threads for COM, prefer best window match, update stale window handles.

### Changed

- **Root `.editorconfig`** — added comprehensive analyzer rules (IDE and CA diagnostics) enforced across all projects.
- **Release pipeline** — split into public CI (build, format, tests) and private signing workflow (code sign, installer, GitHub Release).

## [0.17.3] - 2026-03-06

### Fixed

- **Toast hotkey focus behavior** — when Always on Top is disabled and the window is visible but not focused, `Win+Alt+X` now brings the window to focus instead of hiding it. Always on Top mode retains the toggle behavior.
- **Session alias edit refresh** — editing a session alias no longer triggers a full list reload that resets the active tab. The cell is updated in-place and the background refresh picks up changes naturally.
- **Session page alignment** — warning banner and unsaved-changes card in session.html are now properly centered.
- **Teams handle persistence** — tracked Teams window handles are now cached across app restarts, matching the existing behavior for IDE, Explorer, and Edge windows.

### Changed

- **Bug report template** — simplified to require only a description. Steps to reproduce, expected behavior, version, and OS are now optional (empty sections auto-removed).

## [0.17.2] - 2026-03-04

### Fixed

- **Toast hotkey after Win+D** — `Win+Alt+X` now correctly restores the window after `Win+D` (show desktop). Replaced stale `_toastVisible` flag with computed `IsToastVisible` property that checks actual window state (visibility, window state, and area ratio vs restore bounds).
- **Toast position after minimize** — uses `RestoreBounds.Size` instead of the minimized taskbar thumbnail size (160x28) for position calculation, preventing shifted/wrong placement.
- **CWD validation** — all session actions (launch, terminal, IDE, explorer) now check if the working directory exists before proceeding. If missing, prompts the user to select a new folder and updates the session automatically.
- **Startup CWD warning** — shows a toast warning on app load when sessions have missing working directories.
- **Context menu on empty area** — right-clicking empty grid space no longer opens the context menu.
- **RunGit process deadlock** — fixed potential freeze when git commands produce large stderr output (e.g., `git fetch` progress) by reading stdout/stderr asynchronously.

### Changed

- **Session ID format** — context menu header changed from `#:{sid}` to `Id: {sid}`.
- **Context menu "Start New Session"** — replaced with "Open" submenu containing "Open New" and "Open by Id" options.
- **Open by Id** — validates session exists and is not soft-deleted before opening.
- **Toolbar buttons** — replaced text buttons with borderless icon buttons (Copilot CLI icon for sessions, shell32 gear for settings) with tooltips.
- **CWD column** — persistent width, fixed Date/Ctx/Running columns, text truncation with `...` keeping icons visible.
- **Debug hotkey** — uses F1 (no modifiers) in Debug builds to avoid stuck modifier keys when breaking in debugger.

### Added

- **PR-based workspace creation** — create workspaces from pull request numbers with platform auto-detection (GitHub, GitLab, Azure DevOps, Bitbucket).
- **PR validation** — async validation via `git ls-remote` with GitHub API title fetch and "Use PR title as session name" option.
- **Branch/PR options in "Open as New Session"** — enhanced dialog with Same branch / Switch branch / From PR # modes.
- **Allowed URLs settings** — new Settings tab for managing global Copilot CLI allowed URLs (`~/.copilot/config.json`).
- **Current branch display** — shows current branch in dialogs and marks it with `*` prefix in branch dropdowns.
- **CWD git icon** — embedded PNG git icon replaces the `⎇` text character, with warning icon for missing directories.
- **CWD tooltip** — shows full path on hover.

## [0.17.1] - 2026-03-02

### Fixed

- **YAML escaping for session names** — session names containing YAML-special characters (`:`, `[`, `]`, `#`, etc.) no longer break the workspace.yaml parser. Values are now properly quoted when needed.
- **Worktree folder name truncation** — workspace folder names are now truncated to the 3 leftmost words from the branch name after the repo prefix, preventing excessively long directory paths.

### Added

- **Session ID in context menu** — right-clicking a session now shows a header with the full session ID and a clipboard icon. Clicking it copies the session ID to the clipboard and displays a confirmation toast.

## [0.17.0] - 2026-03-01

### Added

- **Context column (Ctx.)** — new column showing file and Edge tab icons with count overlays for each session. Click the file icon for a context menu listing session files with "Open Session Folder" at the top. Click the Edge icon to open the associated browser workspace. (Closes #1)
- **Configurable date format** — choose between three date formats in Settings: `yyyy-MM-dd HH:mm`, `MM/dd hh:mm AM/PM`, or `dd/MM HH:mm`. The Date column auto-sizes to fit the selected format.
- **Drag-to-tab** — drag selected sessions from the grid onto a different tab header to move them, same behavior as the "Move to" context menu.
- **Spotlight rename** — "Toast mode" has been renamed to "Spotlight" throughout the UI and documentation for clarity.
- **Spotlight settings split** — Spotlight is now controlled by two independent settings: a master "Enable Spotlight" toggle and a separate "Auto-hide on deactivate" option. Enabling/disabling Spotlight applies at runtime without requiring a restart.
- **GitHub Pages dark theme** — switched the project site to the Slate theme for better dark mode readability.

### Changed

- **Date column** — now non-resizable with a "Date Created" tooltip. Width adjusts automatically based on the configured date format.
- **Session column** — uses fill mode to absorb remaining grid width, preventing horizontal overflow.

### Fixed

- **Column order restore** — saved column orders that predate the Context column are handled gracefully by inserting missing columns at their correct default position.
- **vscode.metadata.json excluded** — no longer appears in session context file listings.

## [0.16.1] - 2026-02-28

### Fixed

- **Toast mode stale grid** — when the toast window reappears (via Win+Alt+X or tray icon), the session list now renders instantly from cached data instead of showing stale/empty rows until the next 3-second refresh tick. Split the single polling timer into a background timer (data refresh, Edge tracking, bell notifications — always runs) and a visual timer (grid population — paused while hidden, restarted on show).

## [0.16.0] - 2026-02-28

### Added

- **Window Toast Mode** — the Booster window now slides up from the taskbar like a toast notification. Activated via global hotkey `Win+Alt+X` or by clicking the tray/taskbar icon. Auto-hides when focus is lost (but stays visible when interacting with dialogs, context menus, or settings). Configurable position (6 options), target screen (per-monitor with display numbers), and slide animation toggle. Enabled by default. (Closes #9)
- **Global hotkey (Win+Alt+X)** — system-wide hotkey to show/hide the Booster window from anywhere, registered via Win32 `RegisterHotKey` API.
- **Quick add tab (+) button** — a "+" tab in the session tab strip lets you create new tabs directly from the main window without opening Settings.
- **DarkTabControl** — custom `TabControl` subclass with `UserPaint` for proper dark mode rendering (no white borders or backgrounds).
- **Dark mode flicker prevention** — recursive `DoubleBuffered` on all controls, dark `BackColor` set early in constructor, `SuspendLayout`/`ResumeLayout` during tab switches.
- **Auto-select first row on tab switch** — switching session tabs now immediately selects the first row instead of showing an empty selection.

### Fixed

- **Tab remove guard** — the Remove button in Settings is now disabled when only one tab remains (previously only blocked removing the first tab by index).

## [0.15.0] - 2026-02-27

### Added

- **Draggable session grid columns** — column headers can now be reordered by dragging. Column order is persisted in settings and restored on startup. The Status column stays pinned at position 0. (Closes #6)
- **Anti-flicker grid rendering** — grid updates now use `WM_SETREDRAW` to suppress painting during bulk row operations, eliminating scroll bar flickering during refreshes.
- **Copilot CLI context menu icons** — context menu items that launch Copilot CLI sessions now display the actual `copilot.exe` shell icon instead of the CopilotBooster app icon. (Closes #7)

## [0.14.0] - 2026-02-26

### Added

- **Configurable session tabs** — replace hardcoded Active/Archived with user-defined tabs (up to 10). Manage tabs in Settings with Add, Rename, Remove, and reorder (Up/Down) buttons. Sessions can be moved between tabs via the right-click "Move to" submenu.
- **Stable default tab identity** — new `DefaultTab` setting tracks the default tab by name, surviving renames and reorder operations. Legacy `IsArchived` sessions auto-migrate to an "Archived" tab.
- **Tab reordering** — Up/Down buttons in Session Tabs settings to change tab display order.
- **Dynamic Open Files submenu** — "Open Files" is now a submenu listing all user files in the session folder (plan.md, files/ subfolder) with shell-associated icons. Top item opens the session folder in Explorer. Reserved Copilot files (events.jsonl, workspace.yaml, session.db) and folders (rewind-snapshots, checkpoints) are excluded.
- **Running-first default sort** — sessions with active processes are automatically sorted to the top. Configurable via Settings with three modes: Running first (default), Last updated, Alias/Name.
- **Test-first bug fix workflow** — added to copilot-instructions.md: all bug fixes require a failing test before the fix is applied.

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
