using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace CopilotBooster.Services;

/// <summary>
/// Extracts business logic for session interactions (launching, opening IDEs/terminals, etc.)
/// from the UI layer, making it testable and reusable.
/// </summary>
internal class SessionInteractionManager
{
    private readonly string _sessionStateDir;
    private readonly string _terminalCacheFile;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionInteractionManager"/> class.
    /// </summary>
    /// <param name="sessionStateDir">Path to the directory containing session state.</param>
    /// <param name="terminalCacheFile">Path to the terminal cache JSON file.</param>
    internal SessionInteractionManager(string sessionStateDir, string terminalCacheFile)
    {
        this._sessionStateDir = sessionStateDir;
        this._terminalCacheFile = terminalCacheFile;
    }

    /// <summary>
    /// Launches a Copilot session by starting a new process with <c>--resume</c>.
    /// </summary>
    /// <param name="sessionId">The session ID to resume.</param>
    /// <returns>The started process, or <c>null</c> if the launch failed.</returns>
    internal Process? LaunchSession(string sessionId)
    {
        var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
        try
        {
            return Process.Start(new ProcessStartInfo(exePath, $"--resume {sessionId}")
            {
                UseShellExecute = false
            });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Opens a directory in the specified IDE executable.
    /// </summary>
    /// <param name="idePath">Path to the IDE executable.</param>
    /// <param name="directory">The directory to open.</param>
    /// <returns>The started process, or <c>null</c> if the launch failed.</returns>
    internal static Process? OpenInIde(string idePath, string directory)
    {
        try
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = idePath,
                Arguments = $"\"{directory}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Launches a tracked terminal in the given working directory for a specific session.
    /// </summary>
    /// <param name="cwd">The working directory to open the terminal in.</param>
    /// <param name="sessionId">The session ID for terminal tracking.</param>
    /// <returns>The process ID of the terminal, or <c>null</c> if the launch failed.</returns>
    internal int? OpenTerminal(string cwd, string sessionId)
    {
        var proc = TerminalLauncherService.LaunchTerminal(cwd, sessionId);
        if (proc != null)
        {
            TerminalCacheService.CacheTerminal(this._terminalCacheFile, sessionId, proc.Id);
            return proc.Id;
        }

        return null;
    }

    /// <summary>
    /// Launches a simple (non-tracked) terminal in the given working directory.
    /// </summary>
    /// <param name="cwd">The working directory to open the terminal in.</param>
    /// <returns>The started process, or <c>null</c> if the launch failed.</returns>
    internal static Process? OpenTerminalSimple(string cwd)
    {
        return TerminalLauncherService.LaunchTerminalSimple(cwd);
    }

    /// <summary>
    /// Opens Windows Explorer at the specified path.
    /// </summary>
    /// <param name="path">The directory path to open in Explorer.</param>
    /// <returns>The started process, or <c>null</c> if the launch failed.</returns>
    internal static Process? OpenExplorer(string path)
    {
        try
        {
            return Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Opens an Edge workspace for the specified session.
    /// </summary>
    /// <param name="sessionId">The session ID used as the workspace identifier.</param>
    /// <returns>A new <see cref="EdgeWorkspaceService"/> instance for the session.</returns>
    internal static EdgeWorkspaceService CreateEdgeWorkspace(string sessionId)
    {
        return new EdgeWorkspaceService(sessionId);
    }

    /// <summary>
    /// Deletes a session directory from the session state folder.
    /// </summary>
    /// <param name="sessionId">The session ID to delete.</param>
    /// <returns><c>true</c> if the session was deleted; otherwise, <c>false</c>.</returns>
    /// <summary>
    /// Soft-deletes a session by renaming workspace.yaml to workspace-deleted.yaml.
    /// The session directory and all artifacts are preserved for potential recovery.
    /// </summary>
    internal bool DeleteSession(string sessionId)
    {
        var sessionDir = Path.Combine(this._sessionStateDir, sessionId);
        var workspaceFile = Path.Combine(sessionDir, "workspace.yaml");
        if (!File.Exists(workspaceFile))
        {
            return false;
        }

        try
        {
            File.Move(workspaceFile, Path.Combine(sessionDir, "workspace-deleted.yaml"));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Copies text to the system clipboard.
    /// </summary>
    /// <param name="text">The text to copy.</param>
    internal static void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // Clipboard may be locked by another process
        }
    }

    /// <summary>
    /// Opens the session's dedicated files folder in Explorer.
    /// Creates the folder if it doesn't exist.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <returns>The started Explorer process, or <c>null</c> if the launch failed.</returns>
    internal static Process? OpenSessionFilesFolder(string sessionId)
    {
        var filesDir = GetSessionFilesPath(sessionId);
        try
        {
            Directory.CreateDirectory(filesDir);
            return Process.Start(new ProcessStartInfo("explorer.exe", filesDir) { UseShellExecute = true });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the path to the session's dedicated files folder.
    /// </summary>
    internal static string GetSessionFilesPath(string sessionId)
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot", "session-state", sessionId, "files");
    }

    /// <summary>
    /// Opens a session's plan.md file in the default editor.
    /// </summary>
    /// <param name="sessionStateDir">The session state directory.</param>
    /// <param name="sessionId">The session ID.</param>
    /// <returns>The started process, or <c>null</c> if the file doesn't exist or launch failed.</returns>
    internal static Process? OpenPlanFile(string sessionStateDir, string sessionId)
    {
        var planPath = GetPlanFilePath(sessionStateDir, sessionId);
        if (!File.Exists(planPath))
        {
            return null;
        }

        try
        {
            return Process.Start(new ProcessStartInfo(planPath) { UseShellExecute = true });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the path to a session's plan.md file.
    /// </summary>
    internal static string GetPlanFilePath(string sessionStateDir, string sessionId)
    {
        return Path.Combine(sessionStateDir, sessionId, "plan.md");
    }

    /// <summary>
    /// Checks if a session has a plan.md file.
    /// </summary>
    internal static bool HasPlanFile(string sessionStateDir, string sessionId)
    {
        return File.Exists(GetPlanFilePath(sessionStateDir, sessionId));
    }

    /// <summary>
    /// Reads the CWD from a session's workspace.yaml file.
    /// </summary>
    /// <param name="sessionId">The session ID to look up.</param>
    /// <returns>The CWD value, or <c>null</c> if not found.</returns>
    internal string? GetSessionCwd(string sessionId)
    {
        var workspaceFile = Path.Combine(this._sessionStateDir, sessionId, "workspace.yaml");
        if (!File.Exists(workspaceFile))
        {
            return null;
        }

        foreach (var line in File.ReadLines(workspaceFile))
        {
            if (line.StartsWith("cwd:"))
            {
                return line.Substring("cwd:".Length).Trim();
            }
        }

        return null;
    }
}
