using System;
using System.Diagnostics;
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

    /// <summary>
    /// Attempts to bring the window of the specified process to the foreground.
    /// </summary>
    /// <param name="pid">The process ID whose window should be focused.</param>
    /// <returns><c>true</c> if the window was found and focused; otherwise <c>false</c>.</returns>
    internal static bool TryFocusProcess(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);

            // Refresh to get the latest MainWindowHandle
            proc.Refresh();
            IntPtr hwnd = proc.MainWindowHandle;
            if (hwnd != IntPtr.Zero)
            {
                return FocusWindow(hwnd);
            }

            return false;
        }
        catch
        {
            return false;
        }
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
        uint targetThread = (uint)GetWindowThreadProcessId(hwnd, out _);

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
