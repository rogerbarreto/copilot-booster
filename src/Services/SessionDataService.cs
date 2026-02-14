using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using CopilotApp.Models;

namespace CopilotApp.Services;

/// <summary>
/// Holds all session-related data produced by a single pass over workspace files.
/// </summary>
internal record SessionData(
    List<NamedSession> Sessions,
    Dictionary<string, int> CwdSessionCounts,
    Dictionary<string, bool> CwdGitStatus
);

/// <summary>
/// Unifies session data loading so workspace files are read once per refresh cycle.
/// </summary>
[ExcludeFromCodeCoverage]
internal class SessionDataService
{
    /// <summary>
    /// Cache of Git repository detection results keyed by directory path.
    /// </summary>
    private readonly Dictionary<string, bool> _gitRepoCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns whether <paramref name="path"/> is inside a Git repository,
    /// using a cached result when available.
    /// </summary>
    /// <param name="path">The file-system path to check.</param>
    /// <returns><c>true</c> when a Git root is found; otherwise, <c>false</c>.</returns>
    internal bool IsGitRepo(string path)
    {
        if (this._gitRepoCache.TryGetValue(path, out bool cached))
        {
            return cached;
        }

        bool result = GitService.IsGitRepository(path);
        this._gitRepoCache[path] = result;
        return result;
    }

    /// <summary>
    /// Reads workspace files once and returns sessions, CWD counts, and CWD git status.
    /// </summary>
    /// <param name="sessionStateDir">Path to the directory containing session state.</param>
    /// <param name="pidRegistryFile">Optional path to the PID registry JSON file.</param>
    /// <returns>A <see cref="SessionData"/> with all three collections populated.</returns>
    internal SessionData LoadAll(string sessionStateDir, string? pidRegistryFile = null, IEnumerable<string>? pinnedDirectories = null)
    {
        var sessions = SessionService.LoadNamedSessions(sessionStateDir, pidRegistryFile);
        var cwdCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var cwdGitStatus = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in sessions)
        {
            var cwd = session.Cwd;
            if (string.IsNullOrEmpty(cwd) || !Directory.Exists(cwd))
            {
                continue;
            }

            cwdCounts.TryGetValue(cwd, out int count);
            cwdCounts[cwd] = count + 1;

            if (!cwdGitStatus.ContainsKey(cwd))
            {
                cwdGitStatus[cwd] = this.IsGitRepo(cwd);
            }
        }

        // Add pinned directories that don't already appear in session CWDs
        if (pinnedDirectories != null)
        {
            foreach (var dir in pinnedDirectories)
            {
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && !cwdCounts.ContainsKey(dir))
                {
                    cwdCounts[dir] = 0;
                    cwdGitStatus[dir] = this.IsGitRepo(dir);
                }
            }
        }

        // Update IsGitRepo on sessions using the cache
        foreach (var session in sessions)
        {
            if (!string.IsNullOrEmpty(session.Cwd))
            {
                session.IsGitRepo = this.IsGitRepo(session.Cwd);
            }
        }

        return new SessionData(sessions, cwdCounts, cwdGitStatus);
    }

    /// <summary>
    /// Builds a dictionary mapping non-empty session summaries to session IDs,
    /// excluding generic titles like "GitHub Copilot".
    /// </summary>
    /// <param name="sessions">The sessions to build the map from.</param>
    /// <returns>A summary-to-ID dictionary.</returns>
    internal static Dictionary<string, string> BuildSummaryMap(List<NamedSession> sessions)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GitHub Copilot" };
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in sessions)
        {
            if (!string.IsNullOrWhiteSpace(session.Summary)
                && !ignored.Contains(session.Summary)
                && !map.ContainsKey(session.Summary))
            {
                map[session.Summary] = session.Id;
            }
        }

        return map;
    }
}
