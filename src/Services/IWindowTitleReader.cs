using System;

namespace CopilotBooster.Services;

internal interface IWindowTitleReader
{
    /// <summary>
    /// Returns the HWND of the FIRST visible window owned by processId that has a non-empty title.
    /// Returns IntPtr.Zero if no such window exists.
    /// </summary>
    IntPtr FindMainWindowHandle(int processId);

    /// <summary>
    /// Reads the current title of the given HWND. Returns "" if hwnd is invalid or has no title.
    /// </summary>
    string ReadTitle(IntPtr hwnd);
}
