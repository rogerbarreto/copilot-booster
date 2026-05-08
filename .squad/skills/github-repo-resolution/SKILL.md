---
name: "github-repo-resolution"
description: "Normalize GitHub remotes from git -C cwd and prefer fork parents"
domain: "csharp-services"
confidence: "medium"
source: "issue-19"
---

# GitHub Repo Resolution

Use when a service needs the canonical GitHub repo for a Copilot session cwd.

## Service API

```csharp
var result = GitService.ResolveGitHubRepo(cwd);
var repo = GitService.TryResolveGitHubRepo(cwd);
```

## Rules

* Use `git -C <cwd>`, never literal `.git` checks.
* Probe `upstream` first, then `origin`.
* Return `GitHubRepoResolution.NotAGitRepo` when `git -C cwd rev-parse --is-inside-work-tree` fails.
* Return `GitHubRepoResolution.NoRemote` when neither remote exists.
* Return `GitHubRepoResolution.NonGitHubRemote` when the selected remote exists but host is not `github.com`.
* Do not walk parent directories manually.

## URL Forms

| Form | Result |
|------|--------|
| `https://github.com/owner/repo(.git)?` | `owner/repo` |
| `git@github.com:owner/repo(.git)?` | `owner/repo` |
| `ssh://git@github.com/owner/repo(.git)?` | `owner/repo` |

Host comparison is case-insensitive. Owner and repo casing are preserved.

## Fork Parent

After parsing a GitHub remote, run:

```powershell
gh repo view owner/repo --json parent --jq .parent.nameWithOwner
```

Use a 5 second timeout. Fall back to the parsed remote if `gh` is unavailable, fails, times out, or returns no parent. Tests can set `GH_PATH` to a fake executable.
