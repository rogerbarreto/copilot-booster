# UIA Pane Resolution

Use this skill when wiring UI Automation pane/tab selection for terminal hosts that multiplex multiple sessions in one parent process/window.

## Pattern

1. Resolve the parent host HWND through the normal process-tree path first.
2. Enumerate selectable UIA elements under the parent with a gateway interface, not inline UIA calls.
3. Keep the gateway time-boxed and degradable: partial/empty results must fall back to the parent HWND.
4. Match by pane-root process id when possible: walk `copilotPid` up to the child directly under `WindowsTerminal.exe`, then match that PID to a UIA pane. `AutomationElement.Current.ProcessId` usually reports the UI host process, not the embedded shell/application.
5. Prefer deterministic exact user-visible titles (`SessionId`, launch title, workspace summary, override/alias) only as a fallback; avoid broad substring matching for human titles because `Run Test` can collide with `Run Test 2`.
6. If multiple panes match, prefer the currently selected/active pane, then first match.
7. Cache by `(parent HWND, pane runtime id)` and store the selected child HWND, title, and runtime id. Do not collapse multiple WT tabs to the parent HWND.
8. Invalidate synchronously on parent name/tab changes and child/parent destruction. Do not run UIA inside WinEvent handlers.
9. On focus, foreground the parent WT HWND first, then call a gateway method that re-walks UIA, finds the tab item by runtime id, selects/invokes it, and verifies `SelectionItemPattern.Current.IsSelected`. Use title/process matching only as a fallback when no runtime id is available.

## Windows Terminal Gotchas

- WT tab UIA elements commonly report `ProcessId == WindowsTerminal.exe`; do not assume this is the shell/Copilot PID.
- `NativeWindowHandle` may be zero for tab items. Store the parent HWND plus pane title when no child HWND is exposed.
- `SelectionItemPattern.Select()` is preferred for tab activation; fall back to `InvokePattern.Invoke()`, then read back `SelectionItemPattern.Current.IsSelected` with a short poll before returning success.
- Foreground WT before selecting its tab. Selecting while WT is unfocused can report success yet leave the previously active tab visible on some WT builds.
- For WT XAML-Islands, tabs do not have distinct Win32 HWNDs. The stable dispatch identity is `(parent HWND, UIA runtime id)`.
- Live E2E should assert UIA selected tab identity, WT window title, and a selected-pane content marker after click; selected-tab/title-only assertions can miss swapped session-to-pane mappings.
- WT TextPattern alone may expose only tab-strip/title text. Walk the Raw UIA tree and collect text/name/value surfaces when a test needs selected-terminal content; seed a marker into the visible interactive prompt and fail if the expected marker is absent or another pane's marker is present.
- Logging empty/partial enumeration at Information level makes UIA slowness visible without breaking focus fallback.

## Confidence

High — UIA enumeration, runtime-id tab selection, and pane-root process correlation have been validated across three independent Windows Terminal multi-session bugs.
