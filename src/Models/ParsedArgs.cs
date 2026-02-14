namespace CopilotApp.Models;

/// <summary>
/// Represents the parsed command-line arguments for the launcher.
/// </summary>
/// <param name="ResumeSessionId">The session ID to resume, or <c>null</c> to start a new session.</param>
/// <param name="OpenExisting">Whether to open an existing session instead of creating a new one.</param>
/// <param name="ShowSettings">Whether to display the settings UI.</param>
/// <param name="NewSession">Whether to open the New Session tab.</param>
/// <param name="OpenIdeSessionId">The session ID to open in an IDE, or <c>null</c> if not requested.</param>
/// <param name="WorkDir">The working directory override, or <c>null</c> to use the default.</param>
internal record ParsedArgs(
    string? ResumeSessionId,
    bool OpenExisting,
    bool ShowSettings,
    bool NewSession,
    string? OpenIdeSessionId,
    string? WorkDir);
