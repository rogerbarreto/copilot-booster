/// <summary>
/// Tests for CopilotLogWatcherService — external Copilot session discovery.
///
/// These tests target static/internal helper methods that will be made testable
/// via InternalsVisibleTo. The service watches ~/.copilot/logs/ for new log files
/// and auto-creates workspace.yaml for sessions started outside Copilot Booster.
///
/// Method mapping (spec → implementation):
///   ExtractPidFromFilename → extracts PID from "process-{timestamp}-{pid}.log" regex
///   TryParseLogContent     → parses multi-line log for cli.telemetry session_start JSON + cwd
///   ShouldCreateWorkspace  → checks session dir exists without workspace.yaml
///   CreateWorkspaceYaml    → writes workspace.yaml with id, cwd, summary, timestamps
/// </summary>
public sealed class CopilotLogWatcherServiceTests : IDisposable
{
    private readonly string _tempDir;

    private const string RealisticLogContent = """
        2026-03-17T12:58:44.775Z [DEBUG] Sending telemetry event: copilot-cli/extension.activate
        2026-03-17T12:58:45.264Z [INFO] [Telemetry] cli.telemetry:
        {
          "kind": "session_start",
          "properties": {
            "event_id": "ff872d68-d23f-41e7-9c79-bde795e3fb6a",
            "producer": "copilot-agent",
            "copilot_version": "1.0.6",
            "copilot_pid": "65712"
          },
          "session_id": "63fca5dd-0a5a-4b1a-a311-dc49f5fa65eb"
        }
        2026-03-17T12:58:45.265Z [DEBUG] Sending telemetry event: copilot-cli/cli.telemetry (kind: session_start)
        2026-03-17T12:58:45.265Z [DEBUG] [remoteHosts] Starting remote host detection for cwd=S:\repo, featureFlagEnabled=true
        """;

    public CopilotLogWatcherServiceTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    // ── PID Extraction from Filename ──────────────────────────────────

    [Theory]
    [InlineData("process-17374747448775-59288.log", 59288)]
    [InlineData("process-12345-1234.log", 1234)]
    [InlineData("process-99999999999-0.log", 0)]
    public void ExtractPidFromFilename_ReturnsCorrectPid(string filename, int expectedPid)
    {
        var result = CopilotLogWatcherService.ExtractPidFromFilename(filename);

        Assert.NotNull(result);
        Assert.Equal(expectedPid, result.Value);
    }

    [Theory]
    [InlineData("random-file.log")]
    [InlineData("process-.log")]
    [InlineData("not-a-log.txt")]
    public void ExtractPidFromFilename_ReturnsNull_ForInvalidFilename(string filename)
    {
        var result = CopilotLogWatcherService.ExtractPidFromFilename(filename);

        Assert.Null(result);
    }

    // ── Log Content Parsing ───────────────────────────────────────────

    [Fact]
    public void TryParseLogContent_ExtractsSessionIdAndCwd_FromRealisticLog()
    {
        var lines = RealisticLogContent.Split('\n');

        var (sessionId, cwd) = CopilotLogWatcherService.TryParseLogContent(lines);

        Assert.Equal("63fca5dd-0a5a-4b1a-a311-dc49f5fa65eb", sessionId);
        Assert.Equal(@"S:\repo", cwd);
    }

    [Fact]
    public void TryParseLogContent_ExtractsSessionId_WithoutCwd()
    {
        var logContent = """
            2026-03-17T12:58:45.264Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "session_start",
              "session_id": "aaaa-bbbb-cccc-dddd"
            }
            2026-03-17T12:58:45.265Z [DEBUG] Some other line without cwd info
            """;
        var lines = logContent.Split('\n');

        var (sessionId, cwd) = CopilotLogWatcherService.TryParseLogContent(lines);

        Assert.Equal("aaaa-bbbb-cccc-dddd", sessionId);
        // No cwd in JSON or debug lines → falls back to UserProfile
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), cwd);
    }

    [Fact]
    public void TryParseLogContent_PrefersCwdFromJson_OverDebugLine()
    {
        // JSON block has context.cwd AND debug line has cwd= — JSON should win
        var logContent = """
            2026-03-17T12:58:45.264Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "session_start",
              "session_id": "aaaa-bbbb-cccc-dddd",
              "context": {
                "cwd": "C:\\FromJson"
              }
            }
            2026-03-17T12:58:45.265Z [DEBUG] [remoteHosts] Starting remote host detection for cwd=D:\FromDebugLine, featureFlagEnabled=true
            """;
        var lines = logContent.Split('\n');

        var (sessionId, cwd) = CopilotLogWatcherService.TryParseLogContent(lines);

        Assert.Equal("aaaa-bbbb-cccc-dddd", sessionId);
        Assert.Equal(@"C:\FromJson", cwd);
    }

    [Fact]
    public void TryParseLogContent_UsesFallbackCwd_WhenNoCwdInLogOrJson()
    {
        var logContent = """
            2026-03-17T12:58:45.264Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "session_start",
              "session_id": "aaaa-bbbb-cccc-dddd"
            }
            """;
        var lines = logContent.Split('\n');

        var (sessionId, cwd) = CopilotLogWatcherService.TryParseLogContent(lines, @"E:\MyFallback");

        Assert.Equal("aaaa-bbbb-cccc-dddd", sessionId);
        Assert.Equal(@"E:\MyFallback", cwd);
    }

    [Fact]
    public void TryParseLogContent_UsesUserProfileFallback_WhenNoCwdAnywhere()
    {
        // No cwd in JSON, no debug line, no explicit fallback → UserProfile
        var logContent = """
            2026-03-17T12:58:45.264Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "session_start",
              "session_id": "aaaa-bbbb-cccc-dddd"
            }
            """;
        var lines = logContent.Split('\n');

        var (sessionId, cwd) = CopilotLogWatcherService.TryParseLogContent(lines);

        Assert.Equal("aaaa-bbbb-cccc-dddd", sessionId);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), cwd);
    }

    [Fact]
    public void TryParseLogContent_FallbackCwd_DoesNotOverrideDebugLineCwd()
    {
        // Debug line has cwd= AND fallback provided — debug line wins (level 2 > level 3)
        var logContent = """
            2026-03-17T12:58:45.264Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "session_start",
              "session_id": "aaaa-bbbb-cccc-dddd"
            }
            2026-03-17T12:58:45.265Z [DEBUG] [remoteHosts] Starting remote host detection for cwd=D:\FromDebugLine, featureFlagEnabled=true
            """;
        var lines = logContent.Split('\n');

        var (sessionId, cwd) = CopilotLogWatcherService.TryParseLogContent(lines, @"E:\MyFallback");

        Assert.Equal("aaaa-bbbb-cccc-dddd", sessionId);
        Assert.Equal(@"D:\FromDebugLine", cwd);
    }

    [Fact]
    public void TryParseLogContent_ReturnsNullSessionId_WhenNoTelemetryBlock()
    {
        var logContent = """
            2026-03-17T12:58:44.775Z [DEBUG] Sending telemetry event: copilot-cli/extension.activate
            2026-03-17T12:58:45.265Z [DEBUG] Some other debug line without session data
            """;
        var lines = logContent.Split('\n');

        var (sessionId, cwd) = CopilotLogWatcherService.TryParseLogContent(lines);

        Assert.Null(sessionId);
        // CWD always falls back to UserProfile when not found
        Assert.False(string.IsNullOrEmpty(cwd));
    }

    [Fact]
    public void TryParseLogContent_ReturnsNullSessionId_WhenKindIsNotSessionStart()
    {
        var logContent = """
            2026-03-17T12:58:45.264Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "session_end",
              "session_id": "aaaa-bbbb-cccc-dddd"
            }
            """;
        var lines = logContent.Split('\n');

        var (sessionId, cwd) = CopilotLogWatcherService.TryParseLogContent(lines);

        Assert.Null(sessionId);
        Assert.False(string.IsNullOrEmpty(cwd));
    }

    [Fact]
    public void TryParseLogContent_ReturnsNullSessionId_ForEmptyInput()
    {
        var (sessionId, cwd) = CopilotLogWatcherService.TryParseLogContent([]);

        Assert.Null(sessionId);
        Assert.False(string.IsNullOrEmpty(cwd));
    }

    [Fact]
    public void TryParseLogContent_ReturnsNullSessionId_ForMalformedJson()
    {
        var logContent = """
            2026-03-17T12:58:45.264Z [INFO] [Telemetry] cli.telemetry:
            { not valid json at all {{{
            }
            """;
        var lines = logContent.Split('\n');

        var (sessionId, cwd) = CopilotLogWatcherService.TryParseLogContent(lines);

        Assert.Null(sessionId);
        Assert.False(string.IsNullOrEmpty(cwd));
    }

    // ── Integration: Full Flow — Parse Log + Create workspace.yaml ───

    [Fact]
    public void Integration_ParsesLogAndCreatesWorkspaceYaml()
    {
        // Arrange — simulate ~/.copilot/logs/ with a realistic log file
        var logsDir = Path.Combine(this._tempDir, "logs");
        Directory.CreateDirectory(logsDir);
        var logFile = Path.Combine(logsDir, "process-17374747448775-65712.log");
        File.WriteAllText(logFile, RealisticLogContent);

        // Act — parse the log file
        var lines = File.ReadAllLines(logFile);
        var (sessionId, cwd) = CopilotLogWatcherService.TryParseLogContent(lines);

        // Assert — parser found session_id and cwd
        Assert.Equal("63fca5dd-0a5a-4b1a-a311-dc49f5fa65eb", sessionId);
        Assert.Equal(@"S:\repo", cwd);

        // Arrange — create session folder WITHOUT workspace.yaml
        var sessionStateDir = Path.Combine(this._tempDir, "session-state");
        var sessionDir = Path.Combine(sessionStateDir, sessionId!);
        Directory.CreateDirectory(sessionDir);

        // Assert — should create workspace
        Assert.True(CopilotLogWatcherService.ShouldCreateWorkspace(sessionStateDir, sessionId!));

        // Act — create workspace.yaml
        var wsFile = Path.Combine(sessionDir, "workspace.yaml");
        CopilotLogWatcherService.CreateWorkspaceYaml(wsFile, sessionId!, cwd, sessionId!);

        // Assert — workspace.yaml was created with correct fields
        Assert.True(File.Exists(wsFile));
        var content = File.ReadAllText(wsFile);
        Assert.Contains($"id: {sessionId}", content);
        Assert.Contains(@"cwd: S:\repo", content);
        Assert.Contains("created_at:", content);
        Assert.Contains("updated_at:", content);

        // Assert — ShouldCreateWorkspace now returns false (workspace.yaml exists)
        Assert.False(CopilotLogWatcherService.ShouldCreateWorkspace(sessionStateDir, sessionId!));
    }

    [Fact]
    public void Integration_WorkspaceYaml_NeverHasEmptyCwd()
    {
        // Log file with no cwd anywhere → fallback should produce a non-empty cwd
        var logContent = """
            2026-03-17T12:58:45.264Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "session_start",
              "session_id": "abcd-1234-efgh-5678"
            }
            """;
        var logsDir = Path.Combine(this._tempDir, "logs");
        Directory.CreateDirectory(logsDir);
        var logFile = Path.Combine(logsDir, "process-999-111.log");
        File.WriteAllText(logFile, logContent);

        // Parse — cwd should fall back to UserProfile, never null/empty
        var lines = File.ReadAllLines(logFile);
        var (sessionId, cwd) = CopilotLogWatcherService.TryParseLogContent(lines);

        Assert.Equal("abcd-1234-efgh-5678", sessionId);
        Assert.False(string.IsNullOrEmpty(cwd));

        // Create workspace.yaml and verify cwd is present
        var sessionStateDir = Path.Combine(this._tempDir, "session-state");
        var sessionDir = Path.Combine(sessionStateDir, sessionId!);
        Directory.CreateDirectory(sessionDir);
        var wsFile = Path.Combine(sessionDir, "workspace.yaml");
        CopilotLogWatcherService.CreateWorkspaceYaml(wsFile, sessionId!, cwd, sessionId!);

        var content = File.ReadAllText(wsFile);
        Assert.DoesNotContain("cwd: \r", content);
        Assert.DoesNotContain("cwd: \n", content);
        // cwd line should have actual content after "cwd: "
        var cwdLine = content.Split('\n').First(l => l.TrimStart().StartsWith("cwd:"));
        var cwdValue = cwdLine.Split("cwd: ", 2)[1].Trim();
        Assert.False(string.IsNullOrEmpty(cwdValue));
    }

    // ── Should-Create-Workspace Logic ─────────────────────────────────

    [Fact]
    public void ShouldCreateWorkspace_ReturnsTrue_WhenFolderExistsWithoutWorkspaceYaml()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "events.jsonl"), "{}");

        var result = CopilotLogWatcherService.ShouldCreateWorkspace(this._tempDir, sessionId);

        Assert.True(result);
    }

    [Fact]
    public void ShouldCreateWorkspace_ReturnsFalse_WhenWorkspaceYamlExists()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"), "id: test");

        var result = CopilotLogWatcherService.ShouldCreateWorkspace(this._tempDir, sessionId);

        Assert.False(result);
    }

    [Fact]
    public void ShouldCreateWorkspace_ReturnsFalse_WhenFolderDoesNotExist()
    {
        var sessionId = Guid.NewGuid().ToString();

        var result = CopilotLogWatcherService.ShouldCreateWorkspace(this._tempDir, sessionId);

        Assert.False(result);
    }

    // ── Workspace YAML Creation ───────────────────────────────────────

    [Fact]
    public void CreateWorkspaceYaml_WritesFileWithExpectedFields()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        var wsFile = Path.Combine(sessionDir, "workspace.yaml");
        var cwd = @"S:\repo\community\copilot-booster";
        var summary = "Test Session";

        CopilotLogWatcherService.CreateWorkspaceYaml(wsFile, sessionId, cwd, summary);

        Assert.True(File.Exists(wsFile));

        var content = File.ReadAllText(wsFile);
        Assert.Contains($"id: {sessionId}", content);
        Assert.Contains($"cwd: {cwd}", content);
        Assert.Contains("summary:", content);
        Assert.Contains(summary, content);
        Assert.Contains("created_at:", content);
        Assert.Contains("updated_at:", content);
    }

    [Fact]
    public void CreateWorkspaceYaml_OmitsSummary_WhenNameIsNullOrEmpty()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        var wsFile = Path.Combine(sessionDir, "workspace.yaml");

        CopilotLogWatcherService.CreateWorkspaceYaml(wsFile, sessionId, @"C:\test", "");

        var content = File.ReadAllText(wsFile);
        Assert.Contains($"id: {sessionId}", content);
        Assert.Contains("cwd:", content);
        // Empty summary should not produce a "summary:" line with content
        Assert.DoesNotContain("name:", content);
    }
}
