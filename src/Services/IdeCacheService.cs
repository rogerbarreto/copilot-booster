using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CopilotBooster.Models;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Persists IDE window tracking data across app restarts.
/// Stores session ID → IDE entries with cached window handles.
/// </summary>
internal static class IdeCacheService
{
    private record IdeEntry(string SessionId, string Name, string? FolderPath, long Hwnd);

    /// <summary>
    /// Saves the current IDE tracking state to the cache file.
    /// </summary>
    internal static void Save(string cacheFile, Dictionary<string, List<ActiveProcess>> trackedProcesses)
    {
        try
        {
            var entries = new List<IdeEntry>();
            foreach (var kvp in trackedProcesses)
            {
                foreach (var proc in kvp.Value)
                {
                    if (proc.Hwnd != IntPtr.Zero)
                    {
                        entries.Add(new IdeEntry(kvp.Key, proc.Name, proc.FolderPath, proc.Hwnd.ToInt64()));
                    }
                }
            }

            var dir = Path.GetDirectoryName(cacheFile);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(cacheFile, JsonSerializer.Serialize(entries));
        }
        catch (Exception ex) { Program.Logger.LogError("Failed to save IDE cache: {Error}", ex.Message); }
    }

    /// <summary>
    /// Loads cached IDE entries, re-validates window handles, and returns surviving entries.
    /// </summary>
    internal static Dictionary<string, List<ActiveProcess>> Load(string cacheFile)
    {
        var result = new Dictionary<string, List<ActiveProcess>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(cacheFile))
            {
                return result;
            }

            var entries = JsonSerializer.Deserialize<List<IdeEntry>>(File.ReadAllText(cacheFile)) ?? [];
            foreach (var entry in entries)
            {
                var hwnd = new IntPtr(entry.Hwnd);

                // Re-validate: is the window still alive?
                if (!WindowFocusService.IsWindowAlive(hwnd))
                {
                    continue;
                }

                var proc = new ActiveProcess(entry.Name, 0, entry.FolderPath)
                {
                    Hwnd = hwnd
                };

                if (!result.ContainsKey(entry.SessionId))
                {
                    result[entry.SessionId] = [];
                }

                result[entry.SessionId].Add(proc);
            }
        }
        catch (Exception ex) { Program.Logger.LogWarning("Failed to load IDE cache: {Error}", ex.Message); }

        return result;
    }
}
