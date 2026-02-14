# Copilot App

> A Windows taskbar companion for GitHub Copilot CLI — manage sessions, terminals, IDEs, and browser workspaces from a single pinned icon.

**Copilot App** turns [GitHub Copilot CLI](https://docs.github.com/en/copilot/github-copilot-in-the-cli) into a first-class desktop experience. Pin it to your taskbar and get instant access to new sessions, session history, active context tracking across terminals, Copilot CLI, IDEs, and Edge browser — all without touching config files.

<p align="center">
  <a href="../../releases/latest"><img src="https://img.shields.io/github/v/release/rogerbarreto/copilot-app?label=Latest%20Release&style=for-the-badge" alt="Latest Release"></a>
</p>

<p align="center">
  <a href="../../releases/latest/download/CopilotApp-Setup.exe">📦 <b>Download Installer</b></a> &nbsp;|&nbsp;
  <a href="../../releases/latest/download/CopilotApp-win-x64.zip">📁 <b>Download Portable ZIP</b></a>
</p>

---

## ✨ Features at a Glance

### 📌 Taskbar Jump List

Right-click the pinned icon to access everything:

<p align="center">
  <img src="images/menu-tasks.png" alt="Jump list with tasks" width="300">
</p>

- **New Copilot Session** — start a new session with a smart directory picker
- **Existing Sessions** — browse, resume, or open sessions in your IDE
- **Settings** — configure tools, directories, and IDEs

---

### 🔄 Session Browser & Active Context Tracking

The Existing Sessions tab is the central hub. Each session shows four columns — **Session**, **CWD** (with ⎇ for Git repos), **Date**, and **Active** — giving you a live view of what's running where.

<p align="center">
  <img src="images/existing-sessions-active-tracking.png" alt="Existing sessions with Terminal and Copilot CLI active tracking" width="700">
</p>

The **Active** column tracks running contexts across multiple environments:

| Context | How it's detected |
|---------|-------------------|
| **Terminal** | Windows launched via "Open Terminal" are tracked by PID and cached across restarts |
| **Copilot CLI** | Open terminal windows are scanned by matching session summaries in window titles |
| **IDE** | IDEs launched via the Open menu are tracked by process ID |
| **Edge** | Browser workspaces are tracked via UI Automation anchor-tab detection |

Each active context is a **clickable link** — click to focus the corresponding window instantly.

Other session browser features:
- **Search** — filter sessions by title, folder, or metadata as you type
- **Terminal persistence** — active terminals survive app restarts
- **Auto-refresh** — the list updates when new sessions appear or names change externally
- **Auto-cleanup** — empty sessions with no activity are automatically removed
- **Loading indicator** — shows "Loading sessions..." while session data is being fetched

---

### 📋 Right-Click Context Menu

Right-click any session row to access all actions in a single context menu:

<p align="center">
  <img src="images/context-menu-edit.png" alt="Right-click context menu with all session actions" width="300">
</p>

| Action | Description |
|--------|-------------|
| **Open Session** | Resume the session in its original working directory |
| **Edit Session** | Rename the session or change its working directory |
| **Open as New Copilot Session** | Start a fresh Copilot CLI session in the same directory |
| **Open as New Copilot Session Workspace** | Create a Git worktree workspace (Git repos only) |
| **Open Terminal** | Launch a standalone terminal in the session's directory |
| **Open in {IDE} (CWD)** | Open the working directory in your configured IDE |
| **Open in {IDE} (Repo Root)** | Open the Git repository root in your IDE |
| **Open in Edge** | Launch a managed Edge browser workspace |

IDE entries are added dynamically based on your configured IDEs in Settings.

---

### 🌐 Edge Browser Workspaces

Open a managed Microsoft Edge window linked to any session. Each workspace gets a unique anchor tab that lets Copilot App track, focus, and detect whether the browser window is still open.

- **Active tracking** — the Edge workspace appears as a clickable link in the Active column; click to focus the window
- **Tab-level detection** — uses UI Automation to find the anchor tab across all Edge windows, even when another tab is active
- **Auto-cleanup** — when you close the anchor tab or the Edge window, the workspace is automatically removed from tracking
- **Re-discovery** — if you restart Copilot App while an Edge workspace is still open, it will be re-detected on the next refresh

---

### 📂 Smart Directory Picker

The **New Session** tab shows your most-used working directories — sorted by frequency across all previous sessions. Non-existent paths are automatically cleaned up.

<p align="center">
  <img src="images/new-session-tab.png" alt="New Session tab with directory picker" width="700">
</p>

Each directory shows:
- **# Sessions created** — how many sessions have used this path
- **Git** — whether the directory is inside a Git repository (including worktrees)

Click **Start** to launch a session, **Browse...** to pick any folder, or **Create Workspace** to create an isolated Git worktree.

---

### 🌿 Git Workspace Creation

For Git-enabled directories, Copilot App can create isolated workspaces backed by [git worktrees](https://git-scm.com/docs/git-worktree). Each workspace gets its own branch and directory — perfect for working on multiple features in parallel without stashing or switching branches.

Create a workspace from two places:
- **New Session tab** → select a Git directory → click **Create Workspace**
- **Existing Sessions tab** → **Open ▾** → **Open as New Copilot Session Workspace**

Workspaces are stored in `%APPDATA%\CopilotApp\Workspaces\` and named after the repository and branch (e.g., `myrepo-feature-xyz`).

---

### ⚙️ Settings

All configuration lives in a tabbed UI — no JSON editing required.

<p align="center">
  <img src="images/settings-tab.png" alt="Settings with Allowed Tools, Directories, and IDEs" width="700">
</p>

- **Allowed Tools** — whitelist shell commands and MCP tools that Copilot can use without prompting
- **Allowed Directories** — grant Copilot access to specific directories
- **IDEs** — register your IDEs for the Open menu (e.g., Visual Studio, VS Code, Rider)
- **Default Work Dir** — set the default working directory for new sessions

---

### 🔄 In-App Updates

Copilot App checks for new versions on startup via the GitHub Releases API. When an update is available, a banner appears at the bottom of the window — click to download and install the latest version automatically.

---

## 🚀 Getting Started

### Prerequisites

- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (or SDK for building from source)
- [GitHub Copilot CLI](https://docs.github.com/en/copilot/github-copilot-in-the-cli) — install via `winget install GitHub.Copilot` or `GitHub.Copilot.Prerelease`

### Install

#### Option A: Installer (Recommended)

Download **`CopilotApp-Setup.exe`** from the [latest release](../../releases/latest) and run it.

- Installs to `%APPDATA%\CopilotApp\` — no admin required
- Creates Start Menu and optional desktop shortcuts
- Includes uninstaller (Add/Remove Programs)

#### Option B: Portable EXE

Download **`CopilotApp-win-x64.zip`** from the [latest release](../../releases/latest), extract it anywhere, and run `CopilotApp.exe`.

#### Option C: Build from Source

```powershell
git clone <repo-url> copilot-app
cd copilot-app
.\install.ps1
```

### Pin to Taskbar

1. Run `CopilotApp.exe` from the publish folder
2. Right-click the icon in the taskbar → **Pin to taskbar**
3. Right-click the pinned icon → **Settings** to configure your tools and directories

### Manual Build

```powershell
cd src
dotnet publish -c Release -o ..\publish
```

### Build Installer (requires [Inno Setup](https://jrsoftware.org/isdownload.php))

```powershell
dotnet publish src/CopilotApp.csproj -c Release -o publish
iscc installer.iss
# Output: installer-output\CopilotApp-Setup.exe
```

---

## 💻 Command Line

```powershell
CopilotApp.exe                        # New session (shows CWD picker)
CopilotApp.exe "C:\my\project"        # New session in a specific directory
CopilotApp.exe --resume <sessionId>   # Resume a session in its original CWD
CopilotApp.exe --open-existing        # Open the session browser
CopilotApp.exe --open-ide <sessionId> # Open IDE picker for a session
CopilotApp.exe --settings             # Open settings
```

---

## 🏗️ Architecture

```
CopilotApp.exe (WinForms, persistent taskbar window)
├── Sets AppUserModelID for taskbar grouping
├── Registers PID → session mapping in ~/.copilot/active-pids.json
├── Launches copilot.exe with --allow-tool and --add-dir from settings
├── Detects new session via directory snapshot (before/after launch)
├── Active context tracking (Terminal, Copilot CLI, IDE, Edge)
│   ├── PID registry + process scanning for terminals
│   ├── Window title matching for Copilot CLI detection
│   ├── Process tracking for IDE instances
│   └── UI Automation for Edge anchor-tab detection
├── Terminal cache persistence across restarts
├── Updates jump list on launch + every 5 min (background, coordinated)
└── Cleans up on exit (unregisters PID, refreshes jump list)
```

### Key Services

| Service | Purpose |
|---------|---------|
| `ActiveStatusTracker` | Aggregates active status across all context types |
| `SessionDataService` | Unified session loading with Git detection caching |
| `EdgeWorkspaceService` | Edge browser workspace lifecycle and UI Automation |
| `TerminalCacheService` | Persists terminal sessions across app restarts |
| `WindowFocusService` | HWND-based window focusing with P/Invoke |
| `SessionService` | Session CRUD, search, and Git root detection |
| `CopilotLocator` | Finds the Copilot CLI executable |

### Files

| Path | Purpose |
|------|---------|
| `~/.copilot/launcher-settings.json` | Tools, directories, IDEs, default work dir |
| `~/.copilot/active-pids.json` | PID → session ID mapping |
| `~/.copilot/terminal-cache.json` | Cached terminal sessions for restart persistence |
| `~/.copilot/jumplist-lastupdate.txt` | Update coordination timestamp |
| `~/.copilot/launcher.log` | Debug log |
| `~/.copilot/session-state/` | Session metadata (managed by Copilot CLI) |

---

## 📄 License

MIT
