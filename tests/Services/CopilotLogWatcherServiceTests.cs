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

    // Real CLI v1.0.44 telemetry shape — DO NOT modify without re-harvesting from a current copilot log
    private const string RealisticLogContent = """
        2026-05-09T10:59:33.804Z [INFO] Workspace initialized: ba62613b-7f04-46bc-9c1e-778b12616687 (checkpoints: 0)
        2026-05-09T10:59:33.852Z [INFO] Registering foreground session: ba62613b-7f04-46bc-9c1e-778b12616687
        2026-05-09T10:59:34.127Z [INFO] [Telemetry] cli.telemetry:
        {
          "kind": "cli_ready",
          "properties": {
            "copilot_pid": "74528",
            "engagement_id": "6af655d6-77ff-47d8-bbf2-eb21068f11f1"
          },
          "metrics": {
            "startup_duration_ms": 979
          },
          "session_id": "ba62613b-7f04-46bc-9c1e-778b12616687",
          "features": {
            "FEATURE_FLAG_TEST": "true",
            "copilot-feature-agentic-memory": "true"
          },
          "created_at": "2026-05-09T10:59:34.127Z",
          "copilot_tracking_id": "2334661c5d22c2ad95dcd6ecdf047dd8",
          "client": {
            "cli_version": "1.0.44",
            "os_platform": "win32",
            "os_version": "10.0.26200",
            "os_arch": "x64",
            "node_version": "v24.15.0",
            "copilot_plan": "enterprise",
            "client_name": "github/cli",
            "client_type": "cli-interactive",
            "is_staff": true,
            "dev_device_id": "fe519472-be6d-40d0-9e37-782a38d5ea5e"
          }
        }
        2026-05-09T10:59:33.853Z [DEBUG] [remoteHosts] Starting remote host detection for cwd=S:\repo\community\sandcastle
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

        var sessions = CopilotLogWatcherService.TryParseLogContent(lines);

        var (sessionId, cwd) = sessions.Single();
        Assert.Equal("ba62613b-7f04-46bc-9c1e-778b12616687", sessionId);
        Assert.Equal(@"S:\repo\community\sandcastle", cwd);
    }

    [Fact]
    public void TryParseLogContent_ExtractsSessionId_WithoutCwd()
    {
        var logContent = """
            2026-05-09T10:59:34.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "cli_ready",
              "session_id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
            }
            2026-05-09T10:59:34.128Z [DEBUG] Some other line without cwd info
            """;
        var lines = logContent.Split('\n');

        var sessions = CopilotLogWatcherService.TryParseLogContent(lines);

        var (sessionId, cwd) = sessions.Single();
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", sessionId);
        // No cwd in JSON or debug lines → falls back to UserProfile
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), cwd);
    }

    [Fact]
    public void TryParseLogContent_PrefersCwdFromJson_OverDebugLine()
    {
        // JSON block has context.cwd AND debug line has cwd= — JSON should win
        var logContent = """
            2026-05-09T10:59:34.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "cli_ready",
              "session_id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
              "context": {
                "cwd": "C:\\FromJson"
              }
            }
            2026-05-09T10:59:34.128Z [DEBUG] [remoteHosts] Starting remote host detection for cwd=D:\FromDebugLine, featureFlagEnabled=true
            """;
        var lines = logContent.Split('\n');

        var sessions = CopilotLogWatcherService.TryParseLogContent(lines);

        var (sessionId, cwd) = sessions.Single();
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", sessionId);
        Assert.Equal(@"C:\FromJson", cwd);
    }

    [Fact]
    public void TryParseLogContent_UsesFallbackCwd_WhenNoCwdInLogOrJson()
    {
        var logContent = """
            2026-05-09T10:59:34.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "cli_ready",
              "session_id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
            }
            """;
        var lines = logContent.Split('\n');

        var sessions = CopilotLogWatcherService.TryParseLogContent(lines, @"E:\MyFallback");

        var (sessionId, cwd) = sessions.Single();
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", sessionId);
        Assert.Equal(@"E:\MyFallback", cwd);
    }

    [Fact]
    public void TryParseLogContent_UsesUserProfileFallback_WhenNoCwdAnywhere()
    {
        // No cwd in JSON, no debug line, no explicit fallback → UserProfile
        var logContent = """
            2026-05-09T10:59:34.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "cli_ready",
              "session_id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
            }
            """;
        var lines = logContent.Split('\n');

        var sessions = CopilotLogWatcherService.TryParseLogContent(lines);

        var (sessionId, cwd) = sessions.Single();
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", sessionId);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), cwd);
    }

    [Fact]
    public void TryParseLogContent_FallbackCwd_DoesNotOverrideDebugLineCwd()
    {
        // Debug line has cwd= AND fallback provided — debug line wins (level 2 > level 3)
        var logContent = """
            2026-05-09T10:59:34.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "cli_ready",
              "session_id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
            }
            2026-05-09T10:59:34.128Z [DEBUG] [remoteHosts] Starting remote host detection for cwd=D:\FromDebugLine, featureFlagEnabled=true
            """;
        var lines = logContent.Split('\n');

        var sessions = CopilotLogWatcherService.TryParseLogContent(lines, @"E:\MyFallback");

        var (sessionId, cwd) = sessions.Single();
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", sessionId);
        Assert.Equal(@"D:\FromDebugLine", cwd);
    }

    [Fact]
    public void TryParseLogContent_ReturnsEmptyList_WhenNoTelemetryBlock()
    {
        var logContent = """
            2026-03-17T12:58:44.775Z [DEBUG] Sending telemetry event: copilot-cli/extension.activate
            2026-03-17T12:58:45.265Z [DEBUG] Some other debug line without session data
            """;
        var lines = logContent.Split('\n');

        var sessions = CopilotLogWatcherService.TryParseLogContent(lines);

        Assert.Empty(sessions);
    }

    [Fact]
    public void TryParseLogContent_ExtractsSessionId_FromAnyTelemetryKind()
    {
        // Real CLI v1.0.44 uses many kinds: cli_ready, allow_all_enabled, session_model_change, etc
        // Parser accepts ANY kind as long as session_id is present
        var logContent = """
            2026-05-09T10:59:34.138Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "allow_all_enabled",
              "session_id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
            }
            """;
        var lines = logContent.Split('\n');

        var sessions = CopilotLogWatcherService.TryParseLogContent(lines);

        var (sessionId, cwd) = sessions.Single();
        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", sessionId);
        Assert.False(string.IsNullOrEmpty(cwd));
    }

    [Fact]
    public void TryParseLogContent_ReturnsEmptyList_ForEmptyInput()
    {
        var sessions = CopilotLogWatcherService.TryParseLogContent([]);

        Assert.Empty(sessions);
    }

    [Fact]
    public void TryParseLogContent_ReturnsEmptyList_ForMalformedJson()
    {
        var logContent = """
            2026-05-09T10:59:34.127Z [INFO] [Telemetry] cli.telemetry:
            { not valid json at all {{{
            }
            """;
        var lines = logContent.Split('\n');

        var sessions = CopilotLogWatcherService.TryParseLogContent(lines);

        Assert.Empty(sessions);
    }

    [Fact]
    public void TryParseLogContent_ExtractsSessionId_FromInfoRegisteringForegroundSessionLine()
    {
        // Regex fallback: no telemetry blocks, only INFO line
        var logContent = """
            2026-05-09T10:59:33.852Z [INFO] Registering foreground session: ba62613b-7f04-46bc-9c1e-778b12616687
            2026-05-09T10:59:33.853Z [DEBUG] Broadcasting session lifecycle event
            """;
        var lines = logContent.Split('\n');

        var sessions = CopilotLogWatcherService.TryParseLogContent(lines);

        var (sessionId, cwd) = sessions.Single();

        Assert.Equal("ba62613b-7f04-46bc-9c1e-778b12616687", sessionId);
        Assert.False(string.IsNullOrEmpty(cwd));
    }

    // ── Bug D: Multi-session /resume support ──────────────────────────

    [Fact]
    public void TryParseLogContent_MultipleWorkspaceInitialized_ReturnsAllInOrder()
    {
        // Bug D: `/resume` causes Copilot CLI to switch sessions within a single process.
        // The log will have multiple "Workspace initialized" or telemetry blocks with different session_ids.
        // Parser must return ALL sessions in order, not just the first.
        var logContent = """
            2026-05-10T22:38:28.494Z [INFO] Workspace initialized: 0bb1099b-1111-2222-3333-444444444444 (checkpoints: 0)
            2026-05-10T22:38:29.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "cli_ready",
              "session_id": "0bb1099b-1111-2222-3333-444444444444"
            }
            2026-05-10T22:39:15.000Z [INFO] Workspace initialized: 2d76b3fe-5555-6666-7777-888888888888 (checkpoints: 5)
            2026-05-10T22:39:15.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "session_resumed",
              "session_id": "2d76b3fe-5555-6666-7777-888888888888"
            }
            """;
        var lines = logContent.Split('\n');

        var sessions = CopilotLogWatcherService.TryParseLogContent(lines);

        Assert.Equal(2, sessions.Count);
        Assert.Equal("0bb1099b-1111-2222-3333-444444444444", sessions[0].sessionId);
        Assert.Equal("2d76b3fe-5555-6666-7777-888888888888", sessions[1].sessionId);
        // Both sessions share the same cwd (process-wide)
        Assert.Equal(sessions[0].cwd, sessions[1].cwd);
    }

    [Fact]
    public void TryProcessLogFile_TwoSessionsForSamePid_EmitsTwoDiscoveryEvents()
    {
        // Bug D: when a single PID hosts multiple sessions over time (via /resume),
        // the watcher must emit discovery events for BOTH sessions.
        var logContent = """
            2026-05-10T22:38:28.494Z [INFO] Workspace initialized: 0bb1099b-1111-2222-3333-444444444444 (checkpoints: 0)
            2026-05-10T22:38:29.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "cli_ready",
              "session_id": "0bb1099b-1111-2222-3333-444444444444"
            }
            2026-05-10T22:39:15.000Z [INFO] Workspace initialized: 2d76b3fe-5555-6666-7777-888888888888 (checkpoints: 5)
            2026-05-10T22:39:15.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "session_resumed",
              "session_id": "2d76b3fe-5555-6666-7777-888888888888"
            }
            2026-05-10T22:39:15.128Z [DEBUG] [remoteHosts] Starting remote host detection for cwd=S:\repo\rkti\mari-sali, featureFlagEnabled=true
            """;

        // Write a fake log file with PID 39992
        var logFilePath = Path.Combine(this._tempDir, "process-17374747448775-39992.log");
        File.WriteAllText(logFilePath, logContent);

        // Create session dirs so ShouldCreateWorkspace returns true
        var sessionDir1 = Path.Combine(this._tempDir, "0bb1099b-1111-2222-3333-444444444444");
        var sessionDir2 = Path.Combine(this._tempDir, "2d76b3fe-5555-6666-7777-888888888888");
        Directory.CreateDirectory(sessionDir1);
        Directory.CreateDirectory(sessionDir2);

        var watcher = new CopilotLogWatcherService(this._tempDir);
        var discoveredSessions = new List<(string sessionId, int copilotPid)>();
        watcher.ExternalSessionDiscovered += (sid, pid) => discoveredSessions.Add((sid, pid));

        // Process the log — should emit TWO events
        watcher.GetType()
            .GetMethod("TryProcessLogFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(watcher, [logFilePath, 3]);

        Assert.Equal(2, discoveredSessions.Count);
        Assert.Contains(("0bb1099b-1111-2222-3333-444444444444", 39992), discoveredSessions);
        Assert.Contains(("2d76b3fe-5555-6666-7777-888888888888", 39992), discoveredSessions);
    }

    [Fact]
    public void TryProcessLogFile_SamePidSameSessionTwice_OnlyEmitsOnce()
    {
        // Bug D: dedupe should be per (pid, sessionId) pair — processing the same log twice
        // should not emit duplicate events.
        var logContent = """
            2026-05-10T22:38:28.494Z [INFO] Workspace initialized: 0bb1099b-1111-2222-3333-444444444444 (checkpoints: 0)
            2026-05-10T22:38:29.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "cli_ready",
              "session_id": "0bb1099b-1111-2222-3333-444444444444"
            }
            2026-05-10T22:38:29.128Z [DEBUG] [remoteHosts] Starting remote host detection for cwd=S:\repo\test, featureFlagEnabled=true
            """;

        var logFilePath = Path.Combine(this._tempDir, "process-17374747448775-39992.log");
        File.WriteAllText(logFilePath, logContent);

        var sessionDir = Path.Combine(this._tempDir, "0bb1099b-1111-2222-3333-444444444444");
        Directory.CreateDirectory(sessionDir);

        var watcher = new CopilotLogWatcherService(this._tempDir);
        var discoveredSessions = new List<(string sessionId, int copilotPid)>();
        watcher.ExternalSessionDiscovered += (sid, pid) => discoveredSessions.Add((sid, pid));

        var methodInfo = watcher.GetType()
            .GetMethod("TryProcessLogFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        // Process the log twice
        methodInfo.Invoke(watcher, [logFilePath, 3]);
        methodInfo.Invoke(watcher, [logFilePath, 3]);

        // Should only emit once
        Assert.Single(discoveredSessions);
        Assert.Equal(("0bb1099b-1111-2222-3333-444444444444", 39992), discoveredSessions[0]);
    }

    [Fact]
    public void TryParseLogContent_ExtractsSessionId_FromInfoWorkspaceInitializedLine()
    {
        // Regex fallback: no telemetry blocks, only Workspace initialized INFO line
        var logContent = """
            2026-05-09T10:59:33.804Z [INFO] Workspace initialized: ba62613b-7f04-46bc-9c1e-778b12616687 (checkpoints: 0)
            2026-05-09T10:59:33.805Z [DEBUG] Some other line
            """;
        var lines = logContent.Split('\n');

        var sessions = CopilotLogWatcherService.TryParseLogContent(lines);

        var (sessionId, cwd) = sessions.Single();
        Assert.Equal("ba62613b-7f04-46bc-9c1e-778b12616687", sessionId);
        Assert.False(string.IsNullOrEmpty(cwd));
    }

    [Fact]
    public void TryParseLogContent_ReturnsFirstSessionId_WhenMultipleTelemetryBlocksPresent()
    {
        // Bug D fix: returns ALL session IDs now, but old test expected only first/last one. 
        // For backwards compatibility of this test, check that BOTH sessions are present.
        var logContent = """
            2026-05-09T10:59:34.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "cli_ready",
              "session_id": "11111111-1111-1111-1111-111111111111"
            }
            2026-05-09T10:59:34.138Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "allow_all_enabled",
              "session_id": "22222222-2222-2222-2222-222222222222"
            }
            """;
        var lines = logContent.Split('\n');

        var sessions = CopilotLogWatcherService.TryParseLogContent(lines);

        // Bug D: Parser now returns ALL sessions (for /resume support)
        Assert.Equal(2, sessions.Count);
        Assert.Equal("11111111-1111-1111-1111-111111111111", sessions[0].sessionId);
        Assert.Equal("22222222-2222-2222-2222-222222222222", sessions[1].sessionId);
        Assert.False(string.IsNullOrEmpty(sessions[0].cwd));
    }

    [Fact]
    public void TryParseLogContent_ReturnsEmptyList_WhenNoSessionIdAnywhere()
    {
        // Neither telemetry blocks nor INFO lines with session_id
        var logContent = """
            2026-05-09T10:59:33.617Z [DEBUG] Sending telemetry event: copilot-cli/extension.activate
            2026-05-09T10:59:33.618Z [DEBUG] OpenTelemetry not enabled
            """;
        var lines = logContent.Split('\n');

        var sessions = CopilotLogWatcherService.TryParseLogContent(lines);

        Assert.Empty(sessions);
    }

    [Fact]
    public void TryParseLogContent_ToleratesTruncatedFinalJsonBlock_AndStillReturnsEarlierSessionId()
    {
        // Mid-stream log: first telemetry block is complete, second is truncated
        var logContent = """
            2026-05-09T10:59:34.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "cli_ready",
              "session_id": "ba62613b-7f04-46bc-9c1e-778b12616687"
            }
            2026-05-09T10:59:34.138Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "allow_all_enabled",
              "session_id": "incomplete-truncate
            """;
        var lines = logContent.Split('\n');

        var sessions = CopilotLogWatcherService.TryParseLogContent(lines);

        // Should have parsed the first valid session only
        var (sessionId, cwd) = sessions.Single();
        Assert.Equal("ba62613b-7f04-46bc-9c1e-778b12616687", sessionId);
        Assert.False(string.IsNullOrEmpty(cwd));
    }

    [Fact]
    public void TryParseLogContent_RejectsMalformedSessionId()
    {
        // session_id present but not GUID-shaped → parser should reject it (return empty list)
        var logContent = """
            2026-05-09T10:59:34.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "cli_ready",
              "session_id": "not-a-valid-guid-shape"
            }
            """;
        var lines = logContent.Split('\n');
        var sessions = CopilotLogWatcherService.TryParseLogContent(lines);

        // Trinity's parser correctly validates GUID format and rejects malformed IDs
        Assert.Empty(sessions);
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
        var sessions = CopilotLogWatcherService.TryParseLogContent(lines);

        // Assert — parser found session_id and cwd
        var (sessionId, cwd) = sessions.Single();
        Assert.Equal("ba62613b-7f04-46bc-9c1e-778b12616687", sessionId);
        Assert.Equal(@"S:\repo\community\sandcastle", cwd);

        // Arrange — create session folder WITHOUT workspace.yaml
        var sessionStateDir = Path.Combine(this._tempDir, "session-state");
        var sessionDir = Path.Combine(sessionStateDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        // Assert — should create workspace
        Assert.True(CopilotLogWatcherService.ShouldCreateWorkspace(sessionStateDir, sessionId));

        // Act — create workspace.yaml
        var wsFile = Path.Combine(sessionDir, "workspace.yaml");
        CopilotLogWatcherService.CreateWorkspaceYaml(wsFile, sessionId, cwd, sessionId);

        // Assert — workspace.yaml was created with correct fields
        Assert.True(File.Exists(wsFile));
        var content = File.ReadAllText(wsFile);
        Assert.Contains($"id: {sessionId}", content);
        Assert.Contains(@"cwd: S:\repo", content);
        Assert.Contains("created_at:", content);
        Assert.Contains("updated_at:", content);

        // Assert — ShouldCreateWorkspace now returns false (workspace.yaml exists)
        Assert.False(CopilotLogWatcherService.ShouldCreateWorkspace(sessionStateDir, sessionId));
    }

    [Fact]
    public void Integration_WorkspaceYaml_NeverHasEmptyCwd()
    {
        // Log file with no cwd anywhere → fallback should produce a non-empty cwd
        var logContent = """
            2026-05-09T10:59:34.127Z [INFO] [Telemetry] cli.telemetry:
            {
              "kind": "cli_ready",
              "session_id": "abcdabcd-1234-5678-abcd-1234567890ab"
            }
            """;
        var logsDir = Path.Combine(this._tempDir, "logs");
        Directory.CreateDirectory(logsDir);
        var logFile = Path.Combine(logsDir, "process-999-111.log");
        File.WriteAllText(logFile, logContent);

        // Parse — cwd should fall back to UserProfile, never null/empty
        var lines = File.ReadAllLines(logFile);
        var sessions = CopilotLogWatcherService.TryParseLogContent(lines);

        var (sessionId, cwd) = sessions.Single();
        Assert.Equal("abcdabcd-1234-5678-abcd-1234567890ab", sessionId);
        Assert.False(string.IsNullOrEmpty(cwd));

        // Create workspace.yaml and verify cwd is present
        var sessionStateDir = Path.Combine(this._tempDir, "session-state");
        var sessionDir = Path.Combine(sessionStateDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        var wsFile = Path.Combine(sessionDir, "workspace.yaml");
        CopilotLogWatcherService.CreateWorkspaceYaml(wsFile, sessionId, cwd, sessionId);

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
