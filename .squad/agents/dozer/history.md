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