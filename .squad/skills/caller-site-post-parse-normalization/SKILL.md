# Skill: Caller-Site Post-Parse Normalization Seam

## When to use

Use when a parser should stay pure but production needs environment-dependent fallbacks.

## Pattern

```csharp
internal static string ResolveAfterParse(
    string parsedValue,
    int processId,
    Func<int, string?> probe)
{
    if (!string.IsNullOrWhiteSpace(parsedValue) && !IsKnownSentinel(parsedValue))
    {
        return parsedValue;
    }

    string? probedValue;
    try
    {
        probedValue = probe(processId);
    }
    catch
    {
        probedValue = null;
    }

    var trimmedProbe = probedValue?.Trim();
    if (!string.IsNullOrWhiteSpace(trimmedProbe))
    {
        return trimmedProbe;
    }

    return string.Empty;
}
```

## Rules

- Parser input only: text plus explicit caller fallback.
- No environment defaults inside the parser.
- Put process, settings, file-system, and OS probes at the caller site.
- Inject probe delegates so tests can supply deterministic fakes.
- Normalize sentinels before accepting parsed values.
- Catch probe exceptions because process and file-system state can race.

## Copilot Booster example

- File: `src/Services/CopilotLogWatcherService.cs`.
- Parser chain: JSON CWD, debug-line CWD, explicit fallback, empty string.
- Caller chain: parsed CWD, `Win32ProcessCwd.Get(pid)`, absolute `Program._settings.DefaultWorkDir`, empty string.
