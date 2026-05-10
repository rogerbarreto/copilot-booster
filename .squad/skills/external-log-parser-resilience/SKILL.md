# External Log Parser Resilience

**Pattern:** Design parsers for third-party tool logs that change format silently between versions

**Problem:** Third-party CLIs (like GitHub Copilot CLI) evolve their log formats between versions without versioned schemas or migration guides. Parsers coupled to specific log patterns break silently when the tool updates, causing production features to fail without clear error messages.

**Example:** Copilot CLI v1.0.44 telemetry changed from `kind: session_start` to multiple kinds (`cli_ready`, `tools_available`, etc.). Parser requiring `session_start` rejected ALL real logs, breaking active-status detection for every process.

---

## Solution Pattern

Build parsers with **layered extraction** + **validation** + **fallback**:

### 1. Field-Coupled, Not Shape-Coupled

Accept ANY occurrence of the required field, not a specific shape:

```csharp
// ❌ BAD: Requires exact shape
if (root.TryGetProperty("kind", out var kindProp)
    && kindProp.GetString() == "session_start"
    && root.TryGetProperty("session_id", out var sidProp))
{
    sessionId = sidProp.GetString();
}

// ✅ GOOD: Accepts any JSON with session_id
if (root.TryGetProperty("session_id", out var sidProp))
{
    var candidate = sidProp.GetString();
    if (!string.IsNullOrWhiteSpace(candidate) && IsValid(candidate))
    {
        sessionId = candidate;
    }
}
```

### 2. Deterministic Fallback Patterns

Add regex fallback for deterministic log lines that exist across versions:

```csharp
// Primary: Parse JSON telemetry blocks
if (line.Contains("[Telemetry] cli.telemetry:"))
{
    // ... JSON parsing ...
}

// Fallback: Regex for deterministic INFO lines
if (sessionId == null)
{
    var match = Regex.Match(line, @"\[INFO\]\s+(?:Registering foreground session|Workspace initialized):\s+([0-9a-f-]{36})");
    if (match.Success && IsValidGuid(match.Groups[1].Value))
    {
        sessionId = match.Groups[1].Value;
    }
}
```

### 3. Validation Guard

Validate extracted values against expected formats to reject garbage:

```csharp
private static bool IsValidSessionId(string? sessionId)
{
    if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length != 36)
    {
        return false;
    }

    // GUID format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
    return Regex.IsMatch(sessionId, @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase);
}
```

### 4. Partial Stream Tolerance

Handle truncated/streaming logs gracefully:

```csharp
try
{
    using var doc = JsonDocument.Parse(jsonBuilder.ToString());
    // ... parse ...
}
catch (JsonException)
{
    // Not valid JSON or truncated — skip and continue scanning
}
```

---

## Design Checklist

When parsing logs from external tools:

- [ ] **Field-coupled extraction:** Accept ANY occurrence of required fields, not hardcoded shapes
- [ ] **Deterministic fallback:** Identify log patterns guaranteed to exist across versions (e.g., lifecycle events, error messages)
- [ ] **Validation guard:** Reject garbage values (format validation, range checks, length constraints)
- [ ] **Partial tolerance:** Handle truncated streams without crashing (catch parse exceptions, continue scanning)
- [ ] **Return first valid match:** Stop after finding the first valid value (avoid over-parsing)
- [ ] **Logging on failure:** Log when fallback is used or validation rejects values (helps diagnose format drift)

---

## Why This Works

1. **Field-coupled, not version-coupled:** New tool versions add fields or change shapes, but rarely remove core identity fields
2. **Fallback protects against backend changes:** Deterministic lifecycle events are more stable than telemetry formats
3. **Validation prevents garbage propagation:** Bad parses fail fast instead of poisoning downstream services
4. **Partial tolerance handles real-world logs:** Streaming, rotation, and concurrent writes produce incomplete blocks

---

## When to Use

- Parsing logs from third-party CLIs (GitHub Copilot, `git`, `gh`, `docker`, etc.)
- Extracting identity from logs without versioned schemas
- Integration with tools that change log formats without deprecation notices
- When the tool's official API is unavailable or insufficient

---

## When NOT to Use

- If the tool provides a stable API or SDK (prefer that)
- If log format is documented and versioned (parse the exact schema)
- If you control the log format (version your own schema instead)

---

## Related Patterns

- **Strict JSON Boundary:** For parsing AI/API responses with known schemas
- **Real Log Pinned Fixtures:** For testing parsers against real tool output
- **Layered Fallback Chain:** CWD extraction (JSON → debug line → fallback → UserProfile)

---

## Real-World Example

See `src/Services/CopilotLogWatcherService.cs` → `TryParseLogContent`:
- Primary: ANY `[Telemetry] cli.telemetry:` JSON with `session_id`
- Fallback: Regex for `[INFO] Registering foreground session: <guid>` or `[INFO] Workspace initialized: <guid>`
- Validation: 36-char GUID format check
- Tolerance: Catches `JsonException` on truncated blocks

**Result:** Parser works across Copilot CLI versions without updates. Active-status detection robust to telemetry backend changes.

---

## Status

✅ **Validated** — Used in production to fix shipped regression (Copilot CLI v1.0.44 format change)
