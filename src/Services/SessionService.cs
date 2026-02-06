using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using CopilotApp.Models;

namespace CopilotApp.Services;

class SessionService
{
    private readonly string _pidRegistryFile;
    private readonly string _sessionStateDir;

    internal SessionService(string pidRegistryFile, string sessionStateDir)
    {
        _pidRegistryFile = pidRegistryFile;
        _sessionStateDir = sessionStateDir;
    }

    internal List<SessionInfo> GetActiveSessions() => GetActiveSessions(_pidRegistryFile, _sessionStateDir);

    internal static List<SessionInfo> GetActiveSessions(string pidRegistryFile, string sessionStateDir)
    {
        var sessions = new List<SessionInfo>();
        if (!File.Exists(pidRegistryFile)) return sessions;

        Dictionary<string, JsonElement>? pidRegistry;
        try
        {
            pidRegistry = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(pidRegistryFile));
        }
        catch { return sessions; }

        if (pidRegistry == null) return sessions;

        var toRemove = new List<string>();

        foreach (var (pidStr, entry) in pidRegistry)
        {
            if (!int.TryParse(pidStr, out int pid)) continue;

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
                    sessionId = sidProp.GetString();

                if (sessionId == null) continue;

                var workspaceFile = Path.Combine(sessionStateDir, sessionId, "workspace.yaml");
                if (!File.Exists(workspaceFile)) continue;

                var session = ParseWorkspace(workspaceFile, pid);
                if (session != null)
                    sessions.Add(session);
            }
            catch { toRemove.Add(pidStr); }
        }

        if (toRemove.Count > 0)
        {
            foreach (var pid in toRemove)
                pidRegistry.Remove(pid);
            try { File.WriteAllText(pidRegistryFile, JsonSerializer.Serialize(pidRegistry)); } catch { }
        }

        return sessions;
    }

    internal static SessionInfo? ParseWorkspace(string path, int pid)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            string? id = null, cwd = null, summary = null;

            foreach (var line in lines)
            {
                if (line.StartsWith("id:")) id = line[3..].Trim();
                else if (line.StartsWith("cwd:")) cwd = line[4..].Trim();
                else if (line.StartsWith("summary:")) summary = line[8..].Trim();
            }

            if (id == null) return null;

            var folder = Path.GetFileName(cwd?.TrimEnd('\\') ?? "Unknown");
            return new SessionInfo
            {
                Id = id,
                Cwd = cwd ?? "Unknown",
                Summary = string.IsNullOrEmpty(summary) ? $"[{folder}]" : $"[{folder}] {summary}",
                Pid = pid
            };
        }
        catch { return null; }
    }

    internal static List<NamedSession> LoadNamedSessions(string sessionStateDir)
    {
        var results = new List<NamedSession>();
        if (!Directory.Exists(sessionStateDir)) return results;

        var sessions = Directory.GetDirectories(sessionStateDir)
            .OrderByDescending(d => Directory.GetLastWriteTime(d))
            .Select(d =>
            {
                var wsFile = Path.Combine(d, "workspace.yaml");
                if (!File.Exists(wsFile)) return null;

                try
                {
                    var lines = File.ReadAllLines(wsFile);
                    string? id = null, cwd = null, summary = null;
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("id:")) id = line[3..].Trim();
                        else if (line.StartsWith("cwd:")) cwd = line[4..].Trim();
                        else if (line.StartsWith("summary:")) summary = line[8..].Trim();
                    }

                    if (id == null || string.IsNullOrWhiteSpace(summary)) return null;

                    var folder = Path.GetFileName(cwd?.TrimEnd('\\') ?? "");
                    return new NamedSession
                    {
                        Id = id,
                        Cwd = cwd ?? "",
                        Summary = $"[{folder}] {summary}",
                        LastModified = Directory.GetLastWriteTime(d)
                    };
                }
                catch { return null; }
            })
            .Where(s => s != null)
            .Take(50)
            .ToList();

        foreach (var s in sessions)
            if (s != null) results.Add(s);

        return results;
    }

    internal static string? FindGitRoot(string startPath)
    {
        var dir = startPath;
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == dir) break;
            dir = parent;
        }
        return null;
    }
}
