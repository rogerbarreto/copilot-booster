
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CopilotApp.Models;

internal class LauncherSettings
{
    private static readonly string s_settingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "launcher-settings.json");

    [JsonPropertyName("allowedTools")]
    public List<string> AllowedTools { get; set; } = new();

    [JsonPropertyName("allowedDirs")]
    public List<string> AllowedDirs { get; set; } = new();

    [JsonPropertyName("defaultWorkDir")]
    public string DefaultWorkDir { get; set; } = "";

    [JsonPropertyName("ides")]
    public List<IdeEntry> Ides { get; set; } = new();

    public static LauncherSettings Load() => Load(s_settingsFile);

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

    public void Save() => this.Save(s_settingsFile);

    internal void Save(string settingsFile)
    {
        try
        {
            var dir = Path.GetDirectoryName(settingsFile)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(settingsFile, JsonSerializer.Serialize(this, options));
        }
        catch { }
    }

    internal static LauncherSettings CreateDefault()
    {
        return new LauncherSettings
        {
            AllowedTools = new List<string>(),
            AllowedDirs = new List<string>(),
            DefaultWorkDir = ""
        };
    }

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

internal class IdeEntry
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    public override string ToString() => string.IsNullOrEmpty(this.Description) ? this.Path : $"{this.Description}  —  {this.Path}";
}
