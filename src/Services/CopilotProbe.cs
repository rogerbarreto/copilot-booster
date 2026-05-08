using System;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

internal interface ICopilotProbe
{
    bool IsCopilotAvailable();

    void InvalidateCache();
}

internal sealed class CopilotProbe : ICopilotProbe
{
    private readonly Func<string> _pathGetter;
    private readonly object _lock = new();
    private bool? _cachedResult;
    private string? _cachedForPath;

    internal CopilotProbe(Func<string> pathGetter)
    {
        this._pathGetter = pathGetter;
    }

    public bool IsCopilotAvailable()
    {
        var resolvedPath = this.ResolvePath();
        lock (this._lock)
        {
            if (this._cachedResult.HasValue && string.Equals(this._cachedForPath, resolvedPath, StringComparison.Ordinal))
            {
                return this._cachedResult.Value;
            }

            var result = ProbeVersion(resolvedPath);
            this._cachedForPath = resolvedPath;
            this._cachedResult = result;
            Program.Logger.LogInformation("Copilot probe ran path={CopilotPath} available={Available}", resolvedPath, result);
            return result;
        }
    }

    public void InvalidateCache()
    {
        lock (this._lock)
        {
            this._cachedForPath = null;
            this._cachedResult = null;
        }
    }

    private static bool ProbeVersion(string resolvedPath)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = resolvedPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add("--version");

            if (!process.Start())
            {
                return false;
            }

            if (!process.WaitForExit(5_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private string ResolvePath()
    {
        var configuredPath = this._pathGetter();
        return string.IsNullOrWhiteSpace(configuredPath) ? "copilot" : configuredPath.Trim();
    }
}
