using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Manages a dedicated Microsoft Teams window opened as a PWA via Edge --app flag.
/// Tracks the window handle (HWND) by title matching.
/// </summary>
[ExcludeFromCodeCoverage]
internal partial class TeamsWindowService
{
    internal const string TeamsUrl = "https://teams.microsoft.com";
    private const string WindowTitle = "Microsoft Teams";
    private const string IconFileName = "teams-favicon.ico";

    // Shared across all instances to prevent cross-session HWND theft
    private static readonly HashSet<IntPtr> s_claimedHwnds = [];

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetWindowText(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static partial int GetWindowTextLength(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    /// <summary>
    /// Gets or sets the cached window handle.
    /// </summary>
    internal IntPtr CachedHwnd { get; set; }

    /// <summary>
    /// True while the window is being opened and we're polling for the HWND.
    /// Prevents cleanup from removing the entry before the HWND is captured.
    /// </summary>
    internal bool IsPendingOpen { get; private set; }

    /// <summary>
    /// Fired when the Teams window is detected as closed.
    /// </summary>
    internal event Action? WindowClosed;

    /// <summary>
    /// Returns true if the tracked Teams window is still open.
    /// If the cached HWND died (Edge recycled it), re-scans for an unclaimed replacement.
    /// </summary>
    internal bool IsOpen
    {
        get
        {
            if (this.CachedHwnd != IntPtr.Zero)
            {
                if (IsWindow(this.CachedHwnd) && IsTeamsWindowTitle(GetWindowTitle(this.CachedHwnd)))
                {
                    return true;
                }

                // Cached HWND died — unclaim it and try to re-acquire
                s_claimedHwnds.Remove(this.CachedHwnd);
                this.CachedHwnd = IntPtr.Zero;
            }

            // Re-scan for an unclaimed Teams window (Edge may have recycled the HWND)
            var replacement = FindUnclaimedTeamsWindow();
            if (replacement != IntPtr.Zero)
            {
                this.CachedHwnd = replacement;
                s_claimedHwnds.Add(replacement);
                Program.Logger.LogInformation("[Teams] Re-acquired HWND={Hwnd}", replacement);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Opens Teams in Edge app mode. Captures the new window handle by
    /// snapshotting existing Teams HWNDs before launch and polling until
    /// the HWND stabilizes (survives for multiple consecutive checks).
    /// </summary>
    internal async Task OpenAsync()
    {
        var edgePath = EdgeWorkspaceService.FindEdgePath();
        if (edgePath == null)
        {
            Program.Logger.LogWarning("Cannot open Teams: Edge not found");
            return;
        }

        // Snapshot existing Teams windows so we can detect the new one
        var existingHwnds = FindAllTeamsWindows();
        this.IsPendingOpen = true;
        Program.Logger.LogDebug("[Teams] OpenAsync: existing={Count} HWNDs=[{Hwnds}]",
            existingHwnds.Count, string.Join(",", existingHwnds));

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = edgePath,
                Arguments = BuildAppArguments(),
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("Failed to open Teams: {Error}", ex.Message);
            this.IsPendingOpen = false;
            return;
        }

        // Poll for a STABLE new window: must survive 3 consecutive checks (750ms)
        // Edge PWA mode recycles the initial HWND during startup
        var stableHwnd = await PollForStableWindow(existingHwnds).ConfigureAwait(false);
        if (stableHwnd != IntPtr.Zero)
        {
            this.CachedHwnd = stableHwnd;
            s_claimedHwnds.Add(stableHwnd);
            Program.Logger.LogInformation("[Teams] Stable window captured: HWND={Hwnd}", stableHwnd);
        }
        else
        {
            Program.Logger.LogWarning("[Teams] Could not capture stable window handle after timeout");
        }

        this.IsPendingOpen = false;
    }

    /// <summary>
    /// Polls for a new Teams window that stabilizes (same HWND for 3 consecutive checks).
    /// Timeout: ~30 seconds total.
    /// </summary>
    private static async Task<IntPtr> PollForStableWindow(List<IntPtr> existingHwnds)
    {
        IntPtr candidate = IntPtr.Zero;
        int stableCount = 0;
        const int requiredStable = 3;

        for (int i = 0; i < 120; i++)
        {
            await Task.Delay(250).ConfigureAwait(false);
            var newHwnd = FindNewTeamsWindow(existingHwnds);

            if (newHwnd == IntPtr.Zero)
            {
                // No window found — reset stability counter
                candidate = IntPtr.Zero;
                stableCount = 0;
                continue;
            }

            if (newHwnd == candidate)
            {
                stableCount++;
                if (stableCount >= requiredStable)
                {
                    Program.Logger.LogDebug("[Teams] HWND={Hwnd} stable after {Checks} checks", candidate, i + 1);
                    return candidate;
                }
            }
            else
            {
                // New/different HWND — restart stability counter
                Program.Logger.LogDebug("[Teams] Poll {I}: candidate changed {Old} -> {New}", i, candidate, newHwnd);
                candidate = newHwnd;
                stableCount = 1;
            }
        }

        // Return whatever we have, even if not fully stable
        return candidate;
    }

    /// <summary>
    /// Finds a Teams window HWND that is not in the given set of existing handles.
    /// Extracted for testability.
    /// </summary>
    internal static IntPtr FindNewTeamsWindow(List<IntPtr> existingHwnds)
    {
        var currentHwnds = FindAllTeamsWindows();
        return currentHwnds.Find(h => !existingHwnds.Contains(h));
    }

    /// <summary>
    /// Finds a Teams window that isn't claimed by any other TeamsWindowService instance.
    /// Used for HWND re-acquisition when Edge recycles window handles.
    /// </summary>
    private static IntPtr FindUnclaimedTeamsWindow()
    {
        var allWindows = FindAllTeamsWindows();
        return allWindows.Find(h => !s_claimedHwnds.Contains(h));
    }

    /// <summary>
    /// Releases the claimed HWND when this service is no longer tracking a session.
    /// </summary>
    internal void Release()
    {
        if (this.CachedHwnd != IntPtr.Zero)
        {
            s_claimedHwnds.Remove(this.CachedHwnd);
            this.CachedHwnd = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Brings the Teams window to the foreground.
    /// Uses IsOpen to handle HWND re-acquisition if Edge recycled the window.
    /// </summary>
    internal bool Focus()
    {
        if (!this.IsOpen)
        {
            return false;
        }

        return WindowFocusService.TryFocusWindowHandle(this.CachedHwnd);
    }

    /// <summary>
    /// Checks if the Teams window is still open. If not, fires <see cref="WindowClosed"/>.
    /// </summary>
    internal void CheckAlive()
    {
        if (!this.IsOpen)
        {
            this.WindowClosed?.Invoke();
        }
    }

    /// <summary>
    /// Builds the command-line arguments for launching Edge in app mode.
    /// </summary>
    internal static string BuildAppArguments() => $"--app={TeamsUrl}";

    /// <summary>
    /// Returns true if the window title indicates a Teams window.
    /// Matches both the loaded title ("Microsoft Teams") and the loading title ("teams.microsoft.com").
    /// </summary>
    internal static bool IsTeamsWindowTitle(string title)
    {
        return title.Contains(WindowTitle, StringComparison.OrdinalIgnoreCase)
            || title.Contains("teams.microsoft.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the path where the Teams favicon should be cached.
    /// </summary>
    internal static string GetIconCachePath()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CopilotBooster");
        return Path.Combine(appData, IconFileName);
    }

    /// <summary>
    /// Downloads and caches the Teams favicon. Returns the cached image, or null on failure.
    /// </summary>
    internal static async Task<Image?> GetCachedIconAsync()
    {
        var cachePath = GetIconCachePath();

        // Return cached icon if it exists and is recent (cache for 7 days)
        if (File.Exists(cachePath))
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath);
            if (age.TotalDays < 7)
            {
                try
                {
                    return Image.FromFile(cachePath);
                }
                catch { /* re-download on failure */ }
            }
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var bytes = await client.GetByteArrayAsync($"{TeamsUrl}/favicon.ico").ConfigureAwait(false);

            var dir = Path.GetDirectoryName(cachePath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllBytesAsync(cachePath, bytes).ConfigureAwait(false);

            using var ms = new MemoryStream(bytes);
            return Image.FromStream(ms);
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("Failed to download Teams favicon: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Returns all visible Teams window handles (Edge PWA windows with Teams-related title).
    /// </summary>
    internal static List<IntPtr> FindAllTeamsWindows()
    {
        var results = new List<IntPtr>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
            {
                return true;
            }

            var title = GetWindowTitle(hWnd);
            if (IsTeamsWindowTitle(title))
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                try
                {
                    using var proc = Process.GetProcessById((int)pid);
                    if (proc.ProcessName.Contains("msedge", StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(hWnd);
                    }
                }
                catch { /* process may have exited */ }
            }

            return true;
        }, IntPtr.Zero);

        return results;
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        int length = GetWindowTextLength(hWnd);
        if (length == 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        GetWindowText(hWnd, buffer, buffer.Length);
        return new string(buffer, 0, length);
    }
}
