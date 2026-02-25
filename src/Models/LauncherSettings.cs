using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

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
    /// Gets or sets the custom directory for git worktree workspaces.
    /// When empty, uses the default location (<c>%APPDATA%\CopilotBooster\Workspaces</c>).
    /// </summary>
    [JsonPropertyName("workspacesDir")]
    public string WorkspacesDir { get; set; } = "";

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
    /// Gets or sets whether to auto-minimize windows from other sessions when focusing a session.
    /// </summary>
    [JsonPropertyName("autoHideOnFocus")]
    public bool AutoHideOnFocus { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the CopilotBooster window stays on top of other windows.
    /// </summary>
    [JsonPropertyName("alwaysOnTop")]
    public bool AlwaysOnTop { get; set; }

    /// <summary>
    /// Gets or sets the application theme. Valid values are <c>"system"</c>, <c>"light"</c>, and <c>"dark"</c>.
    /// </summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "system";

    /// <summary>
    /// Gets or sets the maximum number of active (non-archived) sessions allowed.
    /// Set to 0 for unlimited. Default is 50.
    /// </summary>
    [JsonPropertyName("maxActiveSessions")]
    public int MaxActiveSessions { get; set; } = 50;

    /// <summary>
    /// Gets or sets the sort order for pinned sessions.
    /// Valid values: <c>"running"</c> (default, running first then by date), <c>"created"</c> (by creation/update time), <c>"alias"</c> (alphabetical by alias/name).
    /// </summary>
    [JsonPropertyName("pinnedOrder")]
    public string PinnedOrder { get; set; } = "running";

    /// <summary>
    /// Gets or sets whether renaming a session also updates the Edge anchor tab title.
    /// </summary>
    [JsonPropertyName("updateEdgeTabOnRename")]
    public bool UpdateEdgeTabOnRename { get; set; }

    /// <summary>
    /// Gets or sets the list of directory names to skip during IDE file pattern search (non-git fallback).
    /// </summary>
    [JsonPropertyName("ideSearchIgnoredDirs")]
    public List<string> IdeSearchIgnoredDirs { get; set; } =
    [
        ".git", ".vs", ".vscode", ".idea",
        "node_modules", "bower_components",
        "bin", "obj", "out", "build", "dist", "target",
        "__pycache__", ".mypy_cache", ".pytest_cache", ".tox", ".venv", "venv", "env",
        "vendor", "pkg",
        "packages", "TestResults", "artifacts",
        ".next", ".nuget", ".gradle", ".cargo"
    ];

    /// <summary>
    /// Gets or sets the minimum log level. Valid values match <see cref="Microsoft.Extensions.Logging.LogLevel"/> names:
    /// <c>"Trace"</c>, <c>"Debug"</c>, <c>"Information"</c>, <c>"Warning"</c>, <c>"Error"</c>, <c>"Critical"</c>, <c>"None"</c>.
    /// Defaults to <c>null</c> (uses Information in Release, Debug in DEBUG builds).
    /// Set to <c>"Debug"</c> for profiling diagnostics.
    /// </summary>
    [JsonPropertyName("logLevel")]
    public string? LogLevel { get; set; }

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
        catch (Exception ex) { Program.Logger.LogWarning("Failed to load settings: {Error}", ex.Message); }

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
        catch (Exception ex) { Program.Logger.LogError("Failed to save settings: {Error}", ex.Message); }
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
            parts.Add("--allow-tool");
            parts.Add($"\"{tool}\"");
        }

        foreach (var dir in this.AllowedDirs)
        {
            parts.Add("--add-dir");
            parts.Add($"\"{dir}\"");
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
    /// Gets or sets an optional file pattern for the IDE (e.g., "*.sln;*.slnx").
    /// When set, the context menu shows matching files as a sub-menu.
    /// </summary>
    [JsonPropertyName("filePattern")]
    public string FilePattern { get; set; } = "";

    /// <summary>
    /// Returns a display string combining the description and path.
    /// </summary>
    public override string ToString() => string.IsNullOrEmpty(this.Description) ? this.Path : $"{this.Description}  —  {this.Path}";
}
