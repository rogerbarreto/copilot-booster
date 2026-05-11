---
name: ai-detection-integration-tests
description: Wire AiDetectionService into a real SessionGridVisuals DataGridView integration test with a fake copilot process runner.
---

# AI Detection Integration Tests

## Pattern

Use when testing AI detection without launching real `copilot -p`.

1. Create a temp session-state root and session folder.
2. Write minimal `workspace.yaml` plus `events.jsonl`.
3. Build a real `DataGridView` with the seven MainForm columns.
4. Create `ActiveStatusTracker` and `SessionGridVisuals`.
5. Set `visuals.GetGitHubValue` to MainForm's compact format: `PR#42`, `I#8`.
6. Use `FakeProcessRunner` from `tests/Integration/TestTools/FakeProcessRunner.cs`.
7. Construct `AiDetectionService` with the explicit CWD resolver overload:

```csharp
new AiDetectionService(api, processRunner, _ => repoRoot, toastMessages.Add, poller, sessionRoot)
```

8. Subscribe to `DetectionStateChanged`:

```csharp
var snapshot = tracker.IncrementalRefresh(sessions);
visuals.UpdateGridIncremental(snapshot);
grid.InvalidateCell(grid.Rows[0].Cells["GitHub"]);
```

9. Await idle with a bounded poll.
10. Assert process args, `GitHubTrackingService.Load(sid)`, grid cell value, toast, and on-disk `github-tracking.json`.

## Notes

- Use `[StaFact]` for WinForms controls.
- Use a fake `GitHubApiService` process runner so `GetPullRequestAsync` returns deterministic metadata.
- Cleanup both temp session root and `SessionStateService.GetSessionDir(sid)`.

## Failure class tests

Use when testing strict AI detection failures without UI surfacing.

1. Build the same real grid wiring as the happy path.
2. Set `Program.Logger` to `CapturingLogger` and restore the previous logger in `Dispose()`.
3. Configure `FakeProcessRunner`:
   - timeout: `new ProcessResult(-1, "", "", true)`
   - process spawn: `ThrowOnNextCall(new Win32Exception(...))`
   - process failure: nonzero `ExitCode`, even if stdout is parseable
   - malformed JSON: exit zero with prose, fences, or invalid JSON
   - schema violation: exit zero with parsed JSON that fails strict candidate validation
   - no candidates: exit zero with `{"candidates":[]}`
4. Await `StartDetectionAsync(sid)` completion.
5. Assert tracking items stay empty, toast list stays empty, `TryGetState(sid).FailureClass` matches, and a captured log entry contains the enum name at the expected level.

Expected levels: `Timeout` and `NoCandidates` are `Warning`; `MalformedJson`, `SchemaViolation`, `ProcessSpawn`, and `ProcessFailure` are `Error`.

## Menu gating E2E pattern

Use when testing context-menu preconditions.

1. Create fixture cwd values as temp git repos or plain folders.
2. Use `ExistingSessionsVisuals.GetEvaluatedAiMenuItem(sid, cwd)` for the AI leaf item.
3. Set `ExistingSessionsVisuals.AiDetectionService` to the real service and `GetSessionPaths` to the fixture cwd map.
4. Assert `ToolStripMenuItem.Enabled` and `ToolTipText` exactly.
5. For prior-tracking precedence, save `GitHubTrackingData.Owner/Repo`, start detection, and assert `FakeProcessRunner` prompt uses that owner/repo instead of the cwd remote.

## Undecided and error icon dismiss pattern

Use when testing the Issue #22 status corner flow.

1. Put the test class in `[Collection(WindowEventHookCollection.Name)]` if it touches `SessionGridVisuals` status corner or WinForms timer behavior.
2. Inject `SessionGridVisuals.MessageBox = new RecordingMessageBox()` where the fake implements `IMessageBox` and records `Title` plus `Body`.
3. Trigger detection with `FakeProcessRunner`:
   - low confidence candidate -> `DetectionStatus.Undecided`, `GetCornerIconForSession(sid)` non-null
   - all candidates already linked -> `UndecidedReason.AllAlreadyLinked`
   - nonzero exit -> `DetectionStatus.Error`, `FailureClass == ProcessFailure`
4. Click the corner via `HandleGitHubCellClick(rowIndex, pointInsideStatusRegion, cellBounds)`.
5. Assert recorded message body, `AiDetectionService.TryGetState(sid).Status == Idle`, and `GetCornerIconForSession(sid) == null`.
6. Click outside `GetStatusIconRegion(cellBounds)` to assert `OnGitHubColumnClick` fallthrough remains intact for undecided and error states.

## Real `copilot -p` LocalOnly pattern

Use when a test must cross the real Copilot CLI prompt boundary.

1. Mark the class or method with `[Trait("Category", "LocalOnly")]` and the method with `[LocalOnlyFact]`.
2. Pick a fixture from `~/.copilot/session-state/*` where `workspace.yaml` exists and `events.jsonl` is non-empty.
3. Read `cwd:` from `workspace.yaml`, then `Assert.Skip` if it is missing, unavailable, or `GitService.TryResolveGitHubRepo(cwd)` returns null.
4. Copy the fixture folder under `Path.GetTempPath()` using a fresh session id, and rewrite the copied `workspace.yaml` id plus cwd.
5. Construct `AiDetectionService` with production `ProcessRunner`, real `GitHubApiService`, default `LauncherSettings.CreateDefault().AiDetection`, temp session-state root, and temp app-log root.
6. Wait up to configured timeout plus a small buffer. Accept `Idle`, `Undecided`, and `Error` with `NoCandidates`. Skip on `ProcessSpawn` or `ProcessFailure` because these usually mean local copilot/auth setup is missing.
7. Clean the temp fixture root and `SessionStateService.GetSessionDir(sessionId)`.

## Parallel detection E2E pattern

Use when asserting no queueing across sessions.

1. Create a real grid and real `AiDetectionService`.
2. Add five session rows with distinct ids and prior `GitHubTrackingData.Owner/Repo` so repo resolution is deterministic.
3. Use one shared fake `IProcessRunner` keyed by the `--add-dir` session folder. Each call records start and returns a per-session `TaskCompletionSource`.
4. Start all detections without awaiting each individually.
5. Poll until every service state is `Running` and every fake invocation has started before releasing any result.
6. Release completions one at a time and assert the released session reaches `Idle` while unreleased sessions remain `Running`.
