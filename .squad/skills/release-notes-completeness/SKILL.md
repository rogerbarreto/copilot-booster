# Release Notes Completeness Sweep

## Problem

PR-driven changelogs often miss features that land in independent commits or merged branches without explicit CHANGELOG edits. Reviewers update the changelog only for their own PRs, so commits from other branches, squash merges, or follow-up fixes never appear in the release notes.

## Solution

Before releasing a version, perform a systematic `git log <prev-tag>..<this-tag>` sweep to find all commits and identify missing release notes.

## Pattern

1. **Identify the tag range:**
   ```bash
   git log --oneline v0.21.0..v0.22.0
   ```
   This shows all commits between the previous release tag and the current HEAD.

2. **Categorize by feature area:**
   - Group commits by logical theme (e.g., Warp support, performance, session detection)
   - Identify the user-visible behavior each commit enables
   - Note any internal refactorings or bug fixes

3. **Verify CHANGELOG coverage:**
   - Cross-reference each commit against the CHANGELOG [version] entry
   - Look for commits that are missing entirely

4. **Draft bullets in release voice:**
   - Match the tone of existing entries (concrete, technical, names user-visible behavior)
   - Brief mention of internals only when illuminating
   - Use the same section structure (### Added, ### Changed, ### Fixed)

5. **Update GitHub Release body:**
   - Extract new content to a temp file
   - Use `gh release edit <tag> --notes-file <temp>` to update the published release
   - The Release body mirrors the CHANGELOG, so both must be in sync

## Example

From v0.22.0 sweep:
- Commits `3188da8`, `2e9a1a6`, `b56acde`, `1f03332` → "**Warp terminal host integration**" bullet
- Commits `6e56c04`, `0ce9954`, `36f7e31` → "Memory bloat" and "rescanning perf" bullets under ### Fixed
- Commits `a0a5ec9`, `714572b`, `d680f94` → "**Resume rebind: detect pre-existing Copilot sessions**" bullet

## Implementation

Run this as part of release closure, before tagging:

```bash
# Find commits since last tag
git log --oneline v0.21.0..v0.22.0 > release-commits.log

# Review commits manually, looking for missed features
# Update CHANGELOG.md [version] section
# Update GitHub Release body
gh release edit v0.22.0 --notes-file release-notes.txt
```

## Notes

- The GitHub Release body should exactly mirror the CHANGELOG [version] section
- Don't create new section headings unless necessary; fold under existing ### Added / ### Changed / ### Fixed
- Commit the CHANGELOG change to main after the release is published
