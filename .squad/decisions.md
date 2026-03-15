# Squad Decisions

## Issue #12: Async Worktree Creation with Cancellation

**Date:** 2026-03-15  
**Status:** Simplified Implementation (Revised 2026-03-15T163600Z)  
**Contributors:** Neo, Trinity, Morpheus, Tank  
**Directed by:** Roger Barreto

### Problem Statement

The `RunGit` method has a default 10-second timeout. When `git worktree add` runs against a large repository (or one requiring network operations), the timeout fires and **kills the git process mid-operation** — leaving a corrupted or partial worktree. This is the actual bug.

Three of the four creation modes (Issue, New Branch, Existing Branch) also run synchronously on the UI thread, freezing the app during creation.

### Design Philosophy (Simplified per Roger's UX Directive)

> "We can actually just show 'In Progress...' without a progress bar, but don't drop the process if it has not finished."

- **No progress bar, elapsed timer, or cancel button overlay.**
- The PR mode already shows `btnCreate.Text = "Creating..."` with the button disabled — this UX pattern is sufficient for all modes.
- The real fix is: **stop killing the git process prematurely**.

### Architecture

#### 1. `RunGitAsync` — New Async Method in `GitService.cs`

```csharp
internal static async Task<(int exitCode, string stdout, string stderr)> RunGitAsync(
    string repoPath, string arguments, CancellationToken cancellationToken = default)
```

**Key design decisions:**
- **No hard timeout.** The method waits for the process to complete naturally. Callers can pass a `CancellationToken` with a timeout if desired, but the default is indefinite wait.
- **Concurrent stdout/stderr reading.** Both streams are read with `ReadToEndAsync()` concurrently to prevent the well-known .NET deadlock when a child process fills one stream buffer while the parent blocks reading the other.
- **Process tree kill on cancellation.** If the token is cancelled, call `process.Kill(entireProcessTree: true)` wrapped in try/catch.
- **Returns the same tuple shape** as `RunGit` for easy adoption.

#### 2. Existing `RunGit` — Unchanged

The synchronous `RunGit` method stays as-is. It's well-tested, used by dozens of callers for fast operations, and its 10s timeout is appropriate for those use cases.

#### 3. Async Worktree Methods in `GitService.cs`

Add async overloads for the three worktree-creation methods:

```csharp
internal static async Task<(bool success, string error)> CreateWorktreeAsync(...)
internal static async Task<(bool success, string error)> CheckoutExistingBranchWorktreeAsync(...)
internal static async Task<(bool success, string error)> CheckoutLocalBranchWorktreeAsync(...)
```

Each calls `RunGitAsync` instead of `RunGit` with the same arguments. Sync versions remain for backward compatibility.

#### 4. Async Methods in `WorkspaceCreationService.cs`

Add async overloads mirroring the existing sync methods:

```csharp
internal static async Task<(string path, bool success, string? error)> CreateWorkspaceAsync(...)
internal static async Task<(string path, bool success, string? error)> CreateWorkspaceFromExistingBranchAsync(...)
internal static async Task<(string path, bool success, string? error)> CreateWorkspaceFromPrAsync(...)
```

These call the new `*Async` GitService methods. `FetchPrRef` gets a new `FetchPrRefAsync` using `RunGitAsync` with no timeout.

#### 5. UI Changes in `WorkspaceCreatorVisuals.cs`

**All 4 modes use the same pattern the PR mode already uses:**

```csharp
btnCreate.Enabled = false;
btnCreate.Text = "Creating...";

var (worktreePath, success, error) = await WorkspaceCreationService.CreateWorkspaceAsync(...)
    .ConfigureAwait(true);
```

**FormClosing guard (Niobe's correction #2):**

Instead of disabling `ControlBox` during creation:

```csharp
bool isCreating = false;

form.FormClosing += (s, e) =>
{
    if (isCreating && e.CloseReason == CloseReason.UserClosing)
    {
        e.Cancel = true;  // Prevent close while operation is in progress
    }
};
```

Set `isCreating = true` before the await, `false` after. This preserves minimize/maximize and form icon while blocking closure.

#### 6. Cleanup Fallback (Niobe's correction #3)

When a worktree creation fails or is cancelled:

```csharp
RunGit(repoPath, $"worktree remove --force \"{worktreePath}\"");
RunGit(repoPath, "worktree prune");  // Fallback: clean stale worktree entries
if (Directory.Exists(worktreePath))
{
    Directory.Delete(worktreePath, recursive: true);
}
```

The `worktree prune` ensures git's internal worktree list stays clean even if the `worktree remove` partially fails.

#### 7. String Renames — "Workspace" → "Worktree"

10 user-facing string changes (internal code identifiers unchanged):

| # | File | Current | New |
|---|------|---------|-----|
| 1 | `WorkspaceCreatorVisuals.cs:58` | `"Create New Workspace"` | `"Create New Worktree"` |
| 2 | `WorkspaceCreatorVisuals.cs:79` | `"Set up a new isolated workspace..."` | `"Set up a new isolated worktree..."` |
| 3 | `WorkspaceCreatorVisuals.cs:197` | `"...name for your workspace..."` | `"...name for your worktree..."` |
| 4 | `WorkspaceCreatorVisuals.cs:246` | `"...create the workspace from"` | `"...create the worktree from"` |
| 5 | `WorkspaceCreatorVisuals.cs:1156` | `"Failed to create workspace:\n..."` | `"Failed to create worktree:\n..."` |
| 6 | `WorkspaceCreatorVisuals.cs:1199` | `"Failed to create workspace:\n..."` | `"Failed to create worktree:\n..."` |
| 7 | `WorkspaceCreatorVisuals.cs:1224` | `"Failed to create workspace:\n..."` | `"Failed to create worktree:\n..."` |
| 8 | `WorkspaceCreatorVisuals.cs:1242` | `"Failed to create workspace:\n..."` | `"Failed to create worktree:\n..."` |
| 9 | `SettingsForm.cs:277` | `"Workspaces Dir:"` | `"Worktrees Dir:"` |
| 10 | `SettingsForm.cs:713` | `"...session's Edge workspace..."` | `"...session's Edge worktree..."` |

### Implementation Phases

1. **Phase 1 (GitService):** `RunGitAsync`, async worktree methods, unit tests
2. **Phase 2 (WorkspaceCreationService):** Async service method overloads, unit tests
3. **Phase 3 (UI):** All 4 modes async, FormClosing guard
4. **Phase 4 (Naming):** 10 string renames (parallel or after Phases 1–3)

### Status — Phases 1–4 Complete

✅ **Trinity (Phase 1+2):** All async methods added, concurrent stream reading implemented, cleanup fallback with `worktree prune`  
✅ **Morpheus (Phase 4):** All 10 strings renamed  
✅ **Tank:** 5 anticipatory async unit tests written, all 497 pass  
✅ **Neo:** Simplified architecture proposal documented  

### Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| Partial worktree on error | Cleanup: `worktree remove --force` → `worktree prune` → `Directory.Delete` |
| UI thread deadlock | All await calls use `.ConfigureAwait(true)` (WinForms SynchronizationContext) |
| User closes dialog during creation | `FormClosing` event cancellation prevents close while `isCreating` is true |
| Git process hangs forever | Acceptable tradeoff — user can always close the app. Future: add optional soft timeout with user prompt |
| Process zombie on error | `process.Kill(entireProcessTree: true)` in catch, wrapped in try/catch |

### Testing Strategy

- **Unit tests:** Test `RunGitAsync` directly via `InternalsVisibleTo` — verify correct exit codes, stdout, stderr
- **Cancellation test:** Cancel token immediately, expect `OperationCanceledException`
- **Integration tests:** Actual git commands in temp directories for async variants
- **Regression:** Existing sync `RunGit` tests remain untouched — sync path unchanged
- **Manual:** Test all 4 creation modes on a large repo to confirm no UI freezing

### Open Questions (Deferred)

1. Should we add a soft timeout (e.g., 5 minutes) that shows a "Still working..." message instead of killing the process? (Future enhancement)
2. Future: Propagate `CancellationToken` deeper into `RunGit` to kill hung git processes? (Separate issue)

---

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
