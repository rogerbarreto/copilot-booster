# Copilot Booster

A Windows launcher and tracker for parallel Copilot CLI agents. Each session gets isolated terminal, IDE, and browser windows tracked from a single taskbar icon.

## Language

**Session**:
A single Copilot CLI conversation, identified by a GUID `Id`, with a working directory (`Cwd`), a `Summary` (often the conversation topic), and an optional user-set `Alias`.
_Avoid_: chat, conversation, agent (when referring to the persisted entity)

**Internal Session**:
A **Session** launched by Copilot Booster itself. Its launcher PID is registered in the **PID Registry** with the `sessionId` and `copilotPid`.
_Avoid_: managed session, owned session

**External Session**:
A **Session** spawned outside Copilot Booster (e.g., user ran `copilot` directly in Warp). Discovered via `~/.copilot/logs/process-*.log` by `CopilotLogWatcherService`. Has no entry in the **PID Registry**.
_Avoid_: foreign session, untracked session, orphan session

**Copilot Host**:
The nearest ancestor process of a Copilot CLI process that owns a focusable top-level window. Examples: `WindowsTerminal.exe`, `WarpTerminal.exe`, `pwsh.exe`/`cmd.exe` running in their own ConHost, the integrated terminal of an IDE, or a multiplexer client window (`wezterm`, `alacritty`). The Host is the unit Copilot Booster can `SetForegroundWindow` against on the user's behalf.
_Avoid_: parent terminal, owning window, terminal app

**PID Registry**:
The JSON file (`pid-registry.json`) mapping launcher PID → `{sessionId, copilotPid, started}` for **Internal Sessions**. Source of truth for "which Booster instance owns which session".

**Active Status**:
The live, per-session state shown in the **Running** column (e.g., `Terminal`, `Copilot CLI`, `VS Code`, `Edge`). Computed by `ActiveStatusTracker`.

**Sidecar**:
A Booster-owned data file kept outside Copilot CLI–owned files (e.g., `aliases.json`, `session-names.json`). Used to augment a **Session** with Booster-specific data without mutating files Copilot CLI updates after creation.
_Avoid_: cache, override file, side-file

**Booster-Resolved Name**:
A heuristic display name kept in the `session-names.json` **Sidecar**, used only when `workspace.yaml.summary` is empty. Has two states: *unresolved* (`"{HostProcessName}:Copilot"`, set at discovery time when no `user.message` exists yet) and *resolved* (the first `user.message` from `events.jsonl`, whitespace-collapsed and truncated to 32 characters with `…`). A **Booster-Resolved Name** is shadowed the moment Copilot CLI writes a real `workspace.yaml.summary`.
_Avoid_: temporary name, placeholder summary, fallback name

## Relationships

- A **Session** is either **Internal** or **External** — never both
- An **Internal Session** has exactly one entry in the **PID Registry**; an **External Session** has none
- A **Session** with a live Copilot CLI process has exactly one **Copilot Host** at any moment (the host can change if the user moves the pane, e.g., out of `tmux`)
- A **Copilot Host** can host zero, one, or many Copilot CLI processes (e.g., a Windows Terminal window with multiple tabs, a multiplexer with multiple panes)
- Focusing a **Session**'s Copilot CLI = focusing its **Copilot Host**'s window. Pane-level focus inside a multiplexer is out of scope.
- A **Session**'s display name is resolved in priority order: `Alias` → `workspace.yaml.summary` → **Booster-Resolved Name** (sidecar) → cwd folder → GUID. Booster never overwrites `workspace.yaml.summary` once Copilot CLI may have populated it.

## Flagged ambiguities

- "host" was previously used informally for "the parent terminal window" — resolved: **Copilot Host** is now the canonical term, defined as a focusable top-level window owner, not just any parent process.
- "summary" historically referred to two unrelated things: (1) the `summary` field in `workspace.yaml` (Copilot CLI–authored) and (2) Booster's display name. Resolved: `summary` refers strictly to the `workspace.yaml` field; Booster's display name is the **Booster-Resolved Name** when no real summary exists yet.
