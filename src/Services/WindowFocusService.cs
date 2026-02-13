using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace CopilotApp.Services;

/// <summary>
/// Provides methods for finding and focusing existing application windows.
/// </summary>
[ExcludeFromCodeCoverage]
internal static partial class WindowFocusService
{
    private const int SW_RESTORE = 9;
    private const byte VK_MENU = 0x12;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("user32.dll")]
    private static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
#pragma warning disable SYSLIB1054
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);
#pragma warning restore SYSLIB1054

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
#pragma warning disable SYSLIB1054
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
#pragma warning restore SYSLIB1054

    /// <summary>
    /// Finds a visible window owned by the specified process and brings it to the foreground.
    /// </summary>
    /// <param name="processId">The process ID whose window should be focused.</param>
    /// <returns><c>true</c> if a window was found and focused; otherwise <c>false</c>.</returns>
    internal static bool TryFocusProcessWindow(int processId)
    {
        uint targetPid = (uint)processId;
        IntPtr found = IntPtr.Zero;

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint windowPid);
            if (windowPid == targetPid)
            {
                found = hwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        if (found != IntPtr.Zero)
        {
            return FocusWindow(found);
        }

        return false;
    }

    /// <summary>
    /// Finds a visible window whose title contains the specified text and brings it to the foreground.
    /// </summary>
    /// <param name="titleSubstring">Text to search for in window titles (case-insensitive).</param>
    /// <returns><c>true</c> if a matching window was found and focused; otherwise <c>false</c>.</returns>
    internal static bool TryFocusWindowByTitle(string titleSubstring)
    {
        IntPtr found = IntPtr.Zero;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            int len = GetWindowTextLength(hwnd);
            if (len == 0)
            {
                return true;
            }

            var sb = new System.Text.StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            if (sb.ToString().Contains(titleSubstring, StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return found != IntPtr.Zero && FocusWindow(found);
    }

    /// <summary>
    /// Finds a process ID that owns a visible window whose title contains the specified text.
    /// </summary>
    /// <param name="titleSubstring">Text to search for in window titles (case-insensitive).</param>
    /// <returns>The process ID if found; otherwise <c>-1</c>.</returns>
    internal static int FindProcessIdByWindowTitle(string titleSubstring)
    {
        int resultPid = -1;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            int len = GetWindowTextLength(hwnd);
            if (len == 0)
            {
                return true;
            }

            var sb = new System.Text.StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            if (sb.ToString().Contains(titleSubstring, StringComparison.OrdinalIgnoreCase))
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                resultPid = (int)pid;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return resultPid;
    }

    /// <summary>
    /// Brings the specified window handle to the foreground.
    /// </summary>
    /// <param name="hwnd">The window handle to focus.</param>
    /// <returns><c>true</c> if the window was focused; otherwise <c>false</c>.</returns>
    internal static bool TryFocusWindowHandle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        return FocusWindow(hwnd);
    }

    /// <summary>
    /// Restores (if minimized) and brings the specified window to the foreground.
    /// Uses a simulated Alt keypress to bypass Windows foreground restrictions.
    /// </summary>
    private static bool FocusWindow(IntPtr hwnd)
    {
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }

        // Simulate Alt key press/release to bypass Windows foreground lock
        keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY, 0);
        keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);

        SetForegroundWindow(hwnd);

        return true;
    }
}
