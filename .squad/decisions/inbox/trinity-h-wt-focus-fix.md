# Trinity-H — Windows Terminal Multi-Tab Discovery and Focus Fix

**Date:** 2026-05-03
**Status:** Implemented
**Owner:** Trinity

## Bug

v0.21.0 could discover two Copilot CLI sessions running in separate Windows Terminal tabs, but only one session reliably stayed marked as `Copilot CLI` active in the booster grid. Clicking a session's `Copilot CLI` link foregrounded the WT parent window but did not select the tab containing that session.

## Root Cause

Windows Terminal uses XAML Islands/composed UI, so tabs do not have distinct Win32 HWNDs. Trinity-G's fallback stored the WT parent HWND plus title when UIA did not expose a child HWND. Title-change handling then removed entries by parent HWND, collapsing multiple Copilot sessions that shared the same parent. Focus dispatch also had only an HWND at the call site unless the pane could be rediscovered by title.

## Decision

Carry the UIA tab runtime id as part of `CopilotHostInfo` and treat WT session identity as `(parent HWND, pane runtime id)`. Cache WT panes by `(parent HWND, pane runtime id)` and keep host projections from being removed by parent HWND title churn. On focus, if the host is WT and a runtime id is known, call `IWindowsTerminalPaneGateway.FocusPane(parentHwnd, runtimeId)` before foregrounding the parent HWND. `FocusPane` selects the UIA tab item with `SelectionItemPattern.Select()` and falls back to `InvokePattern.Invoke()`.

## Consequences

Non-WT hosts keep the existing one-host-per-HWND behavior. WT focus remains degradable: if runtime-id selection fails, the parent window is still foregrounded and title/process matching remains as a fallback. Live LocalOnly E2E now uses separate WT tabs and asserts both active-grid discovery and selected-tab focus.
