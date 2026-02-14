# Copilot Instructions

## Git Commits

- Keep commit messages concise and descriptive.

## Code Style

- C# with nullable enabled, WinForms (.NET 10).
- `dotnet format` enforced — run before committing.
- Internal classes visible to tests via `InternalsVisibleTo`.

## Testing

- xUnit (not MSTest).
- Validate assertions with integration tests whenever possible.

## Release

- Follows [Semantic Versioning](https://semver.org/): patch for bug fixes, minor for new features. No major bumps until GA.
- When unsure about version bump type, ask before bumping.
- Update version in both `CopilotApp.csproj` and `installer.iss`.
- Update `CHANGELOG.md` before tagging.
- Update `README.md` for every release: add new features/sections when applicable, or at minimum verify version references are current.
- Push `v<version>` tag to trigger release CI.
