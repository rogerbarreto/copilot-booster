using System.Collections.Generic;
using System.Diagnostics;
using CopilotBooster.Models;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Encapsulates the session refresh pipeline: load sessions from disk,
/// refresh active-status tracking, and cache the result.
/// Grid population remains in the UI layer.
/// </summary>
internal class SessionRefreshCoordinator
{
    private readonly string _sessionStateDir;
    private readonly string _pidRegistryFile;
    private readonly ActiveStatusTracker _activeTracker;
    private List<NamedSession> _cachedSessions = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionRefreshCoordinator"/> class.
    /// </summary>
    /// <param name="sessionStateDir">Path to the directory containing session state.</param>
    /// <param name="pidRegistryFile">Path to the PID registry JSON file.</param>
    /// <param name="activeTracker">The active-status tracker instance.</param>
    internal SessionRefreshCoordinator(string sessionStateDir, string pidRegistryFile, ActiveStatusTracker activeTracker)
    {
        this._sessionStateDir = sessionStateDir;
        this._pidRegistryFile = pidRegistryFile;
        this._activeTracker = activeTracker;
    }

    /// <summary>
    /// Loads named sessions from disk, merges aliases, and caches them.
    /// </summary>
    /// <returns>The loaded sessions.</returns>
    internal IReadOnlyList<NamedSession> LoadSessions()
    {
        this._cachedSessions = SessionService.LoadNamedSessions(this._sessionStateDir, this._pidRegistryFile);

        // Merge aliases from the alias file
        var aliases = SessionAliasService.Load(Program.SessionAliasFile);
        foreach (var session in this._cachedSessions)
        {
            if (aliases.TryGetValue(session.Id, out var alias))
            {
                session.Alias = alias;
            }
        }

        return this._cachedSessions;
    }

    /// <summary>
    /// Refreshes active-status tracking for the given sessions.
    /// </summary>
    /// <param name="sessions">The sessions to refresh status for.</param>
    /// <returns>A snapshot of active text, session names, and status icons.</returns>
    internal ActiveStatusSnapshot RefreshActiveStatus(IReadOnlyList<NamedSession> sessions)
    {
        Stopwatch? sw = Program.Logger.IsEnabled(LogLevel.Debug) ? Stopwatch.StartNew() : null;
        var result = this._activeTracker.Refresh((List<NamedSession>)sessions);
        if (sw != null)
        {
            sw.Stop();
            Program.Logger.LogDebug("RefreshActiveStatus: {ElapsedMs}ms ({SessionCount} sessions)", sw.ElapsedMilliseconds, sessions.Count);
        }
        return result;
    }

    /// <summary>
    /// Returns the most recently cached sessions.
    /// </summary>
    internal IReadOnlyList<NamedSession> GetCachedSessions() => this._cachedSessions;
}
