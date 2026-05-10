using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace CopilotBooster.Services;

[ExcludeFromCodeCoverage]
internal sealed partial class Win32WindowTitleReader : IWindowTitleReader
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static partial int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
#pragma warning disable SYSLIB1054
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
#pragma warning restore SYSLIB1054

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public IntPtr FindMainWindowHandle(int processId)
    {
        uint targetPid = (uint)processId;
        IntPtr found = IntPtr.Zero;

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }

            GetWindowThreadProcessId(hwnd, out uint windowPid);
            if (windowPid != targetPid)
            {
                return true;
            }

            // Check for non-empty title
            int titleLen = GetWindowTextLength(hwnd);
            if (titleLen > 0)
            {
                found = hwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    public string ReadTitle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return "";
        }

        int len = GetWindowTextLength(hwnd);
        if (len == 0)
        {
            return "";
        }

        var sb = new StringBuilder(len + 1);
        int result = GetWindowText(hwnd, sb, sb.Capacity);
        return result > 0 ? sb.ToString() : "";
    }
}
