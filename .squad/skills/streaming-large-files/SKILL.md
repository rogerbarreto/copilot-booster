# Skill: Streaming Large Files (Memory-Bounded Parsing)

## Anti-Pattern: Full-File Materialization for Unbounded Files

**Symptom:** Process RSS grows to multiple GB when reading log files, journal files, or other append-only files that grow without rotation.

**Code smell:**
```csharp
// ❌ BAD — allocates entire file as string + split array (2x file size in LOH)
string[] lines = File.ReadAllText(path).Split('\n');
// or
string[] lines = reader.ReadToEnd().Split('\n');
```

**Why this breaks:**
- Large strings (> 85 KB) allocate on LOH (Large Object Heap)
- LOH compaction is rare (only Gen-2 blocking GC)
- `ReadToEnd()` on 678 MB log → ~1.4 GB allocation (UTF-16 internal)
- `Split('\n')` creates string array of similar size → another ~1.4 GB
- With 7 concurrent Copilot processes, RSS balloons to 4+ GB

**Real-world trigger:**
- Copilot CLI process logs in `~/.copilot/logs/` grow indefinitely (NO rotation)
- FileSystemWatcher event fires on every log write
- Startup rescan reads ALL existing logs

## Fix: Streaming TextReader Overload

**Pattern:**
1. Add a streaming `TextReader` overload that never materializes the whole file
2. Refactor existing array overload to wrap `StringReader` (preserves tests)
3. Update production call sites to use streaming overload

**Example from `CopilotLogWatcherService.cs`:**

```csharp
// ✅ GOOD — Streaming overload (production path)
internal static IReadOnlyList<(string sessionId, string cwd)> TryParseLogContent(TextReader reader, string? fallbackCwd = null)
{
    var sessions = new List<string>();
    string? cwdFromJson = null;
    string? cwdFromDebugLine = null;
    var jsonBuilder = new StringBuilder();
    bool collectingJson = false;
    int braceDepth = 0;

    string? line;
    while ((line = reader.ReadLine()) != null)  // ← Line-by-line, O(1) memory per line
    {
        var trimmed = line.Trim();

        // Extract session IDs, cwd values — same logic as before
        // ...

        // Collect multi-line JSON (telemetry blocks)
        if (!collectingJson && trimmed.Contains("[Telemetry] cli.telemetry:"))
        {
            collectingJson = true;
            jsonBuilder.Clear();
            braceDepth = 0;
            continue;
        }

        if (collectingJson)
        {
            jsonBuilder.AppendLine(trimmed);
            // Track brace depth to find JSON end
            // ...
        }
    }

    // Return small output list (sessionId, cwd) tuples
    return sessions.Select(sid => (sid, cwd)).ToList();
}

// ✅ GOOD — Array overload (test compatibility)
internal static IReadOnlyList<(string sessionId, string cwd)> TryParseLogContent(string[] lines, string? fallbackCwd = null)
{
    using var reader = new StringReader(string.Join('\n', lines));
    return TryParseLogContent(reader, fallbackCwd);
}
```

**Production call site:**
```csharp
// ✅ GOOD — Stream directly from file
IReadOnlyList<(string sessionId, string cwd)> sessions;
using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
using (var reader = new StreamReader(fs, Encoding.UTF8))
{
    sessions = TryParseLogContent(reader);  // ← Streaming overload
}
```

## Allocation Budget

**Before (array overload):**
- 678 MB log → ~2.8 GB allocated (ReadToEnd + Split)

**After (streaming overload):**
- 678 MB log → ~10 MB allocated (StreamReader buffer + output list)

**Tank's regression test target:**
- 50 MB synthetic log → < 25 MB allocation budget
- Verified via `GC.GetTotalAllocatedBytes(precise: true)` delta

## When to Apply This Pattern

**Apply streaming when:**
- File grows indefinitely (logs, journals, append-only files)
- File size can exceed 10 MB
- Multiple files read concurrently (e.g., FileSystemWatcher events)
- Parsing can be done line-by-line (no need to backtrack)

**Don't apply when:**
- File is small (< 1 MB) and bounded
- Parsing requires random access or multiple passes
- File is a config file with known max size
- Tests use array fixtures and streaming adds no value

## Other Safe Patterns for Large Files

**Tail-only read (when you only need the end):**
```csharp
// ✅ GOOD — Backward seek + ReadLastLine for events.jsonl
string? lastLine = ReadLastLine(path);

private static string? ReadLastLine(string path)
{
    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    // Seek backward from end, find last newline, read forward
    // ...
}
```

**Memory-mapped file (for random access):**
```csharp
// ✅ GOOD — When you need random access without full materialization
using var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open);
using var accessor = mmf.CreateViewAccessor();
// Read specific offsets without loading entire file
```

## Key Learnings

1. **Behavior guarantee:** Streaming parser MUST produce identical output to array overload (same order, same dedup, same fallback logic)
2. **Test compatibility:** Keep array overload for tests — wrap `StringReader`, don't duplicate logic
3. **Multi-line JSON:** Use `StringBuilder` to accumulate JSON blocks spanning multiple lines, parse once complete
4. **Avoid regex per-line:** Use `IndexOf`, `Contains`, `StartsWith` when possible (faster than regex)
5. **Output size matters:** Only accumulate the OUTPUT (e.g., session ID tuples), not every line read

## References

- **Copilot Booster Issue:** Memory bloat fix (2026-05-10)
- **Files changed:** `CopilotLogWatcherService.cs`, `ActiveStatusTracker.cs`
- **Tests:** `CopilotLogWatcherStreamingTests.cs` (855 unit tests passing)
- **Decision doc:** `.squad/decisions/inbox/trinity-log-streaming.md`
