using System;
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

    private static readonly TimeSpan s_stalenessThreshold = TimeSpan.FromMinutes(30);

    private readonly string _sessionsDir;
    private FileSystemWatcher? _watcher;

    /// <summary>
    /// Fired on the FileSystemWatcher thread when a session's status changes.
    /// Subscribers must marshal to the UI thread.
    /// </summary>
    internal event Action<string, SessionStatus>? StatusChanged;

    /// <summary>
    /// Fired on the FileSystemWatcher thread when a Booster-Resolved Name
    /// is successfully updated from the first user.message in events.jsonl.
    /// Subscribers must marshal to the UI thread.
    /// </summary>
    internal event Action<string>? BoosterResolvedNameUpdated;

    internal enum SessionStatus
    {
        Unknown,
        Working,
        Idle,
        /// <summary>Idle but should not trigger a bell (e.g., user abort, mode change).</summary>
        IdleSilent
    }

    internal EventsJournalService()
        : this(s_copilotSessionsDir)
    {
    }

    internal EventsJournalService(string sessionsDir)
    {
        this._sessionsDir = sessionsDir;
    }

    /// <summary>
    /// Starts the FileSystemWatcher. On each change, reads the last event
    /// and raises <see cref="StatusChanged"/> if the status actually changed.
    /// </summary>
    internal void StartWatching()
    {
        if (!Directory.Exists(this._sessionsDir))
        {
            return;
        }

        try
        {
            this._watcher = new FileSystemWatcher(this._sessionsDir, "events.jsonl")
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

        // Simply fire StatusChanged - MainForm will refresh from disk
        StatusChanged?.Invoke(sessionId, SessionStatus.Unknown);

        // Deferred Booster-Resolved Name resolution: if the current override
        // is unresolved (ResolvedFromUserMessage == false), attempt to extract
        // the first user.message and update the sidecar.
        this.TryResolveBoosterName(sessionId, e.FullPath);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Program.Logger.LogWarning("FileSystemWatcher error: {Error}", e.GetException().Message);
    }

    /// <summary>
    /// Tail-reads events.jsonl to extract the latest CWD.
    /// Scans backwards from EOF to find the last hook.start CWD, falls back to session.start.
    /// Performance budget: ≤500KB allocations, ≤100ms for 8MB file.
    /// </summary>
    internal static string ExtractLatestCwdFromTail(string eventsJsonlPath)
    {
        if (!File.Exists(eventsJsonlPath))
        {
            return string.Empty;
        }

        try
        {
            using var fs = new FileStream(eventsJsonlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length == 0)
            {
                return string.Empty;
            }

            // Read last 64KB chunk (covers ~640 lines at ~100 bytes/line)
            const int tailSize = 65536;
            var bufferSize = (int)Math.Min(tailSize, fs.Length);
            var buffer = new byte[bufferSize];
            fs.Seek(-bufferSize, SeekOrigin.End);
            var bytesRead = fs.Read(buffer, 0, bufferSize);

            // Scan backwards for complete lines, finding hook.start or session.start
            // Pre-filter by type to minimize string allocations
            string? hookCwd = null;
            string? sessionCwd = null;
            int lineEnd = bytesRead;
            var typeMarkerHook = System.Text.Encoding.UTF8.GetBytes("\"type\":\"hook.start\"");
            var typeMarkerSession = System.Text.Encoding.UTF8.GetBytes("\"type\":\"session.start\"");
            
            for (int i = bytesRead - 1; i >= 0; i--)
            {
                if (buffer[i] == (byte)'\n' || i == 0)
                {
                    int lineStart = (i == 0) ? 0 : i + 1;
                    int lineLength = lineEnd - lineStart;
                    if (lineLength > 0)
                    {
                        // Check if line contains hook.start or session.start before allocating string
                        var lineSpan = buffer.AsSpan(lineStart, lineLength);
                        bool isHookStart = lineSpan.IndexOf(typeMarkerHook) >= 0;
                        bool isSessionStart = !isHookStart && lineSpan.IndexOf(typeMarkerSession) >= 0;
                        
                        if (isHookStart || isSessionStart)
                        {
                            var line = System.Text.Encoding.UTF8.GetString(lineSpan).Trim();
                            // Strip UTF-8 BOM if present (test files may include it)
                            if (line.Length > 0 && line[0] == '\uFEFF')
                            {
                                line = line.Substring(1);
                            }
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                try
                                {
                                    using var doc = JsonDocument.Parse(line);
                                    var root = doc.RootElement;
                                    
                                    if (isHookStart && hookCwd == null)
                                    {
                                        hookCwd = TryGetHookStartCwd(root);
                                        if (!string.IsNullOrEmpty(hookCwd))
                                        {
                                            return hookCwd; // Found latest hook.start, return immediately
                                        }
                                    }
                                    else if (isSessionStart && sessionCwd == null)
                                    {
                                        sessionCwd = TryGetSessionStartCwd(root);
                                    }
                                }
                                catch (JsonException)
                                {
                                    // Skip malformed/partial lines
                                }
                            }
                        }
                    }
                    lineEnd = i;
                }
            }

            return hookCwd ?? sessionCwd ?? string.Empty;
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("Failed to tail-read events.jsonl: {Error}", ex.Message);
            return string.Empty;
        }
    }

    /// <summary>
    /// Parses a TextReader sequentially to extract the latest CWD (for tests).
    /// </summary>
    internal static string? ExtractLatestCwd(TextReader reader)
    {
        string? sessionStartCwd = null;
        string? latestHookCwd = null;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp))
                {
                    continue;
                }

                var eventType = typeProp.GetString();
                if (string.Equals(eventType, "session.start", StringComparison.Ordinal))
                {
                    var cwd = TryGetSessionStartCwd(root);
                    if (!string.IsNullOrWhiteSpace(cwd))
                    {
                        sessionStartCwd = cwd;
                    }
                }
                else if (string.Equals(eventType, "hook.start", StringComparison.Ordinal))
                {
                    var cwd = TryGetHookStartCwd(root);
                    if (!string.IsNullOrWhiteSpace(cwd))
                    {
                        latestHookCwd = cwd;
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return latestHookCwd ?? sessionStartCwd;
    }

    private static string? TryGetSessionStartCwd(JsonElement root)
    {
        if (root.TryGetProperty("data", out var data)
            && data.TryGetProperty("context", out var context)
            && context.TryGetProperty("cwd", out var cwd))
        {
            return cwd.GetString();
        }

        return null;
    }

    private static string? TryGetHookStartCwd(JsonElement root)
    {
        if (root.TryGetProperty("data", out var data)
            && data.TryGetProperty("input", out var input)
            && input.TryGetProperty("cwd", out var cwd))
        {
            return cwd.GetString();
        }

        return null;
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

    /// <summary>
    /// Attempts to resolve the Booster-Resolved Name for a session if it's currently unresolved.
    /// Reads the first user.message from events.jsonl, formats it, and updates the sidecar.
    /// Only operates when the existing override has ResolvedFromUserMessage == false.
    /// </summary>
    private void TryResolveBoosterName(string sessionId, string eventsJsonlPath)
    {
        try
        {
            // Check if the current override is unresolved
            var currentOverride = SessionNameOverrideService.Get(Program.SessionNameOverrideFile, sessionId);
            if (currentOverride != null && currentOverride.ResolvedFromUserMessage)
            {
                // Already resolved — no work to do
                return;
            }

            // If currentOverride is null or has ResolvedFromUserMessage == false, attempt resolution

            // Extract the first user.message content
            var rawContent = FirstUserMessageExtractor.Extract(eventsJsonlPath);
            if (rawContent == null)
            {
                // No user.message found yet — try again on next event
                return;
            }

            // Format the content (32-char truncation, whitespace collapse)
            var formattedName = BoosterResolvedNameFormatter.Format(rawContent);
            if (string.IsNullOrWhiteSpace(formattedName))
            {
                // Formatted content is empty — keep placeholder
                return;
            }

            // Update the sidecar with the resolved name
            SessionNameOverrideService.Set(
                Program.SessionNameOverrideFile,
                sessionId,
                formattedName,
                resolvedFromUserMessage: true);

            // Raise event so MainForm can refresh the session list
            BoosterResolvedNameUpdated?.Invoke(sessionId);
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("Failed to resolve Booster name for {SessionId}: {Error}", sessionId, ex.Message);
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
