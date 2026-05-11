---
name: lazy-process-probe
description: Cache a small process availability probe by resolved executable path.
---

# Lazy process probe

Use when a UI gate needs to know whether an optional CLI exists without probing at startup.

## Pattern

```csharp
internal interface IToolProbe
{
    bool IsToolAvailable();
    void InvalidateCache();
}
```

## Rules

- Resolve the executable from settings on every public call.
- Cache the boolean result with the resolved path.
- If the path changes, probe again.
- Keep the probe small. Use `ProcessStartInfo`, `UseShellExecute = false`, and a short timeout.
- Catch `Win32Exception` for missing binaries and return false.
- Log one info line when a real probe runs with path and result.
- Expose `InvalidateCache()` for settings save handlers.

## Copilot Booster example

`CopilotProbe` runs `<configured path or copilot> --version` with a 5 second timeout and is wired into `AiDetectionService.EvaluateMenuState`.