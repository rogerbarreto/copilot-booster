using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CopilotBooster.Models;

/// <summary>
/// Represents the persisted launcher configuration settings.
/// </summary>
internal class LauncherSettings
{
    private static readonly string s_settingsFile = Path.Combine(
        Program.AppDataDir, "launcher-settings.json");

    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

    /// <summary>
    /// Gets or sets the list of tools the launcher is allowed to use.
    /// </summary>
    [JsonPropertyName("allowedTools")]
    public List<string> AllowedTools { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of directories the launcher is allowed to access.
    /// </summary>
    [JsonPropertyName("allowedDirs")]
    public List<string> AllowedDirs { get; set; } = [];

    /// <summary>
    /// Gets or sets the default working directory for new sessions.
    /// </summary>
    [JsonPropertyName("defaultWorkDir")]
    public string DefaultWorkDir { get; set; } = "";

    /// <summary>
    /// Gets or sets the list of configured IDE entries.
    /// </summary>
    [JsonPropertyName("ides")]
    public List<IdeEntry> Ides { get; set; } = [];

    /// <summary>
    /// Gets or sets whether to show a notification when a Copilot CLI session enters the bell state.
    /// </summary>
    [JsonPropertyName("notifyOnBell")]
    public bool NotifyOnBell { get; set; } = true;

    /// <summary>
    /// Loads the launcher settings from the default settings file.
    /// </summary>
    /// <returns>The deserialized <see cref="LauncherSettings"/> instance.</returns>
    public static LauncherSettings Load() => Load(s_settingsFile);

    /// <summary>
    /// Loads the launcher settings from the specified file path.
    /// </summary>
    /// <param name="settingsFile">The path to the settings JSON file.</param>
    /// <returns>The deserialized <see cref="LauncherSettings"/>, or a new default instance if the file does not exist or is invalid.</returns>
    internal static LauncherSettings Load(string settingsFile)
    {
        try
        {
            if (File.Exists(settingsFile))
            {
                var json = File.ReadAllText(settingsFile);
                return JsonSerializer.Deserialize<LauncherSettings>(json) ?? CreateDefault();
            }
        }
        catch { }

        var settings = CreateDefault();
        settings.Save(settingsFile);
        return settings;
    }

    /// <summary>
    /// Saves the current settings to the default settings file.
    /// </summary>
    public void Save() => this.Save(s_settingsFile);

    /// <summary>
    /// Saves the current settings to the specified file path.
    /// </summary>
    /// <param name="settingsFile">The path to write the settings JSON file.</param>
    internal void Save(string settingsFile)
    {
        try
        {
            var dir = Path.GetDirectoryName(settingsFile)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = s_writeOptions;
            File.WriteAllText(settingsFile, JsonSerializer.Serialize(this, options));
        }
        catch { }
    }

    /// <summary>
    /// Creates a new <see cref="LauncherSettings"/> instance with default values.
    /// </summary>
    /// <returns>A default <see cref="LauncherSettings"/> instance.</returns>
    internal static LauncherSettings CreateDefault()
    {
        return new LauncherSettings
        {
            AllowedTools = [],
            AllowedDirs = [],
            DefaultWorkDir = ""
        };
    }

    /// <summary>
    /// Builds a command-line argument string from the current settings and any extra arguments.
    /// </summary>
    /// <param name="extraArgs">Additional command-line arguments to append.</param>
    /// <returns>A space-separated string of command-line arguments.</returns>
    public string BuildCopilotArgs(string[] extraArgs)
    {
        var parts = new List<string>();
        foreach (var tool in this.AllowedTools)
        {
            parts.Add($"\"--allow-tool={tool}\"");
        }

        foreach (var dir in this.AllowedDirs)
        {
            parts.Add($"\"--add-dir={dir}\"");
        }

        foreach (var arg in extraArgs)
        {
            parts.Add(arg);
        }

        return string.Join(" ", parts);
    }
}

/// <summary>
/// Represents a configured IDE with its executable path and display description.
/// </summary>
internal class IdeEntry
{
    /// <summary>
    /// Gets or sets the file path to the IDE executable.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    /// <summary>
    /// Gets or sets a human-readable description of the IDE.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>
    /// Returns a display string combining the description and path.
    /// </summary>
    /// <returns>A formatted string representation of the IDE entry.</returns>
    public override string ToString() => string.IsNullOrEmpty(this.Description) ? this.Path : $"{this.Description}  —  {this.Path}";
}
