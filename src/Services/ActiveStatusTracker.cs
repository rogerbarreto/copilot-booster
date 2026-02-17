using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using CopilotBooster.Models;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Result snapshot returned by <see cref="ActiveStatusTracker.Refresh"/>.
/// </summary>
internal record ActiveStatusSnapshot(
    Dictionary<string, string> ActiveTextBySessionId,
    Dictionary<string, string> SessionNamesById,
    Dictionary<string, string> StatusIconBySessionId
);

/// <summary>
/// Tracks active processes, terminals, and Edge workspaces for sessions.
/// </summary>
[ExcludeFromCodeCoverage]
internal class ActiveStatusTracker
{
    private HashSet<string> _activeSessionIds = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<(string Label, string Title, IntPtr Hwnd)>> _activeTrackedWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ActiveProcess>> _trackedProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EdgeWorkspaceService> _edgeWorkspaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IntPtr> _explorerWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _startedSessionIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _handleCacheInitialLoadDone;

    private static readonly HashSet<string> s_ignoredSummaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "GitHub Copilot"
    };

    /// <summary>
    /// Callback invoked (possibly from a background thread) when an Edge workspace is closed.
    /// </summary>
    internal Action<string>? OnEdgeWorkspaceClosed { get; set; }

    /// <summary>
    /// Seeds sessions present at startup. These will output "" instead of "bell"
    /// until they transition to working first, preventing false bell notifications on app launch.
    /// Only Copilot CLI sessions should be passed here.
    /// </summary>
    internal void InitStartedSessions(IEnumerable<string> copilotCliSessionIds)
    {
        this._startedSessionIds.UnionWith(copilotCliSessionIds);
    }

    internal HashSet<string> LoadActiveSessionIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var activeSessions = SessionService.GetActiveSessions(Program.PidRegistryFile, Program.SessionStateDir);
            foreach (var s in activeSessions)
            {
                ids.Add(s.Id);
            }
        }
        catch (Exception ex) { Program.Logger.LogWarning("Failed to load active session IDs: {Error}", ex.Message); }
        return ids;
    }

    /// <summary>
    /// Syncs the terminal cache file with the set of actually open terminal windows.
    /// Adds newly discovered terminals and removes stale entries.
    /// </summary>
    internal void SyncTerminalCache(HashSet<string> openTerminalSessionIds)
    {
        try
        {
            var cachedIds = TerminalCacheService.GetCachedTerminals(Program.TerminalCacheFile);

            // Remove cache entries for terminals that are no longer open
            foreach (var cachedId in cachedIds)
            {
                if (!openTerminalSessionIds.Contains(cachedId))
                {
                    TerminalCacheService.RemoveTerminal(Program.TerminalCacheFile, cachedId);
                }
            }

            // Add cache entries for newly discovered terminals
            foreach (var openId in openTerminalSessionIds)
            {
                if (!cachedIds.Contains(openId))
                {
                    TerminalCacheService.CacheTerminal(Program.TerminalCacheFile, openId, 0);
                }
            }
        }
        catch (Exception ex) { Program.Logger.LogDebug("Process not found: {Error}", ex.Message); }
    }

    internal string BuildActiveText(string sessionId)
    {
        var parts = new List<string>();

        if (this._activeTrackedWindows.TryGetValue(sessionId, out var tracked))
        {
            // Count by type to decide on numbering
            var terminals = tracked.Where(t => t.Label.StartsWith("Terminal", StringComparison.OrdinalIgnoreCase)).ToList();
            var copilotClis = tracked.Where(t => t.Label.Equals("Copilot CLI", StringComparison.OrdinalIgnoreCase)).ToList();
            int cliIndex = 0;
            foreach (var (label, _, _) in tracked)
            {
                if (label.Equals("Copilot CLI", StringComparison.OrdinalIgnoreCase))
                {
                    cliIndex++;
                    parts.Add(copilotClis.Count > 1 ? $"Copilot CLI #{cliIndex}" : "Copilot CLI");
                }
                else if (label.StartsWith("Terminal", StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add(terminals.Count > 1 ? label : "Terminal");
                }
                else
                {
                    parts.Add(label);
                }
            }
        }
        else if (this._activeSessionIds.Contains(sessionId))
        {
            // Fallback: PID-based detection for Copilot CLI sessions without a titled window
            parts.Add("Copilot CLI");
        }

        if (this._trackedProcesses.TryGetValue(sessionId, out var procs))
        {
            foreach (var proc in procs)
            {
                // Prefer HWND-based liveness check (works for cached IDEs with PID=0)
                if (proc.Hwnd != IntPtr.Zero)
                {
                    if (WindowFocusService.IsWindowAlive(proc.Hwnd))
                    {
                        parts.Add(proc.Name);
                    }
                }
                else
                {
                    try
                    {
                        var p = Process.GetProcessById(proc.Pid);
                        if (!p.HasExited)
                        {
                            parts.Add(proc.Name);
                        }
                    }
                    catch (Exception ex) { Program.Logger.LogWarning("Failed to check process liveness: {Error}", ex.Message); }
                }
            }
        }

        if (this._explorerWindows.TryGetValue(sessionId, out var explorerHwnd)
            && WindowFocusService.IsWindowAlive(explorerHwnd))
        {
            parts.Add("Explorer");
        }

        if (this._edgeWorkspaces.TryGetValue(sessionId, out var ws) && ws.IsOpen)
        {
            parts.Add("Edge");
        }

        return string.Join("\n", parts);
    }

    internal void FocusActiveProcess(string sessionId, int clickedLineIndex)
    {
        var focusTargets = new List<(string name, Action focus)>();

        if (this._activeTrackedWindows.TryGetValue(sessionId, out var tracked))
        {
            foreach (var (label, title, hwnd) in tracked)
            {
                var capturedHwnd = hwnd;
                focusTargets.Add((label, () => WindowFocusService.TryFocusWindowHandle(capturedHwnd)));
            }
        }
        else if (this._activeSessionIds.Contains(sessionId))
        {
            // Fallback: PID-based focus for Copilot CLI sessions without a titled window
            var activeSessions = SessionService.GetActiveSessions(Program.PidRegistryFile, Program.SessionStateDir);
            var session = activeSessions.FirstOrDefault(s => s.Id == sessionId);
            if (session != null && session.CopilotPid > 0)
            {
                var pid = session.CopilotPid;
                focusTargets.Add(("Copilot CLI", () => WindowFocusService.TryFocusProcessWindow(pid)));
            }
        }

        if (this._trackedProcesses.TryGetValue(sessionId, out var procs))
        {
            foreach (var proc in procs)
            {
                // Prefer HWND-based focus (avoids VS/VS Code title collision)
                if (proc.Hwnd != IntPtr.Zero && WindowFocusService.IsWindowAlive(proc.Hwnd))
                {
                    var capturedHwnd = proc.Hwnd;
                    focusTargets.Add((proc.Name, () => WindowFocusService.TryFocusWindowHandle(capturedHwnd)));
                }
                else if (proc.Pid > 0)
                {
                    try
                    {
                        var p = Process.GetProcessById(proc.Pid);
                        if (!p.HasExited)
                        {
                            var capturedPid = proc.Pid;
                            focusTargets.Add((proc.Name, () => WindowFocusService.TryFocusProcessWindow(capturedPid)));
                        }
                    }
                    catch (Exception ex) { Program.Logger.LogDebug("Process not found for focus: {Error}", ex.Message); }
                }
            }
        }

        if (this._explorerWindows.TryGetValue(sessionId, out var explorerHwnd)
            && WindowFocusService.IsWindowAlive(explorerHwnd))
        {
            var capturedHwnd = explorerHwnd;
            focusTargets.Add(("Explorer", () => WindowFocusService.TryFocusWindowHandle(capturedHwnd)));
        }

        if (this._edgeWorkspaces.TryGetValue(sessionId, out var ws) && ws.IsOpen)
        {
            focusTargets.Add(("Edge", () => ws.Focus()));
        }

        if (focusTargets.Count == 0)
        {
            return;
        }

        // Auto-hide: minimize tracked windows from other sessions
        if (Program._settings.AutoHideOnFocus)
        {
            this.MinimizeOtherSessions(sessionId);
        }

        // Directly focus the target matching the clicked line
        var index = Math.Min(clickedLineIndex, focusTargets.Count - 1);
        focusTargets[index].focus();
    }

    /// <summary>
    /// Minimizes all tracked windows belonging to sessions other than the specified one.
    /// Only targets windows tracked by CopilotBooster (terminals, IDEs, Edge workspaces).
    /// </summary>
    internal void MinimizeOtherSessions(string excludeSessionId)
    {
        // Collect HWNDs belonging to the focused session so we never minimize them
        var excludeHwnds = new HashSet<IntPtr>();
        if (this._activeTrackedWindows.TryGetValue(excludeSessionId, out var focusedWindows))
        {
            foreach (var (_, _, hwnd) in focusedWindows)
            {
                if (hwnd != IntPtr.Zero)
                {
                    excludeHwnds.Add(hwnd);
                }
            }
        }

        if (this._edgeWorkspaces.TryGetValue(excludeSessionId, out var focusedEdge)
            && focusedEdge.CachedHwnd != IntPtr.Zero)
        {
            excludeHwnds.Add(focusedEdge.CachedHwnd);
        }

        if (this._explorerWindows.TryGetValue(excludeSessionId, out var focusedExplorer)
            && focusedExplorer != IntPtr.Zero)
        {
            excludeHwnds.Add(focusedExplorer);
        }

        // Snapshot to avoid concurrent modification with Refresh
        var trackedWindows = this._activeTrackedWindows.ToList();
        foreach (var kvp in trackedWindows)
        {
            if (string.Equals(kvp.Key, excludeSessionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var (_, _, hwnd) in kvp.Value)
            {
                if (hwnd != IntPtr.Zero && !excludeHwnds.Contains(hwnd)
                    && WindowFocusService.IsWindowAlive(hwnd)
                    && !IsCmdExeTitle(hwnd))
                {
                    WindowFocusService.MinimizeWindow(hwnd);
                }
            }
        }

        var trackedProcs = this._trackedProcesses.ToList();
        foreach (var kvp in trackedProcs)
        {
            if (string.Equals(kvp.Key, excludeSessionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var proc in kvp.Value.ToList())
            {
                if (proc.Hwnd != IntPtr.Zero && !excludeHwnds.Contains(proc.Hwnd)
                    && WindowFocusService.IsWindowAlive(proc.Hwnd))
                {
                    WindowFocusService.MinimizeWindow(proc.Hwnd);
                }
            }
        }

        foreach (var kvp in this._edgeWorkspaces.ToList())
        {
            if (string.Equals(kvp.Key, excludeSessionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // IsOpen refreshes CachedHwnd if needed
            if (kvp.Value.IsOpen && kvp.Value.CachedHwnd != IntPtr.Zero)
            {
                WindowFocusService.MinimizeWindow(kvp.Value.CachedHwnd);
            }
        }

        foreach (var kvp in this._explorerWindows.ToList())
        {
            if (string.Equals(kvp.Key, excludeSessionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (kvp.Value != IntPtr.Zero && !excludeHwnds.Contains(kvp.Value)
                && WindowFocusService.IsWindowAlive(kvp.Value))
            {
                WindowFocusService.MinimizeWindow(kvp.Value);
            }
        }
    }

    /// <summary>
    /// Returns true if the window title is a generic cmd.exe title (not yet renamed by copilot).
    /// These windows should not be minimized since they can't be reliably re-focused.
    /// </summary>
    private static bool IsCmdExeTitle(IntPtr hwnd)
    {
        var title = WindowFocusService.GetWindowTitle(hwnd);
        return title.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a dictionary mapping non-empty session summaries to session IDs
    /// for window title matching. Excludes generic titles like "GitHub Copilot".
    /// </summary>
    private static Dictionary<string, string> BuildSessionSummaryMap(List<NamedSession> sessions)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in sessions)
        {
            if (!string.IsNullOrWhiteSpace(session.Summary)
                && !s_ignoredSummaries.Contains(session.Summary)
                && !map.ContainsKey(session.Summary))
            {
                map[session.Summary] = session.Id;
            }
        }
        return map;
    }

    /// <summary>
    /// Refreshes active-status tracking state and returns a snapshot of active text and session names.
    /// </summary>
    internal ActiveStatusSnapshot Refresh(List<NamedSession> sessions)
    {
        this._activeSessionIds = this.LoadActiveSessionIds();

        // Scan for open tracked windows by title (including session-summary matching)
        // Pass previously tracked HWNDs as fallback for Copilot CLI windows whose titles change dynamically
        this._activeTrackedWindows = WindowFocusService.FindTrackedWindows(BuildSessionSummaryMap(sessions), this._activeTrackedWindows);

        // Sync terminal cache with actual open windows
        var openTerminalIds = new HashSet<string>(this._activeTrackedWindows.Keys, StringComparer.OrdinalIgnoreCase);
        this.SyncTerminalCache(openTerminalIds);

        // Load cached window handles on first refresh (restores IDE/Explorer/Edge tracking across app restarts)
        if (!this._handleCacheInitialLoadDone)
        {
            this._handleCacheInitialLoadDone = true;
            var (cachedProcesses, cachedExplorers, cachedEdges) = WindowHandleCacheService.Load(Program.WindowHandleCacheFile);
            foreach (var kvp in cachedProcesses)
            {
                if (!this._trackedProcesses.ContainsKey(kvp.Key))
                {
                    this._trackedProcesses[kvp.Key] = kvp.Value;
                }
            }

            foreach (var kvp in cachedExplorers)
            {
                if (!this._explorerWindows.ContainsKey(kvp.Key))
                {
                    this._explorerWindows[kvp.Key] = kvp.Value;
                }
            }

            foreach (var kvp in cachedEdges)
            {
                if (!this._edgeWorkspaces.ContainsKey(kvp.Key))
                {
                    this._edgeWorkspaces[kvp.Key] = new EdgeWorkspaceService(kvp.Key) { CachedHwnd = kvp.Value };
                }
            }

            // Also load legacy ide-cache.json if window-handles.json doesn't exist yet
            if (!File.Exists(Program.WindowHandleCacheFile) && File.Exists(Program.IdeCacheFile))
            {
                var legacyCached = IdeCacheService.Load(Program.IdeCacheFile);
                foreach (var kvp in legacyCached)
                {
                    if (!this._trackedProcesses.ContainsKey(kvp.Key))
                    {
                        this._trackedProcesses[kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        // Clean up dead tracked processes and capture HWNDs for those that don't have one yet
        foreach (var kvp in this._trackedProcesses.ToList())
        {
            for (int i = kvp.Value.Count - 1; i >= 0; i--)
            {
                var proc = kvp.Value[i];

                // If we have a cached HWND, check if it's still alive
                if (proc.Hwnd != IntPtr.Zero)
                {
                    if (!WindowFocusService.IsWindowAlive(proc.Hwnd))
                    {
                        // HWND died — try to recapture from the same PID (e.g. VS opening a .sln)
                        if (proc.Pid > 0)
                        {
                            bool stillAlive;
                            try { stillAlive = !Process.GetProcessById(proc.Pid).HasExited; }
                            catch (Exception ex) { stillAlive = false; Program.Logger.LogDebug("Process exited: {Error}", ex.Message); }

                            if (stillAlive)
                            {
                                var newHwnd = WindowFocusService.FindWindowHandleByPid(proc.Pid);
                                if (newHwnd != IntPtr.Zero)
                                {
                                    proc.Hwnd = newHwnd;
                                    continue;
                                }
                            }
                        }

                        kvp.Value.RemoveAt(i);
                    }

                    continue;
                }

                // No HWND yet — try to capture one from the PID
                bool alive;
                try { alive = !Process.GetProcessById(proc.Pid).HasExited; }
                catch (Exception ex) { alive = false; Program.Logger.LogDebug("Process exited: {Error}", ex.Message); }

                if (alive)
                {
                    // Try to find the window handle by PID
                    var hwnd = WindowFocusService.FindWindowHandleByPid(proc.Pid);
                    if (hwnd != IntPtr.Zero)
                    {
                        proc.Hwnd = hwnd;
                    }
                }
                else if (proc.FolderPath != null)
                {
                    // Launcher exited — try to find the real IDE window by title
                    var folderName = Path.GetFileName(proc.FolderPath.TrimEnd('\\'));
                    var hwnd = WindowFocusService.FindWindowHandleByTitle(folderName, proc.Name);
                    if (hwnd != IntPtr.Zero)
                    {
                        proc.Hwnd = hwnd;
                        proc.Pid = 0;
                    }
                    else
                    {
                        kvp.Value.RemoveAt(i);
                    }
                }
                else
                {
                    kvp.Value.RemoveAt(i);
                }
            }
        }

        // Clean up dead explorer windows
        var deadExplorers = new List<string>();
        foreach (var kvp in this._explorerWindows)
        {
            if (!WindowFocusService.IsWindowAlive(kvp.Value))
            {
                deadExplorers.Add(kvp.Key);
            }
        }

        foreach (var id in deadExplorers)
        {
            this._explorerWindows.Remove(id);
        }

        // Persist window handle cache so tracking survives app restarts
        WindowHandleCacheService.Save(Program.WindowHandleCacheFile, this._trackedProcesses, this._explorerWindows, this._edgeWorkspaces);

        // Clean up closed Edge workspaces
        var closedEdge = new List<string>();
        foreach (var kvp in this._edgeWorkspaces.ToList())
        {
            if (!kvp.Value.IsOpen)
            {
                closedEdge.Add(kvp.Key);
            }
        }

        foreach (var id in closedEdge)
        {
            this._edgeWorkspaces.Remove(id);
        }

        // Edge workspace scanning happens separately via ScanAndTrackEdgeWorkspaces()
        // which must run on the UI (STA) thread for UI Automation to work.

        // Build active text for each session
        var activeTextBySessionId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sessionNamesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in sessions)
        {
            var activeText = this.BuildActiveText(session.Id);
            if (!string.IsNullOrEmpty(activeText))
            {
                activeTextBySessionId[session.Id] = activeText;
            }

            if (!string.IsNullOrEmpty(session.Summary))
            {
                sessionNamesById[session.Id] = session.Summary;
            }
        }

        // Detect working/bell status from Copilot CLI window title prefix.
        // When Copilot CLI is working, it prefixes the title with an emoji (e.g. 🤖).
        // When it's idle/waiting for input, the title is just the session name (no emoji).
        // Sessions present at startup are suppressed (empty status) until they transition
        // to working first, preventing false bell notifications on app launch.
        var statusIconBySessionId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in this._activeTrackedWindows)
        {
            foreach (var (label, title, _) in kvp.Value)
            {
                if (!label.Equals("Copilot CLI", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var stripped = WindowFocusService.StripLeadingEmoji(title);
                if (stripped != title)
                {
                    // Has emoji prefix — session is working
                    this._startedSessionIds.Remove(kvp.Key);
                    statusIconBySessionId[kvp.Key] = "working";
                }
                else
                {
                    // No emoji prefix — session is idle/waiting for input
                    statusIconBySessionId[kvp.Key] = this._startedSessionIds.Contains(kvp.Key) ? "" : "bell";
                }

                break;
            }
        }

        return new ActiveStatusSnapshot(activeTextBySessionId, sessionNamesById, statusIconBySessionId);
    }

    /// <summary>
    /// Tracks a launched process (IDE or terminal) for the given session.
    /// </summary>
    internal void TrackProcess(string sessionId, ActiveProcess process)
    {
        if (!this._trackedProcesses.ContainsKey(sessionId))
        {
            this._trackedProcesses[sessionId] = new List<ActiveProcess>();
        }
        this._trackedProcesses[sessionId].Add(process);
    }

    /// <summary>
    /// Tracks an Explorer window HWND for a session by matching the folder path
    /// via Shell COM ShellWindows. Explorer.exe is single-instance so PID-based
    /// lookup doesn't work — the spawned process exits immediately.
    /// </summary>
    internal void TrackExplorerWindow(string sessionId, string folderPath)
    {
        var hwnd = FindExplorerByPath(folderPath);
        if (hwnd != IntPtr.Zero)
        {
            this._explorerWindows[sessionId] = hwnd;
        }
    }

    /// <summary>
    /// Finds an open Explorer window whose location matches the given folder path
    /// using Shell COM ShellWindows (CLSID 9BA05972-F6A8-11CF-A442-00A0C90A8F39).
    /// </summary>
    private static IntPtr FindExplorerByPath(string targetPath)
    {
        try
        {
            targetPath = Path.GetFullPath(targetPath).TrimEnd('\\');
            var shellWindowsType = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
            if (shellWindowsType == null)
            {
                return IntPtr.Zero;
            }

            dynamic? shellWindows = Activator.CreateInstance(shellWindowsType);
            if (shellWindows == null)
            {
                return IntPtr.Zero;
            }

            int count = (int)shellWindows.Count;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic? window = shellWindows.Item(i);
                    if (window == null)
                    {
                        continue;
                    }

                    string? url = window.LocationURL?.ToString();
                    if (url != null && url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                    {
                        string path = new Uri(url).LocalPath.TrimEnd('\\');
                        if (string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase))
                        {
                            return (IntPtr)(long)window.HWND;
                        }
                    }
                }
                catch
                {
                    // Skip individual window errors
                }
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("Shell COM explorer lookup failed: {Error}", ex.Message);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Checks if an IDE with the given name is already tracked for the session.
    /// If found and still alive, focuses it and returns true (skip launching a new instance).
    /// </summary>
    internal bool TryFocusExistingIde(string sessionId, string ideName)
    {
        if (!this._trackedProcesses.TryGetValue(sessionId, out var procs))
        {
            return false;
        }

        foreach (var proc in procs)
        {
            if (!string.Equals(proc.Name, ideName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (proc.Hwnd != IntPtr.Zero && WindowFocusService.IsWindowAlive(proc.Hwnd))
            {
                WindowFocusService.TryFocusWindowHandle(proc.Hwnd);
                return true;
            }

            if (proc.Pid > 0)
            {
                try
                {
                    if (!Process.GetProcessById(proc.Pid).HasExited)
                    {
                        WindowFocusService.TryFocusProcessWindow(proc.Pid);
                        return true;
                    }
                }
                catch (Exception ex) { Program.Logger.LogDebug("Process not found: {Error}", ex.Message); }
            }
        }

        return false;
    }

    /// <summary>
    /// Tracks an Edge workspace for the given session.
    /// </summary>
    internal void TrackEdge(string sessionId, EdgeWorkspaceService workspace)
    {
        this._edgeWorkspaces[sessionId] = workspace;
    }

    /// <summary>
    /// Removes an Edge workspace for the given session.
    /// </summary>
    internal void RemoveEdge(string sessionId)
    {
        this._edgeWorkspaces.Remove(sessionId);
    }

    /// <summary>
    /// Returns true if the given session has an associated Edge workspace.
    /// </summary>
    internal bool HasEdgeWorkspace(string sessionId)
    {
        return this._edgeWorkspaces.ContainsKey(sessionId);
    }

    /// <summary>
    /// Tries to get the Edge workspace for the given session.
    /// </summary>
    internal bool TryGetEdge(string sessionId, [NotNullWhen(true)] out EdgeWorkspaceService? workspace)
    {
        return this._edgeWorkspaces.TryGetValue(sessionId, out workspace);
    }

    /// <summary>
    /// Scans all Edge windows for session tabs and registers any newly found workspaces.
    /// Must be called on the UI (STA) thread since it uses UI Automation.
    /// </summary>
    /// <returns>True if new Edge workspaces were discovered.</returns>
    internal bool ScanAndTrackEdgeWorkspaces()
    {
        var edgeMatches = EdgeWorkspaceService.ScanEdgeForSessionTabs();
        bool changed = false;
        foreach (var kvp in edgeMatches)
        {
            if (!this._edgeWorkspaces.ContainsKey(kvp.Key))
            {
                var ws = new EdgeWorkspaceService(kvp.Key);
                ws.CachedHwnd = kvp.Value;
                ws.WindowClosed += () => this.OnEdgeWorkspaceClosed?.Invoke(kvp.Key);
                this._edgeWorkspaces[kvp.Key] = ws;
                changed = true;
            }
        }

        return changed;
    }
}
