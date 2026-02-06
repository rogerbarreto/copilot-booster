using System;
using System.IO;

namespace CopilotApp.Services;

internal class LogService
{
    private readonly string _logFile;

    internal LogService(string logFile) { this._logFile = logFile; }

    internal void Log(string message) => Log(message, this._logFile);

    internal static void Log(string message, string logFile)
    {
        try
        {
            var dir = Path.GetDirectoryName(logFile)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.AppendAllText(logFile, $"[{DateTime.Now:o}] {message}\n");
        }
        catch { }
    }
}
