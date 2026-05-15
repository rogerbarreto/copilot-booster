# Dozer — History

## Day 1 Context

- **Project:** copilot-booster — Windows desktop session launcher for the GitHub Copilot CLI, .NET 10 WinForms, C# with nullable enabled
- **Hired by:** Roger Barreto
- **Hired:** 2026-05-15
- **Why I exist:** Tank already covers TDD and integration tests using Claude. I am the second-pair-of-eyes peer reviewer running on GPT-5.5, specifically to surface gaps Tank's reasoning style might miss. Analytical diversity, not redundancy.
- **My counterpart:** Tank. We are peers, not hierarchical. Tank produces first; I review and propose complementary tests.
- **Repo conventions to remember from day 1:**
  - Unit tests: `dotnet run --project tests/CopilotBooster.Tests.csproj -c Release`
  - Integration tests: `dotnet run --project tests/CopilotBooster.IntegrationTests.csproj -c Release`
  - Always `--tl:off` on `dotnet build` / `dotnet test`; do NOT use it on `dotnet format`
  - WinForms tests need `[WinFormsFact]` or `[StaFact]` for STA thread
  - No reflection on our own internals — use InternalsVisibleTo
  - No process kills outside the test's own spawned set (commit 0f9af1c)
  - Read-only against `~/.copilot/session-state/` — never write to a real Copilot session
  - Format: `this.` prefix on instance members; member ordering `s_` statics → `_` privates → protected → public props → constructors → methods
- **Hot bug right now:** Live CWD reactivity. UI keeps stale workspace.yaml cwd when Copilot CLI changes its working directory mid-session. Tank wrote a RED test (`LiveCwdOverlaySeamTests`) demanding an `EventsJournalService.ApplyLiveCwdOverlay(IReadOnlyList<NamedSession>, ...)` seam.

## Learnings

(append below as I work)
- 2026-05-16: Verdict APPROVE WITH GAPS for Tank live CWD production RED. Key gaps found: case-insensitive no-op behavior, multiple-session overlay independence, and overly loose MainForm source guard. Future peer reviews should distrust literal source-contract substring checks unless they verify call ordering around the real production seam.

- 2026-05-17: Verdict APPROVE WITH GAPS for Trinity live CWD implementation. Subtle gap: the normal dirty data refresh overlays correctly, but full refresh paths reload workspace.yaml without overlay and can reintroduce stale CWD; trailing forward slash live CWD also yields an empty Folder.
- 2026-05-17: Verdict APPROVE for Switch live CWD implementation. Subtle check: full refresh now overlays live CWD on the actual LoadSessions path before caching, and the only early return skips loading entirely rather than clobbering the live value.
- 2026-05-17: Verdict REJECT for Tank cache-free RED. Checked current production, filtered builds, and targeted tests. Caught three gaps: newer events.jsonl with only session.start was not tested through LoadNamedSessions, exact cache-name guards were renameable, and integration tests still referenced removed overlay APIs.
- 2026-05-17: Closed remaining cache-free behavioral RED gaps with six SessionService tests. Equal mtime and empty hook cwd pass by existing yaml-only behavior, while missing yaml cwd, mixed multi-session authority, partial final line tail read, and double-wrapped single quotes fail for the intended missing production behavior.
- 2026-05-17: Rescoped tail-read allocation test to measure `ExtractLatestCwdFromTail` directly. Lesson: performance test names must match the measured unit; full-path allocation budgets should not pretend to prove a helper's allocation contract.
- 2026-05-17: Scope back lesson. When speculative tests conflict with established product semantics, remove those tests instead of making convoluted production logic keep them green. The real reported bug was NAME quote wrapping, so SUMMARY quote wrapping coverage was removed.
- 2026-05-17: Lesson — when a verification flagged 'gaps', I should also ask whether the existing tests that pass for the wrong reason might actually encode the right behavior. Don't remove tests until I've verified the test's intent is genuinely out of scope.
- **2026-05-17: Cache-free architecture RED review — binary verdict enforcement:** Roger issued ULTRA 2026-05-15T18-50-00Z (binary verdicts only; gaps = REJECT). Reviewed Tank's cache-free RED suite (31 tests, 19 honest). Caught gaps: (1) events.jsonl with only session.start (no hook.start) not tested through LoadNamedSessions; (2) exact cache-name guards renameable; (3) integration tests still referenced removed overlay APIs. Added 6 behavioral gap tests to complete SessionServiceCwdResolutionTests (equal mtimes, empty hook cwd, missing yaml, multi-session independence, partial final line, double-wrapped quotes). Rescoped tail-read allocation test to measure `ExtractLatestCwdFromTail` directly instead of full LoadNamedSessions (performance test names must match measured unit; full-path budgets don't prove helper contracts). **Verdict: REJECT → After gaps added and rescoped, full unit suite 976 total, 0 failed.** Per binary-verdict directive, gaps locked Tank out; Switch took over implementation. After Switch delivered, all 31 tests GREEN.
