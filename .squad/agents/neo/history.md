# Neo — History

## Core Context

- **Project:** A C# WinForms (.NET 10) desktop application that enhances GitHub Copilot workflow with session management, IDE integration, and GitHub tracking.
- **Role:** Lead
- **Joined:** 2026-03-15T15:50:59.672Z

## Learnings

<!-- Append learnings below -->

- **2026-05-09 — Corruption error triage (CLI-144):** The error "Session file is corrupted (line 1: id: Required)" is **NOT** from booster code — it's from copilot.exe stderr when resuming a session without workspace.yaml. Roger's audit: 30 of 171 sessions (17.5%) have events.jsonl but no workspace.yaml. These are legitimate sessions created by copilot.exe that CopilotLogWatcherService failed to backfill (likely pre-v0.14.0 sessions). Code audit confirms all booster writers always include `id:` on line 1; no corruption is being written. Modern copilot.exe (0.0.410+) auto-creates workspace.yaml on resume with all required fields. Fix: backfill missing workspace.yaml on startup + before AI detection via new `CopilotLogWatcherService.BackfillMissingWorkspaceYaml` method. Trinity owns implementation; Tank owns tests; ships in v0.22.1.
- **2026-05-03 — All-green integration directive (leadership note):** The grilling exposed that accepting environmental baseline failures as "normal" was a process smell. The team was building ceremony (baseline-comparison scripts, TROUBLESHOOTING docs) around test-output noise that shouldn't exist in the first place. User directive: binary green only — either self-bootstrap test environment (fixture installs) or skip explicitly (traits). This is simpler, more honest, and matches the user's actual release policy: "all tests must pass". Supersedes prior tolerance decisions.
### 2026-03-15 — Issue #12 Simplified Architecture

- **Roger's simplification principle:** When the real bug is a timeout killing a process, fix the timeout — don't build an elaborate progress UI around it. The existing "Creating..." button UX is already good enough.
- **Codebase insight:** `RunGit` has a 10s default timeout that kills the process; only `FetchPrRef` (60s) and `ValidatePrRef` (30s) override it. The three worktree creation methods (`CreateWorktree`, `CheckoutExistingBranchWorktree`, `CheckoutLocalBranchWorktree`) all use the 10s default, which is the root cause of issue #12.
- **PR mode is the template:** PR creation already uses `await Task.Run()` + `btnCreate.Text = "Creating..."` + disabled button. The other 3 modes just need to follow this same pattern with proper async service methods.
- **Niobe's three corrections are validated and incorporated:** (1) concurrent stdout/stderr reading already exists in `RunGit` and must be preserved in `RunGitAsync`, (2) `FormClosing` event cancellation is better than `ControlBox = false`, (3) `git worktree prune` as cleanup fallback.
- **Deadlock risk in .NET Process:** When redirecting both stdout and stderr, you must read both concurrently (e.g., `ReadToEndAsync` on both before `WaitForExit`). Reading one synchronously while the other buffer fills causes deadlock. This is a well-known .NET pitfall.
- **Decision:** Wrote simplified proposal to `.squad/decisions/inbox/neo-issue12-simplified.md` — removes progress bar, elapsed timer, cancel button, overlay panel, `GitResult` record, and `IProgress<string>`. Keeps `RunGitAsync` with no hard timeout + all 4 modes async + 10 string renames.
