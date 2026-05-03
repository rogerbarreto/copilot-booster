using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Extracts the first user.message content from a Copilot CLI events.jsonl file.
/// </summary>
internal static class FirstUserMessageExtractor
{
    /// <summary>
    /// Reads events.jsonl and returns the content of the FIRST event whose
    /// `type` == "user.message". Looks for `data.content` (string).
    /// Returns null on:
    ///   - missing file
    ///   - empty file
    ///   - no user.message event found
    ///   - malformed JSON (skip line, keep going)
    ///   - any IO/parse exception
    /// Stops reading as soon as it finds the first user.message (do NOT scan whole file).
    /// Reads line-by-line forward; tolerates trailing partial line (skip it).
    /// </summary>
    internal static string? Extract(string eventsJsonlPath)
    {
        if (!File.Exists(eventsJsonlPath))
        {
            return null;
        }

        try
        {
            using var fs = new FileStream(eventsJsonlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("type", out var typeProp) &&
                        typeProp.GetString() == "user.message")
                    {
                        if (root.TryGetProperty("data", out var dataProp) &&
                            dataProp.TryGetProperty("content", out var contentProp))
                        {
                            return contentProp.GetString();
                        }
                    }
                }
                catch (JsonException)
                {
                    // Malformed JSON, skip and continue
                    continue;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("Failed to extract first user message from {Path}: {Error}", eventsJsonlPath, ex.Message);
            return null;
        }
    }
}
