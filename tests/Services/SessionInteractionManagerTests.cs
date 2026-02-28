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
    public void DeleteSession_RenamesWorkspaceFile_ReturnsTrue()
    {
        var sessionId = "session-1";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"), "cwd: /tmp");

        var manager = new SessionInteractionManager(this._tempDir, "unused.json");
        var result = manager.DeleteSession(sessionId);

        Assert.True(result);
        Assert.True(File.Exists(Path.Combine(sessionDir, "workspace-deleted.yaml")));
        Assert.False(File.Exists(Path.Combine(sessionDir, "workspace.yaml")));
    }

    [Fact]
    public void DeleteSession_NonExistentSession_ReturnsFalse()
    {
        var manager = new SessionInteractionManager(this._tempDir, "unused.json");
        var result = manager.DeleteSession("no-such-session");

        Assert.False(result);
    }

    [Fact]
    public void DeleteSession_SessionWithoutWorkspaceYaml_ReturnsFalse()
    {
        var sessionId = "session-no-yaml";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "other-file.txt"), "data");

        var manager = new SessionInteractionManager(this._tempDir, "unused.json");
        var result = manager.DeleteSession(sessionId);

        Assert.False(result);
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
