using System;
using System.Collections.Generic;
using System.Linq;

namespace CopilotBooster.Services;

internal sealed record WindowsTerminalPaneInfo(
    string Name,
    IntPtr Hwnd,
    int ProcessId,
    bool IsSelected,
    Action Select,
    string? RuntimeId = null);

internal sealed record WindowsTerminalPaneEnumeration(
    IReadOnlyList<WindowsTerminalPaneInfo> Panes,
    bool IsPartial);

/// <summary>
/// Abstraction over UIAutomation queries on a Windows Terminal window.
/// Exists so pane-focus tests can fake the UIA layer.
/// </summary>
internal interface IWindowsTerminalPaneGateway
{
    /// <summary>
    /// Enumerates all selectable panes/tabs in the WT window identified by <paramref name="wtHwnd"/>.
    /// Returns an empty list on error or if no panes are found. Implementations MUST honor the
    /// time budget — completing within 250ms is a soft contract; long-running enumerations should
    /// bail and return what they have so far with <see cref="WindowsTerminalPaneEnumeration.IsPartial"/> true.
    /// </summary>
    WindowsTerminalPaneEnumeration EnumeratePanes(IntPtr wtHwnd)
    {
        var panes = this.EnumerateTabs(wtHwnd)
            .Select(tab => new WindowsTerminalPaneInfo(tab.Name, IntPtr.Zero, 0, false, tab.Select))
            .ToList();
        return new WindowsTerminalPaneEnumeration(panes, IsPartial: false);
    }

    /// <summary>
    /// Selects a known Windows Terminal pane/tab by UIA runtime id.
    /// </summary>
    bool FocusPane(IntPtr wtHwnd, string paneRuntimeId);

    /// <summary>
    /// Enumerates all selectable tab items in the WT window identified by <paramref name="wtHwnd"/>.
    /// Kept for compatibility with the Phase 4 UIA gateway contract.
    /// </summary>
    IReadOnlyList<(string Name, Action Select)> EnumerateTabs(IntPtr wtHwnd);
}
