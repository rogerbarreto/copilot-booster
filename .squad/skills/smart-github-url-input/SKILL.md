---
name: smart-github-url-input
description: Reusable WinForms pattern for textboxes that accept either a bare GitHub PR/Issue number or a full github.com URL.
---

# Smart GitHub URL Input

Use this when a textbox currently accepts a bare PR/Issue number but users commonly paste GitHub URLs.

## Pattern

1. Parse a bare positive integer first.
2. If that fails, call `GitHubLinkService.TryParseIssueOrPrUrl`.
3. If a URL was parsed, match `Owner/Repo` against the current form's configured remote map using `StringComparison.OrdinalIgnoreCase`.
4. Auto-select the matching remote and show `🔀 Switched remote to <remote> (<owner>/<repo>)`.
5. If no remote matches, hard-error: `❌ This URL points to <owner>/<repo>, which is not a configured remote.`
6. Validate and persist using the URL-indicated type (`/pull/` → `pr`, `/issues/` → `issue`).
7. In dual-panel dialogs, do not auto-flip radio buttons. Keep UI state stable, but route validation/creation by the validated URL type.

## Parser rules

Accept `https://github.com/<owner>/<repo>/(issues|pull)/<positive-number>` and scheme-less `github.com/...`, including trailing slash, query, fragment, and surrounding whitespace. Reject `http://`, non-`github.com` hosts, `pulls`, extra path segments, and non-positive numbers.
