using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Watches ~/.copilot/logs/ for new Copilot CLI log files and auto-creates workspace.yaml
/// for sessions started outside Copilot Booster. This enables discovery of external sessions
/// that would otherwise be invisible because they lack a workspace.yaml until task completion.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed partial class CopilotLogWatcherService : IDisposable
{
    private static readonly string s_copilotLogsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".copilot", "logs");

    private static readonly Regex s_logFileNameRegex = LogFileNameRegex();

    private readonly string _sessionStateDir;
    private FileSystemWatcher? _watcher;
    private readonly Dictionary<string, Timer> _pendingTimers = [];
    private readonly HashSet<(int pid, string sessionId)> _processedPidSessions = new(new PidSessionEqualityComparer());
    private readonly HashSet<string> _processedLogFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();

    /// <summary>
    /// Fires when an external Copilot session is discovered. Parameters: sessionId, copilotPid.
    /// </summary>
    internal event Action<string, int>? ExternalSessionDiscovered;

    internal CopilotLogWatcherService(string? sessionStateDir = null)
    {
        this._sessionStateDir = sessionStateDir ?? Program.SessionStateDir;
    }

    /// <summary>
    /// Creates and starts the FileSystemWatcher for new Copilot log files.
    /// </summary>
    internal void StartWatching()
    {
        if (!Directory.Exists(s_copilotLogsDir))
        {
            return;
        }

        try
        {
            this._watcher = new FileSystemWatcher(s_copilotLogsDir, "process-*.log")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            this._watcher.Created += this.OnLogFileCreated;
            this._watcher.Changed += this.OnLogFileChanged;
            this._watcher.Error += this.OnWatcherError;
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("Failed to start Copilot log watcher: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Stops the FileSystemWatcher.
    /// </summary>
    internal void StopWatching()
    {
        this._watcher?.EnableRaisingEvents = false;
    }

    public void Dispose()
    {
        if (this._watcher != null)
        {
            this._watcher.EnableRaisingEvents = false;
            this._watcher.Dispose();
            this._watcher = null;
        }

        lock (this._pendingTimers)
        {
            foreach (var timer in this._pendingTimers.Values)
            {
                timer.Dispose();
            }

            this._pendingTimers.Clear();
        }
    }

    private void OnLogFileCreated(object sender, FileSystemEventArgs e)
    {
        Program.Logger.LogDebug("Processing new Copilot log file: {Path}", e.FullPath);
        this.ScheduleDebounce(e.FullPath);
    }

    private void OnLogFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (this._cacheLock)
        {
            if (this._processedLogFiles.Contains(e.FullPath))
            {
                return;
            }
        }

        Program.Logger.LogDebug("Copilot log file changed: {Path}", e.FullPath);
        this.ScheduleDebounce(e.FullPath);
    }

    private void ScheduleDebounce(string filePath)
    {
        var timer = new Timer(_ => this.OnDebounceElapsed(filePath), null, 1000, Timeout.Infinite);

        lock (this._pendingTimers)
        {
            // Replace any existing timer for this path (resets debounce window)
            if (this._pendingTimers.Remove(filePath, out var existing))
            {
                existing.Dispose();
            }

            this._pendingTimers[filePath] = timer;
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Program.Logger.LogWarning("Copilot log watcher error: {Error}", e.GetException().Message);
    }

    private void OnDebounceElapsed(string filePath)
    {
        lock (this._pendingTimers)
        {
            if (this._pendingTimers.Remove(filePath, out var timer))
            {
                timer.Dispose();
            }
        }

        this.TryProcessLogFile(filePath, retriesLeft: 3);
    }

    private void TryProcessLogFile(string logFilePath, int retriesLeft)
    {
        try
        {
            var fileName = Path.GetFileName(logFilePath);
            var pid = ExtractPidFromFilename(fileName);
            if (pid == null)
            {
                return;
            }

            IReadOnlyList<(string sessionId, string cwd)> sessions;
            using (var fs = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                sessions = TryParseLogContent(reader);
            }

            if (sessions.Count == 0)
            {
                return;
            }

            // Bug D: Process ALL sessions in the log, not just the first
            foreach (var (sessionId, cwd) in sessions)
            {
                // Check if we've already processed this (pid, sessionId) pair
                lock (this._cacheLock)
                {
                    if (this._processedPidSessions.Contains((pid.Value, sessionId)))
                    {
                        continue;
                    }
                }

                if (!ShouldCreateWorkspace(this._sessionStateDir, sessionId))
                {
                    if (!Directory.Exists(Path.Combine(this._sessionStateDir, sessionId)))
                    {
                        // Session folder not created yet — retry after delay
                        if (retriesLeft > 0)
                        {
                            var timer = new Timer(_ => this.TryProcessLogFile(logFilePath, retriesLeft - 1), null, 2000, Timeout.Infinite);
                            lock (this._pendingTimers)
                            {
                                this._pendingTimers[$"retry:{logFilePath}:{retriesLeft}"] = timer;
                            }
                        }
                    }
                    else
                    {
                        // workspace.yaml already exists — cache this (pid, sessionId) pair
                        lock (this._cacheLock)
                        {
                            this._processedPidSessions.Add((pid.Value, sessionId));
                            this._processedLogFiles.Add(logFilePath);
                        }
                    }

                    continue;
                }

                // Session folder exists without workspace.yaml — create one
                var sessionDir = Path.Combine(this._sessionStateDir, sessionId);
                CreateWorkspaceYamlFromPid(Path.Combine(sessionDir, "workspace.yaml"), sessionId, cwd, pid.Value);
                Program.Logger.LogInformation("External Copilot session discovered: {SessionId} at {Cwd}", sessionId, cwd);
                this.ExternalSessionDiscovered?.Invoke(sessionId, pid.Value);

                lock (this._cacheLock)
                {
                    this._processedPidSessions.Add((pid.Value, sessionId));
                    this._processedLogFiles.Add(logFilePath);
                }
            }
        }
        catch (IOException) when (retriesLeft > 0)
        {
            // File may still be locked — retry
            var timer = new Timer(_ => this.TryProcessLogFile(logFilePath, retriesLeft - 1), null, 1000, Timeout.Infinite);
            lock (this._pendingTimers)
            {
                this._pendingTimers[$"io-retry:{logFilePath}:{retriesLeft}"] = timer;
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("Failed to process Copilot log file {Path}: {Error}", logFilePath, ex.Message);
        }
    }

    /// <summary>
    /// Extracts the PID from a Copilot log filename (e.g. "process-17374747448775-59288.log" → 59288).
    /// </summary>
    internal static int? ExtractPidFromFilename(string filename)
    {
        var match = s_logFileNameRegex.Match(filename);
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups[1].Value, out int pid) ? pid : null;
    }

    /// <summary>
    /// Parses log file lines looking for ALL session_id values across cli.telemetry JSON blocks
    /// and INFO patterns (Workspace initialized, Registering foreground session).
    /// Bug D fix: returns a list of ALL sessions encountered in order, not just the first.
    /// CWD fallback chain: (1) JSON context.cwd → (2) debug line cwd= → (3) fallbackCwd → (4) UserProfile.
    /// Returns list of (sessionId, cwd) tuples — cwd is process-wide and shared across all sessions.
    /// Consecutive duplicate session IDs are deduplicated.
    /// </summary>
    internal static IReadOnlyList<(string sessionId, string cwd)> TryParseLogContent(TextReader reader, string? fallbackCwd = null)
    {
        var sessions = new List<string>();
        string? cwdFromJson = null;
        string? cwdFromDebugLine = null;
        var jsonBuilder = new StringBuilder();
        bool collectingJson = false;
        int braceDepth = 0;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            // Fast path: skip lines that have no parser-relevant markers.
            // This avoids the Trim() allocation + regex matches for the ~99.9% of log lines
            // that contain none of the markers we care about. Critical for keeping allocations
            // bounded when scanning multi-hundred-MB process logs.
            if (!collectingJson
                && line.IndexOf("session_id", StringComparison.Ordinal) < 0
                && line.IndexOf("Telemetry", StringComparison.Ordinal) < 0
                && line.IndexOf("cwd=", StringComparison.Ordinal) < 0
                && line.IndexOf("Workspace initialized", StringComparison.Ordinal) < 0
                && line.IndexOf("Registering foreground session", StringComparison.Ordinal) < 0)
            {
                continue;
            }

            var trimmed = line.Trim();

            // Level 2: Look for cwd in remoteHosts debug line: "cwd=S:\repo, featureFlagEnabled=..."
            if (cwdFromDebugLine == null)
            {
                var cwdMatch = CwdRegex().Match(trimmed);
                if (cwdMatch.Success)
                {
                    cwdFromDebugLine = cwdMatch.Groups[1].Value;
                }
            }

            // Fallback: Look for session ID in deterministic INFO patterns
            var regMatch = SessionIdFromInfoRegex().Match(trimmed);
            if (regMatch.Success)
            {
                var candidate = regMatch.Groups[1].Value;
                if (IsValidSessionId(candidate))
                {
                    // Dedupe consecutive duplicates
                    if (sessions.Count == 0 || !string.Equals(sessions[sessions.Count - 1], candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        sessions.Add(candidate);
                    }
                }
            }

            // Look for telemetry header: "[INFO] [Telemetry] cli.telemetry:"
            if (!collectingJson && trimmed.Contains("[Telemetry] cli.telemetry:"))
            {
                collectingJson = true;
                jsonBuilder.Clear();
                braceDepth = 0;
                continue;
            }

            if (collectingJson)
            {
                jsonBuilder.AppendLine(trimmed);

                foreach (char c in trimmed)
                {
                    if (c == '{')
                    {
                        braceDepth++;
                    }
                    else if (c == '}')
                    {
                        braceDepth--;
                    }
                }

                if (braceDepth == 0 && jsonBuilder.Length > 0)
                {
                    collectingJson = false;
                    try
                    {
                        using var doc = JsonDocument.Parse(jsonBuilder.ToString());
                        var root = doc.RootElement;

                        // Accept ANY telemetry JSON with a non-empty session_id field
                        if (root.TryGetProperty("session_id", out var sidProp))
                        {
                            var candidate = sidProp.GetString();
                            if (!string.IsNullOrWhiteSpace(candidate) && IsValidSessionId(candidate))
                            {
                                // Dedupe consecutive duplicates
                                if (sessions.Count == 0 || !string.Equals(sessions[sessions.Count - 1], candidate, StringComparison.OrdinalIgnoreCase))
                                {
                                    sessions.Add(candidate);
                                }

                                // Level 1: Try extracting cwd from JSON context.cwd (only once)
                                if (cwdFromJson == null
                                    && root.TryGetProperty("context", out var ctx)
                                    && ctx.TryGetProperty("cwd", out var cwdProp))
                                {
                                    cwdFromJson = cwdProp.GetString();
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Not valid JSON or truncated — skip and continue scanning
                    }
                }
            }
        }

        // CWD fallback chain: JSON (1) → debug line (2) → caller-provided fallback (3) → UserProfile (4)
        var cwd = cwdFromJson
            ?? cwdFromDebugLine
            ?? fallbackCwd
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Return list of (sessionId, cwd) tuples — all sessions share the same process-wide cwd
        return sessions.Select(sid => (sid, cwd)).ToList();
    }

    /// <summary>
    /// Parses log file lines looking for ALL session_id values across cli.telemetry JSON blocks
    /// and INFO patterns (Workspace initialized, Registering foreground session).
    /// Array overload for test compatibility — wraps the streaming TextReader overload.
    /// </summary>
    internal static IReadOnlyList<(string sessionId, string cwd)> TryParseLogContent(string[] lines, string? fallbackCwd = null)
    {
        using var reader = new StringReader(string.Join('\n', lines));
        return TryParseLogContent(reader, fallbackCwd);
    }

    /// <summary>
    /// Validates that a session ID is a 36-character GUID-shaped string (lowercase hex + hyphens).
    /// </summary>
    private static bool IsValidSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length != 36)
        {
            return false;
        }

        // GUID format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
        var guidRegex = GuidValidationRegex();
        return guidRegex.IsMatch(sessionId);
    }

    /// <summary>
    /// Determines whether a workspace.yaml should be created for a given session.
    /// Returns true if the session folder exists but has no workspace.yaml.
    /// </summary>
    internal static bool ShouldCreateWorkspace(string sessionStateDir, string sessionId)
    {
        var sessionDir = Path.Combine(sessionStateDir, sessionId);
        if (!Directory.Exists(sessionDir))
        {
            return false;
        }

        return !File.Exists(Path.Combine(sessionDir, "workspace.yaml"));
    }

    /// <summary>
    /// Writes a workspace.yaml file for an externally discovered session.
    /// </summary>
    internal static void CreateWorkspaceYaml(string wsFile, string sessionId, string cwd, string sessionName)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        var lines = new List<string>
        {
            $"id: {sessionId}",
            $"cwd: {cwd}",
        };

        if (!string.IsNullOrEmpty(cwd))
        {
            var gitRoot = SessionService.FindGitRoot(cwd);
            if (gitRoot != null)
            {
                lines.Add($"git_root: {gitRoot}");
            }
        }

        lines.Add("summary_count: 0");
        lines.Add($"created_at: {now}");
        lines.Add($"updated_at: {now}");

        if (!string.IsNullOrWhiteSpace(sessionName))
        {
            lines.Add($"summary: {CopilotSessionCreatorService.YamlEscape(sessionName)}");
            lines.Add($"name: {CopilotSessionCreatorService.YamlEscape(sessionName)}");
        }

        File.WriteAllLines(wsFile, lines);
    }

    private static void CreateWorkspaceYamlFromPid(string wsFile, string sessionId, string cwd, int pid)
    {
        try
        {
            // ADR-0001: External sessions must NOT write GUID fallback to summary.
            // Only write the summary if we get a real window title (non-null).
            // If null, omit the summary field entirely — the T1 trigger will populate
            // the Booster-Resolved Name sidecar instead.
            var sessionName = GetWindowTitleByPid(pid);
            CreateWorkspaceYaml(wsFile, sessionId, cwd, sessionName ?? string.Empty);
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("Failed to create workspace.yaml for external session {SessionId}: {Error}", sessionId, ex.Message);
        }
    }

    private static string? GetWindowTitleByPid(int pid)
    {
        try
        {
            var hwnd = WindowFocusService.FindWindowHandleByPid(pid);
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            int length = GetWindowTextLength(hwnd);
            if (length <= 0)
            {
                return null;
            }

            var sb = new StringBuilder(length + 1);
            _ = GetWindowText(hwnd, sb, sb.Capacity);
            var text = sb.ToString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
#pragma warning disable SYSLIB1054
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);
#pragma warning restore SYSLIB1054

    [GeneratedRegex(@"^process-\d+-(\d+)\.log$")]
    private static partial Regex LogFileNameRegex();

    [GeneratedRegex(@"cwd=([^,]+)")]
    private static partial Regex CwdRegex();

    [GeneratedRegex(@"\[INFO\]\s+(?:Registering foreground session|Workspace initialized):\s+([0-9a-f-]{36})", RegexOptions.IgnoreCase)]
    private static partial Regex SessionIdFromInfoRegex();

    [GeneratedRegex(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase)]
    private static partial Regex GuidValidationRegex();

    /// <summary>
    /// Custom equality comparer for (pid, sessionId) tuples with case-insensitive sessionId comparison.
    /// </summary>
    private sealed class PidSessionEqualityComparer : IEqualityComparer<(int pid, string sessionId)>
    {
        public bool Equals((int pid, string sessionId) x, (int pid, string sessionId) y)
        {
            return x.pid == y.pid && string.Equals(x.sessionId, y.sessionId, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((int pid, string sessionId) obj)
        {
            return HashCode.Combine(obj.pid, obj.sessionId.ToLowerInvariant());
        }
    }
}
