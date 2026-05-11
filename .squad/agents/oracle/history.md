# Oracle — History

## Core Context

- **Project:** A C# WinForms (.NET 10) desktop application that enhances GitHub Copilot workflow with session management, IDE integration, and GitHub tracking.
- **Role:** Quality Architect
- **Joined:** 2026-03-15T15:50:59.677Z

## Learnings

<!-- Append learnings below -->

- **2026-05-10 — Team update: WarpPaneFocuser landed and title-probe-tab-focus skill documented:** Trinity shipped deterministic R2 probe-and-match Warp terminal pane focus (7 new service files with seam architecture, 10 constructor overloads for backward compat). Tank shipped 12 unit + 3 LocalOnly live integration tests, all green against Roger's live Warp Hi 1 / Hi 2. Strategy: Ctrl+Tab cycling with title match detection (30 iteration cap, 150ms settle). Session display name sourced from SessionInfo.Summary. Fallback: focus warp.exe window on mismatch (safety net). Skill extracted to `.squad/skills/title-probe-tab-focus/SKILL.md` for future terminal work. Decisions merged into decisions.md (squad-warp-r2-pivot, trinity-warp-pane-focuser, tank-warp-r2-tests). Build clean (0 warn, 0 err), 851 unit tests (+28 from baseline), 141 integration tests.
- **2026-05-03 — All-green integration test directive (quality architecture):** User grilling on IT regression risk exposed process smell: team was accepting baseline failures as "normal" and building ceremony (baseline-comparison scripts) around test-output noise. User directive: binary green only. All 104 IT must pass at all times. Tests failing due to environmental gaps (missing Playwright) are TEST BUGS, not "known baselines". Fix via fixture auto-install or explicit skip-traits. Supersedes prior tolerance decisions.
