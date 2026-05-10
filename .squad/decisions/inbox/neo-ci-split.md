# CI split: Test workflow owns validation; Release delegates to it

**Date:** 2026-05-10
**Owner:** Neo
**Status:** Delivered

## Decision

Create `.github/workflows/test.yml` as the single validation workflow for build, format, unit tests, Playwright browser install, and integration tests. It runs on pull requests to `dev`, `preview`, `main`, and `insider`; pushes to `main`; merge queue `merge_group`; and `workflow_call`.

## Release architecture

`.github/workflows/release.yml` remains tag-only (`v*`) and delegates validation to `test.yml` with:

```yaml
jobs:
  test:
    uses: ./.github/workflows/test.yml
  signing-info:
    needs: test
```

The signing-info job stays unchanged and only runs after the reusable test workflow passes.

## Retired workflow

Deleted `.github/workflows/squad-ci.yml` because it was a scaffolding stub that only echoed a placeholder. Repository ruleset `PR enforment` requires the `test` status context, not `Squad CI`, so no GitHub settings change is required for the old stub name.
