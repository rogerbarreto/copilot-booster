# UIA Pane Resolution

Use this skill when wiring UI Automation pane/tab selection for terminal hosts that multiplex multiple sessions in one parent process/window.

## Pattern

1. Resolve the parent host HWND through the normal process-tree path first.
2. Enumerate selectable UIA elements under the parent with a gateway interface, not inline UIA calls.
3. Keep the gateway time-boxed and degradable: partial/empty results must fall back to the parent HWND.
4. Match by true child process id only if the gateway can prove it. `AutomationElement.Current.ProcessId` usually reports the UI host process, not the embedded shell/application.
5. Prefer deterministic user-visible titles (`SessionId`, launch title, workspace summary, override/alias) as the portable fallback.
6. If multiple panes match, prefer the currently selected/active pane, then first match.
7. Cache by `(parent HWND, target PID)` and store both selected child HWND and title.
8. Invalidate synchronously on parent name/tab changes and child/parent destruction. Do not run UIA inside WinEvent handlers.
9. On focus, re-select the cached pane/title through the gateway, then foreground the parent window.

## Windows Terminal Gotchas

- WT tab UIA elements commonly report `ProcessId == WindowsTerminal.exe`; do not assume this is the shell/Copilot PID.
- `NativeWindowHandle` may be zero for tab items. Store the parent HWND plus pane title when no child HWND is exposed.
- `SelectionItemPattern.Select()` is preferred; fall back to `InvokePattern.Invoke()`.
- Logging empty/partial enumeration at Information level makes UIA slowness visible without breaking focus fallback.
