using System;
using System.Collections.Generic;
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

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
            IntPtr hwnd = proc.MainWindowHandle;
            if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd))
            {
                return FocusWindow(hwnd);
            }

            // The process may be hosted inside a terminal (conhost/Windows Terminal).
            // Walk all top-level windows to find one belonging to the hosting terminal.
            hwnd = FindTerminalWindowForProcess(pid);
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

    /// <summary>
    /// Finds the terminal host window for a console process.
    /// Handles both Windows Terminal (default terminal) and standalone conhost.
    /// </summary>
    private static IntPtr FindTerminalWindowForProcess(int childPid)
    {
        try
        {
            // Strategy 1: The process itself may have a visible window
            var childProc = Process.GetProcessById(childPid);
            if (childProc.MainWindowHandle != IntPtr.Zero && IsWindowVisible(childProc.MainWindowHandle))
            {
                return childProc.MainWindowHandle;
            }

            // Strategy 2: Find conhost child of our process — its parent terminal has the window.
            // When Windows Terminal is the default, the process tree is:
            //   copilot.exe → conhost.exe (child) ← hosted by WindowsTerminal.exe
            // Find all conhost processes whose parent is our target PID.
            foreach (var conhost in Process.GetProcessesByName("conhost"))
            {
                try
                {
                    var parent = GetParentProcess(conhost);
                    if (parent?.Id == childPid)
                    {
                        // This conhost belongs to our process.
                        // Now find the Windows Terminal that owns this conhost's console.
                        // Windows Terminal is the only top-level visible window process
                        // of type "WindowsTerminal" — focus it.
                        foreach (var wt in Process.GetProcessesByName("WindowsTerminal"))
                        {
                            if (wt.MainWindowHandle != IntPtr.Zero && IsWindowVisible(wt.MainWindowHandle))
                            {
                                return wt.MainWindowHandle;
                            }
                        }

                        // Fallback: conhost itself might have a window (legacy console mode)
                        if (conhost.MainWindowHandle != IntPtr.Zero && IsWindowVisible(conhost.MainWindowHandle))
                        {
                            return conhost.MainWindowHandle;
                        }
                    }
                }
                catch { }
            }

            // Strategy 3: Walk parent chain as fallback
            var visited = new HashSet<int> { childPid };
            Process? current = GetParentProcess(childProc);
            while (current != null && visited.Add(current.Id))
            {
                if (current.MainWindowHandle != IntPtr.Zero && IsWindowVisible(current.MainWindowHandle))
                {
                    return current.MainWindowHandle;
                }

                current = GetParentProcess(current);
            }
        }
        catch { }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Gets the parent process of the specified process.
    /// </summary>
    private static Process? GetParentProcess(Process process)
    {
        try
        {
            // Use WMI-free approach via handle
            var handle = process.Handle;
            var pbi = new PROCESS_BASIC_INFORMATION();
            int status = NtQueryInformationProcess(handle, 0, ref pbi, Marshal.SizeOf(pbi), out _);
            if (status == 0 && pbi.InheritedFromUniqueProcessId != IntPtr.Zero)
            {
                return Process.GetProcessById(pbi.InheritedFromUniqueProcessId.ToInt32());
            }
        }
        catch { }

        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(
        IntPtr processHandle, int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation, int processInformationLength,
        out int returnLength);
}
