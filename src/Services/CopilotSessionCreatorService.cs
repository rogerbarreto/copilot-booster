using System;
using System.IO;
using System.Threading.Tasks;

namespace CopilotBooster.Services;

/// <summary>
/// Creates Copilot sessions by pre-creating the session directory and launching the CLI.
/// </summary>
internal static class CopilotSessionCreatorService
{
    /// <summary>
    /// Creates a new Copilot session with the specified working directory and optional name.
    /// Pre-creates the session state directory with workspace.yaml so the CLI picks up the config.
    /// </summary>
    /// <param name="workingDirectory">The working directory for the session.</param>
    /// <param name="sessionName">Optional session name/summary.</param>
    /// <returns>The session ID on success, or null on failure.</returns>
    internal static Task<string?> CreateSessionAsync(string workingDirectory, string? sessionName)
    {
        try
        {
            var sessionId = Guid.NewGuid().ToString();
            var sessionDir = Path.Combine(Program.SessionStateDir, sessionId);
            Directory.CreateDirectory(sessionDir);

            // Write workspace.yaml with CWD and optional name
            SessionService.UpdateSession(sessionDir, sessionName ?? "", workingDirectory);

            return Task.FromResult<string?>(sessionId);
        }
        catch (Exception ex)
        {
            LogService.Log($"Failed to create session: {ex.Message}",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "launcher.log"));
            return Task.FromResult<string?>(null);
        }
    }
}
