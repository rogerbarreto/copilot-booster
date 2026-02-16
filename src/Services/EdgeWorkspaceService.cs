using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;
using Microsoft.Win32;

namespace CopilotBooster.Services;

/// <summary>
/// Manages Edge browser workspace windows identified by a unique GUID in the tab title.
/// Uses UI Automation to find tabs even when they are not the active tab.
/// </summary>
[ExcludeFromCodeCoverage]
internal partial class EdgeWorkspaceService
{
    private const string TitlePrefix = "Copilot Booster Session [";

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr hWnd);

    /// <summary>
    /// Gets the unique identifier for this workspace.
    /// </summary>
    internal string WorkspaceId { get; }

    /// <summary>
    /// The title substring used to find the Edge tab/window.
    /// </summary>
    private string TitleMarker => $"{TitlePrefix}{this.WorkspaceId}]";

    /// <summary>
    /// Gets or sets the cached window handle for external use (e.g. minimize, bulk scan).
    /// </summary>
    internal IntPtr CachedHwnd { get; set; }

    /// <summary>
    /// Fires when the anchor tab is no longer found.
    /// </summary>
    internal event Action? WindowClosed;

    /// <summary>
    /// Returns true if an Edge tab with the anchor title still exists.
    /// </summary>
    internal bool IsOpen
    {
        get
        {
            // Fast path: check cached window first
            if (this.CachedHwnd != IntPtr.Zero && IsWindow(this.CachedHwnd))
            {
                if (FindEdgeTabInWindow(this.CachedHwnd, this.TitleMarker))
                {
                    return true;
                }
            }

            // Slow path: scan all Edge windows
            var result = FindEdgeWindowWithTab(this.TitleMarker);
            this.CachedHwnd = result;
            return result != IntPtr.Zero;
        }
    }

    /// <summary>
    /// Creates a new workspace service with the given identifier.
    /// </summary>
    internal EdgeWorkspaceService(string workspaceId)
    {
        this.WorkspaceId = workspaceId;
    }

    /// <summary>
    /// Launches Edge with a new window pointing to session.html with the workspace GUID.
    /// Waits up to <paramref name="timeoutMs"/> milliseconds for the tab to appear.
    /// </summary>
    /// <returns>True if the tab was detected; false on timeout.</returns>
    internal async Task<bool> OpenAsync(int timeoutMs = 10000)
    {
        var sessionHtml = GetSessionHtmlPath();
        if (sessionHtml == null)
        {
            return false;
        }

        var url = $"file:///{sessionHtml.Replace('\\', '/')}#{this.WorkspaceId}";

        try
        {
            var edgePath = FindEdgePath();
            if (edgePath != null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = edgePath,
                    Arguments = $"--new-window \"{url}\"",
                    UseShellExecute = false
                });
            }
            else
            {
                // Fallback: use the microsoft-edge: protocol handler (no --new-window support)
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"microsoft-edge:{url}",
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            return false;
        }

        // Poll for the tab to appear
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (this.IsOpen)
            {
                return true;
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        return this.IsOpen;
    }

    /// <summary>
    /// Brings the Edge window containing the anchor tab to the foreground.
    /// </summary>
    /// <returns>True if a matching window was found and focused.</returns>
    internal bool Focus()
    {
        if (this.CachedHwnd != IntPtr.Zero && IsWindow(this.CachedHwnd)
            && FindEdgeTabInWindow(this.CachedHwnd, this.TitleMarker))
        {
            return WindowFocusService.TryFocusWindowHandle(this.CachedHwnd);
        }

        var hwnd = FindEdgeWindowWithTab(this.TitleMarker);
        if (hwnd != IntPtr.Zero)
        {
            this.CachedHwnd = hwnd;
            return WindowFocusService.TryFocusWindowHandle(hwnd);
        }

        return false;
    }

    /// <summary>
    /// Checks if the workspace tab is still open. If not, fires <see cref="WindowClosed"/>.
    /// </summary>
    internal void CheckAlive()
    {
        if (!this.IsOpen)
        {
            this.WindowClosed?.Invoke();
        }
    }

    /// <summary>
    /// Uses UI Automation to check if a specific Edge window contains a tab matching the title.
    /// </summary>
    private static bool FindEdgeTabInWindow(IntPtr hwnd, string titleMarker)
    {
        try
        {
            var element = AutomationElement.FromHandle(hwnd);
            var tabCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);
            var tabs = element.FindAll(TreeScope.Descendants, tabCondition);
            foreach (AutomationElement tab in tabs)
            {
                if (tab.Current.Name.Contains(titleMarker, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Scans all Edge windows to find one containing a tab with the given title marker.
    /// Returns the window handle, or IntPtr.Zero if not found.
    /// </summary>
    private static IntPtr FindEdgeWindowWithTab(string titleMarker)
    {
        IntPtr result = IntPtr.Zero;

        try
        {
            var root = AutomationElement.RootElement;
            var edgeCondition = new PropertyCondition(AutomationElement.ClassNameProperty, "Chrome_WidgetWin_1");
            var windows = root.FindAll(TreeScope.Children, edgeCondition);

            foreach (AutomationElement win in windows)
            {
                try
                {
                    var name = win.Current.Name;
                    if (!name.Contains("Edge", StringComparison.OrdinalIgnoreCase)
                        && !name.Contains("Copilot Booster Session", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var hwnd = new IntPtr(win.Current.NativeWindowHandle);
                    if (FindEdgeTabInWindow(hwnd, titleMarker))
                    {
                        result = hwnd;
                        break;
                    }
                }
                catch { }
            }
        }
        catch { }

        return result;
    }

    /// <summary>
    /// Resolves the path to session.html next to the running executable.
    /// </summary>
    private static string? GetSessionHtmlPath()
    {
        var exeDir = AppContext.BaseDirectory;
        var path = Path.Combine(exeDir, "session.html");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Resolves the Edge executable path from the Windows registry App Paths,
    /// falling back to common install locations.
    /// </summary>
    private static string? FindEdgePath()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe");
            var path = key?.GetValue(null) as string;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                return path;
            }
        }
        catch { }

        string[] knownPaths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                @"Microsoft\Edge\Application\msedge.exe"),
        ];

        foreach (var candidate in knownPaths)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Scans all Edge windows once and returns a dictionary mapping session IDs
    /// to their corresponding window handles, for sessions that have a matching tab.
    /// This avoids repeating expensive UI Automation scans per-session.
    /// </summary>
    internal static Dictionary<string, IntPtr> BulkFindEdgeTabs(IEnumerable<string> sessionIds)
    {
        var result = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

        // Build title markers for all candidate session IDs
        var markers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in sessionIds)
        {
            markers[id] = $"{TitlePrefix}{id}]";
        }

        if (markers.Count == 0)
        {
            return result;
        }

        try
        {
            var root = AutomationElement.RootElement;
            var edgeCondition = new PropertyCondition(AutomationElement.ClassNameProperty, "Chrome_WidgetWin_1");
            var windows = root.FindAll(TreeScope.Children, edgeCondition);

            foreach (AutomationElement win in windows)
            {
                try
                {
                    var name = win.Current.Name;
                    if (!name.Contains("Edge", StringComparison.OrdinalIgnoreCase)
                        && !name.Contains("Copilot Booster Session", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var hwnd = new IntPtr(win.Current.NativeWindowHandle);

                    // Get all tabs in this window once (expensive UI Automation call)
                    var tabCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);
                    var tabs = AutomationElement.FromHandle(hwnd).FindAll(TreeScope.Descendants, tabCondition);

                    foreach (AutomationElement tab in tabs)
                    {
                        var tabName = tab.Current.Name;
                        foreach (var kvp in markers)
                        {
                            if (!result.ContainsKey(kvp.Key)
                                && tabName.Contains(kvp.Value, StringComparison.OrdinalIgnoreCase))
                            {
                                result[kvp.Key] = hwnd;
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        return result;
    }
}
