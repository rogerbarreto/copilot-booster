# Scribe — History

## Core Context

- **Project:** A C# WinForms (.NET 10) desktop application that enhances GitHub Copilot workflow with session management, IDE integration, and GitHub tracking.
- **Role:** Session Logger
- **Joined:** 2026-03-15T15:50:59.678Z

## Sessions

### 2026-05-10: Tail-Read Optimization — Startup & Memory Regressions Resolved

**Date:** 2026-05-10  
**Status:** Delivered  
**Contributors:** Roger (diagnosis + live verification), Trinity (tail-read implementation), Tank (test validation), Scribe (session logging)

**Problem:** Roger reported a 1+ minute startup delay and 4 GB+ memory footprint on Copilot Booster.

**Root Cause Analysis:** Two cumulative perf bugs identified:
1. **T0 `RescanExistingSessions`:** Iterated all 256 parsed `/resume` markers, calling `TryParseLogContent` on each file (256 × 297ms = 79 seconds). Fixed in commit `36f7e31` by binding only the latest **LIVE** session per PID.
2. **Full-File Reads on Watcher Events:** Every `FileSystemWatcher` `Changed` event and T0 rescan triggered full reads of 100s-of-MB active Copilot process logs, creating multi-GB transient allocations. Fixed in commit `0ce9954` by tail-reading the last 256 KB aligned to newline boundary.

**Implementation (Trinity):** Created `TryParseLogTail(string logPath, int maxTailBytes = 256*1024, string? fallbackCwd = null)` in `src/Services/CopilotLogWatcherService.cs`. Swapped both call sites:
- `ActiveStatusTracker.RescanExistingSessions` (line 533)
- `CopilotLogWatcherService.TryProcessLogFile` (line 168)

**Testing (Tank):** Wrote 4 tests in `tests/Services/CopilotLogTailReadTests.cs`:
- Small-file parity with full read
- Large-file tail recovery
- Newline alignment after mid-block seek
- Non-existent file handling

Tank initially rejected based on wrong-shape JSON fixtures (incorrectly placed `session_id` under `context` instead of JSON root). Rejection withdrawn after clarification: real Copilot logs place `session_id` at root with only `cwd` under `context`.

**Live Verification (Roger's machine, 9 alive copilot.exe, 547 logs totaling 5+ GB):**
- `RescanExistingSessions`: 79,000 ms → ~1,800 ms (**47× faster**)
- Working set after 60s: 7,800 MB → 125 MB (**~60× lower**)
- Stale session bindings: 256 → 4

**All Tests Green:** 858 unit tests + 141 integration tests. Code formatted. Landed on `issues/15-ai-features-auto-discover-pr-issue-id` in commit `0ce9954`.

---

## Learnings

<!-- Append learnings below -->
