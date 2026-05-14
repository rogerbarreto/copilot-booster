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


### WI-LiveCwd-1 events.jsonl live cwd

- Parser seam: `EventsJournalService.ExtractLatestCwd(TextReader)` scans JSONL line by line with `JsonDocument`, ignores empty or malformed lines, uses `session.start.data.context.cwd` as fallback, and lets latest `hook.start.data.input.cwd` win.
- Concurrent writer read: open `events.jsonl` with `FileStream(FileMode.Open, FileAccess.Read, FileShare.ReadWrite)` and wrap in `StreamReader`.
- Event shape: `LatestCwdChanged(sessionId, cwd)` fires from watcher reads only when cwd changes after case-insensitive cache compare.
- UI wiring: `MainForm.OnLatestCwdChanged` marshals with `BeginInvoke`, updates `NamedSession.Cwd` and `Folder`, then calls `RequestRefresh(sessionId, dataChanged: true)`.
- Key files: `src/Services/EventsJournalService.cs`, `src/Forms/MainForm.cs`, `tests/Services/EventsJournalServiceCwdTests.cs`.

- **Team update 2026-05-16 — Live CWD from events.jsonl:** Completed WI-LiveCwd-1 with Tank's 7 RED tests now GREEN. Parser extracts latest `hook.start.data.input.cwd` from events.jsonl, falls back to `session.start.data.context.cwd`, fires `LatestCwdChanged` event on cwd delta. Backward compat preserved: parameterless ctor chains to new `string sessionsDir` overload for test injection. Read-only enforced: production reads only via `FileShare.ReadWrite`, tests use isolated temp dirs. Skill documented at `.squad/skills/append-only-jsonl-field-extraction.md`. Full suite green (946 unit, 155 integration). Feature closed.
