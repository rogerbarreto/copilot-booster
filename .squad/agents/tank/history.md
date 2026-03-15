# Tank — History

## Core Context

- **Project:** A C# WinForms (.NET 10) desktop application that enhances GitHub Copilot workflow with session management, IDE integration, and GitHub tracking.
- **Role:** Tester
- **Joined:** 2026-03-15T15:50:59.676Z

## Learnings

<!-- Append learnings below -->

- **Async test pattern:** xUnit v3 supports `[Fact] public async Task` natively — no special runner needed. Use `.ConfigureAwait(false)` in tests since they don't run on a UI thread.
- **ImplicitUsings covers Tasks/Threading:** The test project has `<ImplicitUsings>enable</ImplicitUsings>`, so `System.Threading.Tasks` and `System.Threading` are available without explicit imports.
- **GitResult record struct:** Agreed design uses `readonly record struct GitResult(int ExitCode, string Stdout, string Stderr)` — tests reference `.ExitCode`, `.Stdout`, `.Stderr` properties.
- **RunGitAsync signature:** `RunGitAsync(string repoPath, string arguments, IProgress<string>? stderrProgress = null, CancellationToken cancellationToken = default)` — named parameter `cancellationToken:` needed when skipping `stderrProgress`.
- **CreateWorktreeAsync mirrors sync:** Returns `Task<(bool success, string error)>` matching the existing `CreateWorktree` return type.
- **Worktree branch-already-checked-out test:** Passing `"main"` as both branchName and baseBranch triggers git's "already checked out" error — reliable way to test the failure path.
- **Existing test helper `InitBareGitRepo()`:** Creates a real git repo with one commit on `main` in a random temp subdirectory. Reuse for all git integration tests.
- **Tests are proactive (pre-implementation):** These 5 async tests won't compile until Trinity lands `RunGitAsync`, `GitResult`, and `CreateWorktreeAsync` in `GitService.cs`. They're ready to validate the implementation once it arrives.
