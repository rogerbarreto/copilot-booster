using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using CopilotApp.Models;

namespace CopilotApp.Services;

/// <summary>
/// Provides operations for discovering and managing Copilot sessions.
/// </summary>
internal class SessionService
{
    private readonly string _pidRegistryFile;
    private readonly string _sessionStateDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionService"/> class.
    /// </summary>
    /// <param name="pidRegistryFile">Path to the PID registry JSON file.</param>
    /// <param name="sessionStateDir">Path to the directory containing session state.</param>
    internal SessionService(string pidRegistryFile, string sessionStateDir)
    {
        this._pidRegistryFile = pidRegistryFile;
        this._sessionStateDir = sessionStateDir;
    }

    /// <summary>
    /// Gets active sessions using the configured PID registry and session state directory.
    /// </summary>
    /// <returns>A list of currently active sessions.</returns>
    internal List<SessionInfo> GetActiveSessions() => GetActiveSessions(this._pidRegistryFile, this._sessionStateDir);

    /// <summary>
    /// Gets active sessions by cross-referencing the PID registry with running processes.
    /// </summary>
    /// <param name="pidRegistryFile">Path to the PID registry JSON file.</param>
    /// <param name="sessionStateDir">Path to the directory containing session state.</param>
    /// <returns>A list of currently active sessions.</returns>
    internal static List<SessionInfo> GetActiveSessions(string pidRegistryFile, string sessionStateDir)
    {
        var sessions = new List<SessionInfo>();
        if (!File.Exists(pidRegistryFile))
        {
            return sessions;
        }

        Dictionary<string, JsonElement>? pidRegistry;
        try
        {
            pidRegistry = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(pidRegistryFile));
        }
        catch
        {
            return sessions;
        }

        if (pidRegistry == null)
        {
            return sessions;
        }

        var toRemove = new List<string>();

        foreach (var (pidStr, entry) in pidRegistry)
        {
            if (!int.TryParse(pidStr, out int pid))
            {
                continue;
            }

            try
            {
                var proc = Process.GetProcessById(pid);
                if (proc.ProcessName != "CopilotApp")
                {
                    toRemove.Add(pidStr);
                    continue;
                }

                string? sessionId = null;
                if (entry.TryGetProperty("sessionId", out var sidProp) && sidProp.ValueKind == JsonValueKind.String)
                {
                    sessionId = sidProp.GetString();
                }

                if (sessionId == null)
                {
                    continue;
                }

                int copilotPid = 0;
                if (entry.TryGetProperty("copilotPid", out var cpProp) && cpProp.ValueKind == JsonValueKind.Number)
                {
                    copilotPid = cpProp.GetInt32();
                }

                var workspaceFile = Path.Combine(sessionStateDir, sessionId, "workspace.yaml");
                if (!File.Exists(workspaceFile))
                {
                    continue;
                }

                var session = ParseWorkspace(workspaceFile, pid);
                if (session != null)
                {
                    session.CopilotPid = copilotPid;
                    sessions.Add(session);
                }
            }
            catch
            {
                toRemove.Add(pidStr);
            }
        }

        if (toRemove.Count > 0)
        {
            foreach (var pid in toRemove)
            {
                pidRegistry.Remove(pid);
            }

            try
            {
                File.WriteAllText(pidRegistryFile, JsonSerializer.Serialize(pidRegistry));
            }
            catch { }
        }

        return sessions;
    }

    /// <summary>
    /// Parses a workspace YAML file to extract session information.
    /// </summary>
    /// <param name="path">Path to the workspace.yaml file.</param>
    /// <param name="pid">The process ID associated with the session.</param>
    /// <returns>A <see cref="SessionInfo"/> if parsing succeeds; otherwise, <c>null</c>.</returns>
    internal static SessionInfo? ParseWorkspace(string path, int pid)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            string? id = null, cwd = null, summary = null;

            foreach (var line in lines)
            {
                if (line.StartsWith("id:"))
                {
                    id = line[3..].Trim();
                }
                else if (line.StartsWith("cwd:"))
                {
                    cwd = line[4..].Trim();
                }
                else if (line.StartsWith("summary:"))
                {
                    summary = line[8..].Trim();
                }
            }

            if (id == null)
            {
                return null;
            }

            var folder = Path.GetFileName(cwd?.TrimEnd('\\') ?? "");
            return new SessionInfo
            {
                Id = id,
                Cwd = cwd ?? "Unknown",
                Summary = string.IsNullOrWhiteSpace(folder)
                    ? (string.IsNullOrEmpty(summary) ? "(no folder)" : summary)
                    : (string.IsNullOrEmpty(summary) ? $"{folder}" : summary),
                Pid = pid
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads named sessions from the session state directory, ordered by most recently modified.
    /// Automatically deletes empty sessions (no events.jsonl) that are not currently active.
    /// </summary>
    /// <param name="sessionStateDir">Path to the directory containing session state.</param>
    /// <param name="pidRegistryFile">Path to the PID registry JSON file for active session detection.</param>
    /// <returns>A list of named sessions with summaries.</returns>
    internal static List<NamedSession> LoadNamedSessions(string sessionStateDir, string? pidRegistryFile = null)
    {
        var results = new List<NamedSession>();
        if (!Directory.Exists(sessionStateDir))
        {
            return results;
        }

        HashSet<string>? activeIds = null;
        if (pidRegistryFile != null)
        {
            activeIds = GetActiveSessionIds(pidRegistryFile, sessionStateDir);
        }

        var sessions = Directory.GetDirectories(sessionStateDir)
            .OrderByDescending(d => Directory.GetLastWriteTime(d))
            .Select(d =>
            {
                var wsFile = Path.Combine(d, "workspace.yaml");
                if (!File.Exists(wsFile))
                {
                    return null;
                }

                try
                {
                    var lines = File.ReadAllLines(wsFile);
                    string? id = null, cwd = null, summary = null;
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("id:"))
                        {
                            id = line[3..].Trim();
                        }
                        else if (line.StartsWith("cwd:"))
                        {
                            cwd = line[4..].Trim();
                        }
                        else if (line.StartsWith("summary:"))
                        {
                            summary = line[8..].Trim();
                        }
                    }

                    if (id == null)
                    {
                        return null;
                    }

                    // Delete empty, non-active sessions
                    var hasEvents = File.Exists(Path.Combine(d, "events.jsonl"));
                    if (!hasEvents && activeIds != null && !activeIds.Contains(id))
                    {
                        try { Directory.Delete(d, recursive: true); } catch { }
                        return null;
                    }

                    var folder = Path.GetFileName(cwd?.TrimEnd('\\') ?? "");
                    var displaySummary = string.IsNullOrWhiteSpace(summary)
                        ? (string.IsNullOrWhiteSpace(folder) ? "(no summary)" : "")
                        : summary;
                    var isGitRepo = !string.IsNullOrEmpty(cwd) && GitService.IsGitRepository(cwd);
                    return new NamedSession
                    {
                        Id = id,
                        Cwd = cwd ?? "",
                        Folder = folder,
                        IsGitRepo = isGitRepo,
                        Summary = displaySummary,
                        LastModified = Directory.GetLastWriteTime(d)
                    };
                }
                catch
                {
                    return null;
                }
            })
            .Where(s => s != null)
            .ToList();

        foreach (var s in sessions)
        {
            if (s != null)
            {
                results.Add(s);
            }
        }

        return results;
    }

    /// <summary>
    /// Gets the set of session IDs that are currently active (have a running process).
    /// </summary>
    private static HashSet<string> GetActiveSessionIds(string pidRegistryFile, string sessionStateDir)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var s in GetActiveSessions(pidRegistryFile, sessionStateDir))
            {
                ids.Add(s.Id);
            }
        }
        catch { }
        return ids;
    }

    /// <summary>
    /// Searches named sessions by query text, prioritizing summary/title matches over other metadata (cwd, id).
    /// </summary>
    /// <param name="sessions">The list of sessions to search.</param>
    /// <param name="query">The search text to match against session fields.</param>
    /// <returns>Sessions matching the query, with title matches first followed by metadata-only matches.</returns>
    internal static List<NamedSession> SearchSessions(List<NamedSession> sessions, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return sessions;
        }

        var titleMatches = new List<NamedSession>();
        var metadataMatches = new List<NamedSession>();

        foreach (var session in sessions)
        {
            if (session.Summary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                session.Folder.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                titleMatches.Add(session);
            }
            else if (session.Cwd.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     session.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                metadataMatches.Add(session);
            }
        }

        titleMatches.AddRange(metadataMatches);
        return titleMatches;
    }

    /// <summary>
    /// Finds the Git repository root by traversing parent directories from the given path.
    /// </summary>
    /// <param name="startPath">The directory path to start searching from.</param>
    /// <returns>The Git repository root path, or <c>null</c> if not found.</returns>
    internal static string? FindGitRoot(string startPath)
    {
        var dir = startPath;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")))
            {
                return dir;
            }

            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == dir)
            {
                break;
            }

            dir = parent;
        }
        return null;
    }
}
