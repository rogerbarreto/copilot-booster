# Title-Probe Tab Focus Skill

## Problem

Some terminal emulators (Warp, potentially WezTerm, Alacritty) share a single process PID across multiple tabs/panes and expose ZERO UIA automation elements. For these terminals:
- You cannot enumerate tabs programmatically via UI Automation
- You cannot map a child shell PID to its hosting tab/pane UUID
- You cannot directly focus a specific tab by its identifier

However, if the terminal:
1. Exposes the **active tab/pane title** via `GetWindowText` on its main HWND
2. Supports a keyboard shortcut (e.g., Ctrl+Tab) to cycle to the next tab/pane

...then you can **deterministically focus** the correct tab by probing: read title, send Ctrl+Tab, sleep, read title again, repeat until match or cycle back to start.

## Solution Pattern

### Architecture

Three seams for testability:

1. **IWindowTitleReader** — abstracts Win32 window handle and title reading:
   ```csharp
   IntPtr FindMainWindowHandle(int processId); // Returns FIRST visible window with non-empty title
   string ReadTitle(IntPtr hwnd);              // Returns "" if hwnd invalid or no title
   ```

2. **IKeyboardSender** — abstracts keyboard input:
   ```csharp
   void SendCtrlTab(); // Sends Ctrl+Tab sequence to currently-focused window
   ```

3. **IPaneFocusClock** — abstracts timing/sleep:
   ```csharp
   void Sleep(int millis); // Pause execution
   ```

### Core Algorithm (WarpPaneFocuser.TryFocusPane)

```csharp
public bool TryFocusPane(int processId, string expectedTitle)
{
    // 1. Defensive: null/empty title → bail
    if (string.IsNullOrEmpty(expectedTitle)) return false;

    // 2. Find main HWND for process (visible, non-empty title)
    IntPtr hwnd = _titleReader.FindMainWindowHandle(processId);
    if (hwnd == IntPtr.Zero) return false;

    // 3. Foreground the terminal window
    if (!_focusHwnd(hwnd)) return false;

    // 4. Read current title → originalTitle
    string originalTitle = _titleReader.ReadTitle(hwnd);

    // 5. If originalTitle matches expectedTitle → done
    if (string.Equals(originalTitle, expectedTitle, StringComparison.OrdinalIgnoreCase))
        return true;

    // 6. Loop up to maxIterations (default 30)
    for (int i = 0; i < _maxIterations; i++)
    {
        // a. Send Ctrl+Tab
        _keys.SendCtrlTab();

        // b. Sleep (default 150ms) for title update to settle
        _clock.Sleep(_settleMillis);

        // c. Read title → currentTitle
        string currentTitle = _titleReader.ReadTitle(hwnd);

        // d. If currentTitle matches expectedTitle → done
        if (string.Equals(currentTitle, expectedTitle, StringComparison.OrdinalIgnoreCase))
            return true;

        // e. If currentTitle equals originalTitle (cycled back) → break
        if (string.Equals(currentTitle, originalTitle, StringComparison.OrdinalIgnoreCase))
            break;
    }

    // 7. No match found
    return false;
}
```

### Concrete Implementations

**Win32WindowTitleReader:**
- Uses `EnumWindows` → `IsWindowVisible` → `GetWindowTextLength` → `GetWindowText`
- Returns FIRST visible window with non-empty title (deterministic enough for single-window terminals)
- Limitation: multi-window instances may pick the wrong window (rare case)

**Win32KeyboardSender:**
- Uses `SendInput` API (preferred over `keybd_event`)
- INPUT structs for: Press Ctrl → Press Tab → Release Tab → Release Ctrl
- Caller is responsible for foregrounding the target window first

**SystemPaneFocusClock:**
- Wraps `Thread.Sleep(millis)`

All three marked `[ExcludeFromCodeCoverage]` (thin P/Invoke wrappers, not unit-testable).

### Parameters

- **maxIterations** (default 30): Hard cap on probe loops. Prevents infinite cycling if titles mutate live or tabs close mid-probe.
- **settleMillis** (default 150ms): Sleep after SendCtrlTab to allow terminal to update title. 150ms empirically works for Warp; may need tuning for other terminals.

### Edge Cases Handled

1. **Process exits mid-probe:** `FindMainWindowHandle` returns `IntPtr.Zero` on next call if process dies; `TryFocusPane` bails with `false`.
2. **Title hasn't settled after 150ms:** Loop continues; cycle-back detection eventually trips, or iteration cap is reached.
3. **Empty/null expectedTitle:** Returns `false` immediately (defensive).
4. **Cycle-back detection:** If we loop back to originalTitle, we've seen all tabs → no match → return `false`.
5. **Iteration cap reached without cycle-back:** Degenerate case (titles mutating, tabs closing) → return `false`.

### Testing Strategy

**Unit tests** (Tank's domain):
- Stub IWindowTitleReader with scripted title sequence (e.g., ["Hi 1", "Hi 2", "Hi 3", "Hi 1"])
- Stub IKeyboardSender to count SendCtrlTab calls
- Stub IPaneFocusClock (no actual sleep)
- Assert: match found at correct iteration, correct number of Ctrl+Tab sends, cycle-back detection works

**Integration tests** (not recommended for this pattern):
- Require live terminal with known tab setup (brittle, non-hermetic)
- Better to test via production usage and diagnostics logs

## When NOT to Use This Pattern

- Terminal exposes UIA automation elements (e.g., Windows Terminal) → use UIA pane enumeration instead
- Terminal provides a deep-link protocol (e.g., `warp://session/<uuid>`) AND you can map child PIDs to UUIDs → use deep link
- Terminal has a CLI API to focus tabs (e.g., `tmux select-pane`) → use that
- User objects to visible disruption (tabs flashing as they cycle) → no alternative; R2 is inherently invasive

## Applicability to Other Terminals

**WezTerm:**
- Multiplexer model (panes/tabs), Ctrl+Tab cycles tabs
- GetWindowText returns window title (likely active tab title, TBD)
- **Probable fit** if GetWindowText exposes tab title

**Alacritty:**
- Single-tab terminal (no built-in tab support as of v0.13)
- No tab cycling → pattern does NOT apply
- Future: if Alacritty adds native tabs, pattern may work

**kitty:**
- Has tabs, Ctrl+Shift+Right cycles tabs (default)
- Would need IKeyboardSender.SendKittyTabCycle with different key sequence
- GetWindowText behavior unknown → needs investigation

**iTerm2 (macOS):**
- AppleScript automation available → better path than title-probe
- Pattern is Windows-specific (Win32 APIs)

## Implementation Checklist

1. Verify terminal exposes active tab title via `GetWindowText` (live test with Spy++ or custom probe)
2. Verify terminal has tab-cycle keyboard shortcut (check terminal settings/keybinds)
3. Measure settle time empirically (start with 150ms, tune if needed)
4. Implement IWindowTitleReader for terminal's window enumeration quirks (multi-window, child windows, etc.)
5. Implement IKeyboardSender for terminal's cycle shortcut (may not be Ctrl+Tab)
6. Wire into focus dispatcher with host kind label (e.g., HostKindClassifier maps process name → "WezTerm")
7. Add fallback: if TryFocusPane returns false, still focus terminal window as courtesy
8. Log outcomes: success with matched title, failure with warning ("no pane matched session '<title>'")
9. Document as known limitation: multi-window instances may mis-focus

## References

- Original decision: `.squad/decisions/inbox/squad-warp-r2-pivot.md`
- Implementation: `src/Services/WarpPaneFocuser.cs`, `IWindowTitleReader.cs`, `IKeyboardSender.cs`, `IPaneFocusClock.cs`
- Wiring: `src/Services/ActiveStatusTracker.cs` FocusCopilotHost Warp branch
- Decision doc: `.squad/decisions/inbox/trinity-warp-pane-focuser.md`
