# Ralph — History

## Core Context

- **Project:** A C# WinForms (.NET 10) desktop application that enhances GitHub Copilot workflow with session management, IDE integration, and GitHub tracking.
- **Role:** Work Monitor
- **Joined:** 2026-03-15T15:50:59.679Z

## Learnings

<!-- Append learnings below -->

- **2026-05-03 — All-green integration test directive (monitor impact):** All integration tests must be green at all times. No tolerance for environmental baseline failures (13 current Playwright reds). Tests must self-bootstrap environment (fixture install) or skip explicitly with traits honored by runner. This is enforcement of standing user release policy: "all tests must pass before any release". New baseline target: 104 IT, 0 reds.
