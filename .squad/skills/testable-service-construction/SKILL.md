---
name: "testable-service-construction"
description: "Expose WinForms services with injectable seams and no UI dependencies"
domain: "csharp-services"
confidence: "medium"
source: "issue-17"
---

## Pattern

Use constructor injected delegates and concrete services when the codebase has no DI container.

## Shape

```csharp
internal sealed class SomeService : IDisposable
{
    internal event Action<string, OldState, NewState>? StateChanged;

    internal SomeService(
        ConcreteDependency dependency,
        IBoundary boundary,
        Func<string, string?> dataResolver,
        Action<string> notificationSink)
    {
    }
}
```

## Rules

- Keep UI types out of services.
- Use `Action<string>` for toast sinks instead of adding a UI interface for one method.
- Use `Func<...>` for small lookup seams that tests need to control.
- Keep production wiring in `MainForm` or `Program`, matching the existing no-container pattern.
- Expose state through events and `TryGetState` so tests and visuals can observe without polling private fields.