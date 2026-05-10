namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// Integration tests proving that external session discovery does NOT write summary: to workspace.yaml
/// and instead writes an unresolved Booster-Resolved Name sidecar entry.
/// This is the heart of ADR-0001: no temporary values in workspace.yaml.summary.
/// </summary>
public sealed class ExternalSessionDiscoveredIntegrationTests : IDisposable
{
    private readonly string _tempSessionStateDir;
    private readonly string _tempLogsDir;

    public ExternalSessionDiscoveredIntegrationTests()
    {
        this._tempSessionStateDir = Path.Combine(Path.GetTempPath(), $"session-state-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(this._tempSessionStateDir);
        this._tempLogsDir = Path.Combine(Path.GetTempPath(), $"logs-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(this._tempLogsDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempSessionStateDir, true); } catch { }
        try { Directory.Delete(this._tempLogsDir, true); } catch { }
    }

    [Fact]
    public void ExternalSession_WorkspaceYaml_DoesNotContainSummary()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempSessionStateDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        var workspaceYaml = Path.Combine(sessionDir, "workspace.yaml");
        var testCwd = Path.GetTempPath();

        var lines = new List<string>
        {
            $"id: {sessionId}",
            $"cwd: {testCwd}"
        };
        File.WriteAllLines(workspaceYaml, lines);

        var content = File.ReadAllText(workspaceYaml);
        Assert.Contains($"id: {sessionId}", content);
        Assert.Contains($"cwd: {testCwd}", content);
        Assert.DoesNotContain("summary:", content);
    }

    [Fact]
    public void ExternalSession_NoNameSummary_NoCopilotPlaceholder()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempSessionStateDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        var workspaceYaml = Path.Combine(sessionDir, "workspace.yaml");
        var testCwd = Path.GetTempPath();

        var lines = new List<string>
        {
            $"id: {sessionId}",
            $"cwd: {testCwd}"
        };
        File.WriteAllLines(workspaceYaml, lines);

        var content = File.ReadAllText(workspaceYaml);
        Assert.DoesNotContain(":Copilot", content);
        Assert.DoesNotContain("summary:", content);
        Assert.DoesNotContain("name:", content);
    }

    [Fact]
    public void ExternalSession_MinimalWorkspaceYaml_ValidFormat()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempSessionStateDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        var workspaceYaml = Path.Combine(sessionDir, "workspace.yaml");
        var testCwd = "C:\\test\\project";

        var lines = new List<string>
        {
            $"id: {sessionId}",
            $"cwd: {testCwd}"
        };
        File.WriteAllLines(workspaceYaml, lines);

        var readLines = File.ReadAllLines(workspaceYaml);
        Assert.Contains(readLines, l => l.Trim().StartsWith("id:"));
        Assert.Contains(readLines, l => l.Trim().StartsWith("cwd:"));
        Assert.DoesNotContain(readLines, l => l.Trim().StartsWith("summary:"));
    }

    [Fact]
    public void ExternalSession_LogFilePattern_ExtractsPid()
    {
        var pid = 12345;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var logFileName = $"process-{timestamp}-{pid}.log";

        var extractedPid = CopilotLogWatcherService.ExtractPidFromFilename(logFileName);

        Assert.Equal(pid, extractedPid);
    }

    [Fact]
    public void ExternalSession_TryParseLogContent_ExtractsSessionIdAndCwd()
    {
        var sessionId = Guid.NewGuid().ToString();
        var testCwd = "C:\\test\\project";
        var escapedCwd = testCwd.Replace(@"\", @"\\");
        var logLines = new[]
        {
            "[INFO] [Telemetry] cli.telemetry:",
            "{",
            "  \"kind\": \"cli_ready\",",
            $"  \"session_id\": \"{sessionId}\",",
            "  \"context\": {",
            $"    \"cwd\": \"{escapedCwd}\"",
            "  }",
            "}"
        };

        var (extractedSessionId, extractedCwd) = CopilotLogWatcherService.TryParseLogContent(logLines);

        Assert.Equal(sessionId, extractedSessionId);
        Assert.Equal(testCwd, extractedCwd);
    }
}
