# Squad Decisions

## Issue #12: Async Worktree Creation with Cancellation

**Date:** 2026-03-15  
**Status:** Design Consensus  
**Contributors:** Neo, Trinity, Morpheus  

### Architecture

- Add `RunGitAsync(repoPath, arguments, stderrProgress, cancellationToken)` as internal static method in `GitService.cs`
- Default timeout: **120 seconds** (generous for large repos; existing `RunGit` stays at 10s for fast ops)
- Process cancellation via `process.Kill(entireProcessTree: true)` on `CancellationToken` signal
- Post-cancellation cleanup: sync `git worktree remove --force` then delete leftover directory
- **Backward compatibility:** All existing sync methods (`RunGit`, `CreateWorktree`, etc.) remain untouched

### Service Layer

- Introduce `GitResult` record: `readonly record struct GitResult(int ExitCode, string Stdout, string Stderr)`
- Add async overloads to `GitService`: `CreateWorktreeAsync`, `CheckoutExistingBranchWorktreeAsync`, `CheckoutLocalBranchWorktreeAsync`, `FetchPrRefAsync`
- `IProgress<string>` parameter for stderr line-by-line reporting (live progress without buffering)
- Add async overloads to `WorkspaceCreationService`: mirror sync versions, call async `GitService` methods
- Error handling: Let `OperationCanceledException` propagate; callers use linked CTS to distinguish timeout from user cancel

### UI & UX

- **Progress Panel:** Overlay (marquee bar, elapsed timer label, cancel button) shown during creation
- **Elapsed Timer:** `System.Windows.Forms.Timer` fires every 1s, displays `M:SS` format
- **All 4 Creation Modes Async:** PR (already async) + Issue, New Branch, Existing Branch wrapped in `await Task.Run()`
- **Cancellation UX:** New `btnCancelCreation` triggers `CancellationTokenSource.Cancel()`. Original `btnCancel` (Escape) disabled during creation
- **String Renames:** 10 user-facing strings "Workspace" → "Worktree" (dialog title, hints, error messages); internal code identifiers unchanged

### Implementation Phases

1. **Phase 1 (GitService):** `RunGitAsync`, `GitResult`, async worktree methods, unit tests
2. **Phase 2 (WorkspaceCreationService):** Async service method overloads, unit tests
3. **Phase 3 (UI):** Progress panel, all 4 modes async, cancel button wiring, elapsed timer
4. **Phase 4 (Naming):** 10 string renames (can be parallel with 1-3 or after)

### Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| Partial worktree on cancel | Sync cleanup: `git worktree remove --force`, then delete directory |
| UI thread deadlock | All await calls use `.ConfigureAwait(true)` (WinForms pattern) |
| Race condition in cancel | Check `cancellationToken.IsCancellationRequested` after await |
| Process zombie on kill | Wrap `Kill(true)` in try/catch; log and continue worst-case |
| 120s still too short | Error message tells user what happened; they retry or check network |

### Testing

- Test `RunGitAsync` directly via `InternalsVisibleTo` (change visibility from private to internal)
- Integration tests: run actual git commands in temp directories (async variants of existing sync tests)
- Cancellation test: cancel token immediately before call, expect `OperationCanceledException`
- Sync `RunGit` left unchanged — well-tested indirectly through dozens of callers

### Open Questions

1. Should the elapsed timer show milliseconds after 1 minute? (Proposed: `M:SS` format)
2. Should we show the git command being executed in the progress panel? (Deferred — can revisit)
3. Future: Propagate `CancellationToken` deeper into `RunGit` to kill hung git processes? (Separate issue)

---

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
