# Squad Decisions

## 2026-05-08: Issue #20 — Cell Spinner + Click-to-Cancel + JobObject Tree-Kill

**Date:** 2026-05-08  
**Status:** Delivered  
**Contributors:** Trinity (ProcessRunner JobObject), Morpheus (UI spinner + confirm dialog), Tank (state machine + tree-kill tests)

### Trinity: JobObject Tree-Kill in ProcessRunner

**Issue:** #20  

Production `ProcessRunner` owns process tree termination with a raw Win32 Job Object.

**Pattern:**
- Create Job Object
- Set `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` through `JOBOBJECT_EXTENDED_LIMIT_INFORMATION`
- Start child process
- Assign child handle to the job immediately after `Process.Start()`
- On linked cancellation or timeout, call `TerminateJobObject(job, 1)`
- Always close the job handle in `finally`

**Service contract:**
- `IProcessRunner.RunAsync(...)` signature unchanged
- `ProcessResult.WasKilled == true` signals cancellation or timeout tree termination
- `AiDetectionService` classifies `WasKilled && cts.IsCancellationRequested` as `cancelled`, not a failure
- `DetectionStateChanged(sid, Running, Idle)` remains the spinner clear signal for Morpheus

### Morpheus: IConfirmDialog Seam for Cancel Confirmation

**Issue:** #20  

Use `src/Forms/IConfirmDialog.cs` interface for the cancel confirmation seam.

**Shape:**
```csharp
internal interface IConfirmDialog
{
    bool Confirm(string title, string body, string yesLabel, string noLabel);
}
```

**Production implementation:** `MessageBoxConfirmDialog` wraps `MessageBox.Show(YesNo)`. Since native WinForms cannot customize button text, message appends:
```
Yes = Stop
No = Keep running
```

**Consequences:**
- Tank can fake `IConfirmDialog` and assert title, body, and labels
- `SessionGridVisuals.HandleGitHubCellClick(...)` stays testable without showing UI
- Custom Form can replace `MessageBoxConfirmDialog` later if exact button text becomes mandatory

**Deliverables:**
- Top-right 16x16 corner spinner region in SessionGridVisuals
- ONE shared `System.Windows.Forms.Timer` for all running rows
- Click-to-cancel with confirm dialog
- Spinner-frame icon rendering in GitHubIconRenderer
- Test seams: `HandleGitHubCellClick`, `GetStatusIconRegion`, `IsSpinnerVisibleForSession`

### Tank: Spinner Test Serialization Decision

**Issue:** #20  

`AiDetectSpinnerCancelIntegrationTests` belongs to `WindowEventHookCollection` serialized subset.

**Why:** The class does not start `WindowEventHookService`, but uses WinForms timer state plus `Application.DoEvents()` inside the same desktop test process. In full integration runs, parallel execution intermittently left `_spinnerTimer.Enabled` true after the service returned to idle. Serialization preserves the all-green integration bar.

**Scope:** Only this spinner/cancel class was added. No global integration parallelism change.

**Deliverables:**
- Cancel state transition tests
- Dispose-cancels-all test
- Spinner state machine tests
- Confirm/fallthrough E2E integration tests
- Real tree-kill test with PowerShell child process spawning and PID diff capture

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

## Issue #17: Tracer End-to-End Happy Path (AI Auto-Detect GitHub Link)

**Date:** 2026-05-08  
**Status:** Complete  
**Contributors:** Trinity (Services Dev), Morpheus (UI Dev), Tank (Tester)

### Service Contract — Trinity

**Decision:** `AiDetectionService` owns the service orchestration for slice #17 and exposes only service-safe hooks.

- **Process boundary:** `IProcessRunner.RunAsync(...)` returning `ProcessResult`
- **State event:** `DetectionStateChanged(string sid, DetectionStatus oldStatus, DetectionStatus newStatus)`
- **Query:** `TryGetState(string sid)` plus out-param overload
- **Toast:** Injected `Action<string>` sink, no new toast interface
- **CWD:** Injected `Func<string,string?>`, so MainForm uses cached sessions while tests use fixture roots

**Deferred to later slices:**
- Strict schema validation and failure classes → slice #18
- Proper upstream then origin repo resolution → slice #19
- JobObject process tree kill and visual status cells → slices #20 and #22
- Settings-backed timeout, threshold, binary path, and model → slice #21

**Outcome:** Public surface stable. Service API locked before Morpheus and Tank implementation. All 667 unit tests pass, 99/104 integration tests pass.

### Shared Test Double Location — Tank

**Decision:** `FakeProcessRunner` lives at `tests/Integration/TestTools/FakeProcessRunner.cs` and is shared by AI detection slices.

**Contract:**
```csharp
RunAsync(string fileName, IReadOnlyList<string> args, string cwd, int timeoutSeconds, CancellationToken ct)
```

**Usage for exact process boundary assertions:**
- `fileName == "copilot"`
- args contain the rendered prompt and required flags
- cwd equals the session cwd
- timeout equals 300 for slice 17

**Rationale:** Later slices can reuse the same fake for malformed JSON, timeout, cancellation, and real CLI boundary tests without cloning runner logic.

**Outcome:** Tank implemented FakeProcessRunner. Trinity and Morpheus verified contracts pass. All integrations green.

### Windows Terminal Multi-Pane Discovery and Focus — Trinity (Phase 4)

**Date:** 2026-05-03  
**Status:** Implemented  
**Decision:** Carry the UIA tab runtime id as part of `CopilotHostInfo` and treat WT session identity as `(parent HWND, pane runtime id)`.

**Changes:**
- Cache WT panes by `(parent HWND, pane runtime id)`, not parent HWND alone
- Keep host projections from being removed by parent HWND title churn
- On focus, if the host is WT and a runtime id is known, call `IWindowsTerminalPaneGateway.FocusPane(parentHwnd, runtimeId)`
- `FocusPane` selects the UIA tab item with `SelectionItemPattern.Select()` and falls back to `InvokePattern.Invoke()`

**Consequences:**
- Non-WT hosts keep the existing one-host-per-HWND behavior
- WT focus remains degradable: if runtime-id selection fails, parent window is foregrounded and title/process matching remains as fallback
- Live LocalOnly E2E now uses separate WT tabs and asserts both active-grid discovery and selected-tab focus

**Outcome:** All 647 unit tests pass. Trinity-H learnings recorded in history.md.

---

## Issue #18: Strict Response Validator + 6 Failure Classes for AI Auto-Detect

**Date:** 2026-05-08  
**Status:** Complete  
**Contributors:** Trinity (Services Dev), Tank (Tester)  

### Strict Parser Architecture — Trinity

**Decision:** `AiResponseParser.Parse` returns discriminated union `AiParseResult` instead of candidate list.

```csharp
internal abstract record AiParseResult
{
    internal sealed record Success(IReadOnlyList<AiCandidate> Candidates) : AiParseResult;
    internal sealed record Failure(AiFailureClass Class, string Reason) : AiParseResult;
}
```

**Strict Validation Rules:**
- Rejects empty stdout, prose, markdown fences, non-object roots
- Requires `candidates` field (array, not null)
- Rejects candidate non-objects, missing required fields, wrong JSON types
- Enforces case-sensitive type values: `issue` or `pr` only
- Enforces non-positive item ids and PR numbers
- Enforces confidence within inclusive [0.0, 1.0] range
- Accepts up to 3 valid candidates; sorts by confidence descending (array-order tiebreak), truncates excess

**Parser Contract:**
- Empty stdout is malformed (no success object)
- Empty candidates array is parse success (zero candidates, no reason)
- Non-empty success with 1–3 candidates returns `Success(candidates)`
- Any violation returns `Failure(class, reason)` where class is `MalformedJson` (JSON parse) or `SchemaViolation` (shape/type/range)

**Why:** Deterministic classification allows downstream service to route failures to appropriate handling without nested if/catch chains. Empty success (zero candidates) is distinct from schema violation so `AiDetectionService` can classify it as `NoCandidates` (not parser error).

### 6-Class Failure Classifier — Trinity

**Decision:** `AiFailureClass` enum lives in `src/Services/` with six deterministic failure classes:

```
Timeout
ProcessSpawn
ProcessFailure
MalformedJson
SchemaViolation
NoCandidates
```

**Classifier Routing in `AiDetectionService`:**

1. **Process spawn:** Try-catch wrapper around `IProcessRunner.RunAsync` start; classify start exceptions (missing binary, access denied) as `ProcessSpawn`.
2. **Timeout:** If process was killed AND `!ct.IsCancellationRequested`, classify as `Timeout`. If killed after user cancel, leave `FailureClass` null (not a failure).
3. **Process exit:** On nonzero exit (before parsing stdout), classify as `ProcessFailure`.
4. **Parser results:** Exit code zero → call `AiResponseParser.Parse(stdout)`:
   - `Failure(MalformedJson, ...)` → `FailureClass = MalformedJson`
   - `Failure(SchemaViolation, ...)` → `FailureClass = SchemaViolation`
   - `Success([])` (empty) → `FailureClass = NoCandidates`
   - `Success(candidates)` (non-empty) → proceed to threshold/apply logic

**Logging Contract:**

- **Start log:** `session_id`, `resolved_owner_repo`, `configured_timeout_seconds`
- **Debug logs:** `exact_prompt_sent`, `raw_stdout`, `raw_stderr` (no redaction)
- **End log:** `outcome`, `failure_class`, `reason`, `exit_code`, `candidate_count`, `top_confidence`, `applied_items`, `duration_ms`
- **Log levels:** `Timeout` and `NoCandidates` use WARNING; `MalformedJson`, `SchemaViolation`, `ProcessSpawn`, `ProcessFailure` use ERROR

**Observation Point:**
- `DetectionState.FailureClass` is nullable `AiFailureClass?`
- Tests and UI observe via `TryGetState(sid).FailureClass` (no new event arg)

**Why:** Centralizing failure classification in the service (not in parser, not in runner) allows parser to focus on JSON purity and service to own orchestration semantics. Six classes are sufficient for slice #18; future slices (timeout settings, retry policy, etc.) will extend logging and thresholds without new classes.

### Test Infrastructure Extensions — Tank

**Decision:** Extend shared test doubles for AI detection slices.

**FakeProcessRunner Extensions:**
- Already implements `IProcessRunner.RunAsync(fileName, args, cwd, timeoutSeconds, ct)`
- Add `SetResult(ProcessResult result)` for per-test canned outcomes
- Add `ThrowOnNextCall(Exception ex)` for process spawn failure paths

**CapturingLogger (New):**
- Lives at `tests/Integration/TestTools/CapturingLogger.cs`
- Implements `ILogger` to capture level/message/exception tuples
- Used by E2E failure-class tests for log-level assertions without file scraping

**Why:** FakeProcessRunner is already the shared copilot process boundary fake; extending it for canned results and exception support avoids test cloning. CapturingLogger is faster and more deterministic than scraping app log files for assertion.

**Outcome:**
- `tests/Services/AiResponseParserTests.cs` — ~28 strict-violation rows
- `tests/Services/AiDetectionServiceTests.cs` — classifier unit tests for all six classes
- `tests/Integration/AiDetectFailureIntegrationTests.cs` — six E2E tests (one per class)
- `tests/Integration/TestTools/FakeProcessRunner.cs` — extended with SetResult/ThrowOnNextCall
- `tests/Integration/TestTools/CapturingLogger.cs` — ILogger capture

---

## Issue #19: Repo Resolution + AI Menu Preconditions Gating

**Date:** 2026-05-08  
**Status:** Complete  
**Contributors:** Trinity (Services Dev), Morpheus (UI Dev), Tank (Tester), Coordinator (test stabilization)  

### Fork Parent Resolver via GH_PATH Environment — Trinity

**Decision:** Use `GH_PATH` environment variable override for fork parent lookup instead of adding an `IForkResolver` interface.

**Rationale:** Issue #19 needs fork parent detection via `gh repo view owner/repo --json parent --jq .parent.nameWithOwner`. The lookup must be best-effort, bounded to 5 seconds, and must not throw from repo resolution.

**Contract:**
- Production uses `gh` from PATH
- Tests can set `GH_PATH` to a fake executable
- If `gh` fails, times out, or returns no parent, resolution falls back to the remote repo
- No new dependency or service interface is needed

**Outcome:** GitService.TryResolveGitHubRepo implements full HTTPS/SSH/fork-parent chain with timeout protection.

### Window Event Hook Collection Serialization — Tank

**Decision:** Any integration test that starts `WindowEventHookService` belongs in `WindowEventHookCollection`.

**Why:** WinEvent hooks and real window title changes race when multiple classes run in parallel. The all-green integration directive needs these tests serialized, not retried.

**Applied To:**
- `TerminalTitleDetectionIntegrationTests`
- `WindowEventHookIntegrationTests`
- `TimerRefreshAfterFormCloseTests`

**Side Effect Discovered:** Collection-grouping changes shifted parallel test scheduling and exposed latent race in `IdeTrackingIntegrationTests.E2E_IdeWithFolderPath_ProcessKilled_NoDestroyEvent_GridMustClear`. Coordinator added collection attribute to serialize IDE tests alongside window-hook tests, resolving the race.

**Outcome:** All 118 integration tests green (10 LocalOnly skipped per policy). No races or flakes.

### AiMenuState Enum and Preconditions Gating — Trinity

**Decision:** `AiMenuState` enum gates menu enable/disable + tooltip messaging for AI auto-detect menu item.

```csharp
enum AiMenuState
{
    Unavailable,        // Service error or not initialized
    NoSession,          // No active session
    RepoNotFound,       // Repo resolution failed
    TelemetryBlocked,   // AI telemetry disabled in settings
    Ready               // All preconditions met, menu enabled
}
```

**Preconditions:**
1. Service initialized and healthy
2. Active session exists with valid CWD
3. GitService.TryResolveGitHubRepo succeeds
4. AI telemetry enabled (prior-tracking-data trust chain)

**UI Contract:**
- `ExistingSessionsVisuals.GetEvaluatedAiMenuItem()` returns internal accessor for Tank tests
- Menu DropDownOpening event triggers `AiDetectionService.EvaluateMenuState(sessionId)`
- Tooltip messaging via `AiDetectionTooltips` constants per state

**Outcome:** All menu states tested and integrated. UI wiring complete.

---

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
