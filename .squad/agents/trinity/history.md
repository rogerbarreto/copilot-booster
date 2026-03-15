# Trinity — History

## Core Context

- **Project:** A C# WinForms (.NET 10) desktop application that enhances GitHub Copilot workflow with session management, IDE integration, and GitHub tracking.
- **Role:** Services Dev
- **Joined:** 2026-03-15T15:50:59.673Z

## Learnings

<!-- Append learnings below -->
- **2026-03-15 — Issue #12 service design:** Designed async layer for git worktree operations. Key decisions: `CancellationToken`-only (no `timeoutMs` param — caller owns policy via `CancellationTokenSource(TimeSpan)`), `IProgress<string>` for stderr streaming, `GitResult` record struct over raw tuples, `RunGitAsync` promoted to `internal static` for testability. Sync methods kept unchanged — async overloads are additive. `GitService` being all-static with local-only state is safe for async — no thread-safety concerns. `WorkspaceCreationService` async methods keep sync helpers for fast metadata reads, only the worktree/fetch calls go async.
- **Process model:** `RunGit` (line 502) creates a new `Process` per call — fully isolated. `WaitForExitAsync` available since .NET 5+. Cancellation callback registers `Kill(entireProcessTree: true)` wrapped in try/catch for race conditions.
- **UI integration note:** `WorkspaceCreatorVisuals.cs` currently wraps sync calls in `Task.Run()` (line 1129). Phase 2 will replace this with direct `await` on async overloads — Morpheus owns that work.
- **Callers with custom timeouts:** `ValidatePrRef` (30s, line 434) and `FetchPrRef` (60s, line 450) already override the default. The three worktree callers (lines 110, 124, 134) never did — that's the root cause.
- **2026-03-15 — Phase 1+2 implementation:** Implemented `RunGitAsync` (internal static, tuple return, no hard timeout, concurrent stream reads, cancellation callback kills process tree before awaiting). Added 4 async wrappers: `CreateWorktreeAsync`, `CheckoutExistingBranchWorktreeAsync`, `CheckoutLocalBranchWorktreeAsync`, `FetchPrRefAsync`. Added 3 async overloads in `WorkspaceCreationService`: `CreateWorkspaceAsync`, `CreateWorkspaceFromExistingBranchAsync`, `CreateWorkspaceFromPrAsync`. All sync methods untouched. Fixed pre-existing tests that assumed `GitResult` record — corrected to tuple member names and `ThrowsAnyAsync` for `TaskCanceledException` (subclass of `OperationCanceledException` thrown by `ReadToEndAsync`).
- **Key implementation detail:** `ReadToEndAsync(CancellationToken)` in .NET 10 throws `TaskCanceledException` (not `OperationCanceledException` directly). Tests must use `ThrowsAnyAsync<OperationCanceledException>` for correct assertion.
