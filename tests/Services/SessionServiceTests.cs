using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

public class ParseWorkspaceTests : IDisposable
{
    private readonly string _tempDir;

    public ParseWorkspaceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void ParseWorkspace_ValidFile_ReturnsSessionInfo()
    {
        var wsFile = Path.Combine(_tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "id: session-123\ncwd: C:\\myproject\nsummary: Fix the bug");

        var result = SessionService.ParseWorkspace(wsFile, 42);

        Assert.NotNull(result);
        Assert.Equal("session-123", result!.Id);
        Assert.Equal(@"C:\myproject", result.Cwd);
        Assert.Equal("[myproject] Fix the bug", result.Summary);
        Assert.Equal(42, result.Pid);
    }

    [Fact]
    public void ParseWorkspace_MissingId_ReturnsNull()
    {
        var wsFile = Path.Combine(_tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "cwd: C:\\myproject\nsummary: Fix the bug");

        var result = SessionService.ParseWorkspace(wsFile, 1);

        Assert.Null(result);
    }

    [Fact]
    public void ParseWorkspace_MissingSummary_ReturnsFolderOnlyBrackets()
    {
        var wsFile = Path.Combine(_tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "id: session-456\ncwd: C:\\myproject");

        var result = SessionService.ParseWorkspace(wsFile, 1);

        Assert.NotNull(result);
        Assert.Equal("[myproject]", result!.Summary);
    }

    [Fact]
    public void ParseWorkspace_WithSummary_ReturnsFolderAndSummary()
    {
        var wsFile = Path.Combine(_tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "id: s1\ncwd: D:\\repos\\app\nsummary: Implement feature X");

        var result = SessionService.ParseWorkspace(wsFile, 5);

        Assert.NotNull(result);
        Assert.Equal("[app] Implement feature X", result!.Summary);
    }

    [Fact]
    public void ParseWorkspace_FileNotFound_ReturnsNull()
    {
        var result = SessionService.ParseWorkspace(Path.Combine(_tempDir, "missing.yaml"), 1);

        Assert.Null(result);
    }

    [Fact]
    public void ParseWorkspace_EmptyCwd_ReturnsEmptyFolder()
    {
        var wsFile = Path.Combine(_tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "id: s1\ncwd: \nsummary: test");

        var result = SessionService.ParseWorkspace(wsFile, 1);

        Assert.NotNull(result);
        Assert.Equal("[] test", result!.Summary);
    }

    [Fact]
    public void ParseWorkspace_CwdWithTrailingBackslash_StripsIt()
    {
        var wsFile = Path.Combine(_tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "id: s1\ncwd: C:\\myproject\\\nsummary: test");

        var result = SessionService.ParseWorkspace(wsFile, 1);

        Assert.NotNull(result);
        Assert.Equal("[myproject] test", result!.Summary);
    }

    [Fact]
    public void ParseWorkspace_NoCwd_ReturnsUnknown()
    {
        var wsFile = Path.Combine(_tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "id: s1\nsummary: test");

        var result = SessionService.ParseWorkspace(wsFile, 1);

        Assert.NotNull(result);
        Assert.Equal("Unknown", result!.Cwd);
    }
}

public class LoadNamedSessionsTests : IDisposable
{
    private readonly string _tempDir;

    public LoadNamedSessionsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void LoadNamedSessions_NoSessionStateDir_ReturnsEmpty()
    {
        var nonExistent = Path.Combine(_tempDir, "nonexistent");
        var result = SessionService.LoadNamedSessions(nonExistent);
        Assert.Empty(result);
    }

    [Fact]
    public void LoadNamedSessions_WithValidSessions_ReturnsParsedSessions()
    {
        var sessionDir = Path.Combine(_tempDir, "session1");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            "id: session1\ncwd: C:\\project\nsummary: My session");

        var result = SessionService.LoadNamedSessions(_tempDir);

        Assert.Single(result);
        Assert.Equal("session1", result[0].Id);
        Assert.Equal("[project] My session", result[0].Summary);
    }

    [Fact]
    public void LoadNamedSessions_SkipsSessionsWithoutSummary()
    {
        var s1 = Path.Combine(_tempDir, "s1");
        Directory.CreateDirectory(s1);
        File.WriteAllText(Path.Combine(s1, "workspace.yaml"), "id: s1\ncwd: C:\\a\nsummary: Has summary");

        var s2 = Path.Combine(_tempDir, "s2");
        Directory.CreateDirectory(s2);
        File.WriteAllText(Path.Combine(s2, "workspace.yaml"), "id: s2\ncwd: C:\\b");

        var result = SessionService.LoadNamedSessions(_tempDir);

        Assert.Single(result);
        Assert.Equal("s1", result[0].Id);
    }

    [Fact]
    public void LoadNamedSessions_MaxFiftySessions()
    {
        for (int i = 0; i < 60; i++)
        {
            var dir = Path.Combine(_tempDir, $"session-{i:D3}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "workspace.yaml"),
                $"id: session-{i:D3}\ncwd: C:\\proj{i}\nsummary: Session {i}");
        }

        var result = SessionService.LoadNamedSessions(_tempDir);

        Assert.Equal(50, result.Count);
    }

    [Fact]
    public void LoadNamedSessions_OrderedByLastModified()
    {
        var s1 = Path.Combine(_tempDir, "old-session");
        Directory.CreateDirectory(s1);
        File.WriteAllText(Path.Combine(s1, "workspace.yaml"),
            "id: old\ncwd: C:\\old\nsummary: Old session");
        Directory.SetLastWriteTime(s1, DateTime.Now.AddHours(-2));

        var s2 = Path.Combine(_tempDir, "new-session");
        Directory.CreateDirectory(s2);
        File.WriteAllText(Path.Combine(s2, "workspace.yaml"),
            "id: new\ncwd: C:\\new\nsummary: New session");
        Directory.SetLastWriteTime(s2, DateTime.Now);

        var result = SessionService.LoadNamedSessions(_tempDir);

        Assert.Equal(2, result.Count);
        Assert.Equal("new", result[0].Id);
        Assert.Equal("old", result[1].Id);
    }
}

public class GetActiveSessionsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _pidFile;
    private readonly string _sessionStateDir;

    public GetActiveSessionsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _pidFile = Path.Combine(_tempDir, "active-pids.json");
        _sessionStateDir = Path.Combine(_tempDir, "session-state");
        Directory.CreateDirectory(_sessionStateDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void GetActiveSessions_NoPidFile_ReturnsEmpty()
    {
        var result = SessionService.GetActiveSessions(
            Path.Combine(_tempDir, "nonexistent.json"), _sessionStateDir);
        Assert.Empty(result);
    }

    [Fact]
    public void GetActiveSessions_EmptyRegistry_ReturnsEmpty()
    {
        File.WriteAllText(_pidFile, "{}");
        var result = SessionService.GetActiveSessions(_pidFile, _sessionStateDir);
        Assert.Empty(result);
    }

    [Fact]
    public void GetActiveSessions_InvalidJson_ReturnsEmpty()
    {
        File.WriteAllText(_pidFile, "not json at all");
        var result = SessionService.GetActiveSessions(_pidFile, _sessionStateDir);
        Assert.Empty(result);
    }

    [Fact]
    public void GetActiveSessions_PidNotRunning_RemovesStalePid()
    {
        var fakePid = 99999;
        var registry = new Dictionary<string, object>
        {
            [fakePid.ToString()] = new { started = DateTime.Now.ToString("o"), sessionId = "s1" }
        };
        File.WriteAllText(_pidFile, JsonSerializer.Serialize(registry));

        var result = SessionService.GetActiveSessions(_pidFile, _sessionStateDir);

        Assert.Empty(result);
        var updatedJson = File.ReadAllText(_pidFile);
        Assert.DoesNotContain(fakePid.ToString(), updatedJson);
    }

    [Fact]
    public void GetActiveSessions_NonNumericPid_Skipped()
    {
        var registry = new Dictionary<string, object>
        {
            ["not-a-number"] = new { started = DateTime.Now.ToString("o"), sessionId = "s1" }
        };
        File.WriteAllText(_pidFile, JsonSerializer.Serialize(registry));

        var result = SessionService.GetActiveSessions(_pidFile, _sessionStateDir);

        Assert.Empty(result);
    }

    [Fact]
    public void GetActiveSessions_NullSessionId_SkipsEntry()
    {
        // Use current process PID (it's running but sessionId is null)
        var myPid = Environment.ProcessId;
        var registry = new Dictionary<string, object>
        {
            [myPid.ToString()] = new { started = DateTime.Now.ToString("o"), sessionId = (string?)null }
        };
        File.WriteAllText(_pidFile, JsonSerializer.Serialize(registry));

        var result = SessionService.GetActiveSessions(_pidFile, _sessionStateDir);

        // Process is running but not CopilotApp, so it gets removed
        // OR sessionId is null so it gets skipped — either way empty
        Assert.Empty(result);
    }

    [Fact]
    public void GetActiveSessions_NoWorkspaceFile_SkipsEntry()
    {
        var myPid = Environment.ProcessId;
        var registry = new Dictionary<string, object>
        {
            [myPid.ToString()] = new { started = DateTime.Now.ToString("o"), sessionId = "nonexistent-session" }
        };
        File.WriteAllText(_pidFile, JsonSerializer.Serialize(registry));

        var result = SessionService.GetActiveSessions(_pidFile, _sessionStateDir);

        Assert.Empty(result);
    }
}

public class FindGitRootTests : IDisposable
{
    private readonly string _tempDir;

    public FindGitRootTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void FindGitRoot_HasGitDir_ReturnsRoot()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, ".git"));

        var result = SessionService.FindGitRoot(_tempDir);

        Assert.Equal(_tempDir, result);
    }

    [Fact]
    public void FindGitRoot_NoGitDir_ReturnsNull()
    {
        var subDir = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(subDir);

        var result = SessionService.FindGitRoot(subDir);

        Assert.Null(result);
    }

    [Fact]
    public void FindGitRoot_NestedDir_FindsParent()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, ".git"));
        var child = Path.Combine(_tempDir, "src", "deep");
        Directory.CreateDirectory(child);

        var result = SessionService.FindGitRoot(child);

        Assert.Equal(_tempDir, result);
    }

    [Fact]
    public void FindGitRoot_RootDir_ReturnsNull()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var result = SessionService.FindGitRoot(root);

        // Should not infinite loop; may return null or root depending on if .git exists there
        // The key assertion is that it terminates without error
        Assert.True(result == null || Directory.Exists(Path.Combine(result, ".git")));
    }
}
