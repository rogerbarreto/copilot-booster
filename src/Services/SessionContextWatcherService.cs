using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Watches session state directories for file changes to maintain cached file/tab counts.
/// Uses a catch-all filter with post-event filtering to exclude reserved files.
/// </summary>
internal class SessionContextWatcherService : IDisposable
{
    private static readonly string s_defaultSessionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".copilot", "session-state");

    private static readonly HashSet<string> s_reservedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "events.jsonl", "workspace.yaml", "workspace-deleted.yaml",
        "session.db", "vscode.metadata.json", "metadata.js"
    };

    private static readonly HashSet<string> s_reservedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "rewind-snapshots", "checkpoints"
    };

    private readonly string _sessionsDir;
    private readonly Dictionary<string, (int Files, int Tabs)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;

    internal SessionContextWatcherService(string? sessionsDir = null)
    {
        this._sessionsDir = sessionsDir ?? s_defaultSessionsDir;
    }

    /// <summary>
    /// Fires when file or tab counts change for a session. Parameter is the session ID.
    /// </summary>
    internal event Action<string>? CountsChanged;

    /// <summary>
    /// Gets cached counts for a session. Returns (0, 0) if not cached.
    /// </summary>
    internal (int Files, int Tabs) GetCounts(string sessionId)
    {
        lock (this._lock)
        {
            return this._cache.TryGetValue(sessionId, out var counts) ? counts : (0, 0);
        }
    }

    /// <summary>
    /// Computes and caches counts for all sessions at startup.
    /// </summary>
    internal void PrimeCache()
    {
        if (!Directory.Exists(this._sessionsDir))
        {
            return;
        }

        foreach (var dir in Directory.EnumerateDirectories(this._sessionsDir))
        {
            var sessionId = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(sessionId))
            {
                continue;
            }

            var fileCount = CountSessionFiles(dir);
            var tabCount = EdgeTabPersistenceService.LoadTabs(sessionId).Count;

            lock (this._lock)
            {
                this._cache[sessionId] = (fileCount, tabCount);
            }
        }
    }

    /// <summary>
    /// Updates the cached tab count for a session.
    /// Call after saving or loading tabs externally.
    /// </summary>
    internal void UpdateTabCount(string sessionId, int tabCount)
    {
        bool changed;
        lock (this._lock)
        {
            this._cache.TryGetValue(sessionId, out var old);
            changed = old.Tabs != tabCount;
            this._cache[sessionId] = (old.Files, tabCount);
        }

        if (changed)
        {
            this.CountsChanged?.Invoke(sessionId);
        }
    }

    /// <summary>
    /// Starts watching for file changes.
    /// </summary>
    internal void StartWatching()
    {
        if (!Directory.Exists(this._sessionsDir))
        {
            return;
        }

        try
        {
            this._watcher = new FileSystemWatcher(this._sessionsDir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            this._watcher.Created += this.OnFileEvent;
            this._watcher.Deleted += this.OnFileEvent;
            this._watcher.Changed += this.OnFileEvent;
            this._watcher.Error += this.OnWatcherError;
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("Failed to start session context watcher: {Error}", ex.Message);
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        try
        {
            var relativePath = Path.GetRelativePath(this._sessionsDir, e.FullPath);
            var segments = relativePath.Split(Path.DirectorySeparatorChar);
            if (segments.Length < 2)
            {
                return;
            }

            var sessionId = segments[0];
            var fileName = Path.GetFileName(e.FullPath);

            // Skip files in reserved directories
            if (segments.Length >= 3 && s_reservedDirs.Contains(segments[1]))
            {
                return;
            }

            // Skip .lock files (e.g. inuse.48696.lock created by Copilot CLI)
            if (fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var isEdgeTabs = string.Equals(fileName, "edge-tabs.json", StringComparison.OrdinalIgnoreCase);

            // Skip reserved root-level files (except edge-tabs.json)
            if (!isEdgeTabs && segments.Length == 2 && s_reservedFiles.Contains(fileName))
            {
                return;
            }

            (int Files, int Tabs) oldCounts;
            lock (this._lock)
            {
                this._cache.TryGetValue(sessionId, out oldCounts);
            }

            int newFiles, newTabs;
            if (isEdgeTabs)
            {
                newFiles = oldCounts.Files;
                newTabs = EdgeTabPersistenceService.LoadTabs(sessionId).Count;
            }
            else
            {
                var sessionDir = Path.Combine(this._sessionsDir, sessionId);
                newFiles = CountSessionFiles(sessionDir);
                newTabs = oldCounts.Tabs;
            }

            if (newFiles == oldCounts.Files && newTabs == oldCounts.Tabs)
            {
                return;
            }

            lock (this._lock)
            {
                this._cache[sessionId] = (newFiles, newTabs);
            }

            this.CountsChanged?.Invoke(sessionId);
        }
        catch (Exception ex)
        {
            Program.Logger.LogTrace("Session context watcher event error: {Error}", ex.Message);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Program.Logger.LogWarning("Session context watcher error: {Error}", e.GetException().Message);
    }

    /// <summary>
    /// Counts non-reserved files in a session directory.
    /// </summary>
    private static int CountSessionFiles(string sessionDir)
    {
        if (!Directory.Exists(sessionDir))
        {
            return 0;
        }

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(sessionDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sessionDir, file);
            var fileName = Path.GetFileName(file);

            var firstSegment = relativePath.Split(Path.DirectorySeparatorChar)[0];
            if (s_reservedDirs.Contains(firstSegment))
            {
                continue;
            }

            if (!relativePath.Contains(Path.DirectorySeparatorChar) && s_reservedFiles.Contains(fileName))
            {
                continue;
            }

            // Skip .lock files (e.g. inuse.48696.lock created by Copilot CLI)
            if (fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            count++;
        }

        return count;
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
