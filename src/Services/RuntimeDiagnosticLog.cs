using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace CopilotBooster.Services;

internal static class RuntimeDiagnosticLog
{
    private const long MaxFileSizeBytes = 256 * 1024;
    private const long TrimToBytes = 128 * 1024;

    private static readonly object s_lock = new();
    private static int s_writesSinceTrim;
    private static bool s_initialized;

    internal static string LogFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CopilotBooster",
        "logs",
        "diag.log");

    internal static void Write(string message)
    {
        try
        {
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"[{DateTime.UtcNow:o}] [tid={Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");

            lock (s_lock)
            {
                EnsureInitialized();
                File.AppendAllText(LogFile, line, Encoding.UTF8);

                // Cheap amortized trim: only stat + maybe-trim every N writes.
                if (++s_writesSinceTrim >= 256)
                {
                    s_writesSinceTrim = 0;
                    TrimIfOversized();
                }
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

    private static void EnsureInitialized()
    {
        if (s_initialized)
        {
            return;
        }

        var directory = Path.GetDirectoryName(LogFile);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        TrimIfOversized();
        s_initialized = true;
    }

    private static void TrimIfOversized()
    {
        try
        {
            if (!File.Exists(LogFile))
            {
                return;
            }

            var info = new FileInfo(LogFile);
            if (info.Length <= MaxFileSizeBytes)
            {
                return;
            }

            // Read the tail (TrimToBytes) and rewrite. Aligned to a newline.
            using var fs = new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            var startOffset = Math.Max(0, fs.Length - TrimToBytes);
            fs.Seek(startOffset, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            // Drop possibly-partial first line.
            if (startOffset > 0)
            {
                _ = reader.ReadLine();
            }

            var tail = reader.ReadToEnd();
            File.WriteAllText(LogFile, tail, Encoding.UTF8);
        }
        catch
        {
        }
    }
}

