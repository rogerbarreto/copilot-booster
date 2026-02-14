using System.Diagnostics;
using System.Text;

namespace CopilotBooster.E2E;

/// <summary>
/// Launches copilot CLI with -p (non-interactive mode) and captures clean output.
/// Uses --resume to target a specific pre-created session.
/// </summary>
public sealed class CopilotCliHarness : IDisposable
{
    public string SessionId { get; }
    public string SessionDir { get; }

    private bool _disposed;

    private CopilotCliHarness(string sessionId, string sessionDir)
    {
        SessionId = sessionId;
        SessionDir = sessionDir;
    }

    /// <summary>
    /// Creates a session directory with workspace.yaml ready for copilot --resume.
    /// </summary>
    public static CopilotCliHarness CreateSession(
        string workingDirectory,
        string? sessionName = null,
        string? sourceWorkspaceYaml = null)
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionStateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot", "session-state");
        var sessionDir = Path.Combine(sessionStateDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        var wsFile = Path.Combine(sessionDir, "workspace.yaml");
        if (sourceWorkspaceYaml != null && File.Exists(sourceWorkspaceYaml))
        {
            var lines = File.ReadAllLines(sourceWorkspaceYaml);
            var updatedLines = new List<string>();
            foreach (var line in lines)
            {
                if (line.StartsWith("id:"))
                {
                    updatedLines.Add($"id: {sessionId}");
                }
                else if (line.StartsWith("cwd:"))
                {
                    updatedLines.Add($"cwd: {workingDirectory}");
                }
                else if (line.StartsWith("summary:"))
                {
                    updatedLines.Add($"summary: {sessionName ?? ""}");
                }
                else if (line.StartsWith("created_at:") || line.StartsWith("updated_at:"))
                {
                    updatedLines.Add($"{line.Split(':')[0]}: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}");
                }
                else if (line.StartsWith("summary_count:"))
                {
                    updatedLines.Add("summary_count: 0");
                }
                else
                {
                    updatedLines.Add(line);
                }
            }
            File.WriteAllLines(wsFile, updatedLines);
        }
        else
        {
            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var yamlLines = new List<string>
            {
                $"id: {sessionId}",
                $"cwd: {workingDirectory}",
                "summary_count: 0",
                $"created_at: {now}",
                $"updated_at: {now}",
            };
            if (!string.IsNullOrWhiteSpace(sessionName))
            {
                yamlLines.Add($"summary: {sessionName}");
            }
            File.WriteAllLines(wsFile, yamlLines);
        }

        return new CopilotCliHarness(sessionId, sessionDir);
    }

    /// <summary>
    /// Reads the workspace.yaml content from the session directory.
    /// Returns null if the file doesn't exist (copilot may delete it after loading).
    /// </summary>
    public string? ReadWorkspaceYaml()
    {
        var wsFile = Path.Combine(SessionDir, "workspace.yaml");
        return File.Exists(wsFile) ? File.ReadAllText(wsFile) : null;
    }

    /// <summary>
    /// Runs copilot --resume with -p (non-interactive) and a given prompt.
    /// Returns the combined stdout+stderr output.
    /// </summary>
    public (string Output, int ExitCode) RunPrompt(string prompt, string workingDirectory, int timeoutMs = 120_000)
    {
        var copilotExe = CopilotBooster.Services.CopilotLocator.FindCopilotExe()
            ?? throw new InvalidOperationException("Copilot CLI not found.");

        var psi = new ProcessStartInfo
        {
            FileName = copilotExe,
            Arguments = $"--resume {SessionId} -p \"{prompt}\"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start copilot process.");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) { stdout.AppendLine(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) { stderr.AppendLine(e.Data); } };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(timeoutMs))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"Copilot CLI did not exit within {timeoutMs}ms.\nStdout: {stdout}\nStderr: {stderr}");
        }

        var combined = stdout.ToString() + stderr.ToString();
        return (combined, process.ExitCode);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (Directory.Exists(SessionDir))
            {
                Directory.Delete(SessionDir, recursive: true);
            }
        }
        catch { }
    }
}
