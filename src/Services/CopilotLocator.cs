using System;
using System.Diagnostics;
using System.IO;

namespace CopilotApp.Services;

class CopilotLocator
{
    internal static string FindCopilotExe() => FindCopilotExe(null);

    internal static string FindCopilotExe(string[]? candidatePaths)
    {
        candidatePaths ??= new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WinGet\Packages\GitHub.Copilot.Prerelease_Microsoft.Winget.Source_8wekyb3d8bbwe\copilot.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WinGet\Packages\GitHub.Copilot_Microsoft.Winget.Source_8wekyb3d8bbwe\copilot.exe"),
        };

        foreach (var path in candidatePaths)
            if (File.Exists(path)) return path;

        // Fallback: try to find copilot in PATH
        try
        {
            var psi = new ProcessStartInfo("where", "copilot")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd().Trim();
            proc?.WaitForExit();
            if (!string.IsNullOrEmpty(output) && File.Exists(output.Split('\n')[0].Trim()))
                return output.Split('\n')[0].Trim();
        }
        catch { }

        return "copilot.exe";
    }
}
