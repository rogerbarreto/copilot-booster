using System;

namespace CopilotBooster.Models;

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

    /// <summary>
    /// Gets or sets the cached window handle for direct focus.
    /// </summary>
    public IntPtr Hwnd { get; set; }

    /// <summary>
    /// True once an HWND has been successfully captured at least once.
    /// Used to distinguish "never captured" (show via FolderPath while waiting)
    /// from "was captured then lost" (IDE genuinely closed).
    /// </summary>
    public bool HwndEverCaptured { get; set; }

    public ActiveProcess(string name, int pid, string? folderPath = null)
    {
        this.Name = name;
        this.Pid = pid;
        this.FolderPath = folderPath;
    }
}
