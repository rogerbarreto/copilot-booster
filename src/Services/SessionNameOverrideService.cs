using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Information about a session name override stored in the session-names.json sidecar.
/// </summary>
internal record SessionNameOverride(string Name, bool ResolvedFromUserMessage);

/// <summary>
/// Persists session name override mappings in a JSON file.
/// Stores Booster-Resolved Names that must not pollute workspace.yaml.summary.
/// </summary>
internal static class SessionNameOverrideService
{
    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

    /// <summary>
    /// Loads all session name overrides from the override file.
    /// </summary>
    internal static Dictionary<string, SessionNameOverride> Load(string overrideFile)
    {
        try
        {
            if (File.Exists(overrideFile))
            {
                var json = File.ReadAllText(overrideFile);
                return JsonSerializer.Deserialize<Dictionary<string, SessionNameOverride>>(json)
                    ?? new Dictionary<string, SessionNameOverride>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("Failed to load session name overrides: {Error}", ex.Message);
        }

        return new Dictionary<string, SessionNameOverride>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Saves all session name overrides to the override file.
    /// </summary>
    internal static void Save(string overrideFile, Dictionary<string, SessionNameOverride> overrides)
    {
        try
        {
            var dir = Path.GetDirectoryName(overrideFile);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(overrideFile, JsonSerializer.Serialize(overrides, s_writeOptions));
        }
        catch (Exception ex)
        {
            Program.Logger.LogError("Failed to save session name overrides: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Sets the override for a session. If name is null/empty/whitespace, removes the entry.
    /// Stores ResolvedFromUserMessage as given (caller decides).
    /// </summary>
    internal static void Set(string overrideFile, string sessionId, string? name, bool resolvedFromUserMessage)
    {
        var overrides = Load(overrideFile);
        if (string.IsNullOrWhiteSpace(name))
        {
            overrides.Remove(sessionId);
        }
        else
        {
            overrides[sessionId] = new SessionNameOverride(name, resolvedFromUserMessage);
        }

        Save(overrideFile, overrides);
    }

    /// <summary>
    /// Returns the override entry for sessionId, or null if missing.
    /// </summary>
    internal static SessionNameOverride? Get(string overrideFile, string sessionId)
    {
        var overrides = Load(overrideFile);
        return overrides.TryGetValue(sessionId, out var entry) ? entry : null;
    }

    /// <summary>
    /// Removes the entry. No-op if missing.
    /// </summary>
    internal static void Remove(string overrideFile, string sessionId)
    {
        var overrides = Load(overrideFile);
        if (overrides.Remove(sessionId))
        {
            Save(overrideFile, overrides);
        }
    }
}
