---
name: git-integration-test-fixtures
description: Build real git source, bare remote, and local clone fixtures for CopilotBooster integration tests.
---

# Git Integration Test Fixtures

## Use when

* Testing `WorkspaceCreationService` or `GitService` end-to-end behavior.
* Need local clone behind remote by one or more commits.
* Need checked-out branch, no-upstream branch, or bad remote edge cases.

## Fixture

Use `tests/Integration/TestTools/GitTestRepo.cs`.

```csharp
using var repo = await GitTestRepo.CreateAsync(TestContext.Current.CancellationToken);
await repo.CreateRemoteBranchAsync("feature", TestContext.Current.CancellationToken);
var remoteTip = await repo.CommitAndPushAsync("feature", TestContext.Current.CancellationToken);
```

## Pattern

1. Create source repo with `main`.
2. Clone it as a bare remote.
3. Clone bare remote as the local repo under test.
4. Push new commits from source after local clone exists.
5. Call product APIs against `repo.LocalPath`.
6. Assert refs through `GitTestRepo.RevParseAsync` or `GitTestRepo.HeadAsync`.

## Rules

* Route git commands through `GitService.RunGitAsync`.
* Dispose `GitTestRepo` to delete all temp directories.
* Use a serialized collection when mutating `Program._settings`.
* For worktree tests, set `Program._settings.WorkspacesDir = repo.WorkspacesPath`.

## Edge cases

* Checked-out branch blocks `git fetch remote branch:branch`; fallback must refresh remote-tracking refs before using `origin/<branch>`.
* No-upstream local branch should fetch fallback remote but keep local tip.
* Bogus remote should assert `success=false`, non-empty error, unchanged effective source ref.