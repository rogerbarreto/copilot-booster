using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Persists and loads Edge tab URLs for a session using per-session state files.
/// </summary>
internal static class EdgeTabPersistenceService
{
    private const string FileName = "edge-tabs.json";

    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

    /// <summary>
    /// Saves the given tab URLs for the specified session.
    /// </summary>
    internal static void SaveTabs(string sessionId, List<string> urls)
    {
        try
        {
            var dir = SessionStateService.EnsureSessionDir(sessionId);
            var path = Path.Combine(dir, FileName);
            File.WriteAllText(path, JsonSerializer.Serialize(urls, s_writeOptions));
            Program.Logger.LogDebug("Saved {Count} Edge tabs for session {SessionId}", urls.Count, sessionId);
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("Failed to save Edge tabs for {SessionId}: {Error}", sessionId, ex.Message);
        }
    }

    /// <summary>
    /// Loads previously saved tab URLs for the specified session.
    /// </summary>
    internal static List<string> LoadTabs(string sessionId)
    {
        try
        {
            var path = Path.Combine(SessionStateService.GetSessionDir(sessionId), FileName);
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<string>>(json) ?? [];
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("Failed to load Edge tabs for {SessionId}: {Error}", sessionId, ex.Message);
        }

        return [];
    }

    /// <summary>
    /// Returns true if the session has saved Edge tabs.
    /// </summary>
    internal static bool HasSavedTabs(string sessionId)
    {
        var path = Path.Combine(SessionStateService.GetSessionDir(sessionId), FileName);
        return File.Exists(path);
    }
}
