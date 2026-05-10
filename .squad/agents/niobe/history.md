# Niobe — Researcher History

## Project Context

- **Project:** copilot-booster — A WinForms (.NET 10) desktop app that enhances GitHub Copilot CLI
- **Stack:** C# / .NET 10 / WinForms / nullable enabled / xUnit v3
- **User:** Roger Barreto
- **Key services:** GitService, WorkspaceCreationService, SessionService, EdgeWorkspaceService
- **Key forms:** MainForm, WorkspaceCreatorVisuals, ExistingSessionsVisuals, NewSessionVisuals

## Learnings

- **2026-05-10 — Team update: Warp terminal R2 probe-and-match strategy delivered (Trinity/Tank):** Squad shipped deterministic pane focus for Warp terminals (7 new service files, WarpPaneFocuser with IWindowTitleReader/IKeyboardSender/IPaneFocusClock seams). Integrated into ActiveStatusTracker via _warpPaneFocuser and _sessionDisplayNameProvider seams with 10 chained constructor overloads (backward compat). FocusCopilotHost branches on HostKindLabel == "Warp": reads session summary, cycles Ctrl+Tab up to 30× (150ms settle), detects title match or cycle-back. On mismatch logs warning + focuses warp.exe window fallback. Non-Warp hosts unchanged. Tank shipped 12 unit tests (StubTitleReader/KeyboardSender/PaneFocusClock infrastructure) + 3 LocalOnly live integration tests (restore-on-teardown via IDisposable). Verified all green against Roger's live Warp Hi 1 / Hi 2. Skill documented at `.squad/skills/title-probe-tab-focus/SKILL.md`. Decisions merged: decisions.md now contains warp R2 strategy, implementation, and test coverage.

- **2026-05-10 — Copilot CLI log format: /resume session transition logging:** When a user executes `/resume session_b` inside an active process running session_a, the Copilot CLI log file records BOTH session IDs with distinct timestamps. First occurrence: "No persisted remote state for session_a" (early log line), followed by "Workspace initialized: session_b" (~135ms later in observed case), then "session_resume" telemetry with session_id=session_b. The log is the primary source of truth for session activation order and timing. Additionally, session-state files (workspace.yaml, events.jsonl) mtimes are updated AFTER log entry, not before; do not rely solely on mtimes for session state causality — correlate with log timestamps.

- **2026-05-09 — Documentation for 0.22.0 refinements:** Updated CHANGELOG.md (added two bullets under Changed section) and README.md (removed Copilot CLI path field, enhanced Model dropdown documentation with API cache details). No version bumps as per directive.

- **2026-05-09 — GitHub Copilot models API (auth flow correction):** The endpoint `https://api.githubcopilot.com/models` works with standard GitHub PAT (via `gh auth token`), not a special `copilot_internal/v2/token` endpoint. The internal endpoint returns 404 and does not exist. API returns 35 models; Copilot CLI help lists 17. Use API as primary source with 24h cache, hardcoded fallback.

- **2026-05-03 — All-green integration test directive (validation impact):** User grilling established binary all-green policy: no tolerance for environmental baseline failures. Tests must self-bootstrap (e.g., Playwright auto-install in collection fixture) or skip explicitly with traits. This is a standing release policy enforcement — validates that team release quality is not compromised by accepting noise baseline.

- **2026-05-09 — Issue #15 refinement — Documentation (CHANGELOG + README) for 0.22.0 in-flight refinements:** Updated CHANGELOG.md under 0.22.0 § Changed with 3 bullets: (1) auto-resolved Copilot CLI path, (2) dynamic model dropdown fetched from GitHub Copilot models API, (3) cache-first + stale-fallback resilience. Updated README.md: removed CopilotPath row from Settings table, enhanced model description to reflect "auto-discovered from GitHub Copilot". No version bump (per user constraint: refinements only, 0.22.0 locked).

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
