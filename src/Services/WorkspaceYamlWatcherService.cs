using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Monitors workspace.yaml files in Copilot CLI session directories.
/// Fires events when a workspace.yaml is created, changed, or deleted.
/// </summary>
internal class WorkspaceYamlWatcherService : IDisposable
{
    private static readonly string s_copilotSessionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".copilot", "session-state");

    private readonly string _sessionsDir;
    private FileSystemWatcher? _watcher;

    internal WorkspaceYamlWatcherService(string? sessionsDir = null)
    {
        this._sessionsDir = sessionsDir ?? s_copilotSessionsDir;
    }

    /// <summary>
    /// Fires when a workspace.yaml is created or changed. Parameter is the session ID.
    /// </summary>
    internal event Action<string>? WorkspaceChanged;

    /// <summary>
    /// Fires when a workspace.yaml is deleted. Parameter is the session ID.
    /// </summary>
    internal event Action<string>? WorkspaceDeleted;

    /// <summary>
    /// Starts the FileSystemWatcher for workspace.yaml files.
    /// </summary>
    internal void StartWatching()
    {
        if (!Directory.Exists(this._sessionsDir))
        {
            return;
        }

        try
        {
            this._watcher = new FileSystemWatcher(this._sessionsDir, "workspace.yaml")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            this._watcher.Changed += this.OnFileChanged;
            this._watcher.Created += this.OnFileChanged;
            this._watcher.Deleted += this.OnFileDeleted;
            this._watcher.Error += this.OnWatcherError;
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("Failed to start workspace.yaml watcher: {Error}", ex.Message);
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var sessionId = Path.GetFileName(Path.GetDirectoryName(e.FullPath));
        if (!string.IsNullOrEmpty(sessionId))
        {
            this.WorkspaceChanged?.Invoke(sessionId);
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        var sessionId = Path.GetFileName(Path.GetDirectoryName(e.FullPath));
        if (!string.IsNullOrEmpty(sessionId))
        {
            this.WorkspaceDeleted?.Invoke(sessionId);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Program.Logger.LogWarning("Workspace.yaml watcher error: {Error}", e.GetException().Message);
    }

    public void Dispose()
    {
        if (this._watcher != null)
        {
            this._watcher.EnableRaisingEvents = false;
            this._watcher.Dispose();
            this._watcher = null;
        }
    }
}
