namespace CopilotApp.Models;

/// <summary>
/// Represents a tracked process (terminal or IDE) associated with a session.
/// </summary>
internal class ActiveProcess
{
    /// <summary>
    /// Gets the display name (e.g. "Terminal", "VS Code").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets or sets the process ID.
    /// </summary>
    public int Pid { get; set; }

    /// <summary>
    /// Gets the folder path used to launch the IDE (for re-matching after launcher exits).
    /// </summary>
    public string? FolderPath { get; }

    public ActiveProcess(string name, int pid, string? folderPath = null)
    {
        this.Name = name;
        this.Pid = pid;
        this.FolderPath = folderPath;
    }
}
