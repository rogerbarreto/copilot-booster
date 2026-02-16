using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Creates Copilot sessions by pre-creating the session directory and launching the CLI.
/// </summary>
internal static class CopilotSessionCreatorService
{
    /// <summary>
    /// Creates a new Copilot session with the specified working directory and optional name.
    /// Copies workspace.yaml from an existing source session (if available) to get a valid
    /// template, then overrides id, cwd, and summary for the new session.
    /// </summary>
    /// <param name="workingDirectory">The working directory for the session.</param>
    /// <param name="sessionName">Optional session name/summary.</param>
    /// <param name="sourceSessionDir">Optional path to an existing session dir to copy workspace.yaml from.</param>
    /// <returns>The session ID on success, or null on failure.</returns>
    internal static Task<string?> CreateSessionAsync(string workingDirectory, string? sessionName, string? sourceSessionDir = null)
    {
        try
        {
            var sessionId = Guid.NewGuid().ToString();
            var sessionDir = Path.Combine(Program.SessionStateDir, sessionId);
            Directory.CreateDirectory(sessionDir);

            var wsFile = Path.Combine(sessionDir, "workspace.yaml");
            var sourceWsFile = sourceSessionDir != null ? Path.Combine(sourceSessionDir, "workspace.yaml") : null;

            if (sourceWsFile != null && File.Exists(sourceWsFile))
            {
                // Copy from source and override id, cwd, summary
                var lines = File.ReadAllLines(sourceWsFile);
                var updatedLines = new List<string>();
                foreach (var line in lines)
                {
                    if (line.StartsWith("id:"))
                    {
                        updatedLines.Add($"id: {sessionId}");
                    }
                    else if (line.StartsWith("cwd:"))
                    {
                        updatedLines.Add($"cwd: {workingDirectory}");
                    }
                    else if (line.StartsWith("summary:"))
                    {
                        updatedLines.Add($"summary: {sessionName ?? ""}");
                    }
                    else if (line.StartsWith("created_at:") || line.StartsWith("updated_at:"))
                    {
                        updatedLines.Add($"{line.Split(':')[0]}: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}");
                    }
                    else if (line.StartsWith("summary_count:"))
                    {
                        updatedLines.Add("summary_count: 0");
                    }
                    else
                    {
                        updatedLines.Add(line);
                    }
                }
                File.WriteAllLines(wsFile, updatedLines);
            }
            else
            {
                // Create from scratch using a known-good format
                WriteNewWorkspaceYaml(wsFile, sessionId, workingDirectory, sessionName);
            }

            // Create events.jsonl with session.start event
            WriteSessionStartEvent(sessionDir, sessionId, workingDirectory);

            return Task.FromResult<string?>(sessionId);
        }
        catch (Exception ex)
        {
            Program.Logger.LogError("Failed to create session: {Error}", ex.Message);
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>
    /// Finds the first existing session directory that has a workspace.yaml to use as a template.
    /// </summary>
    internal static string? FindTemplateSessionDir()
    {
        if (!Directory.Exists(Program.SessionStateDir))
        {
            return null;
        }

        foreach (var dir in Directory.EnumerateDirectories(Program.SessionStateDir))
        {
            if (File.Exists(Path.Combine(dir, "workspace.yaml")))
            {
                return dir;
            }
        }

        return null;
    }

    private static void WriteNewWorkspaceYaml(string wsFile, string sessionId, string workingDirectory, string? sessionName)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var lines = new List<string>
        {
            $"id: {sessionId}",
            $"cwd: {workingDirectory}",
        };

        var gitRoot = SessionService.FindGitRoot(workingDirectory);
        if (gitRoot != null)
        {
            lines.Add($"git_root: {gitRoot}");
        }

        lines.Add("summary_count: 0");
        lines.Add($"created_at: {now}");
        lines.Add($"updated_at: {now}");

        if (!string.IsNullOrWhiteSpace(sessionName))
        {
            lines.Add($"summary: {sessionName}");
        }

        File.WriteAllLines(wsFile, lines);
    }

    private static void WriteSessionStartEvent(string sessionDir, string sessionId, string workingDirectory)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var eventId = Guid.NewGuid().ToString();
        var escapedCwd = workingDirectory.Replace(@"\", @"\\");
        var json = $"{{\"type\":\"session.start\",\"data\":{{\"sessionId\":\"{sessionId}\",\"version\":1,\"producer\":\"copilot-agent\",\"copilotVersion\":\"0.0.410\",\"startTime\":\"{now}\",\"context\":{{\"cwd\":\"{escapedCwd}\"}}}},\"id\":\"{eventId}\",\"timestamp\":\"{now}\",\"parentId\":null}}";
        File.WriteAllText(Path.Combine(sessionDir, "events.jsonl"), json + Environment.NewLine);
    }
}
