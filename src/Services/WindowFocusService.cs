using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace CopilotApp.Services;

/// <summary>
/// Provides methods for finding and focusing existing application windows.
/// </summary>
[ExcludeFromCodeCoverage]
internal static partial class WindowFocusService
{
    private const int SW_RESTORE = 9;

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
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(IntPtr hWnd);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowText(IntPtr hWnd, string lpString);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowText(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static partial int GetWindowTextLength(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
#pragma warning disable SYSLIB1054
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
#pragma warning restore SYSLIB1054

    /// <summary>
    /// Finds a window by its exact title and brings it to the foreground.
    /// </summary>
    /// <param name="title">The window title to search for.</param>
    /// <returns><c>true</c> if the window was found and focused; otherwise <c>false</c>.</returns>
    internal static bool TryFocusWindowByTitle(string title)
    {
        // First try exact match with FindWindow
        IntPtr hwnd = FindWindow(null, title);
        if (hwnd != IntPtr.Zero)
        {
            return FocusWindow(hwnd);
        }

        // Fallback: enumerate all windows looking for a title that contains our marker
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h))
            {
                return true;
            }

            int len = GetWindowTextLength(h);
            if (len > 0)
            {
                var buf = new char[len + 1];
                GetWindowText(h, buf, buf.Length);
                var windowTitle = new string(buf, 0, len);
                if (windowTitle.Contains(title, StringComparison.Ordinal))
                {
                    found = h;
                    return false;
                }
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
    /// Finds a window by its current title and renames it.
    /// </summary>
    /// <param name="currentTitle">The current window title to search for.</param>
    /// <param name="newTitle">The new title to set.</param>
    internal static void TrySetWindowTitle(string currentTitle, string newTitle)
    {
        IntPtr hwnd = FindWindow(null, currentTitle);
        if (hwnd == IntPtr.Zero)
        {
            // Try enumeration fallback
            EnumWindows((h, _) =>
            {
                if (!IsWindowVisible(h))
                {
                    return true;
                }

                int len = GetWindowTextLength(h);
                if (len > 0)
                {
                    var buf = new char[len + 1];
                    GetWindowText(h, buf, buf.Length);
                    var title = new string(buf, 0, len);
                    if (title.Contains(currentTitle, StringComparison.Ordinal))
                    {
                        hwnd = h;
                        return false;
                    }
                }

                return true;
            }, IntPtr.Zero);
        }

        if (hwnd != IntPtr.Zero)
        {
            SetWindowText(hwnd, newTitle);
        }
    }

    /// <summary>
    /// Finds all visible windows with the specified exact title.
    /// </summary>
    /// <param name="title">The window title to search for.</param>
    /// <returns>A set of window handles matching the title.</returns>
    internal static HashSet<IntPtr> FindWindowsByTitle(string title)
    {
        var handles = new HashSet<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            int len = GetWindowTextLength(hwnd);
            if (len > 0)
            {
                var buf = new char[len + 1];
                GetWindowText(hwnd, buf, buf.Length);
                var windowTitle = new string(buf, 0, len);
                if (windowTitle == title)
                {
                    handles.Add(hwnd);
                }
            }

            return true;
        }, IntPtr.Zero);
        return handles;
    }

    /// <summary>
    /// Attempts to focus a window by its handle.
    /// </summary>
    /// <param name="hwnd">The window handle to focus.</param>
    /// <returns><c>true</c> if the window was focused; otherwise <c>false</c>.</returns>
    internal static bool TryFocusWindowHandle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd))
        {
            return false;
        }

        return FocusWindow(hwnd);
    }

    /// <summary>
    /// Restores (if minimized) and brings the specified window to the foreground.
    /// Uses AttachThreadInput to bypass Windows foreground restrictions.
    /// </summary>
    private static bool FocusWindow(IntPtr hwnd)
    {
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }

        // Attach to the foreground window's thread to gain SetForegroundWindow permission
        IntPtr foregroundHwnd = GetForegroundWindow();
        uint currentThread = GetCurrentThreadId();
        uint foregroundThread = (uint)GetWindowThreadProcessId(foregroundHwnd, out _);

        bool attached = false;
        if (currentThread != foregroundThread)
        {
            attached = AttachThreadInput(currentThread, foregroundThread, true);
        }

        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        return true;
    }
}
