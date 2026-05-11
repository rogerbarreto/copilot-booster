---
name: "localappdata-test-isolation"
description: "Isolate tests for services that write under %LOCALAPPDATA% without touching the real user profile."
domain: "testing"
confidence: "high"
source: "earned"
---

## Context
Use this skill when a unit test exercises code that writes beneath `%LOCALAPPDATA%`, such as `%LOCALAPPDATA%\CopilotBooster\models-cache.json`.

## Patterns
- Create a unique temp directory per test instance.
- Save the original `LOCALAPPDATA` environment value.
- Set `LOCALAPPDATA` before constructing the service under test.
- Restore the original environment value in `Dispose` or `finally`.
- Delete the temp tree during cleanup.
- Production code should resolve `Environment.GetEnvironmentVariable("LOCALAPPDATA")` first, then fall back to `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)`, so tests can redirect the location deterministically.

## Examples
`tests\Services\CopilotModelsServiceTests.cs` uses a per-test temp `%LOCALAPPDATA%` root and writes/reads `CopilotBooster\models-cache.json` there.

## Anti-Patterns
- Do not write tests against the real user `%LOCALAPPDATA%` folder.
- Do not assume `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` honors process-level `LOCALAPPDATA` changes on Windows.
- Do not leave global environment variables modified after a test.
