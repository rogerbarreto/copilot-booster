using System;
using System.Collections.Generic;

namespace CopilotBooster.Services;

/// <summary>
/// Abstraction for process tree operations, enabling testability.
/// </summary>
internal interface IProcessTreeProvider
{
    /// <summary>
    /// Returns the parent PID of <paramref name="pid"/>, or null if no parent or process is gone.
    /// </summary>
    int? GetParentPid(int pid);

    /// <summary>
    /// Returns the process name (without .exe) for <paramref name="pid"/>, or null if not found.
    /// </summary>
    string? GetProcessName(int pid);

    /// <summary>
    /// Returns the focusable top-level visible HWND owned by <paramref name="pid"/>, or <see cref="IntPtr.Zero"/> if none.
    /// "Focusable" = top-level, visible, not WS_EX_TOOLWINDOW. Same heuristic as <see cref="WindowFocusService.FindWindowHandleByPid"/>.
    /// </summary>
    IntPtr GetTopLevelWindow(int pid);

    /// <summary>
    /// Returns ALL focusable top-level visible HWNDs owned by <paramref name="pid"/> in
    /// <c>EnumWindows</c> order, or an empty list if none. Necessary for processes that
    /// host multiple top-level windows under a single PID — most notably the Sun Valley
    /// Windows Terminal monarch (one <c>WindowsTerminal.exe</c> hosts every wt window).
    /// </summary>
    IReadOnlyList<IntPtr> EnumerateTopLevelWindows(int pid);
}
