# Startup Rescan Integration Test Pattern

**Author:** Tank (Tester)  
**Created:** 2026-05-09  
**Pattern:** Deterministic integration test for services that must discover pre-existing external state at boot

---

## Problem Class

Services that discover external runtime state (processes, files, registry keys, network resources) often implement FileSystemWatcher / event-driven patterns. These patterns miss PRE-EXISTING state that existed BEFORE the service started.

**Regression risk:** Tests that manually trigger discovery after launching external resources never exercise the realistic ordering "external state exists → service starts → service must discover it."

---

## Pattern

### Test Structure

```csharp
[LocalOnlyFact]
[Trait("Category", "LocalOnly")]
public async Task ServiceStartup_PreExistingExternalState_MustDiscoverAsync()
{
    // 1. BEFORE constructing service: create external state
    LaunchExternalResource();
    
    // 2. DETERMINISTIC WAIT: confirm external state is stable
    await WaitForExternalStateStableAsync();
    
    // 3. NOW construct service using PRODUCTION startup path
    var service = new ServiceUnderTest();
    service.Initialize();
    
    // 4. Assert: service exposes the discovered state
    var discovered = service.GetDiscoveredState();
    Assert.NotNull(discovered);
    Assert.Contains(expectedValue, discovered);
}
```

### Determinism Rules

1. **No Thread.Sleep(N) as "give it time" hacks**
   - Use polling loops with hard timeouts that fail fast

2. **Explicit state confirmation before service construction**
   - Confirm external state is stable (process running, file exists, etc)

3. **Production startup path — no cheat calls**
   - Do NOT manually trigger internal discovery methods

4. **Cleanup in finally or IDisposable.Dispose**
   - Kill launched processes by PID list
   - Delete test-created files/dirs with sentinel markers

---

## Example: Copilot Process Discovery

**Test:** `tests/Integration/WindowsTerminalMultiPaneE2ETests.cs:93-230`

Launches Windows Terminal + copilot BEFORE constructing tracker, then asserts ACTIVE icon appears.

---

## References

- `tests/Integration/WindowsTerminalMultiPaneE2ETests.cs:93-230`
- `.squad/decisions/inbox/tank-startup-rescan-red.md`
- `.squad/agents/tank/history.md:250-252`
