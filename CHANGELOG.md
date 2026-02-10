# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.0.3] - 2026-02-10

### Added

- **Session search** — search box in the Existing Sessions tab filters sessions as you type, matching title/summary first and falling back to metadata (cwd, session id).
- 10 new unit tests for search functionality.

## [0.0.2] - 2026-02-06

### Added

- **Window focus for active sessions** — clicking an active session in the jump list now focuses the existing terminal window instead of launching a duplicate.
- **Unit test suite** — 72 xUnit tests covering all testable business logic (models, services, argument parsing).
- **Test gate in release pipeline** — builds, format checks, and tests must pass before publishing.
- **XML documentation** on all public and internal members.
- **`.editorconfig`** adopted from microsoft/agent-framework with project-specific relaxations.
- **`dotnet format` verification** in the CI pipeline as a pre-release check.

### Changed

- **SOLID architecture refactor** — extracted services (`SessionService`, `PidRegistryService`, `WindowFocusService`, `LogService`, `CopilotLocator`, `JumpListService`) from monolithic `Program.cs`.
- **Singleton MainForm** — prevents duplicate UI windows; signals existing instance to switch tabs.
- Expanded single-line conditional and try-catch blocks to multi-line for readability.
- Moved `RuntimeIdentifier` from project file to publish-time only (`-r win-x64`), fixing MSIL/AMD64 architecture mismatch warning.
- Added `ExcludeFromCodeCoverage` to UI forms and P/Invoke-heavy code.

### Fixed

- **Window focus** — uses `keybd_event` (Alt key) trick to bypass Windows `SetForegroundWindow` restrictions; matches terminal window by cmd.exe process ID instead of window title.
- Jump list no longer requires custom `AppUserModelID`; keeps form visible for taskbar integration.

## [0.0.1] - 2026-02-04

### Added

- Initial release of Copilot App — a Windows taskbar companion for GitHub Copilot CLI.
- Taskbar-pinnable launcher with custom icon.
- Jump list integration with active and recent sessions.
- Session resume via `--resume` flag.
- Working directory picker for new sessions.
- Settings UI with configurable allowed tools, directories, and default work directory.
- IDE picker integration (VS Code, Rider, Visual Studio).
- PID registry for tracking active launcher instances.
- Install script (`install.ps1`) for automated setup.
- MIT license.
- GitHub Actions release workflow with `.zip` artifact publishing.

[0.0.3]: https://github.com/rogerbarreto/copilot-app/compare/v0.0.2...v0.0.3
[0.0.2]: https://github.com/rogerbarreto/copilot-app/compare/v0.0.1...v0.0.2
[0.0.1]: https://github.com/rogerbarreto/copilot-app/releases/tag/v0.0.1
