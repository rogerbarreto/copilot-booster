using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Monitors Copilot CLI events.jsonl files to determine session working/idle status.
/// Fully event-driven: FileSystemWatcher fires → read last event → raise StatusChanged.
/// Uses content-based detection: parses assistant.message for toolRequests presence
/// and tool.execution_start for ask_user to reliably detect HitL (Human-in-the-Loop).
/// </summary>
internal class EventsJournalService : IDisposable
{
    private static readonly string s_copilotSessionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".copilot", "session-state");

    private static readonly string s_cacheFile = Path.Combine(Program.AppDataDir, "events-cache.json");

    private static readonly TimeSpan s_stalenessThreshold = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan s_fallbackPollInterval = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, CachedState> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();
    private FileSystemWatcher? _watcher;
    private bool _needsFallbackPoll;
    private DateTime _lastFallbackPollUtc = DateTime.MinValue;

    /// <summary>
    /// Fired on the FileSystemWatcher thread when a session's status changes.
    /// Subscribers must marshal to the UI thread.
    /// </summary>
    internal event Action<string, SessionStatus>? StatusChanged;

    /// <summary>
    /// When true, StatusChanged events are suppressed (during startup priming).
    /// </summary>
    internal bool SuppressEvents { get; set; } = true;

    internal enum SessionStatus
    {
        Unknown,
        Working,
        Idle,
        /// <summary>Idle but should not trigger a bell (e.g., user abort, mode change).</summary>
        IdleSilent
    }

    private record CachedState(DateTime LastModifiedUtc, SessionStatus Status);

    /// <summary>
    /// Starts the FileSystemWatcher. On each change, reads the last event
    /// and raises <see cref="StatusChanged"/> if the status actually changed.
    /// </summary>
    internal void StartWatching()
    {
        if (!Directory.Exists(s_copilotSessionsDir))
        {
            return;
        }

        try
        {
            this._watcher = new FileSystemWatcher(s_copilotSessionsDir, "events.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            this._watcher.Changed += this.OnFileChanged;
            this._watcher.Error += this.OnWatcherError;
        }
        catch (Exception ex)
        {
            Program.Logger.LogWarning("Failed to start FileSystemWatcher: {Error}", ex.Message);
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var dir = Path.GetDirectoryName(e.FullPath);
        if (dir == null)
        {
            return;
        }

        var sessionId = Path.GetFileName(dir);
        if (string.IsNullOrEmpty(sessionId))
        {
            return;
        }

        SessionStatus previousStatus;
        lock (this._cacheLock)
        {
            previousStatus = this._cache.TryGetValue(sessionId, out var prev) ? prev.Status : SessionStatus.Unknown;
        }

        var newStatus = this.ReadAndCacheStatus(sessionId);

        // Skip if unknown, unchanged, or suppressed
        if (newStatus == SessionStatus.Unknown || this.SuppressEvents)
        {
            return;
        }

        // Normalize for comparison: IdleSilent and Idle are both "not working"
        bool wasWorking = previousStatus == SessionStatus.Working;
        bool isWorking = newStatus == SessionStatus.Working;
        if (wasWorking != isWorking || (wasWorking && isWorking))
        {
            // Only fire on actual transitions (working↔idle)
            if (wasWorking != isWorking)
            {
                StatusChanged?.Invoke(sessionId, newStatus);
            }
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Program.Logger.LogWarning("FileSystemWatcher error: {Error}", e.GetException().Message);
        this._needsFallbackPoll = true;
    }

    /// <summary>
    /// Returns the cached status for a session (never reads disk).
    /// Used by the 3-second refresh loop to include status in snapshots.
    /// </summary>
    internal SessionStatus GetCachedStatus(string sessionId)
    {
        lock (this._cacheLock)
        {
            if (!this._cache.TryGetValue(sessionId, out var cached))
            {
                return SessionStatus.Unknown;
            }

            if (cached.Status == SessionStatus.Working
                && DateTime.UtcNow - cached.LastModifiedUtc > s_stalenessThreshold)
            {
                return SessionStatus.IdleSilent;
            }

            return cached.Status;
        }
    }

    /// <summary>
    /// Performs an initial disk read for all given session IDs to prime the cache.
    /// Call once at startup after loading the persisted cache. Does NOT raise events.
    /// </summary>
    internal void PrimeCache(IReadOnlyList<string> sessionIds)
    {
        foreach (var sid in sessionIds)
        {
            this.ReadAndCacheStatus(sid);
        }
    }

    /// <summary>
    /// Processes fallback poll on watcher error. Rate-limited to 1/30s.
    /// Call from the refresh tick.
    /// </summary>
    internal void ProcessFallbackPoll(IReadOnlyList<string> sessionIds)
    {
        if (!this._needsFallbackPoll)
        {
            return;
        }

        if (DateTime.UtcNow - this._lastFallbackPollUtc < s_fallbackPollInterval)
        {
            return;
        }

        this._needsFallbackPoll = false;
        this._lastFallbackPollUtc = DateTime.UtcNow;

        foreach (var sid in sessionIds)
        {
            SessionStatus previousStatus;
            lock (this._cacheLock)
            {
                previousStatus = this._cache.TryGetValue(sid, out var prev) ? prev.Status : SessionStatus.Unknown;
            }

            var status = this.ReadAndCacheStatus(sid);
            if (status == SessionStatus.Unknown || this.SuppressEvents)
            {
                continue;
            }

            bool wasWorking = previousStatus == SessionStatus.Working;
            bool isWorking = status == SessionStatus.Working;
            if (wasWorking != isWorking)
            {
                StatusChanged?.Invoke(sid, status);
            }
        }
    }

    private SessionStatus ReadAndCacheStatus(string sessionId)
    {
        var eventsPath = Path.Combine(s_copilotSessionsDir, sessionId, "events.jsonl");
        if (!File.Exists(eventsPath))
        {
            return SessionStatus.Unknown;
        }

        try
        {
            var lastWrite = File.GetLastWriteTimeUtc(eventsPath);
            var status = DetermineStatusFromFile(eventsPath, lastWrite);

            // Only cache definitive statuses — Unknown means the last event
            // was ambiguous (turn_end, tool.execution_complete), keep previous.
            if (status != SessionStatus.Unknown)
            {
                lock (this._cacheLock)
                {
                    this._cache[sessionId] = new CachedState(lastWrite, status);
                }
            }

            return status;
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("Failed to read events for {SessionId}: {Error}", sessionId, ex.Message);
            return SessionStatus.Unknown;
        }
    }

    /// <summary>
    /// Reads the last line of events.jsonl and determines status using content-based detection.
    /// </summary>
    private static SessionStatus DetermineStatusFromFile(string path, DateTime lastWriteUtc)
    {
        // Stale files — session hasn't been active recently, skip entirely
        if (DateTime.UtcNow - lastWriteUtc > s_stalenessThreshold)
        {
            return SessionStatus.Unknown;
        }

        string? lastLine = ReadLastLine(path);
        if (lastLine == null)
        {
            return SessionStatus.Unknown;
        }

        using var doc = JsonDocument.Parse(lastLine);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProp))
        {
            return SessionStatus.Unknown;
        }

        var eventType = typeProp.GetString();
        if (eventType == null)
        {
            return SessionStatus.Unknown;
        }

        switch (eventType)
        {
            case "assistant.turn_start":
            case "user.message":
            case "session.truncation":
                return SessionStatus.Working;

            case "assistant.message":
                // Content-based: if toolRequests present → Working, else → Idle (final answer)
                if (root.TryGetProperty("data", out var msgData)
                    && msgData.TryGetProperty("toolRequests", out var toolReqs)
                    && toolReqs.ValueKind == JsonValueKind.Array
                    && toolReqs.GetArrayLength() > 0)
                {
                    return SessionStatus.Working;
                }

                return SessionStatus.Idle;

            case "tool.execution_start":
                // ask_user → Idle (HitL), any other tool → Working
                if (root.TryGetProperty("data", out var toolData)
                    && toolData.TryGetProperty("toolName", out var toolName)
                    && string.Equals(toolName.GetString(), "ask_user", StringComparison.OrdinalIgnoreCase))
                {
                    return SessionStatus.Idle;
                }

                return SessionStatus.Working;

            case "abort":
            case "session.mode_changed":
            case "session.plan_changed":
                return SessionStatus.IdleSilent;

            // assistant.turn_end, tool.execution_complete — ambiguous mid-chain, skip
            default:
                return SessionStatus.Unknown;
        }
    }

    /// <summary>
    /// Reads the last complete line from a file using reverse seeking.
    /// </summary>
    private static string? ReadLastLine(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length == 0)
            {
                return null;
            }

            var buffer = new byte[Math.Min(8192, fs.Length)];
            var readStart = Math.Max(0, fs.Length - buffer.Length);
            fs.Seek(readStart, SeekOrigin.Begin);
            var bytesRead = fs.Read(buffer, 0, buffer.Length);

            int lastNewline = -1;
            int secondLastNewline = -1;
            for (int i = bytesRead - 1; i >= 0; i--)
            {
                if (buffer[i] == (byte)'\n')
                {
                    if (lastNewline == -1)
                    {
                        if (i == bytesRead - 1)
                        {
                            continue;
                        }

                        lastNewline = i;
                    }
                    else
                    {
                        secondLastNewline = i;
                        break;
                    }
                }
            }

            string lastLine;
            if (lastNewline >= 0)
            {
                var start = secondLastNewline >= 0 ? secondLastNewline + 1 : 0;
                lastLine = System.Text.Encoding.UTF8.GetString(buffer, start, lastNewline - start);
            }
            else
            {
                lastLine = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead).TrimEnd('\n', '\r');
            }

            return string.IsNullOrWhiteSpace(lastLine) ? null : lastLine;
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("Failed to read events.jsonl: {Error}", ex.Message);
            return null;
        }
    }

    internal void LoadCache()
    {
        try
        {
            if (!File.Exists(s_cacheFile))
            {
                return;
            }

            var entries = JsonSerializer.Deserialize<Dictionary<string, CachedState>>(File.ReadAllText(s_cacheFile));
            if (entries != null)
            {
                var now = DateTime.UtcNow;
                lock (this._cacheLock)
                {
                    foreach (var kvp in entries)
                    {
                        // Discard stale cached entries — prevents false bells on restart
                        if (now - kvp.Value.LastModifiedUtc <= s_stalenessThreshold)
                        {
                            this._cache[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("Failed to load events cache: {Error}", ex.Message);
        }
    }

    internal void SaveCache()
    {
        try
        {
            var dir = Path.GetDirectoryName(s_cacheFile);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            Dictionary<string, CachedState> snapshot;
            lock (this._cacheLock)
            {
                snapshot = new Dictionary<string, CachedState>(this._cache, StringComparer.OrdinalIgnoreCase);
            }

            File.WriteAllText(s_cacheFile, JsonSerializer.Serialize(snapshot));
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("Failed to save events cache: {Error}", ex.Message);
        }
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
