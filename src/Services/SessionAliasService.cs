using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CopilotBooster.Services;

/// <summary>
/// Persists session alias mappings in a JSON file.
/// </summary>
internal static class SessionAliasService
{
    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

    /// <summary>
    /// Loads all session aliases from the alias file.
    /// </summary>
    internal static Dictionary<string, string> Load(string aliasFile)
    {
        try
        {
            if (File.Exists(aliasFile))
            {
                var json = File.ReadAllText(aliasFile);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Saves all session aliases to the alias file.
    /// </summary>
    internal static void Save(string aliasFile, Dictionary<string, string> aliases)
    {
        try
        {
            var dir = Path.GetDirectoryName(aliasFile);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(aliasFile, JsonSerializer.Serialize(aliases, s_writeOptions));
        }
        catch { }
    }

    /// <summary>
    /// Sets the alias for a session. If alias is empty, removes the entry.
    /// </summary>
    internal static void SetAlias(string aliasFile, string sessionId, string alias)
    {
        var aliases = Load(aliasFile);
        if (string.IsNullOrWhiteSpace(alias))
        {
            aliases.Remove(sessionId);
        }
        else
        {
            aliases[sessionId] = alias;
        }

        Save(aliasFile, aliases);
    }

    /// <summary>
    /// Gets the alias for a session, or null if not set.
    /// </summary>
    internal static string? GetAlias(string aliasFile, string sessionId)
    {
        var aliases = Load(aliasFile);
        return aliases.TryGetValue(sessionId, out var alias) ? alias : null;
    }

    /// <summary>
    /// Removes the alias for a session.
    /// </summary>
    internal static void RemoveAlias(string aliasFile, string sessionId)
    {
        var aliases = Load(aliasFile);
        if (aliases.Remove(sessionId))
        {
            Save(aliasFile, aliases);
        }
    }
}
