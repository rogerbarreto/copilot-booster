# Skill: Win32 PEB Process CWD Probe

## When to use
Reading the current working directory of another process on Windows (64-bit only).

## Pattern

```csharp
[SupportedOSPlatform("windows")]
internal static class Win32ProcessCwd
{
    // P/Invoke declarations
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        SafeProcessHandle handle, int infoClass, out PROCESS_BASIC_INFORMATION pbi,
        int size, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        SafeProcessHandle handle, IntPtr baseAddress, byte[] buffer,
        int size, out int bytesRead);

    // Constants
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint PROCESS_VM_READ = 0x0010;
    private const int ProcessBasicInformation = 0;

    // Cache: (pid, startTime) → cwd
    private static readonly ConcurrentDictionary<(int, DateTime), string?> s_cache = new();

    internal static string? Get(int pid)
    {
        // 1. Get process start time for cache key (fail → return null)
        // 2. Check cache
        // 3. OpenProcess → SafeProcessHandle (auto-disposes via using)
        // 4. NtQueryInformationProcess → PEB address
        // 5. ReadProcessMemory → RTL_USER_PROCESS_PARAMETERS pointer from PEB
        // 6. ReadProcessMemory → UNICODE_STRING CurrentDirectory.DosPath
        // 7. Cache and return
        // ALL steps wrapped in try/catch → null on any failure
    }
}
```

## Key constraints

- **Handle safety:** Always use `SafeProcessHandle` (wraps `CloseHandle`). Never raw `IntPtr`.
- **64-bit only:** Copilot CLI is 64-bit Node.js. Skip WoW64 handling. Document assumption.
- **Never throw:** All failures return `null`. Log at `Debug` level only.
- **Cache key:** `(pid, processStartTime)` because PIDs recycle on Windows.
- **PEB offsets (64-bit):** `PEB.ProcessParameters` at offset 0x20, `CurrentDirectory.DosPath` at offset 0x38 inside `RTL_USER_PROCESS_PARAMETERS`.

## Codebase conventions

- Follow `Win32JobObject.cs` pattern for P/Invoke style (`DllImport`, `SetLastError`)
- `[SupportedOSPlatform("windows")]` attribute required
- Namespace: `CopilotBooster.Services`
- No `[ExcludeFromCodeCoverage]` — static methods are testable

## Testing

- Integration test: spawn child process with known CWD, assert `Get(pid)` returns it
- Edge cases: `Get(int.MaxValue)` → null, `Get(-1)` → null, process exits mid-probe → null
- Use `[Trait("Category", "LocalOnly")]` for live Windows tests
