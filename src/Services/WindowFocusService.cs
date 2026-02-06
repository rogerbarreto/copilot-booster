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
internal static class WindowFocusService
{
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

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
    /// </summary>
    private static bool FocusWindow(IntPtr hwnd)
    {
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }

        SetForegroundWindow(hwnd);
        return true;
    }

    /// <summary>
    /// Finds the terminal host window (conhost or Windows Terminal) for a console process
    /// by looking for parent/ancestor process windows.
    /// </summary>
    private static IntPtr FindTerminalWindowForProcess(int childPid)
    {
        // Strategy: walk up the parent process chain from the copilot process
        // to find a terminal host that has a visible window.
        try
        {
            var childProc = Process.GetProcessById(childPid);
            var visited = new HashSet<int> { childPid };

            // Walk parent chain (copilot → cmd/powershell → WindowsTerminal/conhost)
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

        // Fallback: enumerate all top-level windows and check if any terminal
        // window's process tree contains our child PID.
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out uint windowPid);
            try
            {
                var windowProc = Process.GetProcessById((int)windowPid);
                var name = windowProc.ProcessName.ToLowerInvariant();
                if (name is "windowsterminal" or "conhost" or "cmd" or "powershell" or "pwsh")
                {
                    // Check if this terminal is hosting our process
                    if (IsAncestorOf((int)windowPid, childPid))
                    {
                        found = hwnd;
                        return false; // stop enumeration
                    }
                }
            }
            catch { }

            return true;
        }, IntPtr.Zero);

        return found;
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

    /// <summary>
    /// Checks whether ancestorPid is an ancestor of childPid in the process tree.
    /// </summary>
    private static bool IsAncestorOf(int ancestorPid, int childPid)
    {
        var visited = new HashSet<int>();
        int currentPid = childPid;

        while (visited.Add(currentPid))
        {
            try
            {
                var proc = Process.GetProcessById(currentPid);
                var parent = GetParentProcess(proc);
                if (parent == null)
                {
                    return false;
                }

                if (parent.Id == ancestorPid)
                {
                    return true;
                }

                currentPid = parent.Id;
            }
            catch
            {
                return false;
            }
        }

        return false;
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

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle, int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation, int processInformationLength,
        out int returnLength);
}
