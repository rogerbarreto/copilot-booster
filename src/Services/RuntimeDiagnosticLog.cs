using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CopilotBooster.Services;

internal static class RuntimeDiagnosticLog
{
    private const int MaxLines = 1000;
    private static readonly object s_lock = new();

    internal static string LogFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CopilotBooster",
        "logs",
        "diag.log");

    internal static void Write(string message)
    {
        try
        {
            lock (s_lock)
            {
                var directory = Path.GetDirectoryName(LogFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var line = $"[{DateTime.UtcNow:o}] {message}";
                File.AppendAllText(LogFile, line + Environment.NewLine);
                TrimIfNeeded();
            }
        }
        catch
        {
        }
    }

    internal static void Write(string format, params object?[] args)
    {
        Write(string.Format(CultureInfo.InvariantCulture, format, args));
    }

    private static void TrimIfNeeded()
    {
        if (!File.Exists(LogFile))
        {
            return;
        }

        var lines = File.ReadAllLines(LogFile);
        if (lines.Length <= MaxLines)
        {
            return;
        }

        File.WriteAllLines(LogFile, lines.Skip(lines.Length - MaxLines));
    }
}

