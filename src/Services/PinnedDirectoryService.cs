using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CopilotApp.Services;

/// <summary>
/// Manages pinned directories persisted in ~/.copilot/pinned-directories.json.
/// </summary>
internal static class PinnedDirectoryService
{
    private static readonly string s_pinnedFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "pinned-directories.json");

    /// <summary>
    /// Loads the list of pinned directories from disk.
    /// </summary>
    internal static List<string> Load()
    {
        try
        {
            if (File.Exists(s_pinnedFile))
            {
                var json = File.ReadAllText(s_pinnedFile);
                return JsonSerializer.Deserialize<List<string>>(json) ?? [];
            }
        }
        catch { }
        return [];
    }

    /// <summary>
    /// Saves the list of pinned directories to disk.
    /// </summary>
    internal static void Save(List<string> directories)
    {
        try
        {
            var dir = Path.GetDirectoryName(s_pinnedFile)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(s_pinnedFile, JsonSerializer.Serialize(directories, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>
    /// Adds a directory to the pinned list if not already present.
    /// </summary>
    internal static void Add(string path)
    {
        var dirs = Load();
        if (!dirs.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            dirs.Add(path);
            Save(dirs);
        }
    }

    /// <summary>
    /// Removes a directory from the pinned list.
    /// </summary>
    internal static void Remove(string path)
    {
        var dirs = Load();
        dirs.RemoveAll(d => string.Equals(d, path, StringComparison.OrdinalIgnoreCase));
        Save(dirs);
    }

    /// <summary>
    /// Gets all pinned directories.
    /// </summary>
    internal static List<string> GetAll() => Load();
}
