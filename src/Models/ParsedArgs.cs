namespace CopilotApp.Models;

internal record ParsedArgs(
    string? ResumeSessionId,
    bool OpenExisting,
    bool ShowSettings,
    string? OpenIdeSessionId,
    string? WorkDir);
