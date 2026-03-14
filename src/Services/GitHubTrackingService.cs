using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CopilotBooster.Models;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Manages per-session GitHub tracking data persistence (<c>github-tracking.json</c>).
/// </summary>
internal class GitHubTrackingService
{
    private const string FileName = "github-tracking.json";

    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Loads tracking data for a session. Returns null if no data exists.
    /// </summary>
    internal static GitHubTrackingData? Load(string sessionId)
    {
        var path = GetFilePath(sessionId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<GitHubTrackingData>(json);
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("Failed to load GitHub tracking for {SessionId}: {Error}", sessionId, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Saves tracking data for a session.
    /// </summary>
    internal static void Save(string sessionId, GitHubTrackingData data)
    {
        try
        {
            var dir = SessionStateService.EnsureSessionDir(sessionId);
            var path = Path.Combine(dir, FileName);
            File.WriteAllText(path, JsonSerializer.Serialize(data, s_writeOptions));
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("Failed to save GitHub tracking for {SessionId}: {Error}", sessionId, ex.Message);
        }
    }

    /// <summary>
    /// Adds a tracked item to a session. Creates the tracking file if it doesn't exist.
    /// </summary>
    internal static void AddItem(string sessionId, string owner, string repo, GitHubTrackedItem item)
    {
        var data = Load(sessionId) ?? new GitHubTrackingData { Owner = owner, Repo = repo };
        data.Owner = owner;
        data.Repo = repo;

        // Avoid duplicates
        if (!data.Items.Any(i => i.Type == item.Type && i.Number == item.Number))
        {
            data.Items.Add(item);
        }

        Save(sessionId, data);
    }

    /// <summary>
    /// Removes a tracked item from a session.
    /// </summary>
    internal static void RemoveItem(string sessionId, string type, int number)
    {
        var data = Load(sessionId);
        if (data == null)
        {
            return;
        }

        data.Items.RemoveAll(i => i.Type == type && i.Number == number);
        Save(sessionId, data);
    }

    /// <summary>
    /// Marks an item as seen (clears the red dot).
    /// </summary>
    internal static void MarkSeen(string sessionId, string type, int number)
    {
        var data = Load(sessionId);
        if (data == null)
        {
            return;
        }

        var item = data.Items.FirstOrDefault(i => i.Type == type && i.Number == number);
        if (item != null)
        {
            item.HasNewActivity = false;
            item.LastSeenAt = DateTime.UtcNow.ToString("o");
            Save(sessionId, data);
        }
    }

    /// <summary>
    /// Updates a tracked item with fresh data from the API.
    /// </summary>
    internal static void UpdateItem(string sessionId, GitHubTrackedItem updated)
    {
        var data = Load(sessionId);
        if (data == null)
        {
            return;
        }

        var existing = data.Items.FirstOrDefault(i => i.Type == updated.Type && i.Number == updated.Number);
        if (existing == null)
        {
            return;
        }

        // Detect new activity
        if (!string.IsNullOrEmpty(updated.LastModifiedAt)
            && updated.LastModifiedAt != existing.LastModifiedAt
            && !string.IsNullOrEmpty(existing.LastSeenAt))
        {
            existing.HasNewActivity = true;
        }

        existing.State = updated.State;
        existing.Draft = updated.Draft;
        existing.Title = updated.Title;
        existing.Author = updated.Author;
        existing.Labels = updated.Labels;
        existing.Checks = updated.Checks;
        existing.Approvals = updated.Approvals;
        existing.Approvers = updated.Approvers;
        existing.HeadBranch = updated.HeadBranch;
        existing.LastModifiedAt = updated.LastModifiedAt;

        Save(sessionId, data);
    }

    /// <summary>
    /// Returns true if the session has any tracked items with new activity.
    /// </summary>
    internal static bool HasNewActivity(string sessionId)
    {
        var data = Load(sessionId);
        return data?.Items.Any(i => i.HasNewActivity) ?? false;
    }

    private static string GetFilePath(string sessionId)
    {
        return Path.Combine(SessionStateService.GetSessionDir(sessionId), FileName);
    }
}
