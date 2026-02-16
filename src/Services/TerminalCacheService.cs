using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Manages terminal PID caching for tracking running terminal sessions across app restarts.
/// </summary>
internal static class TerminalCacheService
{
    /// <summary>
    /// Adds or updates a terminal cache entry for the specified session.
    /// </summary>
    /// <param name="cacheFile">Path to the terminal cache JSON file.</param>
    /// <param name="sessionId">The session ID to cache.</param>
    /// <param name="copilotPid">The process ID (informational only; liveness is checked by window title).</param>
    internal static void CacheTerminal(string cacheFile, string sessionId, int copilotPid)
    {
        try
        {
            Dictionary<string, JsonElement> cache = [];
            if (File.Exists(cacheFile))
            {
                try
                {
                    cache = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(cacheFile)) ?? [];
                }
                catch (Exception ex) { Program.Logger.LogWarning("Failed to parse terminal cache: {Error}", ex.Message); }
            }

            cache[sessionId] = JsonSerializer.Deserialize<JsonElement>(
                JsonSerializer.Serialize(new { copilotPid, started = DateTime.Now.ToString("o") }));

            File.WriteAllText(cacheFile, JsonSerializer.Serialize(cache));
        }
        catch (Exception ex) { Program.Logger.LogError("Failed to cache terminal: {Error}", ex.Message); }
    }

    /// <summary>
    /// Returns all cached terminal session IDs.
    /// Unlike GetActiveSessions, liveness is NOT checked here — callers should verify
    /// by window title matching since wt.exe launcher PIDs exit immediately.
    /// </summary>
    /// <param name="cacheFile">Path to the terminal cache JSON file.</param>
    /// <returns>A set of cached session IDs.</returns>
    internal static HashSet<string> GetCachedTerminals(string cacheFile)
    {
        var ids = new HashSet<string>();
        try
        {
            if (!File.Exists(cacheFile))
            {
                return ids;
            }

            var cache = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(cacheFile)) ?? [];
            foreach (var entry in cache)
            {
                ids.Add(entry.Key);
            }
        }
        catch (Exception ex) { Program.Logger.LogWarning("Failed to read terminal cache: {Error}", ex.Message); }

        return ids;
    }

    /// <summary>
    /// Removes the cache entry for the specified session.
    /// </summary>
    /// <param name="cacheFile">Path to the terminal cache JSON file.</param>
    /// <param name="sessionId">The session ID to remove.</param>
    internal static void RemoveTerminal(string cacheFile, string sessionId)
    {
        try
        {
            if (!File.Exists(cacheFile))
            {
                return;
            }

            var cache = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(cacheFile)) ?? [];
            cache.Remove(sessionId);
            File.WriteAllText(cacheFile, JsonSerializer.Serialize(cache));
        }
        catch (Exception ex) { Program.Logger.LogError("Failed to remove terminal cache: {Error}", ex.Message); }
    }
}
