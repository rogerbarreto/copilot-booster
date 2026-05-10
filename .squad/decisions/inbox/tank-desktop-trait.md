# Decision Note: RequiresInteractiveDesktop Test Trait

**Date:** 2026-05-10
**Author:** Tank
**Status:** Inbox

## Decision

Use `[Trait("Category", "RequiresInteractiveDesktop")]` for integration tests that require an interactive Windows desktop.

## Meaning

The trait means the test spawns real windows, drives terminal/IDE processes, or relies on global WinEvent hooks such as `EVENT_OBJECT_NAMECHANGE`, window create/destroy, or foreground-change notifications.

## CI Filter

Hosted CI excludes these tests with xUnit v3 `-notrait` flags:

```powershell
dotnet run --project tests/CopilotBooster.IntegrationTests.csproj -c Release -- -notrait "Category=LocalOnly" -notrait "Category=RequiresInteractiveDesktop"
```

## When Future Tests Should Opt In

Mark a test or whole class with this trait when it:

- launches `cmd.exe`, `wt.exe`, Warp, `mspaint.exe`, IdeSimVS, or another real terminal/IDE/window process;
- waits for `WindowEventHookService` events from the OS;
- depends on global WinEvent hooks firing for externally spawned windows;
- requires a real interactive desktop rather than a headless or hosted runner desktop.

Use class-level scope when every test in the class has this dependency. Use method-level scope for mixed classes.
