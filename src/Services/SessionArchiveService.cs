using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Persists session archive and pin state in a JSON file.
/// </summary>
internal static class SessionArchiveService
{
    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

    internal class SessionState
    {
        public bool IsArchived { get; set; }
        public bool IsPinned { get; set; }
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

    /// <summary>
    /// Sets the archive state for a session.
    /// </summary>
    internal static void SetArchived(string stateFile, string sessionId, bool isArchived)
    {
        var states = Load(stateFile);
        var state = GetOrCreate(states, sessionId);
        state.IsArchived = isArchived;
        CleanupIfDefault(states, sessionId, state);
        Save(stateFile, states);
    }

    /// <summary>
    /// Returns whether a session is archived.
    /// </summary>
    internal static bool IsArchived(string stateFile, string sessionId)
    {
        var states = Load(stateFile);
        return states.TryGetValue(sessionId, out var state) && state.IsArchived;
    }

    /// <summary>
    /// Sets the pin state for a session.
    /// </summary>
    internal static void SetPinned(string stateFile, string sessionId, bool isPinned)
    {
        var states = Load(stateFile);
        var state = GetOrCreate(states, sessionId);
        state.IsPinned = isPinned;
        CleanupIfDefault(states, sessionId, state);
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

    private static SessionState GetOrCreate(Dictionary<string, SessionState> states, string sessionId)
    {
        if (!states.TryGetValue(sessionId, out var state))
        {
            state = new SessionState();
            states[sessionId] = state;
        }
        return state;
    }

    private static void CleanupIfDefault(Dictionary<string, SessionState> states, string sessionId, SessionState state)
    {
        if (!state.IsArchived && !state.IsPinned)
        {
            states.Remove(sessionId);
        }
    }
}
