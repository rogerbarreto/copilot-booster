namespace CopilotBooster.Services;

/// <summary>
/// Classifies process names into friendly host-kind labels for display and focus routing.
/// </summary>
internal static class HostKindClassifier
{
    /// <summary>
    /// Classifies a Process.ProcessName (without .exe) into a friendly host-kind label.
    /// Case-insensitive matching. Unknown names return "Unknown".
    /// Used for HostKindLabel dispatch in ActiveStatusTracker / pane-focus routing.
    /// </summary>
    internal static string Classify(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return "Unknown";
        }

        var normalized = processName.Trim();

        return normalized.ToLowerInvariant() switch
        {
            "windowsterminal" => "Windows Terminal",
            "warp" or "warpterminal" or "warp-terminal" => "Warp",
            "wezterm-gui" or "wezterm" => "WezTerm",
            "alacritty" => "Alacritty",
            "conhost" => "Console",
            "powershell" or "pwsh" => "PowerShell",
            "cmd" => "Command Prompt",
            "code" => "VS Code",
            "code - insiders" => "VS Code Insiders",
            "cursor" => "Cursor",
            "devenv" => "Visual Studio",
            _ => "Unknown"
        };
    }
}
