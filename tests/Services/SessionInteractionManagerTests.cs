public sealed class SessionInteractionManagerTests : IDisposable
{
    private readonly string _tempDir;

    public SessionInteractionManagerTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    [Fact]
    public void DeleteSession_CreatesMarkerFile_ReturnsTrue()
    {
        var sessionId = "session-1";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"), "cwd: /tmp");

        var manager = new SessionInteractionManager(this._tempDir, "unused.json");
        var result = manager.DeleteSession(sessionId);

        Assert.True(result);
        Assert.True(File.Exists(Path.Combine(sessionDir, "workspace-deleted.yaml")));
        Assert.True(File.Exists(Path.Combine(sessionDir, "workspace.yaml")));
    }

    [Fact]
    public void DeleteSession_NonExistentSession_ReturnsFalse()
    {
        var manager = new SessionInteractionManager(this._tempDir, "unused.json");
        var result = manager.DeleteSession("no-such-session");

        Assert.False(result);
    }

    [Fact]
    public void DeleteSession_SessionWithoutWorkspaceYaml_StillCreatesMarker()
    {
        var sessionId = "session-no-yaml";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "other-file.txt"), "data");

        var manager = new SessionInteractionManager(this._tempDir, "unused.json");
        var result = manager.DeleteSession(sessionId);

        Assert.True(result);
        Assert.True(File.Exists(Path.Combine(sessionDir, "workspace-deleted.yaml")));
    }

    [Fact]
    public void DeleteSession_AlreadyDeleted_ReturnsTrue()
    {
        var sessionId = "session-already-deleted";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"), "cwd: /tmp");
        File.WriteAllText(Path.Combine(sessionDir, "workspace-deleted.yaml"), "");

        var manager = new SessionInteractionManager(this._tempDir, "unused.json");
        var result = manager.DeleteSession(sessionId);

        Assert.True(result);
    }

    [Fact]
    public void LoadSessions_ExcludesDeletedSessions()
    {
        // Active session — has workspace.yaml, no marker
        var activeDir = Path.Combine(this._tempDir, "active-session");
        Directory.CreateDirectory(activeDir);
        File.WriteAllText(Path.Combine(activeDir, "workspace.yaml"), "id: active-session\ncwd: /tmp\nsummary: Active");
        File.WriteAllText(Path.Combine(activeDir, "events.jsonl"), "{}");

        // Deleted session — has both workspace.yaml and workspace-deleted.yaml
        var deletedDir = Path.Combine(this._tempDir, "deleted-session");
        Directory.CreateDirectory(deletedDir);
        File.WriteAllText(Path.Combine(deletedDir, "workspace.yaml"), "id: deleted-session\ncwd: /tmp\nsummary: Deleted");
        File.WriteAllText(Path.Combine(deletedDir, "events.jsonl"), "{}");
        File.WriteAllText(Path.Combine(deletedDir, "workspace-deleted.yaml"), "");

        var sessions = SessionService.LoadNamedSessions(this._tempDir);

        Assert.Single(sessions);
        Assert.Equal("active-session", sessions[0].Id);
    }

    [Fact]
    public void GetValidatedSessionCwd_NonExistentDirectory_ReturnsNull()
    {
        var sessionId = "session-bad-cwd";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"), "id: session-bad-cwd\ncwd: Z:\\NonExistent\\FakeDir");

        var manager = new SessionInteractionManager(this._tempDir, "unused.json");
        var result = manager.GetValidatedSessionCwd(sessionId);

        Assert.Null(result);
    }

    [Fact]
    public void GetValidatedSessionCwd_ExistingDirectory_ReturnsCwd()
    {
        var sessionId = "session-good-cwd";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        var existingDir = Path.Combine(this._tempDir, "real-project");
        Directory.CreateDirectory(existingDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"), $"id: session-good-cwd\ncwd: {existingDir}");

        var manager = new SessionInteractionManager(this._tempDir, "unused.json");
        var result = manager.GetValidatedSessionCwd(sessionId);

        Assert.Equal(existingDir, result);
    }

    [Fact]
    public void GetValidatedSessionCwd_NoWorkspaceYaml_ReturnsNull()
    {
        var sessionId = "session-no-ws";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        var manager = new SessionInteractionManager(this._tempDir, "unused.json");
        var result = manager.GetValidatedSessionCwd(sessionId);

        Assert.Null(result);
    }
}
