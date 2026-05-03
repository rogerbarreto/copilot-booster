# Oracle — History

## Core Context

- **Project:** A C# WinForms (.NET 10) desktop application that enhances GitHub Copilot workflow with session management, IDE integration, and GitHub tracking.
- **Role:** Quality Architect
- **Joined:** 2026-03-15T15:50:59.677Z

## Learnings

<!-- Append learnings below -->

- **2026-05-03 — All-green integration test directive (quality architecture):** User grilling on IT regression risk exposed process smell: team was accepting baseline failures as "normal" and building ceremony (baseline-comparison scripts) around test-output noise. User directive: binary green only. All 104 IT must pass at all times. Tests failing due to environmental gaps (missing Playwright) are TEST BUGS, not "known baselines". Fix via fixture auto-install or explicit skip-traits. Supersedes prior tolerance decisions.
