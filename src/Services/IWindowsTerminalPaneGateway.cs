using System;
using System.Collections.Generic;

namespace CopilotBooster.Services;

/// <summary>
/// Abstraction over UIAutomation queries on a Windows Terminal window.
/// Exists so pane-focus tests can fake the UIA layer.
/// </summary>
internal interface IWindowsTerminalPaneGateway
{
    /// <summary>
    /// Enumerates all selectable tab items in the WT window identified by <paramref name="wtHwnd"/>.
    /// Each tuple carries the tab's display name and an Action that, when invoked, selects the tab
    /// (UIA SelectionItemPattern.Select or InvokePattern.Invoke).
    /// Returns an empty list on error or if no tabs are found.
    /// Implementations MUST honor the time budget — completing within 250ms is a soft contract;
    /// long-running enumerations should bail and return what they have so far.
    /// </summary>
    IReadOnlyList<(string Name, Action Select)> EnumerateTabs(IntPtr wtHwnd);
}
