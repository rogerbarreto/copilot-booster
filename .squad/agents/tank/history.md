# Tank — History

## Learnings

<!-- Append learnings below -->

- **2026-05-14: WI-3 editor CWD save regression tests:** Neo Q3 chose no production seam for the WinForms context-menu lambda, so Tank pinned the adjacent source contract in `tests/Forms/MainFormContextMenuEditorSaveTests.cs`: no `UpdateSessionCwd`, no in-memory `session.Cwd` or `session.Folder` mutation, no `CWD` grid-cell update, and alias `SetAlias` remains. RED could not be reproduced in this worktree because WI-3 source removal was already present before the test landed; build and unit tests are green.

- **2026-05-14: WI-2 CWD fallback RED tests:** `tests/Services/CopilotLogWatcherServiceTests.cs` now pins parser purity and caller fallback ordering. Trinity needs a caller-side `ResolveCwdAfterParse` style seam that accepts parsed CWD, PID, and injectable `Func<int, string?>` PEB probe so Tank can fake PEB without depending on live Win32 process state.

- **Team update 2026-05-14 — CWD feature complete:** All 15 RED/GREEN tests (11 WI-2 + 4 WI-3 regression) passed. Oracle approved WI-2 design and Trinity's implementation. WI-3 closed-as-absorbed per Neo Q3. Build clean, full suite green (939 unit, 155 integration, 0 failed). Feature closed.

