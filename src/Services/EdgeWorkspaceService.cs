using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace CopilotBooster.Services;

/// <summary>
/// Manages Edge browser workspace windows identified by a unique GUID in the tab title.
/// Uses UI Automation to find tabs even when they are not the active tab.
/// </summary>
[ExcludeFromCodeCoverage]
internal partial class EdgeWorkspaceService
{
    private const string TitlePrefix = "CB Session [";

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr hWnd);

    /// <summary>
    /// Gets the unique identifier for this workspace.
    /// </summary>
    internal string WorkspaceId { get; }

    /// <summary>
    /// The title substring used to find the Edge tab/window.
    /// Uses just the bracketed ID which is unique enough (GUID) and appears in both
    /// named titles "[Name] - [id]" and unnamed titles "[id]".
    /// </summary>
    private string TitleMarker => $"[{this.WorkspaceId}]";

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
    /// Launches Edge with a new window pointing to session.html with the workspace GUID and optional session name.
    /// Opens an additional new-tab so the session anchor tab is not navigated away.
    /// Waits up to <paramref name="timeoutMs"/> milliseconds for the tab to appear.
    /// </summary>
    /// <returns>True if the tab was detected; false on timeout.</returns>
    internal async Task<bool> OpenAsync(string? sessionName = null, int timeoutMs = 10000)
    {
        var sessionHtml = GetSessionHtmlPath();
        if (sessionHtml == null)
        {
            Program.Logger.LogWarning("session.html not found in {BaseDir}", AppContext.BaseDirectory);
            return false;
        }

        var url = BuildSessionUrl(sessionHtml, this.WorkspaceId, sessionName);

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
                // Open a new tab so the session anchor tab is not navigated away
                if (this.CachedHwnd != IntPtr.Zero)
                {
                    WindowFocusService.TryFocusWindowHandle(this.CachedHwnd);
                    WindowFocusService.WaitForForeground(this.CachedHwnd);
                    WindowFocusService.SendCtrlT(this.CachedHwnd);
                }

                return true;
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        return this.IsOpen;
    }

    /// <summary>
    /// Updates the session name displayed in the Edge tab by navigating to the updated hash URL.
    /// The session.html hashchange listener updates the title and content live.
    /// </summary>
    internal void UpdateSessionName(string? sessionName)
    {
        var sessionHtml = GetSessionHtmlPath();
        if (sessionHtml == null || !this.IsOpen)
        {
            return;
        }

        var url = BuildSessionUrl(sessionHtml, this.WorkspaceId, sessionName);

        try
        {
            var edgePath = FindEdgePath();
            if (edgePath != null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = edgePath,
                    Arguments = $"\"{url}\"",
                    UseShellExecute = false
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"microsoft-edge:{url}",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex) { Program.Logger.LogDebug("Failed to update Edge session name: {Error}", ex.Message); }
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
        catch (Exception ex) { Program.Logger.LogDebug("UI Automation error scanning Edge tab: {Error}", ex.Message); }

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
                        && !name.Contains("CB Session", StringComparison.OrdinalIgnoreCase))
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
                catch (Exception ex) { Program.Logger.LogDebug("UI Automation error scanning Edge window: {Error}", ex.Message); }
            }
        }
        catch (Exception ex) { Program.Logger.LogDebug("UI Automation error: {Error}", ex.Message); }

        return result;
    }

    /// <summary>
    /// Builds the file:// URL for session.html with the workspace ID and optional URL-encoded session name in the hash.
    /// </summary>
    internal static string BuildSessionUrl(string sessionHtmlPath, string workspaceId, string? sessionName)
    {
        var baseUrl = $"file:///{sessionHtmlPath.Replace('\\', '/')}#{workspaceId}";
        if (!string.IsNullOrWhiteSpace(sessionName))
        {
            baseUrl += $"/{Uri.EscapeDataString(sessionName)}";
        }

        return baseUrl;
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
        catch (Exception ex) { Program.Logger.LogDebug("Failed to get session HTML path: {Error}", ex.Message); }

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
    /// Scans all Edge windows for tabs containing "CB Session [sessionId]"
    /// and returns a mapping of session ID → window handle.
    /// This is Edge-first: we scan Edge windows (fewer) and extract session IDs
    /// from tab names, avoiding per-session probing.
    /// </summary>
    internal static Dictionary<string, IntPtr> ScanEdgeForSessionTabs()
    {
        var result = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

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
                        && !name.Contains("CB Session", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var hwnd = new IntPtr(win.Current.NativeWindowHandle);

                    var tabCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);
                    var tabs = AutomationElement.FromHandle(hwnd).FindAll(TreeScope.Descendants, tabCondition);

                    foreach (AutomationElement tab in tabs)
                    {
                        var tabName = tab.Current.Name;
                        var sessionId = ExtractSessionId(tabName);
                        if (sessionId != null && !result.ContainsKey(sessionId))
                        {
                            result[sessionId] = hwnd;
                        }
                    }
                }
                catch (Exception ex) { Program.Logger.LogDebug("UI Automation error scanning Edge window: {Error}", ex.Message); }
            }
        }
        catch (Exception ex) { Program.Logger.LogDebug("UI Automation error: {Error}", ex.Message); }

        return result;
    }

    /// <summary>
    /// Extracts the session ID from a tab name like "CB Session [name] - [guid]"
    /// or "CB Session [guid]". Returns the content of the last bracketed segment.
    /// </summary>
    internal static string? ExtractSessionId(string tabName)
    {
        var prefixIdx = tabName.IndexOf(TitlePrefix, StringComparison.OrdinalIgnoreCase);
        if (prefixIdx < 0)
        {
            return null;
        }

        // Find the last [...] bracket pair — that's the session ID
        var lastOpen = tabName.LastIndexOf('[');
        if (lastOpen < prefixIdx)
        {
            return null;
        }

        var lastClose = tabName.IndexOf(']', lastOpen);
        if (lastClose < 0)
        {
            return null;
        }

        return tabName[(lastOpen + 1)..lastClose];
    }

    /// <summary>
    /// Reads the URLs of all non-anchor tabs in this workspace's Edge window by activating
    /// each tab via UI Automation, reading the address bar, then restoring the original tab.
    /// Must be called on an STA thread.
    /// </summary>
    internal List<string> GetTabUrls()
    {
        var urls = new List<string>();
        if (this.CachedHwnd == IntPtr.Zero || !IsWindow(this.CachedHwnd))
        {
            return urls;
        }

        try
        {
            var window = AutomationElement.FromHandle(this.CachedHwnd);
            var tabCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);
            var tabs = window.FindAll(TreeScope.Descendants, tabCondition);

            // Find the address bar (Edit control with "Address and search bar" name)
            var editCondition = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.NameProperty, "Address and search bar"));
            var addressBar = window.FindFirst(TreeScope.Descendants, editCondition);
            if (addressBar == null)
            {
                Program.Logger.LogDebug("Could not find Edge address bar via UI Automation");
                return urls;
            }

            // Remember the original tab's address bar URL to restore later
            string? originalUrl = null;
            if (addressBar.TryGetCurrentPattern(ValuePattern.Pattern, out var initVal)
                && initVal is ValuePattern initVp)
            {
                originalUrl = initVp.Current.Value;
            }

            foreach (AutomationElement tab in tabs)
            {
                var tabName = tab.Current.Name;

                // Skip the CB Session anchor tab
                if (tabName.Contains(TitlePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Skip "New Tab" tabs
                if (string.Equals(tabName, "New Tab", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tabName, "New tab", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Activate the tab
                if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var patternObj)
                    && patternObj is SelectionItemPattern selPattern)
                {
                    try
                    {
                        selPattern.Select();

                        if (addressBar.TryGetCurrentPattern(ValuePattern.Pattern, out var valObj)
                            && valObj is ValuePattern vp)
                        {
                            var url = vp.Current.Value;
                            if (!string.IsNullOrWhiteSpace(url))
                            {
                                if (!url.Contains("://"))
                                {
                                    url = "https://" + url;
                                }

                                urls.Add(url);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Program.Logger.LogDebug("Failed to read URL from Edge tab '{Tab}': {Error}", tabName, ex.Message);
                    }
                }
            }

            // Restore the originally selected tab by finding the one whose address bar matches
            if (originalUrl != null)
            {
                try
                {
                    var freshTabs = window.FindAll(TreeScope.Descendants, tabCondition);
                    foreach (AutomationElement tab in freshTabs)
                    {
                        if (tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var restoreObj)
                            && restoreObj is SelectionItemPattern restorePattern)
                        {
                            restorePattern.Select();

                            if (addressBar.TryGetCurrentPattern(ValuePattern.Pattern, out var valObj)
                                && valObj is ValuePattern vp
                                && string.Equals(vp.Current.Value, originalUrl, StringComparison.OrdinalIgnoreCase))
                            {
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Program.Logger.LogDebug("Failed to restore original Edge tab: {Error}", ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("Failed to read Edge tab URLs: {Error}", ex.Message);
        }

        Program.Logger.LogDebug("Read {Count} Edge tab URLs for workspace {Id}", urls.Count, this.WorkspaceId);
        return urls;
    }

    /// <summary>
    /// Opens each URL in a new tab in this workspace's Edge window.
    /// </summary>
    internal void RestoreTabs(List<string> urls)
    {
        if (urls.Count == 0 || this.CachedHwnd == IntPtr.Zero)
        {
            return;
        }

        var edgePath = FindEdgePath();
        if (edgePath == null)
        {
            return;
        }

        foreach (var url in urls)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = edgePath,
                    Arguments = $"\"{url}\"",
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                Program.Logger.LogDebug("Failed to restore Edge tab '{Url}': {Error}", url, ex.Message);
            }
        }

        Program.Logger.LogInformation("Restored {Count} Edge tabs for workspace {Id}", urls.Count, this.WorkspaceId);
    }
}
