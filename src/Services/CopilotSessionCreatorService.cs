using System;
using System.IO;
using System.Threading.Tasks;
using GitHub.Copilot.SDK;

namespace CopilotApp.Services;

/// <summary>
/// Creates Copilot sessions programmatically using the GitHub Copilot SDK.
/// </summary>
internal static class CopilotSessionCreatorService
{
    /// <summary>
    /// Creates a new Copilot session with the specified working directory and optional name.
    /// </summary>
    /// <param name="workingDirectory">The working directory for the session.</param>
    /// <param name="sessionName">Optional session name/summary.</param>
    /// <returns>The session ID on success, or null on failure.</returns>
    internal static async Task<string?> CreateSessionAsync(string workingDirectory, string? sessionName)
    {
        try
        {
            var client = new CopilotClient();
            await using (client.ConfigureAwait(false))
            {
                var session = await client.CreateSessionAsync(new SessionConfig
                {
                    WorkingDirectory = workingDirectory
                }).ConfigureAwait(false);

                await using (session.ConfigureAwait(false))
                {
                    var sessionId = session.SessionId;

                    // Set the session name if provided
                    if (!string.IsNullOrWhiteSpace(sessionName))
                    {
                        var sessionDir = Path.Combine(Program.SessionStateDir, sessionId);
                        SessionService.UpdateSession(sessionDir, sessionName, workingDirectory);
                    }

                    return sessionId;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Log($"Failed to create session: {ex.Message}",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "launcher.log"));
            return null;
        }
    }
}
