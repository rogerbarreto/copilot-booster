# Memory Allocation Regression Tests

A pattern for writing regression tests that prove memory optimizations actually work and catch future regressions.

## When to Use

- After refactoring to reduce memory consumption (e.g., streaming parser replacing in-memory array)
- When a feature had a known memory bloat issue (e.g., 4GB RSS from loading large files)
- When you need proof that an optimization stays effective over time
- When memory regression would be catastrophic but invisible until production

## The Pattern

### 1. Force GC for Clean Baseline

```csharp
GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
GC.WaitForPendingFinalizers();
GC.Collect();
```

This ensures:
- All pending finalizers run
- All unreferenced objects are collected
- Gen-0, Gen-1, Gen-2 heaps are compacted
- Baseline measurement isn't contaminated by prior test allocations

### 2. Snapshot Allocations Before

```csharp
var before = GC.GetTotalAllocatedBytes(precise: true);
```

`precise: true` forces a GC collection to get exact count (slower but necessary for regression tests).

### 3. Execute the Code Under Test

```csharp
// Example: streaming parser
using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
using var reader = new StreamReader(fs, Encoding.UTF8);
var result = MyService.ParseStreamingLog(reader);
```

### 4. Snapshot Allocations After

```csharp
var after = GC.GetTotalAllocatedBytes(precise: true);
var deltaBytes = after - before;
```

### 5. Assert Against Budget

```csharp
const long MaxAllocationBytes = 25_000_000; // 25 MB
Assert.True(deltaBytes < MaxAllocationBytes, 
    $"Allocation budget exceeded: {deltaBytes:N0} bytes > {MaxAllocationBytes:N0} bytes");
```

## Choosing a Budget

Budget should be:
- **Generous enough** to avoid false positives from GC noise or legitimate buffering
- **Strict enough** to catch partial regressions (e.g., 90% streaming but 10% buffering)
- **Relative to expected behavior** — NOT relative to old broken behavior

Example: 50 MB file streaming parser
- Expected: ~5 MB (line buffers + parse results)
- Budget: **25 MB** (5× expected)
- Old broken code: ~100 MB (2× file size for string + array)
- Budget catches any regression toward broken behavior

## Gen-2 GC Promotion Ceiling Test

Complement allocation budget test with gen-2 collection count to catch LOH thrashing:

```csharp
GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
GC.WaitForPendingFinalizers();
GC.Collect();

var gen2Before = GC.CollectionCount(2);

// Execute code under test
using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
using var reader = new StreamReader(fs, Encoding.UTF8);
var result = MyService.ParseStreamingLog(reader);

var gen2After = GC.CollectionCount(2);
var gen2Delta = gen2After - gen2Before;

Assert.True(gen2Delta <= 1, 
    $"Gen-2 collection ceiling exceeded: {gen2Delta} collections > 1");
```

This catches:
- Large Object Heap (LOH) thrashing (objects ≥ 85 KB go to LOH, promoted to gen-2 immediately)
- Repeated allocations of mid-sized objects that survive to gen-2
- `Substring` storms creating thousands of intermediate strings

## Parity Test

Always pair memory regression tests with a parity test proving the optimized code produces identical output:

```csharp
[Fact]
public void StreamingOverload_ProducesIdenticalOutput_ToArrayOverload()
{
    var content = GetTestFixture();
    
    // Old path
    var lines = content.Split('\n');
    var oldResult = MyService.ParseLogArray(lines);
    
    // New streaming path
    using var reader = new StringReader(content);
    var newResult = MyService.ParseLogStreaming(reader);
    
    // Assert exact equality
    Assert.Equal(oldResult.Count, newResult.Count);
    for (int i = 0; i < oldResult.Count; i++)
    {
        Assert.Equal(oldResult[i], newResult[i]);
    }
}
```

Without parity test, memory regression test is meaningless — code could "pass" by returning empty results.

## Synthetic Test Data Generation

For large-file tests (e.g., 50 MB log), generate synthetic data without bloating test process:

```csharp
private string GenerateSyntheticLog50MB()
{
    var tempFile = Path.Combine(Path.GetTempPath(), $"test-{DateTime.UtcNow.Ticks}.log");
    
    using var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 8192);
    using var writer = new StreamWriter(fs, Encoding.UTF8);
    
    const long TargetBytes = 50 * 1024 * 1024; // 50 MB
    var bytesWritten = 0L;
    var lineCount = 0;
    
    while (bytesWritten < TargetBytes)
    {
        // Scatter valid content among filler lines
        if (lineCount % 50000 == 0)
        {
            // Valid session line
            writer.WriteLine("2026-05-09T10:59:33.804Z [INFO] Workspace initialized: aaaaaaaa-bbbb-cccc-dddd-000000000001");
            bytesWritten += 100;
        }
        else
        {
            // Filler line
            var filler = $"2026-05-09T10:59:33.{lineCount % 1000:000}Z [DEBUG] Processing message {lineCount}";
            writer.WriteLine(filler);
            bytesWritten += filler.Length + 2; // +2 for \r\n
        }
        lineCount++;
    }
    
    writer.Flush();
    return tempFile;
}
```

Key: write in chunks (8 KB buffer) instead of materializing entire 50 MB string in memory.

## LocalOnly Test Marking

Heavy memory tests should skip in CI:

```csharp
[Fact(Skip = "LocalOnly heavy memory test; set COPILOT_BOOSTER_RUN_LOCALONLY=1 to run.")]
[Trait("Category", "LocalOnly")]
public void StreamingParser_50MbLog_AllocationsStayBounded()
{
    // ...
}
```

Run locally before release:
```powershell
$env:COPILOT_BOOSTER_RUN_LOCALONLY="1"
dotnet run --project tests/MyProject.Tests.csproj -c Release
```

## Common Pitfalls

### ❌ Asserting Against Old Broken Behavior
```csharp
// BAD: budget = old broken allocation (100 MB)
Assert.True(deltaBytes < 100_000_000);
// This wouldn't catch 90% regression (90 MB still passes)
```

### ✅ Assert Against Expected Optimal Behavior + Margin
```csharp
// GOOD: budget = expected (5 MB) × 5 = 25 MB
Assert.True(deltaBytes < 25_000_000);
// This catches any significant regression toward old behavior
```

### ❌ Forgetting to Force GC Before Snapshot
```csharp
// BAD: contaminated baseline
var before = GC.GetTotalAllocatedBytes(precise: true);
```

### ✅ Force Full GC Collection
```csharp
// GOOD: clean baseline
GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
GC.WaitForPendingFinalizers();
GC.Collect();
var before = GC.GetTotalAllocatedBytes(precise: true);
```

### ❌ Memory Test Without Parity Test
```csharp
// BAD: no proof that streaming code produces correct output
[Fact]
public void StreamingParser_50MbLog_AllocationsStayBounded() { ... }
```

### ✅ Memory Test + Parity Test
```csharp
// GOOD: parity proves correctness, memory test proves optimization
[Fact]
public void StreamingOverload_ProducesIdenticalOutput() { ... }

[Fact(Skip = "LocalOnly...")]
public void StreamingOverload_50MbLog_AllocationsStayBounded() { ... }
```

## Real-World Example

From `CopilotLogWatcherStreamingTests.cs`:

**Problem:** `reader.ReadToEnd().Split('\n')` on 678 MB log → 4.4 GB RSS

**Fix:** Streaming `TextReader.ReadLine()` loop

**Tests:**
1. **Parity** — Streaming vs array produce identical session lists (always runs)
2. **Allocation Budget** — 50 MB log allocates < 25 MB (LocalOnly)
3. **Gen-2 GC Ceiling** — 50 MB log triggers ≤ 1 gen-2 collection (LocalOnly)
4. **Real-World Fixture** — 5 KB sanitized log parses 2 sessions (always runs)

**Result:** All GREEN. Streaming overload allocates ~5 MB for 50 MB log (5× under budget). Array overload would allocate ~100 MB (4× over budget).

## When NOT to Use This Pattern

- **Micro-optimizations** — Don't write 50 MB file tests for saving 100 KB. Use profiler instead.
- **Allocation is feature-dependent** — E.g., session count grows linearly with input size. Assert relative budget instead: `deltaBytes < inputSize * 0.5`.
- **GC.GetTotalAllocatedBytes unavailable** — Pattern requires .NET 6+ (`precise: true` needs .NET 8+).

## Further Reading

- [GC.GetTotalAllocatedBytes docs](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalallocatedbytes)
- [Large Object Heap (LOH) fundamentals](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/large-object-heap)
- [GC.Collect best practices](https://learn.microsoft.com/en-us/dotnet/api/system.gc.collect)

---

**Extracted from:** Bug C memory regression tests (2026-05-10)  
**Author:** Tank (Tester)  
**Last Updated:** 2026-05-10
