---
name: "project-conventions"
description: "Core conventions and patterns for this codebase"
domain: "project-conventions"
confidence: "medium"
source: "template"
---

## Context

> **This is a starter template.** Replace the placeholder patterns below with your actual project conventions. Skills train agents on codebase-specific practices — accurate documentation here improves agent output quality.

## Patterns

### Deterministic Fallbacks for User-Visible Identifiers

Always provide a deterministic, non-empty fallback for user-visible identifiers (session names, display names, labels). Never rely on external state (sidecar files, API responses, host resolution) being present synchronously.

**Why:** Timing races, I/O failures, and external state initialization delays can leave user-facing fields empty. An empty string in a grid cell or label breaks user comprehension and violates the principle of "every item has a meaningful name."

**How:**
- Use stable, deterministic properties like GUIDs, timestamps, or IDs as fallback sources
- Format fallbacks clearly: `"Session {first-8-chars-of-guid}"`, `"Untitled {timestamp}"`, `"Item {id}"`
- Place fallback logic at the service layer where data is loaded for presentation
- Document the fallback chain priority explicitly (alias → summary → override → fallback)

**Example from SessionService.cs:**
```csharp
string displaySummary;
if (aliases.TryGetValue(id, out var alias))
{
    displaySummary = alias;
}
else if (!string.IsNullOrWhiteSpace(summary))
{
    displaySummary = summary;
}
else if (overrides.TryGetValue(id, out var overrideEntry))
{
    displaySummary = overrideEntry.Name;
}
else
{
    // Fallback: use first 8 chars of session ID as deterministic display name
    displaySummary = id.Length >= 8 ? $"Session {id.Substring(0, 8)}" : $"Session {id}";
}
```

**Anti-pattern:**
```csharp
// ❌ BAD: Returns empty string when folder exists but no summary
displaySummary = string.IsNullOrWhiteSpace(folder) ? "(no summary)" : "";
```

**Citation:** Trinity's empty-title fix (2026-05-09), `SessionService.cs:341-358`, `.squad/decisions/inbox/trinity-empty-title-fix.md`

### [Pattern Name]

Describe a key convention or practice used in this codebase. Be specific about what to do and why.

### Error Handling

<!-- Example: How does your project handle errors? -->
<!-- - Use try/catch with specific error types? -->
<!-- - Log to a specific service? -->
<!-- - Return error objects vs throwing? -->

### Testing

<!-- Example: What test framework? Where do tests live? How to run them? -->
<!-- - Test framework: Jest/Vitest/node:test/etc. -->
<!-- - Test location: test/, __tests__/, *.test.ts, etc. -->
<!-- - Run command: npm test, etc. -->

### Code Style

<!-- Example: Linting, formatting, naming conventions -->
<!-- - Linter: ESLint config? -->
<!-- - Formatter: Prettier? -->
<!-- - Naming: camelCase, snake_case, etc.? -->

### File Structure

<!-- Example: How is the project organized? -->
<!-- - src/ — Source code -->
<!-- - test/ — Tests -->
<!-- - docs/ — Documentation -->

## Examples

```
// Add code examples that demonstrate your conventions
```

## Anti-Patterns

<!-- List things to avoid in this codebase -->
- **[Anti-pattern]** — Explanation of what not to do and why.
