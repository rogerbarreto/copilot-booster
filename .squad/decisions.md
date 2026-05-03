# Squad Decisions

## 2026-05-03: Round 3 Completion — Copilot Host Discovery Phase 3 + All-Green Integration Directive

**Date:** 2026-05-03  
**Status:** Complete  
**Contributors:** Trinity (E-Triggers, Cleanup, F-UIA), Tank (Round 3 tests), Roger Barreto (grilling + directive)

### User Directive — All Integration Tests Must Be Green

**By:** Roger Barreto  
**Precedence:** Supersedes prior decisions to tolerate environmental baseline failures

**The Directive:**
All integration tests must be GREEN. Period. There is no acceptable baseline of failing integration tests — locally OR in CI. Tests that fail today because of environmental gaps (e.g., Playwright not installed locally) are TEST BUGS to be fixed, not "known baseline failures" to document and tolerate. Either:
1. The test self-bootstraps its environment (e.g., `playwright install chromium` runs as IT collection fixture/setup), OR
2. The test is explicitly skipped with a `[Trait("Category", ...)]` filter that the runner honors both locally and in CI (no red bar from missing setup).

**Why:** The user's standing release rule ("all tests must pass before any release — both unit tests and integration tests") was being violated by the team accepting environmental reds as a normal-state baseline. The grilling exposed that this acceptance had become a process smell: inventing baseline-comparison ceremony to interpret test-output noise that shouldn't exist in the first place. Restoring the "binary green" bar is simpler, more honest, and matches the user's actual policy.

**Supersedes:** Any prior decision to "document local Playwright pain in TROUBLESHOOTING.md", "maintain a tolerance list of named-diff baseline failures", or "compare test output against main to interpret failures". The right answer was always "auto-install Playwright (option c from earlier triage) or mark tests skippable so the local run is also green."

### User Directive — Zero Warnings Policy

**By:** Roger Barreto  
**Status:** Ongoing

There should be no warnings at all in the CI pipeline. All warnings must be addressed without excuse — they fail the pipeline.

### Phase 3 Delivery Summary

| Task | Owner | Status | Tests | Notes |
|------|-------|--------|-------|-------|
| T1/T2/T4/T5 Trigger Wiring | Trinity | ✅ | 647 unit | External discovery, PID registration, FullRefresh, window eviction, focus migration |
| GUID Fallback Cleanup | Trinity | ✅ | Build clean | ADR-0001 compliance, no placeholder writes |
| Deferred Name Resolution | Trinity | ✅ | Integration | First user.message extraction, sidecar update on change |
| UIA Gateway (Windows Terminal) | Trinity | ✅ | 250ms time-box | SelectionItemPattern → InvokePattern fallback |
| Test Coverage (Unit + IT) | Tank | ✅ | 10 unit + 20 IT | Host dict scaffolding + Phase 5 integration paths |

**Unit Tests:** 647/647 pass (was 637, +10)  
**Integration Tests:** 104 total with 13 pre-existing baseline reds (Playwright browser + LocalOnly skips) — now superseded by all-green directive.  
**Build:** 0 warnings, 0 errors across all 3 projects  
**Format:** `dotnet format --verify-no-changes` clean

### Phase 3 Architecture — Trinity E-Triggers

**File:** `.squad/decisions/inbox/trinity-0210-phase3-triggers.md`

5 distinct triggers populate and maintain `_copilotHosts` dictionary:

1. **T1 (External Session Discovery):** `CopilotLogWatcherService.ExternalSessionDiscovered` signature changed from `Action<string>` to `Action<string, int>` (sessionId + copilotPid). Handler calls `_hostResolver.Resolve(copilotPid)` and writes Booster-Resolved Name placeholder via sidecar.
2. **T2 (Internal PID Registration):** `PidRegistryService.CopilotPidRegisteredStatic` static event with dedup logic (`s_lastCopilotPidByLauncherPidStatic` dict). Static event needed because `Program.cs` calls `UpdatePidSessionId` statically.
3. **T4 (FullRefresh Re-Resolution):** `ActiveStatusTracker.FullRefresh` iterates `SessionService.GetActiveSessions()` after cleanup, re-resolving hosts for sessions with `CopilotPid > 0` where host is missing or dead.
4. **T5 (Window Destruction):** `WindowEventHookService.WindowDestroyed` wired to `ActiveStatusTracker.HandleWindowDestroyed` for HWND-based host eviction.
5. **Focus Migration:** `TryFocusCopilotCli` and `FocusActiveProcess` check `_copilotHosts` first (Priority 1: direct HWND), then legacy title-scan (Priority 2), then PID-based fallback (Priority 3).

**Key Design Decisions:**
- Short-circuit HWND-alive checks in T2 and T4 to avoid redundant re-resolution
- Placeholder format: `"{HostProcessName}:Copilot"` (unresolved state, replaced by deferred path)
- Focus dedup: `FocusActiveProcess` skips duplicate HWND insertion when host already added
- Legacy paths preserved as safety net (no regression risk for older sessions)

### Phase 3 Architecture — Trinity Cleanup

**File:** `.squad/decisions/inbox/trinity-0210-cleanup-summary-and-deferred.md`

Two components establish ADR-0001 compliance and deferred name resolution:

1. **Drop GUID Fallback:** `CopilotLogWatcherService.CreateWorkspaceYamlFromPid` no longer writes `workspace.yaml.summary` when window title resolution fails. External sessions now create workspace.yaml with NO `summary:` field (vs. previous GUID fallback).
2. **Deferred Resolution:** `EventsJournalService.TryResolveBoosterName` hook on `OnFileChanged` extracts first user.message content from events.jsonl and updates sidecar when content is available. Short-circuited once `ResolvedFromUserMessage == true` to avoid redundant extraction.

**New Event:** `BoosterResolvedNameUpdated(sessionId)` for UI refresh signals.

**Contract:**
- `CreateWorkspaceYamlFromPid` NEVER writes GUID to `workspace.yaml.summary`
- `TryResolveBoosterName` runs on FileSystemWatcher thread — subscribers must marshal to UI
- Resolution happens at most once per session (idempotent after first success)

### Phase 4 Architecture — Trinity F-UIA

**File:** `.squad/decisions/inbox/trinity-0210-uia-gateway-impl.md`

`WindowsTerminalPaneGateway` implements `IWindowsTerminalPaneGateway` via `System.Windows.Automation` to enable pane-level focus for Windows Terminal.

**Key Design Decisions:**
- **`<UseWPF>true</UseWPF>` enabled:** Direct `<Reference Include="UIAutomationClient" />` approach failed in .NET 10 with assembly resolution errors. UseWPF transitively provides assemblies from Desktop shared framework.
- **250ms time-box:** `Stopwatch` elapsed check inside `foreach (AutomationElement tabItem)` loop. Breaks early if budget exceeded.
- **Pattern preference:** Try `SelectionItemPattern.Select()` first, fall back to `InvokePattern.Invoke()`.
- **Exception strategy:** Top-level try/catch logs warning and returns empty list. Per-action try/catch for pattern invocations (swallows `ElementNotAvailableException` and `InvalidOperationException`).

**Consequence:** `System.Windows.Automation` is now available throughout the codebase. Tests can fake `IWindowsTerminalPaneGateway` without touching real UIA. Adding pane focus for other hosts (tmux, Warp) requires implementing a new gateway behind the same interface pattern.

### Round 3 Test Coverage — Tank

**File:** `.squad/decisions/inbox/tank-0210-round3-tests.md`

30 new tests (10 unit + 20 integration) for Phase 3 scaffolding and Phase 5 integration:

**Unit Tests (10 total):**
- `ActiveStatusTrackerHostTests.cs`: Round-trip, idempotency, updates, removal, projection, unprojection, dedup, event data verification

**Integration Tests (20 total):**
- `InternalSessionHostResolutionIntegrationTests.cs`: Real process tree, PID skip, deep nesting, host classification
- `InternalSessionTitleChangesIntegrationTests.cs`: HWND-based host path robustness against title changes (proves fix for the hardest existing bug)
- `ExternalSessionWorkspaceYamlTests.cs`: No `summary:` writes (ADR-0001 compliance)
- `DeferredNameResolutionIntegrationTests.cs`: Placeholder → resolved name transition

**Test Count Growth:** 637 unit → 647 unit (+10), 84 IT → 104 IT (+20)

### Round 4 Integration Tests All-Green Mandate — Tank

**Date:** 2026-05-03  
**Status:** Complete  
**Implementation:** Tank (Round 4)  
**Directive:** Enforce Roger Barreto's all-green mandate across all integration tests

**Problem:** Round 3 ended with 13 baseline-red integration tests (Playwright browser not installed locally, LocalOnly tests). These were documented as "known environmental failures" — a process smell. The directive supersedes tolerance: ALL tests must be GREEN, period. Either tests self-bootstrap their environment or skip cleanly.

**Solution:**

1. **Playwright auto-bootstrap fixture:**
   - New xUnit collection: `PlaywrightBootstrap`
   - Collection fixture probes `Playwright.CreateAsync().Chromium.LaunchAsync()`
   - On missing browser: auto-calls `Microsoft.Playwright.Program.Main(["install", "chromium"])`
   - On install failure: skips cleanly (xUnit skip mechanism, no red bar)
   - Timing: 36s first run (with install), 29s cached, vs. 39s pre-bootstrap inventory with 13 reds

2. **LocalOnly conditional skip policy:**
   - Custom attributes: `LocalOnlyFactAttribute`, `LocalOnlyStaFactAttribute`
   - Skip unless `COPILOT_BOOSTER_RUN_LOCALONLY=1` or `true` is set
   - Trait-only approach fails; attribute-based skip is reliable in in-process xUnit runner

3. **Test-shape corrections:**
   - `CopilotLogWatcherService.TryParseLogContent` expects `kind: "session_start"`, `session_id`, `context.cwd` (not raw top-level `cwd`)
   - Deferred name resolution code-fence test corrected to exercise production formatter contract (not invented trailing-fence stripping)

4. **Build isolation and stability:**
   - `tests/Directory.Build.props` gives `CopilotBooster.IntegrationTests` its own `obj-integration/` intermediate path
   - Prevents solution-level restore/build from racing with unit test project and dropping Playwright references
   - `RunningAppsGridDetectionTests` assigned to non-parallel xUnit collection (uses global WinEvent hooks and real windows)
   - Collection narrowing keeps full IT run at ~29s instead of disabling all parallelism

**Outcome:**

| Metric | Result |
|--------|--------|
| Integration Tests Passed | 99 / 104 |
| Integration Tests Skipped | 5 / 104 |
| Integration Tests Failed | 0 / 104 ✅ |
| Unit Tests | 647 / 647 ✅ |
| Build (dotnet build --tl:off) | 0 warnings, 0 errors ✅ |
| Format (dotnet format --verify-no-changes) | Clean ✅ |
| Cached IT Runtime | 29.247s |

**Consequence:** Directive satisfied. Binary green bar restored. Local `dotnet run --project tests/CopilotBooster.IntegrationTests.csproj -c Release` produces zero failures. All tests either pass or skip cleanly.

---

## Issue #12: Async Worktree Creation with Cancellation

**Date:** 2026-03-15  
**Status:** Simplified Implementation (Revised 2026-03-15T163600Z)  
**Contributors:** Neo, Trinity, Morpheus, Tank  
**Directed by:** Roger Barreto

### Problem Statement

The `RunGit` method has a default 10-second timeout. When `git worktree add` runs against a large repository (or one requiring network operations), the timeout fires and **kills the git process mid-operation** — leaving a corrupted or partial worktree. This is the actual bug.

Three of the four creation modes (Issue, New Branch, Existing Branch) also run synchronously on the UI thread, freezing the app during creation.

### Design Philosophy (Simplified per Roger's UX Directive)

> "We can actually just show 'In Progress...' without a progress bar, but don't drop the process if it has not finished."

- **No progress bar, elapsed timer, or cancel button overlay.**
- The PR mode already shows `btnCreate.Text = "Creating..."` with the button disabled — this UX pattern is sufficient for all modes.
- The real fix is: **stop killing the git process prematurely**.

### Architecture

#### 1. `RunGitAsync` — New Async Method in `GitService.cs`

```csharp
internal static async Task<(int exitCode, string stdout, string stderr)> RunGitAsync(
    string repoPath, string arguments, CancellationToken cancellationToken = default)
```

**Key design decisions:**
- **No hard timeout.** The method waits for the process to complete naturally. Callers can pass a `CancellationToken` with a timeout if desired, but the default is indefinite wait.
- **Concurrent stdout/stderr reading.** Both streams are read with `ReadToEndAsync()` concurrently to prevent the well-known .NET deadlock when a child process fills one stream buffer while the parent blocks reading the other.
- **Process tree kill on cancellation.** If the token is cancelled, call `process.Kill(entireProcessTree: true)` wrapped in try/catch.
- **Returns the same tuple shape** as `RunGit` for easy adoption.

#### 2. Existing `RunGit` — Unchanged

The synchronous `RunGit` method stays as-is. It's well-tested, used by dozens of callers for fast operations, and its 10s timeout is appropriate for those use cases.

#### 3. Async Worktree Methods in `GitService.cs`

Add async overloads for the three worktree-creation methods:

```csharp
internal static async Task<(bool success, string error)> CreateWorktreeAsync(...)
internal static async Task<(bool success, string error)> CheckoutExistingBranchWorktreeAsync(...)
internal static async Task<(bool success, string error)> CheckoutLocalBranchWorktreeAsync(...)
```

Each calls `RunGitAsync` instead of `RunGit` with the same arguments. Sync versions remain for backward compatibility.

#### 4. Async Methods in `WorkspaceCreationService.cs`

Add async overloads mirroring the existing sync methods:

```csharp
internal static async Task<(string path, bool success, string? error)> CreateWorkspaceAsync(...)
internal static async Task<(string path, bool success, string? error)> CreateWorkspaceFromExistingBranchAsync(...)
internal static async Task<(string path, bool success, string? error)> CreateWorkspaceFromPrAsync(...)
```

These call the new `*Async` GitService methods. `FetchPrRef` gets a new `FetchPrRefAsync` using `RunGitAsync` with no timeout.

#### 5. UI Changes in `WorkspaceCreatorVisuals.cs`

**All 4 modes use the same pattern the PR mode already uses:**

```csharp
btnCreate.Enabled = false;
btnCreate.Text = "Creating...";

var (worktreePath, success, error) = await WorkspaceCreationService.CreateWorkspaceAsync(...)
    .ConfigureAwait(true);
```

**FormClosing guard (Niobe's correction #2):**

Instead of disabling `ControlBox` during creation:

```csharp
bool isCreating = false;

form.FormClosing += (s, e) =>
{
    if (isCreating && e.CloseReason == CloseReason.UserClosing)
    {
        e.Cancel = true;  // Prevent close while operation is in progress
    }
};
```

Set `isCreating = true` before the await, `false` after. This preserves minimize/maximize and form icon while blocking closure.

#### 6. Cleanup Fallback (Niobe's correction #3)

When a worktree creation fails or is cancelled:

```csharp
RunGit(repoPath, $"worktree remove --force \"{worktreePath}\"");
RunGit(repoPath, "worktree prune");  // Fallback: clean stale worktree entries
if (Directory.Exists(worktreePath))
{
    Directory.Delete(worktreePath, recursive: true);
}
```

The `worktree prune` ensures git's internal worktree list stays clean even if the `worktree remove` partially fails.

#### 7. String Renames — "Workspace" → "Worktree"

10 user-facing string changes (internal code identifiers unchanged):

| # | File | Current | New |
|---|------|---------|-----|
| 1 | `WorkspaceCreatorVisuals.cs:58` | `"Create New Workspace"` | `"Create New Worktree"` |
| 2 | `WorkspaceCreatorVisuals.cs:79` | `"Set up a new isolated workspace..."` | `"Set up a new isolated worktree..."` |
| 3 | `WorkspaceCreatorVisuals.cs:197` | `"...name for your workspace..."` | `"...name for your worktree..."` |
| 4 | `WorkspaceCreatorVisuals.cs:246` | `"...create the workspace from"` | `"...create the worktree from"` |
| 5 | `WorkspaceCreatorVisuals.cs:1156` | `"Failed to create workspace:\n..."` | `"Failed to create worktree:\n..."` |
| 6 | `WorkspaceCreatorVisuals.cs:1199` | `"Failed to create workspace:\n..."` | `"Failed to create worktree:\n..."` |
| 7 | `WorkspaceCreatorVisuals.cs:1224` | `"Failed to create workspace:\n..."` | `"Failed to create worktree:\n..."` |
| 8 | `WorkspaceCreatorVisuals.cs:1242` | `"Failed to create workspace:\n..."` | `"Failed to create worktree:\n..."` |
| 9 | `SettingsForm.cs:277` | `"Workspaces Dir:"` | `"Worktrees Dir:"` |
| 10 | `SettingsForm.cs:713` | `"...session's Edge workspace..."` | `"...session's Edge worktree..."` |

### Implementation Phases

1. **Phase 1 (GitService):** `RunGitAsync`, async worktree methods, unit tests
2. **Phase 2 (WorkspaceCreationService):** Async service method overloads, unit tests
3. **Phase 3 (UI):** All 4 modes async, FormClosing guard
4. **Phase 4 (Naming):** 10 string renames (parallel or after Phases 1–3)

### Status — Phases 1–4 Complete

✅ **Trinity (Phase 1+2):** All async methods added, concurrent stream reading implemented, cleanup fallback with `worktree prune`  
✅ **Morpheus (Phase 4):** All 10 strings renamed  
✅ **Tank:** 5 anticipatory async unit tests written, all 497 pass  
✅ **Neo:** Simplified architecture proposal documented  

### Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| Partial worktree on error | Cleanup: `worktree remove --force` → `worktree prune` → `Directory.Delete` |
| UI thread deadlock | All await calls use `.ConfigureAwait(true)` (WinForms SynchronizationContext) |
| User closes dialog during creation | `FormClosing` event cancellation prevents close while `isCreating` is true |
| Git process hangs forever | Acceptable tradeoff — user can always close the app. Future: add optional soft timeout with user prompt |
| Process zombie on error | `process.Kill(entireProcessTree: true)` in catch, wrapped in try/catch |

### Testing Strategy

- **Unit tests:** Test `RunGitAsync` directly via `InternalsVisibleTo` — verify correct exit codes, stdout, stderr
- **Cancellation test:** Cancel token immediately, expect `OperationCanceledException`
- **Integration tests:** Actual git commands in temp directories for async variants
- **Regression:** Existing sync `RunGit` tests remain untouched — sync path unchanged
- **Manual:** Test all 4 creation modes on a large repo to confirm no UI freezing

### Open Questions (Deferred)

1. Should we add a soft timeout (e.g., 5 minutes) that shows a "Still working..." message instead of killing the process? (Future enhancement)
2. Future: Propagate `CancellationToken` deeper into `RunGit` to kill hung git processes? (Separate issue)

---

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
