using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using CopilotBooster.Models;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

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
                if (proc.ProcessName != "CopilotBooster")
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
            catch (Exception ex) { Program.Logger.LogWarning("Failed to clean PID registry: {Error}", ex.Message); }
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
    /// Cache of Git repository detection results keyed by CWD path.
    /// Once discovered, a directory's git status won't change during the app lifetime.
    /// </summary>
    private static readonly Dictionary<string, bool> s_gitRepoCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loads named sessions from the session state directory, ordered by most recently modified.
    /// Automatically deletes empty sessions (no events.jsonl) that are not currently active.
    /// </summary>
    /// <param name="sessionStateDir">Path to the directory containing session state.</param>
    /// <param name="pidRegistryFile">Path to the PID registry JSON file for active session detection.</param>
    /// <returns>A list of named sessions with summaries.</returns>
    internal static List<NamedSession> LoadNamedSessions(string sessionStateDir, string? pidRegistryFile = null)
    {
        var profiling = Program.Logger.IsEnabled(LogLevel.Debug);
        var totalSw = profiling ? Stopwatch.StartNew() : null;
        var results = new List<NamedSession>();
        if (!Directory.Exists(sessionStateDir))
        {
            return results;
        }

        var sw = profiling ? Stopwatch.StartNew() : null;
        HashSet<string>? activeIds = null;
        if (pidRegistryFile != null)
        {
            activeIds = GetActiveSessionIds(pidRegistryFile, sessionStateDir);
        }
        var activeIdsMs = sw?.ElapsedMilliseconds ?? 0;

        sw?.Restart();
        var dirs = Directory.GetDirectories(sessionStateDir);
        var getDirsMs = sw?.ElapsedMilliseconds ?? 0;

        sw?.Restart();
        var sortedDirs = dirs.OrderByDescending(d => Directory.GetLastWriteTime(d)).ToArray();
        var sortDirsMs = sw?.ElapsedMilliseconds ?? 0;

        int gitCacheMisses = 0;
        int gitCacheHits = 0;
        long gitTotalMs = 0;
        int deletedCount = 0;

        sw?.Restart();
        var sessions = sortedDirs
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
                        try { Directory.Delete(d, recursive: true); deletedCount++; } catch (Exception ex) { Program.Logger.LogError("Failed to delete session directory: {Error}", ex.Message); }
                        return null;
                    }

                    var folder = Path.GetFileName(cwd?.TrimEnd('\\') ?? "");
                    var displaySummary = string.IsNullOrWhiteSpace(summary)
                        ? (string.IsNullOrWhiteSpace(folder) ? "(no summary)" : "")
                        : summary;
                    var isGitRepo = false;
                    if (!string.IsNullOrEmpty(cwd))
                    {
                        if (!s_gitRepoCache.TryGetValue(cwd, out isGitRepo))
                        {
                            var gitSw = profiling ? Stopwatch.StartNew() : null;
                            isGitRepo = GitService.IsGitRepository(cwd);
                            gitTotalMs += gitSw?.ElapsedMilliseconds ?? 0;
                            s_gitRepoCache[cwd] = isGitRepo;
                            gitCacheMisses++;
                        }
                        else
                        {
                            gitCacheHits++;
                        }
                    }
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
        var parseMs = sw?.ElapsedMilliseconds ?? 0;

        foreach (var s in sessions)
        {
            if (s != null)
            {
                results.Add(s);
            }
        }

        if (profiling)
        {
            totalSw!.Stop();
            Program.Logger.LogDebug(
                "LoadNamedSessions: total={TotalMs}ms | activeIds={ActiveIdsMs}ms | getDirs={GetDirsMs}ms ({DirCount} dirs) | " +
                "sortDirs={SortDirsMs}ms | parse={ParseMs}ms | git: misses={GitMisses} ({GitMs}ms) hits={GitHits} | " +
                "deleted={Deleted} | results={Results}",
                totalSw.ElapsedMilliseconds, activeIdsMs, getDirsMs, dirs.Length,
                sortDirsMs, parseMs, gitCacheMisses, gitTotalMs, gitCacheHits,
                deletedCount, results.Count);
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
        catch (Exception ex) { Program.Logger.LogWarning("Failed to get active session IDs: {Error}", ex.Message); }
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
                session.Folder.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                session.Alias.Contains(query, StringComparison.OrdinalIgnoreCase))
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
    /// Updates the summary and cwd fields in a session's workspace.yaml file.
    /// Preserves all other lines in the file.
    /// </summary>
    /// <param name="sessionDir">Path to the session directory containing workspace.yaml.</param>
    /// <param name="newSummary">The new summary value.</param>
    /// <param name="newCwd">The new current working directory value.</param>
    /// <returns><c>true</c> if the update succeeded; otherwise, <c>false</c>.</returns>
    internal static bool UpdateSessionCwd(string sessionDir, string newCwd)
    {
        var wsFile = Path.Combine(sessionDir, "workspace.yaml");
        if (!File.Exists(wsFile))
        {
            return false;
        }

        try
        {
            var lines = File.ReadAllLines(wsFile);
            var updatedLines = new List<string>();
            bool foundCwd = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("cwd:"))
                {
                    updatedLines.Add($"cwd: {newCwd}");
                    foundCwd = true;
                }
                else
                {
                    updatedLines.Add(line);
                }
            }

            if (!foundCwd)
            {
                updatedLines.Add($"cwd: {newCwd}");
            }

            File.WriteAllLines(wsFile, updatedLines);
            return true;
        }
        catch
        {
            return false;
        }
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
