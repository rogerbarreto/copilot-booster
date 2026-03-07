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
- All tests must pass before any release — both unit tests and integration tests.
- **Bug fixes require a failing test first.** Before fixing a bug, write a test that reproduces the issue and confirm it fails. Only then apply the fix and verify the test passes.
- **Unit tests:** `dotnet run --project tests/CopilotBooster.Tests.csproj -c Release`
- **Integration tests:** `dotnet run --project tests/CopilotBooster.IntegrationTests.csproj -c Release`

## Release Process

### Before Release

1. Decide version bump — patch for bug fixes, minor for new features (semver, no major bumps until GA). When unsure, ask before bumping. **Always check GitHub Releases (not local files) as the source of truth for the latest published version.**
2. Update version in both `src/CopilotBooster.csproj` and `installer.iss`.
3. Update `CHANGELOG.md` with the new version's changes.
4. Update `README.md` — add new features/sections when applicable, or at minimum verify version references are current.
5. Run `dotnet format` — ensure code is clean.
6. Run unit tests — all must pass.
7. Run integration tests — all must pass.
8. Commit all changes with a descriptive message.
9. Tag with `v<version>` (e.g., `git tag v0.18.0`).

### Push Release

10. `git push origin main --tags` — push commit and tag.
11. The `v*` tag triggers the `release.yml` CI workflow which:
    - Builds, format-checks, runs unit tests and integration tests.
    - On success, dispatches to the private `copilot-booster-signing` repo.
12. The signing workflow (self-hosted runner) then:
    - Publishes and signs `CopilotBooster.exe` with Certum code signing certificate.
    - Builds and signs `CopilotBooster-Setup.exe` via Inno Setup.
    - Creates the GitHub Release with both signed artifacts.
