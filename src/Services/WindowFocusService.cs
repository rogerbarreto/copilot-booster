using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

namespace CopilotBooster.Services;

/// <summary>
/// Provides methods for finding and focusing existing application windows.
/// </summary>
[ExcludeFromCodeCoverage]
internal static partial class WindowFocusService
{
    private const int SW_RESTORE = 9;
    private const int SW_MINIMIZE = 6;
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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr hWnd);

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
    /// Finds the first visible window handle owned by the specified process.
    /// </summary>
    /// <param name="processId">The process ID to search for.</param>
    /// <returns>The window handle if found; otherwise <see cref="IntPtr.Zero"/>.</returns>
    internal static IntPtr FindWindowHandleByPid(int processId)
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
            if (windowPid == targetPid)
            {
                found = hwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>
    /// Finds a visible window handle whose title contains all specified substrings.
    /// </summary>
    /// <param name="titleSubstring">Primary text to search for in window titles (case-insensitive).</param>
    /// <param name="secondarySubstring">Optional secondary text that must also be present (case-insensitive).</param>
    /// <returns>The window handle if found; otherwise <see cref="IntPtr.Zero"/>.</returns>
    internal static IntPtr FindWindowHandleByTitle(string titleSubstring, string? secondarySubstring)
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
            var title = sb.ToString();
            if (title.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase)
                && (secondarySubstring == null || title.Contains(secondarySubstring, StringComparison.OrdinalIgnoreCase)))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>
    /// Finds a visible window whose title contains the specified text and brings it to the foreground.
    /// </summary>
    /// <param name="titleSubstring">Text to search for in window titles (case-insensitive).</param>
    /// <returns><c>true</c> if a matching window was found and focused; otherwise <c>false</c>.</returns>
    internal static bool TryFocusWindowByTitle(string titleSubstring)
    {
        return TryFocusWindowByTitle(titleSubstring, null);
    }

    /// <summary>
    /// Finds a visible window whose title contains all specified substrings and brings it to the foreground.
    /// </summary>
    /// <param name="titleSubstring">Primary text to search for in window titles (case-insensitive).</param>
    /// <param name="secondarySubstring">Optional secondary text that must also be present (case-insensitive).</param>
    /// <returns><c>true</c> if a matching window was found and focused; otherwise <c>false</c>.</returns>
    internal static bool TryFocusWindowByTitle(string titleSubstring, string? secondarySubstring)
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
            var title = sb.ToString();
            if (title.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase)
                && (secondarySubstring == null || title.Contains(secondarySubstring, StringComparison.OrdinalIgnoreCase)))
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
        return FindProcessIdByWindowTitle(titleSubstring, null);
    }

    /// <summary>
    /// Finds a process ID that owns a visible window whose title contains all specified substrings.
    /// </summary>
    /// <param name="titleSubstring">Primary text to search for in window titles (case-insensitive).</param>
    /// <param name="secondarySubstring">Optional secondary text that must also be present (case-insensitive).</param>
    /// <returns>The process ID if found; otherwise <c>-1</c>.</returns>
    internal static int FindProcessIdByWindowTitle(string titleSubstring, string? secondarySubstring)
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
            var title = sb.ToString();
            if (title.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase)
                && (secondarySubstring == null || title.Contains(secondarySubstring, StringComparison.OrdinalIgnoreCase)))
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
    /// Scans all visible windows for titles matching tracked session patterns.
    /// Returns results grouped by session ID with display label and window title.
    /// Matches: "Terminal - {id}", "Terminal #N - {id}", "Copilot CLI - {id}",
    /// and optionally matches window titles equal to known session summaries.
    /// Previously tracked HWNDs are re-validated as a fallback when titles change dynamically.
    /// </summary>
    /// <param name="sessionSummaries">Optional mapping of session summary to session ID for title matching.</param>
    /// <param name="previouslyTracked">Previously tracked windows to re-validate if title matching fails.</param>
    /// <returns>A dictionary mapping session IDs to lists of (label, windowTitle, hwnd) tuples.</returns>
    internal static Dictionary<string, List<(string Label, string Title, IntPtr Hwnd)>> FindTrackedWindows(
        Dictionary<string, string>? sessionSummaries = null,
        Dictionary<string, List<(string Label, string Title, IntPtr Hwnd)>>? previouslyTracked = null)
    {
        var results = new Dictionary<string, List<(string, string, IntPtr)>>(StringComparer.OrdinalIgnoreCase);
        var matchedHwnds = new HashSet<IntPtr>();

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
            var title = sb.ToString();

            string? sessionId = null;
            string? label = null;

            // Match "Copilot CLI - {sessionId}"
            if (title.StartsWith("Copilot CLI - ", StringComparison.OrdinalIgnoreCase))
            {
                sessionId = title.Substring("Copilot CLI - ".Length);
                label = "Copilot CLI";
            }
            // Match "Terminal - {sessionId}"
            else if (title.StartsWith("Terminal - ", StringComparison.OrdinalIgnoreCase))
            {
                sessionId = title.Substring("Terminal - ".Length);
                label = "Terminal";
            }
            // Match "Terminal #N - {sessionId}"
            else if (title.StartsWith("Terminal #", StringComparison.OrdinalIgnoreCase))
            {
                var dashIdx = title.IndexOf(" - ", "Terminal #".Length, StringComparison.Ordinal);
                if (dashIdx > 0)
                {
                    sessionId = title.Substring(dashIdx + 3);
                    label = title.Substring(0, dashIdx);
                }
            }
            // Match window title equal to a known session summary (Copilot CLI sets title to session name)
            // Strip leading emoji/symbol prefixes (e.g. "🤖 " when Copilot is working)
            else if (sessionSummaries != null)
            {
                var stripped = StripLeadingEmoji(title);
                if (sessionSummaries.TryGetValue(stripped, out var matchedId))
                {
                    sessionId = matchedId;
                    label = "Copilot CLI";
                }
            }

            if (sessionId != null && sessionId.Length > 0 && label != null)
            {
                if (!results.ContainsKey(sessionId))
                {
                    results[sessionId] = new List<(string, string, IntPtr)>();
                }
                results[sessionId].Add((label, title, hwnd));
                matchedHwnds.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);

        // Fallback: re-validate previously tracked HWNDs that weren't matched by title
        // (Copilot CLI changes the terminal title dynamically while working)
        if (previouslyTracked != null)
        {
            foreach (var kvp in previouslyTracked)
            {
                if (results.ContainsKey(kvp.Key))
                {
                    continue;
                }

                foreach (var (label, _, prevHwnd) in kvp.Value)
                {
                    if (!matchedHwnds.Contains(prevHwnd) && IsWindow(prevHwnd) && IsWindowVisible(prevHwnd))
                    {
                        // Read current title for display
                        int len = GetWindowTextLength(prevHwnd);
                        var currentTitle = "";
                        if (len > 0)
                        {
                            var sb = new System.Text.StringBuilder(len + 1);
                            GetWindowText(prevHwnd, sb, sb.Capacity);
                            currentTitle = sb.ToString();
                        }

                        if (!results.ContainsKey(kvp.Key))
                        {
                            results[kvp.Key] = new List<(string, string, IntPtr)>();
                        }
                        results[kvp.Key].Add((label, currentTitle, prevHwnd));
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Strips leading emoji/symbol characters and whitespace from a window title.
    /// Copilot CLI prefixes titles with emoji like "🤖 " while working.
    /// </summary>
    internal static string StripLeadingEmoji(string title)
    {
        int i = 0;
        while (i < title.Length)
        {
            var category = char.GetUnicodeCategory(title, i);
            if (category == UnicodeCategory.OtherSymbol
                || category == UnicodeCategory.Surrogate
                || category == UnicodeCategory.ModifierSymbol
                || category == UnicodeCategory.Format
                || category == UnicodeCategory.NonSpacingMark
                || category == UnicodeCategory.SpaceSeparator
                || title[i] == ' ')
            {
                i += char.IsSurrogatePair(title, i) ? 2 : 1;
            }
            else
            {
                break;
            }
        }

        return i > 0 ? title[i..] : title;
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
    /// Gets the title text of a window handle.
    /// </summary>
    internal static string GetWindowTitle(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        int len = GetWindowTextLength(hwnd);
        if (len == 0)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(len + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// Checks if a window handle is still valid and visible.
    /// </summary>
    internal static bool IsWindowAlive(IntPtr hwnd)
    {
        return hwnd != IntPtr.Zero && IsWindow(hwnd) && IsWindowVisible(hwnd);
    }

    /// <summary>
    /// Minimizes the specified window.
    /// </summary>
    internal static void MinimizeWindow(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            ShowWindow(hwnd, SW_MINIMIZE);
        }
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
