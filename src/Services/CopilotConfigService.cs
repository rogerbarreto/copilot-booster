using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Reads and writes settings in the Copilot CLI global config (~/.copilot/config.json).
/// Only touches the <c>allowed_urls</c> property; all other fields are preserved.
/// </summary>
internal static class CopilotConfigService
{
    private static readonly string s_configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "config.json");

    internal static List<string> LoadAllowedUrls()
    {
        try
        {
            if (!File.Exists(s_configPath))
            {
                return [];
            }

            var json = File.ReadAllText(s_configPath);
            var root = JsonNode.Parse(json);
            var urls = root?["allowed_urls"]?.AsArray();
            if (urls is null)
            {
                return [];
            }

            var result = new List<string>();
            foreach (var item in urls)
            {
                var value = item?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.Add(value);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("Failed to load allowed_urls from config.json: {Error}", ex.Message);
            return [];
        }
    }

    internal static void SaveAllowedUrls(List<string> urls)
    {
        try
        {
            JsonNode? root;
            if (File.Exists(s_configPath))
            {
                var json = File.ReadAllText(s_configPath);
                root = JsonNode.Parse(json) ?? new JsonObject();
            }
            else
            {
                var dir = Path.GetDirectoryName(s_configPath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                root = new JsonObject();
            }

            var array = new JsonArray();
            foreach (var url in urls)
            {
                array.Add(url);
            }

            root["allowed_urls"] = array;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(s_configPath, root.ToJsonString(options));
        }
        catch (Exception ex)
        {
            Program.Logger.LogError("Failed to save allowed_urls to config.json: {Error}", ex.Message);
        }
    }
}
