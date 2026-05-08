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