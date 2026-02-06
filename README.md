# Copilot App

A Windows taskbar launcher for [GitHub Copilot CLI](https://github.com/github/copilot-cli) that provides:

- **Taskbar pinning** with the Copilot icon and jump list
- **Settings UI** to configure allowed tools, directories, and IDEs
- **CWD picker** for new sessions — shows previously-used directories sorted by frequency
- **Session browser** to resume any previous named session
- **IDE integration** — open a session's working directory or git repo root in your configured IDE
- **Active sessions** in the jump list with resume support
- **Background jump list updates** with multi-instance coordination

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- [GitHub Copilot CLI](https://docs.github.com/en/copilot/github-copilot-in-the-cli) installed via `winget install GitHub.Copilot` or `GitHub.Copilot.Prerelease`

## Quick Start

```powershell
# 1. Clone and build
git clone <repo-url> copilot-app
cd copilot-app
.\install.ps1

# 2. Pin to taskbar
#    Launch CopilotApp.exe, right-click its taskbar icon → "Pin to taskbar"

# 3. Configure
#    Right-click pinned icon → Settings
```

## Manual Setup

### Build

```powershell
cd src
dotnet publish -c Release -o ..\publish
```

## Configuration

All settings are managed via the **Settings UI** (right-click pinned icon → Settings, or run `CopilotApp.exe --settings`).

Settings are stored in `~/.copilot/launcher-settings.json`:

| Tab | Description |
|-----|-------------|
| **Allowed Tools** | Shell commands and tools Copilot can use without prompting |
| **Allowed Directories** | Directories Copilot can access (passed as `--add-dir`) |
| **IDEs** | IDE executables for "Open in IDE" feature (description + path) |
| **Default Work Dir** | Starting directory for the folder browser |

### Example Tools

On first run, settings are empty. Example tools you may want to add:

| Tool | Description |
|------|-------------|
| `shell(dotnet:*)` | .NET CLI commands |
| `shell(git diff:*)` | Git diff commands |
| `shell(git log:*)` | Git log commands |
| `shell(Set-Location:*)` | Change directory |
| `mcp__github-mcp-server` | GitHub MCP server tools |

### Example Directories

| Directory | Description |
|-----------|-------------|
| `D:\repo` | Your repository root |
| `~\.copilot` | Copilot session state |

## Usage

### Jump List (right-click pinned icon)

| Action | Description |
|--------|-------------|
| **New Copilot Session** | Shows CWD picker with previously-used directories, then launches Copilot |
| **Existing Sessions** | Browse and resume named sessions, or open their folder in an IDE |
| **Settings** | Configure allowed tools, directories, IDEs, and default work dir |
| **Active Sessions** | Click to resume a running session |

### CWD Picker (New Session)

When starting a new session, a directory picker shows:
- Previously-used directories sorted by most-used across all sessions
- Non-existent directories are automatically filtered out
- **Browse...** button to pick any folder (starts at default work dir)

### Existing Sessions

The session browser shows all named sessions with:
- Session name, full CWD path, and last-used date
- **Open Session** — resumes the session in its original CWD
- **Open in IDE** — opens the session's CWD or git repo root in a configured IDE

### IDE Integration

When clicking "Open in IDE", a compact picker shows each configured IDE with:
- **Open CWD** — opens the session's working directory
- **Open Repo** — opens the git repository root (only shown when different from CWD)

### Command Line

```powershell
CopilotApp.exe                        # New session with CWD picker
CopilotApp.exe "C:\my\project"        # New session in specified directory
CopilotApp.exe --resume <sessionId>   # Resume a session (uses its original CWD)
CopilotApp.exe --open-existing        # Show session browser
CopilotApp.exe --open-ide <sessionId> # Open IDE picker for a session
CopilotApp.exe --settings             # Open settings dialog
```

## Architecture

```
CopilotApp.exe (WinForms, hidden window)
├── Sets AppUserModelID for taskbar grouping
├── Registers PID in ~/.copilot/active-pids.json
├── Launches: copilot.exe --allow-tool=... --add-dir=... [--resume id]
├── WorkingDirectory set to chosen CWD (new) or session's original CWD (resume)
├── Detects new session folder via directory snapshot
├── Updates jump list immediately + every 5min (background)
└── Cleans up on exit (unregisters PID, updates jump list)
```

### Files

| Path | Purpose |
|------|---------|
| `~/.copilot/launcher-settings.json` | Allowed tools, dirs, IDEs, default work dir |
| `~/.copilot/active-pids.json` | PID → session ID registry |
| `~/.copilot/jumplist-lastupdate.txt` | Timestamp for update coordination |
| `~/.copilot/launcher.log` | Debug log |
| `~/.copilot/session-state/<id>/workspace.yaml` | Session metadata (managed by Copilot CLI) |

## License

MIT
