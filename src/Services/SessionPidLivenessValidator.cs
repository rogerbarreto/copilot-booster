using System;
using System.Diagnostics;
using System.IO;

namespace CopilotBooster.Services;

/// <summary>
/// Validates that a (sessionId, copilotPid) binding is currently live by comparing
/// the events.jsonl mtime against the copilot process StartTime. A copilot.exe
/// process that did NOT generate events for a given session after its StartTime
/// is not the live host of that session — it likely /resume'd into a different
/// session. This guard prevents stale bindings from surviving (Bug B).
/// </summary>
internal static class SessionPidLivenessValidator
{
    /// <summary>
    /// Real-FS check used at runtime.
    /// </summary>
    /// <param name="sessionStateDir">Typically Program.SessionStateDir.</param>
    /// <param name="sessionId">Session GUID-shaped string.</param>
    /// <param name="copilotPid">PID of the copilot.exe to validate.</param>
    /// <param name="allowMissingEventsJsonl">
    /// True for fresh T1 watcher discovery (events.jsonl may not yet exist);
    /// false for cache rehydration / startup rescan paths.
    /// </param>
    /// <param name="fudgeSeconds">Allowance for clock skew + write race. Default 5s.</param>
    internal static bool IsLive(
        string sessionStateDir,
        string sessionId,
        int copilotPid,
        bool allowMissingEventsJsonl,
        double fudgeSeconds = 5)
    {
        if (copilotPid <= 0)
        {
            return false;
        }

        DateTime processStartUtc;
        try
        {
            var p = Process.GetProcessById(copilotPid);
            if (p.HasExited)
            {
                return false;
            }

            processStartUtc = p.StartTime.ToUniversalTime();
        }
        catch
        {
            return false;
        }

        var eventsJsonl = Path.Combine(sessionStateDir, sessionId, "events.jsonl");
        if (!File.Exists(eventsJsonl))
        {
            return allowMissingEventsJsonl;
        }

        var mtimeUtc = File.GetLastWriteTimeUtc(eventsJsonl);
        return IsLive(mtimeUtc, processStartUtc, fudgeSeconds);
    }

    /// <summary>
    /// Pure overload — exposed for unit testing.
    /// </summary>
    internal static bool IsLive(
        DateTime eventsJsonlMtimeUtc,
        DateTime copilotStartTimeUtc,
        double fudgeSeconds = 5)
    {
        return eventsJsonlMtimeUtc.AddSeconds(fudgeSeconds) >= copilotStartTimeUtc;
    }
}
