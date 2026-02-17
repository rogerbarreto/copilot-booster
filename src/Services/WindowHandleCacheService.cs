using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CopilotBooster.Models;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Persists all tracked window handles (IDEs, Explorer, Edge) across app restarts.
/// Replaces IdeCacheService with a unified cache for all window types.
/// </summary>
internal static class WindowHandleCacheService
{
    private record HandleEntry(string SessionId, string Type, string Name, string? FolderPath, long Hwnd);

    /// <summary>
    /// Saves all tracked window handles to the cache file.
    /// </summary>
    internal static void Save(
        string cacheFile,
        Dictionary<string, List<ActiveProcess>> trackedProcesses,
        Dictionary<string, IntPtr> explorerWindows,
        Dictionary<string, EdgeWorkspaceService> edgeWorkspaces)
    {
        try
        {
            var entries = new List<HandleEntry>();

            foreach (var kvp in trackedProcesses)
            {
                foreach (var proc in kvp.Value)
                {
                    if (proc.Hwnd != IntPtr.Zero)
                    {
                        entries.Add(new HandleEntry(kvp.Key, "ide", proc.Name, proc.FolderPath, proc.Hwnd.ToInt64()));
                    }
                }
            }

            foreach (var kvp in explorerWindows)
            {
                if (kvp.Value != IntPtr.Zero)
                {
                    entries.Add(new HandleEntry(kvp.Key, "explorer", "Explorer", null, kvp.Value.ToInt64()));
                }
            }

            foreach (var kvp in edgeWorkspaces)
            {
                if (kvp.Value.CachedHwnd != IntPtr.Zero)
                {
                    entries.Add(new HandleEntry(kvp.Key, "edge", "Edge", null, kvp.Value.CachedHwnd.ToInt64()));
                }
            }

            var dir = Path.GetDirectoryName(cacheFile);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(cacheFile, JsonSerializer.Serialize(entries));
        }
        catch (Exception ex) { Program.Logger.LogError("Failed to save window handle cache: {Error}", ex.Message); }
    }

    /// <summary>
    /// Loads cached window handle entries, re-validates liveness, and returns surviving entries
    /// grouped by type.
    /// </summary>
    internal static (
        Dictionary<string, List<ActiveProcess>> Processes,
        Dictionary<string, IntPtr> Explorers,
        Dictionary<string, IntPtr> Edges) Load(string cacheFile)
    {
        var processes = new Dictionary<string, List<ActiveProcess>>(StringComparer.OrdinalIgnoreCase);
        var explorers = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
        var edges = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!File.Exists(cacheFile))
            {
                return (processes, explorers, edges);
            }

            var entries = JsonSerializer.Deserialize<List<HandleEntry>>(File.ReadAllText(cacheFile)) ?? [];
            foreach (var entry in entries)
            {
                var hwnd = new IntPtr(entry.Hwnd);
                if (!WindowFocusService.IsWindowAlive(hwnd))
                {
                    continue;
                }

                switch (entry.Type)
                {
                    case "ide":
                        var proc = new ActiveProcess(entry.Name, 0, entry.FolderPath) { Hwnd = hwnd };
                        if (!processes.ContainsKey(entry.SessionId))
                        {
                            processes[entry.SessionId] = [];
                        }

                        processes[entry.SessionId].Add(proc);
                        break;

                    case "explorer":
                        explorers[entry.SessionId] = hwnd;
                        break;

                    case "edge":
                        edges[entry.SessionId] = hwnd;
                        break;
                }
            }
        }
        catch (Exception ex) { Program.Logger.LogWarning("Failed to load window handle cache: {Error}", ex.Message); }

        return (processes, explorers, edges);
    }
}
