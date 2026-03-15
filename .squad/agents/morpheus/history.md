# Morpheus — History

## Core Context

- **Project:** A C# WinForms (.NET 10) desktop application that enhances GitHub Copilot workflow with session management, IDE integration, and GitHub tracking.
- **Role:** UI Dev
- **Joined:** 2026-03-15T15:50:59.675Z

## Learnings

<!-- Append learnings below -->

### 2026-03-15 — Issue #12 UI Design (Worktree Creation Dialog)

- **WorkspaceCreatorVisuals.cs** is a 1252-line static class that builds the entire worktree creation dialog programmatically in `ShowWorkspaceCreator()`. No Designer file — all layout is manual coordinate-based.
- The form uses `FormBorderStyle.FixedDialog` with dynamic height changes via `RelayoutControls()`. Four radio-button modes (Existing Branch, New Branch, PR, Issue) each rearrange controls and resize the form.
- Dark mode detection uses `Application.IsDarkModeEnabled` throughout all Forms. Standard pattern: ternary for colors, e.g., `Application.IsDarkModeEnabled ? Color.FromArgb(0x11, 0x11, 0x11) : SystemColors.Window`.
- Only PR mode uses `await Task.Run()` for async creation. Issue, New Branch, and Existing Branch modes call `WorkspaceCreationService` synchronously, blocking the UI thread.
- `GitService.RunGit` has its own timeout (default 10s, 60s for fetch). Returns `"Git command timed out."` on timeout. No `CancellationToken` support yet.
- WinForms ProgressBar doesn't support dark theming natively — it uses the system accent color. Acceptable for this project.
- Found **10 user-facing strings** to rename (not 9 as originally estimated) — the four error messages at lines 1156, 1199, 1224, 1242 were undercounted.
- Settings form has `"Workspaces Dir:"` which refers to the directory setting, not the creation feature — should NOT be renamed.
