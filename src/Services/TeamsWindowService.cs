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
    /// Fired when the Teams window is detected as closed.
    /// </summary>
    internal event Action? WindowClosed;

    /// <summary>
    /// Returns true if the tracked Teams window is still open.
    /// Only checks the cached HWND — does NOT scan for windows (avoids stealing another session's window).
    /// </summary>
    internal bool IsOpen
    {
        get
        {
            if (this.CachedHwnd == IntPtr.Zero)
            {
                return false;
            }

            if (!IsWindow(this.CachedHwnd))
            {
                this.CachedHwnd = IntPtr.Zero;
                return false;
            }

            return GetWindowTitle(this.CachedHwnd).Contains(WindowTitle, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Opens Teams in Edge app mode. Captures the new window handle by
    /// snapshotting existing Teams HWNDs before launch and polling for a new one.
    /// </summary>
    internal void Open()
    {
        var edgePath = EdgeWorkspaceService.FindEdgePath();
        if (edgePath == null)
        {
            Program.Logger.LogWarning("Cannot open Teams: Edge not found");
            return;
        }

        // Snapshot existing Teams windows so we can detect the new one
        var existingHwnds = FindAllTeamsWindows();

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
            return;
        }

        // Poll for the new window (up to 10 seconds)
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(250).ConfigureAwait(false);
                var currentHwnds = FindAllTeamsWindows();
                var newHwnd = currentHwnds.Find(h => !existingHwnds.Contains(h));
                if (newHwnd != IntPtr.Zero)
                {
                    this.CachedHwnd = newHwnd;
                    Program.Logger.LogInformation("Teams window captured: HWND={Hwnd}", newHwnd);
                    return;
                }
            }

            Program.Logger.LogWarning("Could not capture Teams window handle after 10s");
        });
    }

    /// <summary>
    /// Brings the Teams window to the foreground.
    /// Only operates on the cached HWND — does NOT scan for windows.
    /// </summary>
    internal bool Focus()
    {
        if (this.CachedHwnd != IntPtr.Zero && IsWindow(this.CachedHwnd)
            && GetWindowTitle(this.CachedHwnd).Contains(WindowTitle, StringComparison.OrdinalIgnoreCase))
        {
            return WindowFocusService.TryFocusWindowHandle(this.CachedHwnd);
        }

        return false;
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
    /// Returns all visible Teams window handles (Edge PWA windows with "Microsoft Teams" in title).
    /// </summary>
    private static List<IntPtr> FindAllTeamsWindows()
    {
        var results = new List<IntPtr>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
            {
                return true;
            }

            var title = GetWindowTitle(hWnd);
            if (title.Contains(WindowTitle, StringComparison.OrdinalIgnoreCase))
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
