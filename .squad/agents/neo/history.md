# Neo — History

## Core Context

- **Project:** A C# WinForms (.NET 10) desktop application that enhances GitHub Copilot workflow with session management, IDE integration, and GitHub tracking.
- **Role:** Lead
- **Joined:** 2026-03-15T15:50:59.672Z

## Learnings

<!-- Append learnings below -->

- **2026-05-10 — Team update: WarpPaneFocuser landed (Trinity) and title-probe-tab-focus skill available:** Trinity shipped R2 probe-and-match Warp terminal pane focus (7 new service files, 10 constructor overloads on ActiveStatusTracker for backward compat). Tank shipped 12 unit tests + 3 LocalOnly live integration tests, all green. Squad verified against Roger's live Warp Hi 1 / Hi 2. Strategy: deterministic Ctrl+Tab cycling with title match detection, 30 iteration cap, 150ms settle delay. Skill documented at `.squad/skills/title-probe-tab-focus/SKILL.md` for future terminal work. No UI work needed in this iteration.
- **2026-05-03 — All-green integration directive (leadership note):** The grilling exposed that accepting environmental baseline failures as "normal" was a process smell. The team was building ceremony (baseline-comparison scripts, TROUBLESHOOTING docs) around test-output noise that shouldn't exist in the first place. User directive: binary green only — either self-bootstrap test environment (fixture installs) or skip explicitly (traits). This is simpler, more honest, and matches the user's actual release policy: "all tests must pass". Supersedes prior tolerance decisions.
### 2026-03-15 — Issue #12 Simplified Architecture

- **Roger's simplification principle:** When the real bug is a timeout killing a process, fix the timeout — don't build an elaborate progress UI around it. The existing "Creating..." button UX is already good enough.
- **Codebase insight:** `RunGit` has a 10s default timeout that kills the process; only `FetchPrRef` (60s) and `ValidatePrRef` (30s) override it. The three worktree creation methods (`CreateWorktree`, `CheckoutExistingBranchWorktree`, `CheckoutLocalBranchWorktree`) all use the 10s default, which is the root cause of issue #12.
- **PR mode is the template:** PR creation already uses `await Task.Run()` + `btnCreate.Text = "Creating..."` + disabled button. The other 3 modes just need to follow this same pattern with proper async service methods.
- **Niobe's three corrections are validated and incorporated:** (1) concurrent stdout/stderr reading already exists in `RunGit` and must be preserved in `RunGitAsync`, (2) `FormClosing` event cancellation is better than `ControlBox = false`, (3) `git worktree prune` as cleanup fallback.
- **Deadlock risk in .NET Process:** When redirecting both stdout and stderr, you must read both concurrently (e.g., `ReadToEndAsync` on both before `WaitForExit`). Reading one synchronously while the other buffer fills causes deadlock. This is a well-known .NET pitfall.
- **Decision:** Wrote simplified proposal to `.squad/decisions/inbox/neo-issue12-simplified.md` — removes progress bar, elapsed timer, cancel button, overlay panel, `GitResult` record, and `IProgress<string>`. Keeps `RunGitAsync` with no hard timeout + all 4 modes async + 10 string renames.
