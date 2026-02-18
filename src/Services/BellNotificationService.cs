using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace CopilotBooster.Services;

/// <summary>
/// Detects bell-state transitions in Copilot CLI sessions and shows
/// tray-icon balloon notifications for newly idle sessions.
/// </summary>
internal sealed class BellNotificationService
{
    private readonly NotifyIcon _trayIcon;
    private readonly Func<bool> _isEnabled;
    private readonly HashSet<string> _notifiedBellSessionIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the session ID of the most recent bell notification (used for balloon-tip click handling).
    /// </summary>
    internal string? LastNotifiedSessionId { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BellNotificationService"/> class.
    /// </summary>
    /// <param name="trayIcon">The system-tray icon used to display balloon tips.</param>
    /// <param name="isEnabled">Delegate that returns whether bell notifications are enabled.</param>
    internal BellNotificationService(NotifyIcon trayIcon, Func<bool> isEnabled)
    {
        this._trayIcon = trayIcon;
        this._isEnabled = isEnabled;
    }

    /// <summary>
    /// Seeds the notified set with session IDs that are already in bell state at startup,
    /// preventing false notifications when the application launches.
    /// </summary>
    internal void SeedStartupSessions(IEnumerable<string> bellSessionIds)
    {
        this._notifiedBellSessionIds.UnionWith(bellSessionIds);
    }

    /// <summary>
    /// Checks the snapshot for bell-state transitions and shows a balloon notification
    /// for each session that newly entered the bell state.
    /// </summary>
    internal void CheckAndNotify(ActiveStatusSnapshot snapshot)
    {
        var currentBellIds = new HashSet<string>(
            snapshot.StatusIconBySessionId
                .Where(kvp => kvp.Value == "bell")
                .Select(kvp => kvp.Key),
            StringComparer.OrdinalIgnoreCase);

        // Re-arm: remove sessions that left bell state
        this._notifiedBellSessionIds.IntersectWith(currentBellIds);

        if (!this._isEnabled())
        {
            return;
        }

        // Notify new bell sessions
        foreach (var bellId in currentBellIds)
        {
            if (this._notifiedBellSessionIds.Add(bellId))
            {
                var sessionName = snapshot.SessionNamesById.GetValueOrDefault(bellId, "Copilot CLI");
                this.LastNotifiedSessionId = bellId;
                this._trayIcon.ShowBalloonTip(
                    5000,
                    $"🔔 Session Ready",
                    sessionName,
                    ToolTipIcon.None);
            }
        }
    }

    /// <summary>
    /// Notifies for a single session entering bell state (called from watcher event handler).
    /// </summary>
    internal void NotifySingle(string sessionId, string sessionName)
    {
        if (!this._isEnabled())
        {
            return;
        }

        if (this._notifiedBellSessionIds.Add(sessionId))
        {
            this.LastNotifiedSessionId = sessionId;
            var tipText = string.IsNullOrWhiteSpace(sessionName) ? sessionId : sessionName;
            this._trayIcon.ShowBalloonTip(
                5000,
                $"🔔 Session Ready",
                tipText,
                ToolTipIcon.None);
        }
    }

    /// <summary>
    /// Clears the bell-notified state for a session that transitioned back to working.
    /// </summary>
    internal void ClearNotified(string sessionId)
    {
        this._notifiedBellSessionIds.Remove(sessionId);
    }
}
