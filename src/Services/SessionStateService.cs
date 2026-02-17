using System.IO;

namespace CopilotBooster.Services;

/// <summary>
/// Manages per-session state directories under %APPDATA%/CopilotBooster/sessions/{sessionId}/.
/// </summary>
internal static class SessionStateService
{
    private static readonly string s_sessionsRoot = Path.Combine(Program.AppDataDir, "sessions");

    /// <summary>
    /// Gets the per-session state directory path. Does not create it.
    /// </summary>
    internal static string GetSessionDir(string sessionId)
        => Path.Combine(s_sessionsRoot, sessionId);

    /// <summary>
    /// Ensures the per-session state directory exists and returns its path.
    /// </summary>
    internal static string EnsureSessionDir(string sessionId)
    {
        var dir = GetSessionDir(sessionId);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        return dir;
    }
}
