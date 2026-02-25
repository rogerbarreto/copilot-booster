using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Persists session tab and pin state in a JSON file.
/// </summary>
internal static class SessionArchiveService
{
    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

    internal class SessionState
    {
        /// <summary>
        /// The tab this session belongs to. Empty or null means the default (first) tab.
        /// </summary>
        [JsonPropertyName("Tab")]
        public string Tab { get; set; } = "";

        [JsonPropertyName("IsPinned")]
        public bool IsPinned { get; set; }

        /// <summary>
        /// Legacy property for backward compatibility. Writing always uses <see cref="Tab"/>.
        /// </summary>
        [JsonPropertyName("IsArchived")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool IsArchived
        {
            get => false;
            set
            {
                // Migrate: archived sessions go to "Archived" tab
                if (value && string.IsNullOrEmpty(this.Tab))
                {
                    this.Tab = "Archived";
                }
            }
        }
    }

    /// <summary>
    /// Loads all session states from the state file.
    /// </summary>
    internal static Dictionary<string, SessionState> Load(string stateFile)
    {
        try
        {
            if (File.Exists(stateFile))
            {
                var json = File.ReadAllText(stateFile);
                return JsonSerializer.Deserialize<Dictionary<string, SessionState>>(json)
                    ?? new Dictionary<string, SessionState>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) { Program.Logger.LogWarning("Failed to load session states: {Error}", ex.Message); }

        return new Dictionary<string, SessionState>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Saves all session states to the state file.
    /// </summary>
    internal static void Save(string stateFile, Dictionary<string, SessionState> states)
    {
        try
        {
            var dir = Path.GetDirectoryName(stateFile);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(stateFile, JsonSerializer.Serialize(states, s_writeOptions));
        }
        catch (Exception ex) { Program.Logger.LogError("Failed to save session states: {Error}", ex.Message); }
    }

    private static string GetDefaultTab()
    {
        return Program._settings?.DefaultTab ?? "Active";
    }

    /// <summary>
    /// Sets the tab for a session.
    /// </summary>
    internal static void SetTab(string stateFile, string sessionId, string tab)
    {
        var defaultTab = GetDefaultTab();
        var states = Load(stateFile);
        var state = GetOrCreate(states, sessionId);
        state.Tab = tab;
        CleanupIfDefault(states, sessionId, state, defaultTab);
        Save(stateFile, states);
    }

    /// <summary>
    /// Returns the tab name for a session, or the default tab if not set.
    /// </summary>
    internal static string GetTab(string stateFile, string sessionId)
    {
        var defaultTab = GetDefaultTab();
        var states = Load(stateFile);
        if (states.TryGetValue(sessionId, out var state) && !string.IsNullOrEmpty(state.Tab))
        {
            return state.Tab;
        }

        return defaultTab;
    }

    /// <summary>
    /// Sets the pin state for a session.
    /// </summary>
    internal static void SetPinned(string stateFile, string sessionId, bool isPinned)
    {
        var defaultTab = GetDefaultTab();
        var states = Load(stateFile);
        var state = GetOrCreate(states, sessionId);
        state.IsPinned = isPinned;
        CleanupIfDefault(states, sessionId, state, defaultTab);
        Save(stateFile, states);
    }

    /// <summary>
    /// Returns whether a session is pinned.
    /// </summary>
    internal static bool IsPinned(string stateFile, string sessionId)
    {
        var states = Load(stateFile);
        return states.TryGetValue(sessionId, out var state) && state.IsPinned;
    }

    /// <summary>
    /// Removes a session's state entirely.
    /// </summary>
    internal static void Remove(string stateFile, string sessionId)
    {
        var states = Load(stateFile);
        if (states.Remove(sessionId))
        {
            Save(stateFile, states);
        }
    }

    /// <summary>
    /// Renames a tab across all session states.
    /// </summary>
    internal static void RenameTab(string stateFile, string oldName, string newName)
    {
        var states = Load(stateFile);
        bool changed = false;
        foreach (var state in states.Values)
        {
            if (string.Equals(state.Tab, oldName, StringComparison.OrdinalIgnoreCase))
            {
                state.Tab = newName;
                changed = true;
            }
        }

        if (changed)
        {
            Save(stateFile, states);
        }
    }

    /// <summary>
    /// Returns the count of sessions in each tab.
    /// </summary>
    internal static Dictionary<string, int> CountByTab(string stateFile, IReadOnlyList<string> sessionIds, string defaultTab)
    {
        var states = Load(stateFile);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var sid in sessionIds)
        {
            var tab = states.TryGetValue(sid, out var state) && !string.IsNullOrEmpty(state.Tab)
                ? state.Tab
                : defaultTab;
            counts[tab] = counts.GetValueOrDefault(tab) + 1;
        }

        return counts;
    }

    private static SessionState GetOrCreate(Dictionary<string, SessionState> states, string sessionId)
    {
        if (!states.TryGetValue(sessionId, out var state))
        {
            state = new SessionState();
            states[sessionId] = state;
        }
        return state;
    }

    private static void CleanupIfDefault(Dictionary<string, SessionState> states, string sessionId, SessionState state, string defaultTab)
    {
        if ((string.IsNullOrEmpty(state.Tab) || string.Equals(state.Tab, defaultTab, StringComparison.OrdinalIgnoreCase))
            && !state.IsPinned)
        {
            states.Remove(sessionId);
        }
    }
}
