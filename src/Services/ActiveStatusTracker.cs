using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using CopilotApp.Models;

namespace CopilotApp.Services;

/// <summary>
/// Result snapshot returned by <see cref="ActiveStatusTracker.Refresh"/>.
/// </summary>
internal record ActiveStatusSnapshot(
    Dictionary<string, string> ActiveTextBySessionId,
    Dictionary<string, string> SessionNamesById
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
    private bool _edgeInitialScanDone;

    private static readonly HashSet<string> s_ignoredSummaries = new(StringComparer.OrdinalIgnoreCase)
    {
        "GitHub Copilot"
    };

    /// <summary>
    /// Callback invoked (possibly from a background thread) when an Edge workspace is closed.
    /// </summary>
    internal Action<string>? OnEdgeWorkspaceClosed { get; set; }

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
        catch { }
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
        catch { }
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
                try
                {
                    var p = Process.GetProcessById(proc.Pid);
                    if (!p.HasExited)
                    {
                        parts.Add(proc.Name);
                    }
                }
                catch { }
            }
        }

        if (this._edgeWorkspaces.TryGetValue(sessionId, out var ws) && ws.IsOpen)
        {
            parts.Add("Edge");
        }
        else if (!this._edgeInitialScanDone && !this._edgeWorkspaces.ContainsKey(sessionId))
        {
            // On first refresh, probe for Edge workspaces opened before app restart
            var probe = new EdgeWorkspaceService(sessionId);
            if (probe.IsOpen)
            {
                probe.WindowClosed += () => this.OnEdgeWorkspaceClosed?.Invoke(sessionId);
                this._edgeWorkspaces[sessionId] = probe;
                parts.Add("Edge");
            }
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
                try
                {
                    var p = Process.GetProcessById(proc.Pid);
                    if (!p.HasExited)
                    {
                        var capturedProc = proc;
                        focusTargets.Add((proc.Name, () =>
                        {
                            if (capturedProc.FolderPath != null)
                            {
                                var folderName = Path.GetFileName(capturedProc.FolderPath.TrimEnd('\\'));
                                WindowFocusService.TryFocusWindowByTitle(folderName);
                            }
                            else
                            {
                                WindowFocusService.TryFocusProcessWindow(capturedProc.Pid);
                            }
                        }
                        ));
                    }
                }
                catch { }
            }
        }

        if (this._edgeWorkspaces.TryGetValue(sessionId, out var ws) && ws.IsOpen)
        {
            focusTargets.Add(("Edge", () => ws.Focus()));
        }

        if (focusTargets.Count == 0)
        {
            return;
        }

        // Directly focus the target matching the clicked line
        var index = Math.Min(clickedLineIndex, focusTargets.Count - 1);
        focusTargets[index].focus();
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
        this._activeTrackedWindows = WindowFocusService.FindTrackedWindows(BuildSessionSummaryMap(sessions));

        // Sync terminal cache with actual open windows
        var openTerminalIds = new HashSet<string>(this._activeTrackedWindows.Keys, StringComparer.OrdinalIgnoreCase);
        this.SyncTerminalCache(openTerminalIds);

        // Clean up dead tracked processes (IDEs only — terminals use scan-based detection)
        foreach (var kvp in this._trackedProcesses)
        {
            for (int i = kvp.Value.Count - 1; i >= 0; i--)
            {
                var proc = kvp.Value[i];

                // IDE processes — try to re-match by window title for launcher shims (e.g. VSCode)
                bool alive;
                try { alive = !Process.GetProcessById(proc.Pid).HasExited; }
                catch { alive = false; }

                if (!alive && proc.FolderPath != null)
                {
                    var folderName = Path.GetFileName(proc.FolderPath.TrimEnd('\\'));
                    var matchPid = WindowFocusService.FindProcessIdByWindowTitle(folderName);
                    if (matchPid > 0)
                    {
                        proc.Pid = matchPid;
                    }
                    else
                    {
                        kvp.Value.RemoveAt(i);
                    }
                }
                else if (!alive)
                {
                    kvp.Value.RemoveAt(i);
                }
            }
        }

        // Clean up closed Edge workspaces
        var closedEdge = new List<string>();
        foreach (var kvp in this._edgeWorkspaces)
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

        this._edgeInitialScanDone = true;

        return new ActiveStatusSnapshot(activeTextBySessionId, sessionNamesById);
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
}
