# Niobe — Researcher History

## Project Context

- **Project:** copilot-booster — A WinForms (.NET 10) desktop app that enhances GitHub Copilot CLI
- **Stack:** C# / .NET 10 / WinForms / nullable enabled / xUnit v3
- **User:** Roger Barreto
- **Key services:** GitService, WorkspaceCreationService, SessionService, EdgeWorkspaceService
- **Key forms:** MainForm, WorkspaceCreatorVisuals, ExistingSessionsVisuals, NewSessionVisuals

## Learnings

- **2026-05-03 — All-green integration test directive (validation impact):** User grilling established binary all-green policy: no tolerance for environmental baseline failures. Tests must self-bootstrap (e.g., Playwright auto-install in collection fixture) or skip explicitly with traits. This is a standing release policy enforcement — validates that team release quality is not compromised by accepting noise baseline.

### Issue #12 Validation (2026-03-15)

**APIs validated (all confirmed):**
- `Process.WaitForExitAsync(CancellationToken)` — .NET 5+
- `Process.Kill(bool entireProcessTree)` — .NET Core 3.0+
- `Progress<T>` SynchronizationContext auto-marshaling — confirmed
- `CancellationTokenSource.CreateLinkedTokenSource` — .NET Framework 4.0+
- `readonly record struct` — C# 10 (.NET 6+)
- `Application.IsDarkModeEnabled` — .NET 10 (Windows 11+ only)
- `Application.SetColorMode(SystemColorMode)` — .NET 9 experimental, .NET 10 stable
- `ProgressBar.MarqueeAnimationSpeed` — int in ms, 30 = ~33 FPS (good)

**Critical findings:**
1. **Deadlock risk** — Reading stderr via `ReadLineAsync` while stdout is also redirected can deadlock if stdout buffer fills. Must drain both streams concurrently.
2. **`ControlBox = false` is too aggressive** — Removes ALL title bar buttons, not just X. Use `FormClosing` cancellation instead.
3. **`git worktree add` has no `--progress` flag** — DEFINITIVELY confirmed via 4 sources: (a) git-scm.com docs, (b) git source code builtin/worktree.c, (c) `git worktree add -h` on git 2.49.0, (d) empirical test with 3000 files. The checkout is done via `git reset --hard` (NOT `git checkout`), and its progress only goes to TTYs. With redirected streams: ONE stderr line ("Preparing worktree..."), ONE stdout line ("HEAD is now at..."). No percentages. Marquee bar is the only correct UX.
4. **`git worktree prune`** — Should be added as fallback cleanup step after `remove --force`.
5. **Process.Kill race condition** — `InvalidOperationException` is the correct exception when process already exited. Try/catch is essential due to TOCTOU race.
6. **Linked CancellationTokenSource must be disposed** — Memory leak risk via dangling registrations (dotnet/runtime #78180).

**Community patterns confirmed:**
- GitExtensions uses Process + event-driven `OutputDataReceived`/`ErrorDataReceived` for git CLI progress
- CancellationToken + WaitForExitAsync + Kill(true) is the standard community pattern
- IProgress<T> with Progress<T> on UI thread is the official Microsoft TAP pattern
