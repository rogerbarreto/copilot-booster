# Copilot Availability Check — Canonical Pattern

**Date:** 2026-05-09  
**Author:** Trinity  
**Domain:** Services / CLI Tool Detection  
**Codebase:** copilot-booster (C#, .NET 10, WinForms)

---

## Problem Statement

Need a fast, deterministic, non-flaky way to check whether GitHub Copilot CLI is installed and available on the user's machine.

**Anti-patterns to avoid:**
- Running `copilot --version` with stdout redirection (blocks on background subprocess inheriting stdout handle)
- Ad-hoc `File.Exists` checks scattered across codebase (duplication, inconsistency)
- Multiple implementations of "is copilot installed?" (loose ties)

---

## Canonical Solution

Two-tier architecture:

### 1. Path Resolution: `CopilotLocator.FindCopilotExe()`

Single source of truth for resolving the copilot executable path.

**Resolution order:**
1. WinGet prerelease: `%LOCALAPPDATA%\Microsoft\WinGet\Packages\GitHub.Copilot.Prerelease_Microsoft.Winget.Source_8wekyb3d8bbwe\copilot.exe`
2. WinGet stable: `%LOCALAPPDATA%\Microsoft\WinGet\Packages\GitHub.Copilot_Microsoft.Winget.Source_8wekyb3d8bbwe\copilot.exe`
3. `where copilot` (searches PATH)
4. Fallback: `"copilot.exe"` (bare string, signals "not found")

**Validation:** Each candidate is checked with `File.Exists` before returning.

### 2. Availability Check: `ICopilotProbe.IsCopilotAvailable()`

Single source of truth for "is copilot installed?".

**Strategy:** File-existence check (NOT process execution).

```csharp
private static bool ProbeVersion(string resolvedPath)
{
    if (string.IsNullOrWhiteSpace(resolvedPath)) return false;
    if (string.Equals(resolvedPath, "copilot.exe", StringComparison.OrdinalIgnoreCase)) return false;
    
    try
    {
        return File.Exists(resolvedPath);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException 
                                  or ArgumentException or NotSupportedException)
    {
        return false;
    }
}
```

---

## Usage

### For Consumers

**Path resolution:**
```csharp
var copilotPath = CopilotLocator.FindCopilotExe();
var processResult = await _processRunner.RunAsync(copilotPath, args, cwd, timeout, ct);
```

**Availability check:**
```csharp
if (!_copilotProbe.IsCopilotAvailable())
{
    return AiMenuState.CopilotUnavailable;
}
```

**UI icon extraction:**
```csharp
internal static readonly string CopilotExePath = CopilotLocator.FindCopilotExe();
var copilotIcon = TryGetExeIcon(Program.CopilotExePath);
```

### For Tests

**Inject custom path:**
```csharp
var probe = new CopilotProbe(() => @"C:\fake\copilot.exe", _ => true);
Assert.True(probe.IsCopilotAvailable());
```

---

## Why File Existence Is Sufficient

1. **Locator already validates:** WinGet paths and `where copilot` output checked with `File.Exists`
2. **Auth/network checks belong elsewhere:** Auth failures → `AiFailureClass.ProcessFailure` in `AiDetectionService.InvokeAsync`
3. **Performance:** No process spawn → <1ms, no stdout redirection trap, no timeout flakes
4. **Bare `"copilot.exe"` means not found:** Locator returns this when nothing resolved, probe treats as `false`

---

## Why NOT Process Execution

**The stdout redirection trap:**

When you run `copilot --version` with `RedirectStandardOutput = true`, and copilot spawns a background auto-update subprocess, that child inherits the stdout handle. The pipe stays open even after copilot prints output, because the background process hasn't exited. `WaitForExit(timeout)` waits for the entire process tree and times out.

**Reproduction (100% deterministic on WinGet installs):**
```powershell
$cp = "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GitHub.Copilot.Prerelease_...\copilot.exe"
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName=$cp; $psi.ArgumentList.Add("--version")
$psi.RedirectStandardOutput=$true
$psi.UseShellExecute=$false
$p=[System.Diagnostics.Process]::Start($psi)
$p.WaitForExit(5000)  # → False (timeout)
```

**Impact:** Probe returns false even though copilot is installed and working. Menu permanently disabled.

---

## Design Principles

1. **Single source of truth for path resolution:** `CopilotLocator.FindCopilotExe()`
2. **Single source of truth for availability:** `ICopilotProbe.IsCopilotAvailable()`
3. **No ad-hoc checks:** All sites must delegate to these two APIs
4. **Fast and deterministic:** File-existence check, no process spawn, <1ms
5. **Thread-safe caching:** Per-path cache with lock, invalidate on path change
6. **Test seams preserved:** Inject custom path getter and existence checker

---

## Anti-Patterns

❌ **Don't** run `copilot --version` with stdout redirection  
❌ **Don't** use ad-hoc `File.Exists` checks outside of `CopilotLocator`  
❌ **Don't** shell out `where copilot` outside of `CopilotLocator`  
❌ **Don't** implement multiple "is copilot installed?" methods  

✅ **Do** use `CopilotLocator.FindCopilotExe()` for paths  
✅ **Do** use `ICopilotProbe.IsCopilotAvailable()` for availability  
✅ **Do** let failures surface in the invocation layer  
✅ **Do** treat file-existence as sufficient for "installed" status  

---

## Related Files

- `src/Services/CopilotLocator.cs` — path resolution
- `src/Services/CopilotProbe.cs` — availability check
- `src/Services/ICopilotProbe.cs` — interface
- `src/Services/AiDetectionService.cs` — consumer
- `src/Program.cs` — `CopilotExePath` static field for UI icons
- `tests/Services/CopilotProbeTests.cs` — unit tests
- `tests/Integration/CopilotProbeIntegrationTests.cs` — LocalOnly integration tests

---

## When to Use This Pattern

- Detecting any locally-installed CLI tool (git, gh, node, python, etc.)
- Need fast, deterministic availability check
- Tool may spawn background subprocesses (auto-update, telemetry)
- Locator already validates paths with file-system checks

**Key insight:** If the locator validates paths, the probe can just re-check existence. Auth/network/execution failures are handled at invocation time, not detection time.
