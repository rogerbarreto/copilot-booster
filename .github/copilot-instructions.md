# Copilot Instructions

## Git Commits

- Keep commit messages concise and descriptive.
- Never add `Co-authored-by` trailers to git commits.

## Code Style

- C# with nullable enabled, WinForms (.NET 10).
- `dotnet format` enforced — run before committing.
- Internal classes visible to tests via `InternalsVisibleTo`.

## Build & Test

- Always use `--tl:off` for `dotnet build` and `dotnet test` (disables terminal logger). This flag does NOT work with `dotnet format`.
- xUnit (not MSTest).
- Validate assertions with integration tests whenever possible.
- All tests must pass before any release.

## Release Process

### Before Release

1. Decide version bump — patch for bug fixes, minor for new features (semver, no major bumps until GA). When unsure, ask before bumping. **Always check GitHub Releases (not local files) as the source of truth for the latest published version.**
2. Update version in both `src/CopilotBooster.csproj` and `installer.iss`.
3. Update `CHANGELOG.md` with the new version's changes.
4. Update `README.md` — add new features/sections when applicable, or at minimum verify version references are current.
5. Run `dotnet format` — ensure code is clean.
6. Run `dotnet test --tl:off` — all tests must pass.
7. Commit all changes with a descriptive message.
8. Tag with `v<version>` (e.g., `git tag v0.8.0`).

### Push Release

9. `git push origin main --tags` — push commit and tag.
10. The `v*` tag triggers the `release.yml` CI workflow which:
    - Publishes `CopilotBooster-win-x64.zip` (portable).
    - Builds `CopilotBooster-Setup.exe` via Inno Setup (installed via choco in CI).
    - Uploads both artifacts to a GitHub Release.
