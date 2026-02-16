using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// A minimal <see cref="ILogger"/> that writes timestamped messages to a file.
/// </summary>
internal sealed class FileLogger : ILogger
{
    private readonly string _logFile;
    private readonly object _lock = new();

    internal FileLogger(string logFile, LogLevel minLevel = LogLevel.Information)
    {
        this._logFile = logFile;
        this.MinLevel = minLevel;

        var dir = Path.GetDirectoryName(logFile)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>
    /// Changes the minimum log level at runtime (e.g. to enable debug/profiling).
    /// </summary>
    internal LogLevel MinLevel { get; init; }

    public bool IsEnabled(LogLevel logLevel) => logLevel >= this.MinLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!this.IsEnabled(logLevel))
        {
            return;
        }

        var levelTag = logLevel switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };

        var message = formatter(state, exception);
        var line = $"[{DateTime.UtcNow:o}] [{levelTag}] {message}";
        if (exception != null)
        {
            line += $"\n{exception}";
        }

        try
        {
            lock (this._lock)
            {
                File.AppendAllText(this._logFile, line + "\n");
            }
        }
        catch { } // Logger cannot log its own write failure
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
