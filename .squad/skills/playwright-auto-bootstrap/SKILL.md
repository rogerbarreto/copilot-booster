---
name: playwright-auto-bootstrap
description: Make .NET/xUnit Playwright integration tests self-install missing Chromium browsers and skip cleanly if bootstrap cannot complete.
---

# Playwright Auto-Bootstrap

Use this when Playwright integration tests fail locally or in CI with `Microsoft.Playwright.PlaywrightException` messages such as `Executable doesn't exist` or `Please run ... playwright install`.

## Pattern

1. Put all Playwright-consuming test classes in an xUnit collection, for example `[Collection("PlaywrightBootstrap")]`.
2. Add an `ICollectionFixture<T>` fixture that runs once per test process.
3. In `InitializeAsync`, guard with a static `SemaphoreSlim` and static `bool` so parallel discovery/classes do not install browsers more than once.
4. Probe availability by launching headless Chromium:

```csharp
using var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true }).ConfigureAwait(false);
```

5. On missing-browser exceptions only, run:

```csharp
var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
```

6. If install exits non-zero or throws, call `Assert.Skip(...)` so environmental gaps do not red-bar the suite.

## Notes

- Keep any CI-level `playwright install chromium` step; it remains idempotent and makes the fixture a fast probe.
- Do not catch all Playwright exceptions as missing browsers. Only bootstrap for messages that indicate the executable/cache is missing; real browser/test failures should stay red.
- For headed-browser tests, installing `chromium` also provides the headed executable; headless tests use the headless shell cache.
