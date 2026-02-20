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
    private const string TitleHashFileName = "edge-tabs-title-hash.txt";

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

    /// <summary>
    /// Computes a lightweight hash from saved tab URLs for change detection.
    /// Uses sorted URL list joined with count prefix.
    /// </summary>
    internal static string ComputeSavedTabHash(string sessionId)
    {
        var tabs = LoadTabs(sessionId);
        return ComputeHash(tabs);
    }

    /// <summary>
    /// Saves a tab title hash alongside the tab URLs for lightweight change detection.
    /// </summary>
    internal static void SaveTabTitleHash(string sessionId, string hash)
    {
        try
        {
            var dir = SessionStateService.EnsureSessionDir(sessionId);
            var path = Path.Combine(dir, TitleHashFileName);
            File.WriteAllText(path, hash);
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("Failed to save tab title hash for {SessionId}: {Error}", sessionId, ex.Message);
        }
    }

    /// <summary>
    /// Loads the previously saved tab title hash for change detection.
    /// </summary>
    internal static string? LoadTabTitleHash(string sessionId)
    {
        try
        {
            var path = Path.Combine(SessionStateService.GetSessionDir(sessionId), TitleHashFileName);
            if (File.Exists(path))
            {
                return File.ReadAllText(path).Trim();
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("Failed to load tab title hash for {SessionId}: {Error}", sessionId, ex.Message);
        }

        return null;
    }

    /// <summary>
    /// Computes a hash string from a list of strings (tab names or URLs).
    /// </summary>
    internal static string ComputeHash(IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return "0:";
        }

        var sorted = new List<string>(items);
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        return $"{sorted.Count}:{string.Join("|", sorted)}";
    }
}
