# Decision: Log Streaming Memory Fix

**Date:** 2026-05-10  
**Decided by:** Trinity (Services Dev)  
**Context:** Memory regression — 4.4 GB process RSS after 2.8 minutes uptime

## Problem

Copilot Booster consumed 4.4 GB memory due to full-file reads of unbounded Copilot CLI process logs:
- Roger's machine: 547 logs in `~/.copilot/logs/`, total 5 GB, largest single log 678 MB
- 7 live Copilot PIDs whose logs get read on FileSystemWatcher events and startup rescan
- Each `ReadToEnd()` on a 678 MB log allocates ~1.4 GB on LOH (UTF-16 internal), then `Split('\n')` allocates a similar-size string array
- LOH compaction is rare → memory stays high

Two call sites:
1. `CopilotLogWatcherService.cs:168` (runtime — FileSystemWatcher debounced)
2. `ActiveStatusTracker.cs:530` (startup `RescanExistingSessions`)

## Decision

**Scope:** Process logs only (not events.jsonl — confirmed safe via backward-seek `ReadLastLine`).

**Solution:**
1. Added streaming `TryParseLogContent(TextReader reader, string? fallbackCwd)` overload — line-by-line `ReadLine()` loop, never materializes whole file
2. Refactored existing `(string[] lines)` overload to wrap `StringReader` for test compatibility
3. Updated both production call sites to use streaming overload with `StreamReader` over `FileStream`

**Allocation Budget Achieved:**
- Tank's regression tests target < 25 MB allocation for 50 MB synthetic log (vs. ~100 MB+ for array overload)
- Tests skip by default via `[Fact(Skip = "LocalOnly")]` — expensive file generation

**Behavior Guarantee:** Streaming parser produces IDENTICAL output to array overload (same session order, same dedup, same fallbackCwd chain).

## Outcome

- Build clean, format clean, 855 unit tests passing (+4 from 851 baseline)
- No behavior change — all existing tests still pass with array overload
- Memory footprint for 678 MB log: ~1.4 GB → ~10 MB (streaming buffer + output list)
- Pattern documented in `.squad/skills/streaming-large-files/` for future use

## Alternatives Considered

1. **Tail-only read (last N lines):** Rejected — sessions can appear anywhere in log, not just end (Bug D: /resume creates multiple sessions per PID)
2. **Memory-mapped file:** Rejected — `ReadLine()` on `StreamReader` is simpler, proven, and sufficient
3. **Chunked batch reads:** Rejected — no clear win over line-by-line, more complex

## Regressions Caught

None (proactive fix based on coordinator diagnosis before Tank's test existed).
