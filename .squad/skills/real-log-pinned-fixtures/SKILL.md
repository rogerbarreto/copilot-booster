# Real-Log Pinned Fixtures Pattern

**Author:** Tank (Tester)  
**Created:** 2026-05-09  
**Pattern:** Pin at least one test fixture to a verbatim slice of real third-party-tool output to catch silent format drift

---

## Problem Class

Tests that parse output from third-party tools (CLI tools, APIs, log files) often use **synthetic fixtures** — hand-crafted examples that match the documented format but may not match what the tool **actually** emits in production.

**Regression risk:** Tool vendors silently change output formats in new versions. Tests pass because they validate against the synthetic fixture, but production code fails because it parses real tool output differently.

---

## Pattern

### Pinned Fixture Strategy

1. **Harvest a real output sample** from the actual third-party tool:
   ```powershell
   # Example: copilot CLI logs
   Get-ChildItem $env:USERPROFILE\.copilot\logs\process-*.log | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Get-Content -TotalCount 250
   ```

2. **Create a verbatim fixture** — copy the exact output, sanitize sensitive data (IDs, paths) but preserve structure:
   ```csharp
   // Real CLI v1.0.44 telemetry shape — DO NOT modify without re-harvesting from a current copilot log
   private const string RealisticLogContent = """
       2026-05-09T10:59:33.804Z [INFO] Workspace initialized: ba62613b-7f04-46bc-9c1e-778b12616687 (checkpoints: 0)
       2026-05-09T10:59:34.127Z [INFO] [Telemetry] cli.telemetry:
       {
         "kind": "cli_ready",
         "session_id": "ba62613b-7f04-46bc-9c1e-778b12616687",
         ...
       }
       """;
   ```

3. **Add a guard comment** to prevent "tidying":
   ```csharp
   // Real CLI v1.0.44 telemetry shape — DO NOT modify without re-harvesting from a current copilot log
   ```

4. **Test against the verbatim fixture:**
   ```csharp
   [Fact]
   public void TryParseLogContent_ExtractsSessionIdAndCwd_FromRealisticLog()
   {
       var lines = RealisticLogContent.Split('\n');
       var (sessionId, cwd) = CopilotLogWatcherService.TryParseLogContent(lines);
       
       Assert.Equal("ba62613b-7f04-46bc-9c1e-778b12616687", sessionId);
       Assert.Equal(@"S:\repo\community\sandcastle", cwd);
   }
   ```

5. **Supplement with synthetic fixtures** for edge cases, but keep ONE verbatim fixture as a regression guard

---

## When to Use

✅ **Use this pattern when:**
- Parsing output from CLI tools you don't control (git, gh, copilot, npm, docker)
- Consuming API responses from third-party services (GitHub API, external webhooks)
- Reading log files from external processes (browser devtools, language servers, CI logs)
- Parsing config files from tools that may change format (package.json, tsconfig.json)

❌ **Skip this pattern when:**
- The format is under your control (your own JSON/YAML config files)
- The tool has an official library/SDK (use the library, not raw output parsing)
- The output is too dynamic to pin (UUIDs, timestamps, random data)

---

## Maintenance

### When Tool Version Changes

1. Re-harvest a fresh sample from the new version
2. Compare against the pinned fixture — if structure changed:
   - Update parser code to handle new format
   - Update pinned fixture to new version
   - Add comment with version number: `// Real CLI v1.0.52 telemetry shape`
3. If structure is unchanged, keep the existing fixture

### Sanitization Rules

- **Replace sensitive data** but keep the shape:
  - Session IDs: `ba62613b-7f04-46bc-9c1e-778b12616687` → synthetic but valid GUID
  - Paths: `C:\Users\Alice\repo` → `C:\FromJson` or test-specific path
  - API tokens: `ghp_abc123` → `ghp_REDACTED`
- **Preserve exact structure:**
  - Field names (casing matters: `session_id` not `sessionId`)
  - Nesting and indentation
  - Whitespace (trailing spaces, blank lines)
  - Order of fields

---

## Example: Copilot CLI v1.0.44 Fixture Refresh

**Before:** Synthetic fixture with `"kind": "session_start"` (never emitted by real CLI)  
**After:** Verbatim CLI v1.0.44 slice with `"kind": "cli_ready"` (actual production output)

**Impact:** 12 tests went RED because parser still filtered on `"session_start"`. Tests now validate against REAL CLI behavior — once parser ships, tests will pass AND catch future format drift.

**Files:**
- `tests/Services/CopilotLogWatcherServiceTests.cs` — pinned fixture at line 18-52
- `.squad/decisions/inbox/tank-fixture-refresh-cli144.md` — decision memo
- `.squad/agents/tank/history.md` — learning entry

---

## Why This Matters

1. **Prevents false confidence:** Tests passing against synthetic fixtures but failing in production are worse than no tests
2. **Documents third-party contract:** The verbatim fixture serves as living documentation of what the tool actually emits
3. **Detects silent drift:** If the tool changes output format in a future version, the pinned fixture catches it immediately
4. **Enables fearless parsing:** With a real-world fixture, you can refactor the parser with confidence

---

## Anti-Patterns

❌ **DON'T "tidy" the verbatim fixture:**
```csharp
// BAD: simplified, lost real CLI structure
private const string LogContent = """
    [INFO] [Telemetry] cli.telemetry:
    { "kind": "cli_ready", "session_id": "test-id" }
    """;
```

✅ **DO keep exact structure:**
```csharp
// GOOD: verbatim slice from real log
// Real CLI v1.0.44 telemetry shape — DO NOT modify without re-harvesting
private const string RealisticLogContent = """
    2026-05-09T10:59:34.127Z [INFO] [Telemetry] cli.telemetry:
    {
      "kind": "cli_ready",
      "properties": {
        "copilot_pid": "74528",
        "engagement_id": "6af655d6-77ff-47d8-bbf2-eb21068f11f1"
      },
      ...
    """;
```

---

## References

- `tests/Services/CopilotLogWatcherServiceTests.cs:18-52` — pinned copilot CLI v1.0.44 fixture
- `.squad/decisions/inbox/tank-fixture-refresh-cli144.md` — decision memo
- `.squad/agents/tank/history.md` — learning entry on CLI v1.0.44 log shape
