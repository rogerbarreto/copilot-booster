using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Launches terminal windows in a specified working directory with trackable titles.
/// </summary>
internal static class TerminalLauncherService
{
    /// <summary>
    /// Launches a terminal in the given working directory with a custom title.
    /// Tries Windows Terminal first, then PowerShell 7, then cmd.exe.
    /// </summary>
    /// <param name="workDir">The working directory to open the terminal in.</param>
    /// <param name="sessionId">Session ID used to create a trackable window title.</param>
    /// <returns>The launched Process, or null if launch failed.</returns>
    internal static Process? LaunchTerminal(string workDir, string sessionId)
    {
        var terminal = DetectTerminal();

        // Determine the next instance number for this session
        var existing = WindowFocusService.FindTrackedWindows();
        int instance = 0;
        if (existing.TryGetValue(sessionId, out var list))
        {
            instance = list.Count(t => t.Label.StartsWith("Terminal", StringComparison.OrdinalIgnoreCase));
        }
        instance++;
        var title = instance == 1
            ? $"Terminal - {sessionId}"
            : $"Terminal #{instance} - {sessionId}";

        ProcessStartInfo psi = terminal switch
        {
            "wt" => new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"--title \"{title}\" --suppressApplicationTitle -d \"{workDir}\"",
                UseShellExecute = true
            },
            "pwsh" => new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                Arguments = $"-NoExit -Command \"$Host.UI.RawUI.WindowTitle = '{title}'\"",
                WorkingDirectory = workDir,
                UseShellExecute = true
            },
            _ => new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/K title {title}",
                WorkingDirectory = workDir,
                UseShellExecute = true
            }
        };

        try
        {
            return Process.Start(psi);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Launches a terminal in the given working directory without session tracking.
    /// Tries Windows Terminal first, then PowerShell 7, then cmd.exe.
    /// </summary>
    /// <param name="workDir">The working directory to open the terminal in.</param>
    /// <returns>The launched Process, or null if launch failed.</returns>
    internal static Process? LaunchTerminalSimple(string workDir)
    {
        var terminal = DetectTerminal();

        ProcessStartInfo psi = terminal switch
        {
            "wt" => new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"-d \"{workDir}\"",
                UseShellExecute = true
            },
            "pwsh" => new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                Arguments = "-NoExit",
                WorkingDirectory = workDir,
                UseShellExecute = true
            },
            _ => new ProcessStartInfo
            {
                FileName = "cmd.exe",
                WorkingDirectory = workDir,
                UseShellExecute = true
            }
        };

        try
        {
            return Process.Start(psi);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the best available terminal executable.
    /// </summary>
    /// <returns>The terminal type found: "wt", "pwsh", or "cmd".</returns>
    internal static string DetectTerminal()
    {
        // Check Windows Terminal
        var wtPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "wt.exe");
        if (File.Exists(wtPath))
        {
            return "wt";
        }

        // Check PowerShell 7
        try
        {
            var psi = new ProcessStartInfo("pwsh.exe", "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(2000);
            if (proc?.ExitCode == 0)
            {
                return "pwsh";
            }
        }
        catch (Exception ex) { Program.Logger.LogDebug("Failed to detect pwsh: {Error}", ex.Message); }

        return "cmd";
    }
}
