# Skill: LocalOnly Integration Tests with State Restoration

## Problem

Integration tests that interact with live user sessions (terminals, IDEs, browsers) risk leaving the user's environment in a different state if:
- Tests fail mid-execution
- Assertions throw exceptions
- Tests are aborted

This creates a poor developer experience and makes tests feel "invasive."

## Solution Pattern

**LocalOnly tests with guaranteed restore-on-teardown:**

1. **Runtime detection:** Probe for the live scenario at test startup. If absent → skip cleanly (no red).
2. **Capture original state:** Snapshot the state you'll be mutating (active tab, window title, file contents, etc.) before doing anything.
3. **IDisposable cleanup:** Implement `IDisposable` or use try-finally to guarantee restoration attempt even on failure.
4. **Iteration-capped restore:** If restoration requires loops (e.g., cycling tabs to find original), use a hard iteration cap to prevent infinite hangs.
5. **Never destructive:** Only send non-destructive actions (e.g., Ctrl+Tab for tab cycling, NOT Ctrl+W to close tabs or `exit` to kill shells).

## Example Implementation

See `tests/Integration/WarpPaneFocusLiveTests.cs` (Warp R2):

```csharp
[Collection(WindowEventHookCollection.Name)]  // Serialize if hooks conflict
public sealed class WarpPaneFocusLiveTests : IDisposable
{
    private readonly LiveWarpScenario _scenario;

    public WarpPaneFocusLiveTests()
    {
        // 1. Runtime detection
        this._scenario = LiveWarpScenario.Detect();
    }

    [LocalOnlyStaFact]
    [Trait("Category", "LocalOnly")]
    public void MyLiveTest()
    {
        if (!this._scenario.IsAvailable)
        {
            return;  // Skip cleanly
        }

        // 2. Original state already captured in constructor

        // ... test actions that mutate state ...

        Assert.True(someCondition);
    }

    public void Dispose()
    {
        // 3. Guaranteed cleanup
        if (this._scenario.IsAvailable)
        {
            var reader = new Win32TitleReader();
            this._scenario.RestoreToOriginal(reader);
        }
    }
}

internal sealed class LiveWarpScenario
{
    public bool IsAvailable { get; }
    public string OriginalTitle { get; }

    public static LiveWarpScenario Detect()
    {
        // Probe for warp.exe + copilot.exe descendant
        var warpProcs = Process.GetProcessesByName("warp");
        var copilotProcs = Process.GetProcessesByName("copilot");

        var match = warpProcs.FirstOrDefault(w =>
            copilotProcs.Any(c => IsDescendantOf(c.Id, w.Id))
        );

        if (match == null)
        {
            return new LiveWarpScenario(false, "");
        }

        var title = ReadCurrentTitle(match.Id);
        return new LiveWarpScenario(true, title);
    }

    public void RestoreToOriginal(ITitleReader reader)
    {
        // 4. Iteration-capped restore loop
        for (var i = 0; i < 30; i++)
        {
            var current = reader.ReadTitle();
            if (string.Equals(current, this.OriginalTitle, StringComparison.OrdinalIgnoreCase))
            {
                return;  // Success
            }

            SendCtrlTab();
            Thread.Sleep(150);
        }

        // Failed to restore within cap — user's screen left in different state.
        // Logged as flake risk in Tank history.
    }
}
```

## When to Use

- Live terminal integration tests (Warp, Windows Terminal, iTerm2, etc.)
- IDE window/project state tests (VS Code, Visual Studio, IntelliJ)
- Browser automation tests where closing tabs would lose user work
- Any test that Roger (or another user) will run against their active desktop session

## When NOT to Use

- Controlled spawned processes (spawn, test, kill) — no restoration needed.
- CI-only tests with ephemeral environments — no user state to preserve.
- Unit tests with fakes/mocks — no real state to restore.

## Flake Risks

**Documented in Tank history (2026-05-10):**

- If restore loop doesn't find original state within iteration cap → user's screen left changed.
- Can happen if: (1) tabs close/change mid-test, (2) timing races UI rendering, (3) state order changes.
- **Mitigations:** LocalOnly opt-in (user authorization), iteration cap (no infinite loops), visible failures (user sees wrong state).

## xUnit Integration

**LocalOnlyFact attribute:**

```csharp
[LocalOnlyFact]
[Trait("Category", "LocalOnly")]
public void MyTest() { ... }
```

- Skips when `COPILOT_BOOSTER_RUN_LOCALONLY ≠ 1`.
- Default `dotnet run --project tests/...IntegrationTests.csproj -c Release` skips these tests (no baseline reds).
- Release CI uses `-notrait "Category=LocalOnly"` to filter them out.

## Conversion Path to Non-LocalOnly

To make LocalOnly tests run in CI:
1. Spawn the host app with known configuration (e.g., warp.exe with a test tab config).
2. Wait for full initialization (e.g., via WinEvent hooks detecting expected titles).
3. Run the test against the controlled instance.
4. Kill the spawned instance at teardown.

This eliminates dependency on live user sessions and enables CI execution.

## Related Decisions

- `squad-warp-r2-pivot.md` — Warp R2 adoption (invasive but deterministic tab cycling).
- `tank-warp-r2-tests.md` — Tank's Warp R2 test implementation (first use of this pattern).
