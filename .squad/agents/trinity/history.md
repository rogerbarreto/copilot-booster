# Trinity — History

## Learnings

<!-- Append learnings below -->

### WI-2 caller-site CWD fallback seam

- Parser stays pure: `src/Services/CopilotLogWatcherService.cs` returns `cwdFromJson ?? cwdFromDebugLine ?? fallbackCwd ?? ""`.
- Caller owns environment fallback via `ResolveCwdAfterParse(parsedCwd, pid, getProcessCwd)`.
- Unresolved parsed CWD: empty, whitespace, or UserProfile after trim with ordinal-ignore-case compare.
- Fallback order after parse: parsed CWD, PEB probe, absolute `Program._settings.DefaultWorkDir`, empty string.
- PEB probe exceptions are swallowed at the seam because the process may exit between parse and probe.
- Full-suite flake found while validating: `SessionService.s_gitRepoCache` must be thread-safe because xUnit parallel calls `LoadNamedSessions`.

### Team update 2026-05-14 — CWD feature complete

- **WI-1, WI-2, WI-3, WI-4 shipped:** CWD fallback reorder with caller-site seam, parser purified (→ ""), SessionEditorVisuals read-only conversion, all 11 WI-2 tests GREEN, 4 WI-3 regression guards GREEN. Concurrent dictionary fix resolved parallel-test flake. Full suite: 939 unit, 155 integration, all green. Feature closed.

