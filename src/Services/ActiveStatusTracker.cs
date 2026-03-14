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
/// Result snapshot returned by <see cref="ActiveStatusTracker.FullRefresh"/> or <see cref="ActiveStatusTracker.IncrementalRefresh"/>.
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
    private readonly Dictionary<string, TeamsWindowService> _teamsWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<(string Label, IntPtr Hwnd)>> _explorerWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _startedSessionIds = new(StringComparer.OrdinalIgnoreCase);
    internal readonly EventsJournalService EventsJournal = new();
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
    /// Callback invoked when a tracked Teams window is detected as closed.
    /// </summary>
    internal Action<string>? OnTeamsWindowClosed { get; set; }

    /// <summary>
    /// Seeds sessions present at startup. These will output "" instead of "bell"
    /// until they transition to working first, preventing false bell notifications on app launch.
    /// </summary>
    internal void InitStartedSessions(IEnumerable<string> copilotCliSessionIds)
    {
        this._startedSessionIds.UnionWith(copilotCliSessionIds);
    }

    /// <summary>
    /// Marks a session as having transitioned to working (clears startup suppression).
    /// </summary>
    internal void MarkSessionWorking(string sessionId)
    {
        this._startedSessionIds.Remove(sessionId);
    }

    /// <summary>
    /// Returns true if this session is still in startup-suppression (hasn't worked yet).
    /// If true, idle status should show "" instead of "bell".
    /// </summary>
    internal bool IsStartupSuppressed(string sessionId)
    {
        return this._startedSessionIds.Contains(sessionId);
    }

    internal static HashSet<string> LoadActiveSessionIds()
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
    /// Resolves which session owns the given window HWND by checking all tracking collections.
    /// Returns the session ID or null if the window is not tracked.
    /// </summary>
    internal string? ResolveSessionForHwnd(IntPtr hwnd)
    {
        int pid = WindowFocusService.GetWindowProcessId(hwnd);

        // Check tracked windows (Terminal, Copilot CLI)
        foreach (var kvp in this._activeTrackedWindows)
        {
            if (kvp.Value.Any(t => t.Hwnd == hwnd))
            {
                return kvp.Key;
            }
        }

        // Check tracked processes (IDEs)
        foreach (var kvp in this._trackedProcesses)
        {
            if (kvp.Value.Any(p => p.Hwnd == hwnd || (p.Pid > 0 && p.Pid == pid)))
            {
                return kvp.Key;
            }
        }

        // Check Edge workspaces
        foreach (var kvp in this._edgeWorkspaces)
        {
            if (kvp.Value.CachedHwnd == hwnd)
            {
                return kvp.Key;
            }
        }

        // Check Explorer windows
        foreach (var kvp in this._explorerWindows)
        {
            if (kvp.Value.Any(e => e.Hwnd == hwnd))
            {
                return kvp.Key;
            }
        }

        return null;
    }

    /// <summary>
    /// Reloads <see cref="_activeSessionIds"/> from the PID registry so that the
    /// PID-based fallback in <see cref="BuildActiveText"/> is current during
    /// incremental refreshes (which otherwise skip this reload).
    /// </summary>
    internal void RefreshActiveSessionIds()
    {
        this._activeSessionIds = LoadActiveSessionIds();
    }

    /// <summary>
    /// Syncs the terminal cache file with the set of actually open terminal windows.
    /// Adds newly discovered terminals and removes stale entries.
    /// </summary>
    internal static void SyncTerminalCache(HashSet<string> openTerminalSessionIds)
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
                if (proc.Hwnd != IntPtr.Zero)
                {
                    if (WindowFocusService.IsWindowAlive(proc.Hwnd))
                    {
                        parts.Add(proc.Name);
                    }
                    else if (proc.Pid > 0)
                    {
                        // HWND died but process still alive (e.g. VS splash screen closed,
                        // real window opened). Try to recapture immediately.
                        bool stillAlive = false;
                        try { stillAlive = !Process.GetProcessById(proc.Pid).HasExited; }
                        catch { }

                        if (stillAlive)
                        {
                            var newHwnd = WindowFocusService.FindWindowHandleByPid(proc.Pid);
                            if (newHwnd != IntPtr.Zero)
                            {
                                proc.Hwnd = newHwnd;
                                parts.Add(proc.Name);
                            }
                        }
                        // PID dead + HWND dead = IDE genuinely closed. Don't show.
                    }
                    // Pid=0 + HWND dead = launcher exited and window was destroyed. Don't show.
                }
                else if (proc.Pid > 0)
                {
                    bool alive = false;
                    try { alive = !Process.GetProcessById(proc.Pid).HasExited; }
                    catch { }

                    if (alive)
                    {
                        // Try to recapture HWND (e.g., after splash → main transition)
                        var newHwnd = WindowFocusService.FindWindowHandleByPid(proc.Pid);
                        if (newHwnd != IntPtr.Zero)
                        {
                            proc.Hwnd = newHwnd;
                            proc.HwndEverCaptured = true;
                        }

                        parts.Add(proc.Name);
                    }
                    else if (proc.FolderPath != null && !proc.HwndEverCaptured)
                    {
                        // Launcher exited, HWND never captured — keep showing while
                        // ForegroundChanged/OnWindowCreated tries to capture the real window.
                        parts.Add(proc.Name);
                    }
                    // PID dead + HWND was previously captured = IDE genuinely closed. Don't show.
                }
                else if (proc.FolderPath != null && !proc.HwndEverCaptured)
                {
                    // Pid=0, HWND never captured — launcher exited, waiting for window.
                    parts.Add(proc.Name);
                }
            }
        }

        if (this._explorerWindows.TryGetValue(sessionId, out var explorers))
        {
            foreach (var (label, hwnd) in explorers)
            {
                if (WindowFocusService.IsWindowAlive(hwnd))
                {
                    parts.Add(label);
                }
            }
        }

        if (this._edgeWorkspaces.TryGetValue(sessionId, out var ws) && ws.IsOpen)
        {
            parts.Add("Edge");
        }

        if (this._teamsWindows.TryGetValue(sessionId, out var teams) && (teams.IsOpen || teams.IsPendingOpen))
        {
            parts.Add("Teams");
        }

        return string.Join("\n", parts);
    }

    /// <summary>
    /// Tries to focus an existing Copilot CLI window for the given session.
    /// Returns true if a window was found and focused, false otherwise.
    /// </summary>
    internal bool TryFocusCopilotCli(string sessionId)
    {
        // Check tracked windows first (HWND-based, most reliable)
        if (this._activeTrackedWindows.TryGetValue(sessionId, out var tracked))
        {
            var cli = tracked.FirstOrDefault(t => t.Label.Equals("Copilot CLI", StringComparison.OrdinalIgnoreCase));
            if (cli != default && WindowFocusService.IsWindowAlive(cli.Hwnd))
            {
                WindowFocusService.TryFocusWindowHandle(cli.Hwnd);
                return true;
            }
        }

        // Fallback: PID-based
        if (this._activeSessionIds.Contains(sessionId))
        {
            var activeSessions = SessionService.GetActiveSessions(Program.PidRegistryFile, Program.SessionStateDir);
            var session = activeSessions.FirstOrDefault(s => s.Id == sessionId);
            if (session != null && session.CopilotPid > 0)
            {
                try
                {
                    var p = Process.GetProcessById(session.CopilotPid);
                    if (!p.HasExited)
                    {
                        WindowFocusService.TryFocusProcessWindow(session.CopilotPid);
                        return true;
                    }
                }
                catch { }
            }
        }

        return false;
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

        if (this._explorerWindows.TryGetValue(sessionId, out var explorers))
        {
            foreach (var (label, hwnd) in explorers)
            {
                if (WindowFocusService.IsWindowAlive(hwnd))
                {
                    var capturedHwnd = hwnd;
                    focusTargets.Add((label, () => WindowFocusService.TryFocusWindowHandle(capturedHwnd)));
                }
            }
        }

        if (this._edgeWorkspaces.TryGetValue(sessionId, out var ws) && ws.IsOpen)
        {
            focusTargets.Add(("Edge", () => ws.Focus()));
        }

        if (this._teamsWindows.TryGetValue(sessionId, out var teams) && teams.IsOpen)
        {
            focusTargets.Add(("Teams", () => teams.Focus()));
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

        if (this._teamsWindows.TryGetValue(excludeSessionId, out var focusedTeams)
            && focusedTeams.CachedHwnd != IntPtr.Zero)
        {
            excludeHwnds.Add(focusedTeams.CachedHwnd);
        }

        if (this._explorerWindows.TryGetValue(excludeSessionId, out var focusedExplorers))
        {
            foreach (var (_, hwnd) in focusedExplorers)
            {
                if (hwnd != IntPtr.Zero)
                {
                    excludeHwnds.Add(hwnd);
                }
            }
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

        foreach (var kvp in this._teamsWindows.ToList())
        {
            if (string.Equals(kvp.Key, excludeSessionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

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

            foreach (var (_, hwnd) in kvp.Value)
            {
                if (hwnd != IntPtr.Zero && !excludeHwnds.Contains(hwnd)
                    && WindowFocusService.IsWindowAlive(hwnd))
                {
                    WindowFocusService.MinimizeWindow(hwnd);
                }
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
    internal static Dictionary<string, string> BuildSessionSummaryMap(List<NamedSession> sessions)
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
    /// Performs a full refresh of active-status tracking state including Win32 calls,
    /// process liveness checks, and cache persistence. Called at startup and by the fallback timer.
    /// </summary>
    internal ActiveStatusSnapshot FullRefresh(List<NamedSession> sessions)
    {
        // Snapshot to avoid concurrent modification from UI thread
        var sessionSnapshot = sessions.ToList();
        this._activeSessionIds = LoadActiveSessionIds();

        // Scan for open tracked windows by title (including session-summary matching)
        // Pass previously tracked HWNDs as fallback for Copilot CLI windows whose titles change dynamically
        this._activeTrackedWindows = WindowFocusService.FindTrackedWindows(BuildSessionSummaryMap(sessionSnapshot), this._activeTrackedWindows);

        // Sync terminal cache with actual open windows
        var openTerminalIds = new HashSet<string>(this._activeTrackedWindows.Keys, StringComparer.OrdinalIgnoreCase);
        SyncTerminalCache(openTerminalIds);

        // Load cached window handles on first refresh (restores IDE/Explorer/Edge tracking across app restarts)
        if (!this._handleCacheInitialLoadDone)
        {
            this._handleCacheInitialLoadDone = true;
            var (cachedProcesses, cachedExplorers, cachedEdges, cachedTeams) = WindowHandleCacheService.Load(Program.WindowHandleCacheFile);
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

            foreach (var kvp in cachedTeams)
            {
                if (!this._teamsWindows.ContainsKey(kvp.Key))
                {
                    var teams = new TeamsWindowService();
                    teams.RestoreCachedHwnd(kvp.Value);
                    this._teamsWindows[kvp.Key] = teams;
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
        foreach (var kvp in this._explorerWindows)
        {
            kvp.Value.RemoveAll(e => !WindowFocusService.IsWindowAlive(e.Hwnd));
        }

        var emptyExplorers = new List<string>();
        foreach (var kvp in this._explorerWindows)
        {
            if (kvp.Value.Count == 0)
            {
                emptyExplorers.Add(kvp.Key);
            }
        }

        foreach (var id in emptyExplorers)
        {
            this._explorerWindows.Remove(id);
        }

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

        // Clean up closed Teams windows (skip entries still pending HWND capture)
        var closedTeams = new List<string>();
        foreach (var kvp in this._teamsWindows.ToList())
        {
            if (!kvp.Value.IsPendingOpen && !kvp.Value.IsOpen)
            {
                closedTeams.Add(kvp.Key);
            }
        }

        foreach (var id in closedTeams)
        {
            if (this._teamsWindows.TryGetValue(id, out var tw))
            {
                tw.Release();
            }

            this._teamsWindows.Remove(id);
        }

        // Edge workspace scanning happens separately via ScanAndTrackEdgeWorkspaces()
        // which must run on the UI (STA) thread for UI Automation to work.

        // Build active text for each session
        var activeTextBySessionId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sessionNamesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in sessionSnapshot)
        {
            var activeText = this.BuildActiveText(session.Id);
            if (!string.IsNullOrEmpty(activeText))
            {
                activeTextBySessionId[session.Id] = activeText;
            }

            var displayName = !string.IsNullOrEmpty(session.Alias) ? session.Alias : session.Summary;
            if (!string.IsNullOrEmpty(displayName))
            {
                sessionNamesById[session.Id] = displayName;
            }
        }

        // Status icons from events.jsonl — read from cache only (watcher updates async).
        // Fallback poll runs only on watcher errors, rate-limited to 1/30s.
        this.EventsJournal.ProcessFallbackPoll(sessionSnapshot.Select(s => s.Id).ToList());
        var statusIconBySessionId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in sessionSnapshot)
        {
            var status = this.EventsJournal.GetCachedStatus(session.Id);
            switch (status)
            {
                case EventsJournalService.SessionStatus.Working:
                    statusIconBySessionId[session.Id] = "working";
                    break;
                case EventsJournalService.SessionStatus.Idle:
                    statusIconBySessionId[session.Id] = this._startedSessionIds.Contains(session.Id) ? "" : "bell";
                    break;
                case EventsJournalService.SessionStatus.IdleSilent:
                    // Silent idle — no bell, just clear the working state
                    statusIconBySessionId[session.Id] = "";
                    break;
            }
        }

        return new ActiveStatusSnapshot(activeTextBySessionId, sessionNamesById, statusIconBySessionId);
    }

    /// <summary>
    /// Builds an <see cref="ActiveStatusSnapshot"/> from the already-cached in-memory state
    /// without any Win32 calls, process liveness checks, or cache persistence.
    /// Intended for fast, event-driven refreshes between full refresh cycles.
    /// </summary>
    internal ActiveStatusSnapshot IncrementalRefresh(List<NamedSession> sessions)
    {
        var sessionSnapshot = sessions.ToList();

        var activeTextBySessionId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sessionNamesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in sessionSnapshot)
        {
            var activeText = this.BuildActiveText(session.Id);
            if (!string.IsNullOrEmpty(activeText))
            {
                activeTextBySessionId[session.Id] = activeText;
            }

            var displayName = !string.IsNullOrEmpty(session.Alias) ? session.Alias : session.Summary;
            if (!string.IsNullOrEmpty(displayName))
            {
                sessionNamesById[session.Id] = displayName;
            }
        }

        this.EventsJournal.ProcessFallbackPoll(sessionSnapshot.Select(s => s.Id).ToList());
        var statusIconBySessionId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in sessionSnapshot)
        {
            var status = this.EventsJournal.GetCachedStatus(session.Id);
            switch (status)
            {
                case EventsJournalService.SessionStatus.Working:
                    statusIconBySessionId[session.Id] = "working";
                    break;
                case EventsJournalService.SessionStatus.Idle:
                    statusIconBySessionId[session.Id] = this._startedSessionIds.Contains(session.Id) ? "" : "bell";
                    break;
                case EventsJournalService.SessionStatus.IdleSilent:
                    statusIconBySessionId[session.Id] = "";
                    break;
            }
        }

        return new ActiveStatusSnapshot(activeTextBySessionId, sessionNamesById, statusIconBySessionId);
    }

    /// <summary>
    /// Called when a window's title changes. Updates tracking state by matching the new title
    /// against tracked session patterns. If the HWND was previously tracked but no longer matches,
    /// it is removed from tracking.
    /// </summary>
    /// <param name="hwnd">The window handle whose title changed.</param>
    /// <param name="title">The new window title.</param>
    /// <param name="sessionSummaries">Optional mapping of session summary to session ID for title matching.</param>
    internal HashSet<string> OnWindowTitleChanged(IntPtr hwnd, string title, Dictionary<string, string>? sessionSummaries)
    {
        var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var match = WindowFocusService.MatchTrackedWindowTitle(title, sessionSummaries);

        // Remove this HWND from any session it was previously tracked under
        foreach (var kvp in this._activeTrackedWindows)
        {
            int before = kvp.Value.Count;
            kvp.Value.RemoveAll(t => t.Hwnd == hwnd);
            if (kvp.Value.Count < before)
            {
                affected.Add(kvp.Key);
            }
        }

        if (match != null)
        {
            var (sessionId, label) = match.Value;
            if (!this._activeTrackedWindows.TryGetValue(sessionId, out List<(string Label, string Title, nint Hwnd)>? value))
            {
                value = [];
                this._activeTrackedWindows[sessionId] = value;
            }

            // Avoid duplicate HWND entries
            if (!value.Any(t => t.Hwnd == hwnd))
            {
                value.Add((label, title, hwnd));
            }

            // Keep PID registry current so BuildActiveText fallback works during incremental refreshes
            if (label.Equals("Copilot CLI", StringComparison.OrdinalIgnoreCase))
            {
                this.RefreshActiveSessionIds();
            }

            affected.Add(sessionId);
        }

        return affected;
    }

    /// <summary>
    /// Called when a new window is created or gains focus. If the window's owning process matches
    /// a tracked process, captures the HWND for that process entry. Also handles launcher-based
    /// IDEs where the launcher PID exited (Pid=0) by matching the window title to FolderPath.
    /// </summary>
    /// <param name="hwnd">The window handle to try to associate.</param>
    internal string? OnWindowCreated(IntPtr hwnd)
    {
        int pid = WindowFocusService.GetWindowProcessId(hwnd);
        if (pid <= 0)
        {
            return null;
        }

        // First pass: match by PID (direct process match)
        foreach (var kvp in this._trackedProcesses)
        {
            foreach (var proc in kvp.Value)
            {
                if (proc.Pid == pid && proc.Hwnd == IntPtr.Zero)
                {
                    proc.Hwnd = hwnd;
                    proc.HwndEverCaptured = true;
                    return kvp.Key;
                }
            }
        }

        // Second pass: for launcher-based IDEs where the HWND hasn't been captured yet,
        // match by checking if the window title contains the folder name.
        // This handles both cases: Pid=0 (launcher already exited) and Pid>0 (launcher
        // still exiting while the host creates the window under a different PID).
        var title = WindowFocusService.GetWindowTitle(hwnd);
        if (!string.IsNullOrEmpty(title))
        {
            foreach (var kvp in this._trackedProcesses)
            {
                foreach (var proc in kvp.Value)
                {
                    if (proc.Hwnd == IntPtr.Zero && proc.FolderPath != null)
                    {
                        var folderName = Path.GetFileName(proc.FolderPath.TrimEnd('\\'));
                        if (!string.IsNullOrEmpty(folderName)
                            && title.Contains(folderName, StringComparison.OrdinalIgnoreCase))
                        {
                            proc.Hwnd = hwnd;
                            proc.HwndEverCaptured = true;
                            return kvp.Key;
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Called when a window is destroyed. Removes the HWND from all tracking collections.
    /// </summary>
    /// <param name="hwnd">The destroyed window handle.</param>
    internal HashSet<string> OnWindowDestroyed(IntPtr hwnd)
    {
        var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in this._activeTrackedWindows)
        {
            int before = kvp.Value.Count;
            kvp.Value.RemoveAll(t => t.Hwnd == hwnd);
            if (kvp.Value.Count < before)
            {
                affected.Add(kvp.Key);
            }
        }

        foreach (var kvp in this._trackedProcesses)
        {
            for (int i = kvp.Value.Count - 1; i >= 0; i--)
            {
                var proc = kvp.Value[i];
                if (proc.Hwnd != hwnd)
                {
                    continue;
                }

                // HWND destroyed — try to recapture from the same PID
                if (proc.Pid > 0)
                {
                    bool stillAlive = false;
                    try { stillAlive = !Process.GetProcessById(proc.Pid).HasExited; }
                    catch { }

                    if (stillAlive)
                    {
                        var newHwnd = WindowFocusService.FindWindowHandleByPid(proc.Pid);
                        if (newHwnd != IntPtr.Zero && newHwnd != hwnd && !this.IsHwndTracked(newHwnd))
                        {
                            proc.Hwnd = newHwnd;
                            affected.Add(kvp.Key);
                            continue;
                        }

                        bool sharedPid = false;
                        foreach (var other in this._trackedProcesses)
                        {
                            if (string.Equals(other.Key, kvp.Key, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (other.Value.Any(p => p.Pid == proc.Pid || (p.Hwnd != IntPtr.Zero
                                && WindowFocusService.GetWindowProcessId(p.Hwnd) == proc.Pid)))
                            {
                                sharedPid = true;
                                break;
                            }
                        }

                        if (sharedPid)
                        {
                            kvp.Value.RemoveAt(i);
                            affected.Add(kvp.Key);
                            continue;
                        }

                        proc.Hwnd = IntPtr.Zero;
                        affected.Add(kvp.Key);
                        continue;
                    }
                }

                // PID dead and HWND destroyed — the IDE window is genuinely closed.
                // Remove the entry (don't keep it alive via FolderPath — that's only for
                // entries that never had an HWND captured).
                kvp.Value.RemoveAt(i);
                affected.Add(kvp.Key);
            }
        }

        foreach (var kvp in this._explorerWindows)
        {
            int before = kvp.Value.Count;
            kvp.Value.RemoveAll(e => e.Hwnd == hwnd);
            if (kvp.Value.Count < before)
            {
                affected.Add(kvp.Key);
            }
        }

        var emptyExplorers = this._explorerWindows.Where(kvp => kvp.Value.Count == 0).Select(kvp => kvp.Key).ToList();
        foreach (var id in emptyExplorers)
        {
            this._explorerWindows.Remove(id);
        }

        var deadEdge = this._edgeWorkspaces.Where(kvp => kvp.Value.CachedHwnd == hwnd).Select(kvp => kvp.Key).ToList();
        foreach (var id in deadEdge)
        {
            affected.Add(id);
            this._edgeWorkspaces.Remove(id);
        }

        var deadTeams = this._teamsWindows.Where(kvp => kvp.Value.CachedHwnd == hwnd).Select(kvp => kvp.Key).ToList();
        foreach (var id in deadTeams)
        {
            affected.Add(id);
            if (this._teamsWindows.TryGetValue(id, out var tw))
            {
                tw.Release();
            }

            this._teamsWindows.Remove(id);
            this.OnTeamsWindowClosed?.Invoke(id);
        }

        return affected;
    }

    /// <summary>
    /// Returns true if the given HWND is already tracked by any session in <see cref="_trackedProcesses"/>.
    /// </summary>
    private bool IsHwndTracked(IntPtr hwnd)
    {
        foreach (var kvp in this._trackedProcesses)
        {
            foreach (var proc in kvp.Value)
            {
                if (proc.Hwnd == hwnd)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Called when a tracked process exits. Removes all entries from <see cref="_trackedProcesses"/>
    /// where the PID matches.
    /// </summary>
    /// <param name="pid">The process ID that exited.</param>
    internal HashSet<string> OnProcessExited(int pid)
    {
        var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in this._trackedProcesses)
        {
            int before = kvp.Value.Count;

            // Entries with FolderPath are IDE launchers — don't remove them,
            // just clear the PID so FullRefresh can recapture via title fallback.
            foreach (var proc in kvp.Value)
            {
                if (proc.Pid == pid && proc.FolderPath != null)
                {
                    proc.Pid = 0;
                    affected.Add(kvp.Key);
                }
            }

            kvp.Value.RemoveAll(p => p.Pid == pid && p.FolderPath == null);
            if (kvp.Value.Count < before)
            {
                affected.Add(kvp.Key);
            }
        }

        return affected;
    }

    /// <summary>
    /// Tracks a launched process (IDE or terminal) for the given session.
    /// </summary>
    internal void TrackProcess(string sessionId, ActiveProcess process)
    {
        if (!this._trackedProcesses.TryGetValue(sessionId, out List<ActiveProcess>? value))
        {
            value = [];
            this._trackedProcesses[sessionId] = value;
        }

        value.Add(process);
    }

    /// <summary>
    /// Tracks an Explorer window HWND for a session by matching the folder path
    /// via Shell COM ShellWindows. Explorer.exe is single-instance so PID-based
    /// lookup doesn't work — the spawned process exits immediately.
    /// </summary>
    internal void TrackExplorerWindow(string sessionId, string folderPath, string label = "Explorer")
    {
        var hwnd = FindExplorerByPath(folderPath);
        if (hwnd != IntPtr.Zero)
        {
            if (!this._explorerWindows.TryGetValue(sessionId, out List<(string Label, nint Hwnd)>? list))
            {
                list = [];
                this._explorerWindows[sessionId] = list;
            }

            var idx = list.FindIndex(e => string.Equals(e.Label, label, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                list[idx] = (label, hwnd);
            }
            else
            {
                list.Add((label, hwnd));
            }
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
                            return checked((IntPtr)(long)window.HWND);
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
    /// Tracks a Teams window for the given session.
    /// </summary>
    internal void TrackTeams(string sessionId, TeamsWindowService teamsWindow)
    {
        this._teamsWindows[sessionId] = teamsWindow;
    }

    /// <summary>
    /// Removes a Teams window for the given session.
    /// </summary>
    internal void RemoveTeams(string sessionId)
    {
        if (this._teamsWindows.TryGetValue(sessionId, out var tw))
        {
            tw.Release();
        }

        this._teamsWindows.Remove(sessionId);
    }

    /// <summary>
    /// Returns true if the given session has an associated Teams window.
    /// </summary>
    internal bool HasTeamsWindow(string sessionId)
    {
        return this._teamsWindows.ContainsKey(sessionId);
    }

    /// <summary>
    /// Tries to get the Teams window for the given session.
    /// </summary>
    internal bool TryGetTeams(string sessionId, [NotNullWhen(true)] out TeamsWindowService? teamsWindow)
    {
        return this._teamsWindows.TryGetValue(sessionId, out teamsWindow);
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
            if (!this._edgeWorkspaces.TryGetValue(kvp.Key, out EdgeWorkspaceService? value))
            {
                var ws = new EdgeWorkspaceService(kvp.Key)
                {
                    CachedHwnd = kvp.Value
                };
                ws.WindowClosed += () => this.OnEdgeWorkspaceClosed?.Invoke(kvp.Key);
                this._edgeWorkspaces[kvp.Key] = ws;
                changed = true;
            }
            else if (value.CachedHwnd != kvp.Value)
            {
                value.CachedHwnd = kvp.Value;
            }
        }

        return changed;
    }

    /// <summary>
    /// Returns all currently tracked Edge workspace services for change detection.
    /// </summary>
    internal IEnumerable<EdgeWorkspaceService> GetTrackedEdgeWorkspaces()
        => this._edgeWorkspaces.Values.ToList();

    /// <summary>
    /// Persists the window handle cache to disk. Called once on shutdown.
    /// </summary>
    internal void SaveWindowHandleCache()
    {
        WindowHandleCacheService.Save(Program.WindowHandleCacheFile, this._trackedProcesses, this._explorerWindows, this._edgeWorkspaces, this._teamsWindows);
    }
}
