# FileSystemWatcher Startup Rescan Pattern

**Pattern:** Reconcile pre-existing state at startup for FileSystemWatcher-based services

**Problem:** FileSystemWatcher only fires events for NEW file changes after watcher starts. Pre-existing files that existed BEFORE the watcher started never trigger events, leading to incomplete state.

**Example:** `ActiveStatusTracker` watches `~/.copilot/session-state/*/events.jsonl` for session status changes. Sessions loaded from disk at startup have no corresponding host binding because their jsonl files existed BEFORE the watcher started.

---

## Solution Pattern

Add a **startup rescan** method that:
1. Enumerates the watched directory to find pre-existing files
2. Processes each file using the same logic as the watcher callback
3. Calls the same event handler methods to maintain consistency
4. Is idempotent (safe to call multiple times)
5. Is synchronous or exposes a hard completion signal for testing

---

## Implementation Template

```csharp
/// <summary>
/// Startup rescan for pre-existing files in the watched directory.
/// Reconciles state for files that existed before the FileSystemWatcher started.
/// Idempotent: safe to call multiple times.
/// </summary>
public void RescanExistingFiles()
{
    if (!Directory.Exists(_watchedDirectory))
    {
        return;
    }

    try
    {
        var files = Directory.GetFiles(_watchedDirectory, _filePattern, SearchOption.AllDirectories);
        int scannedCount = 0;
        int processedCount = 0;

        foreach (var filePath in files)
        {
            // Extract identity from file (e.g., PID, session ID, etc.)
            var identity = ExtractIdentity(filePath);
            if (identity == null)
            {
                continue;
            }

            // Check if item is still live/valid
            if (!IsStillValid(identity))
            {
                continue;
            }

            scannedCount++;

            // Skip if already processed (idempotency)
            if (AlreadyProcessed(identity))
            {
                continue;
            }

            // Process using the same handler as the FileSystemWatcher
            ProcessFile(filePath, identity);
            processedCount++;
        }

        if (processedCount > 0)
        {
            Logger.LogInformation(
                "RescanExistingFiles: scanned {ScannedCount} valid item(s), processed {ProcessedCount}",
                scannedCount,
                processedCount);
        }
    }
    catch (Exception ex)
    {
        Logger.LogWarning("RescanExistingFiles failed: {Error}", ex.Message);
    }
}
```

---

## Wire-Up Location

Call the rescan method AFTER the service is constructed but BEFORE the first state query:

```csharp
// Bad: rescan happens too late, first query sees incomplete state
var service = new MyWatcherService();
var state = service.GetState(); // ❌ Missing pre-existing items
service.RescanExistingFiles();

// Good: rescan happens before first query
var service = new MyWatcherService();
service.RescanExistingFiles(); // ✅ Reconciles pre-existing items
var state = service.GetState(); // ✅ Complete state
```

---

## Idempotency Requirements

The rescan method must be safe to call multiple times:
- Use the same deduplication logic as the watcher callback
- Check if an item is already processed before processing it again
- Avoid double-binding, double-firing events, or corrupting state

**Example:** `ActiveStatusTracker.SetCopilotHost` deduplicates by HWND/PID identity — early return if identical host already set.

---

## Testing Contract

- Rescan must be **synchronous** OR expose a hard completion signal
- Tests must NOT use `Sleep()` or timing-based assertions
- Tests can call `RescanExistingFiles()` and immediately assert the result

**Example:** Tank's RED test launches copilot.exe FIRST, constructs the tracker, calls `RescanExistingSessions()`, then asserts the tracker reports the process as active — WITHOUT manually calling `HandleExternalSessionDiscovered`.

---

## Real-World Example

See `src/Services/ActiveStatusTracker.cs` → `RescanExistingSessions()`:
- Enumerates `~/.copilot/logs/process-*.log` files
- Extracts PID and session ID from each log file
- Verifies the process is still alive
- Calls `HandleExternalSessionDiscovered(sessionId, pid)` for each live process
- Wired into `MainForm.LoadInitialDataAsync()` after `LoadSessions()` but before first `RefreshActiveStatus()`

**Result:** Fixed shipped regression where pre-existing copilot.exe processes never showed as ACTIVE in the grid.

---

## When NOT to Use

- If the watcher can catch up by replaying history (e.g., event sourcing)
- If the watched files are ephemeral and don't persist across restarts
- If the service can tolerate incomplete state at startup (eventual consistency)

---

## Related Patterns

- **FileSystemWatcher Error Recovery:** Use `FileSystemWatcher.Error` event to restart the watcher and rescan on buffer overflow
- **Cache Priming:** Load initial state from persistent storage before starting the watcher (e.g., `EventsJournalService.LoadCache()`)
- **Polling Fallback:** Use a periodic timer to rescan if the watcher becomes unreliable (e.g., network drives)

---

## Status

✅ **Validated** — Used in production to fix shipped regression in 0.22.0
