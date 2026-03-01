public sealed class SessionInteractionManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _stateFile;

    public SessionInteractionManagerTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this._tempDir);
        this._stateFile = Path.Combine(this._tempDir, "session-states.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    [Fact]
    public void DeleteSession_SetsDeletedInStateFile_ReturnsTrue()
    {
        var sessionId = "session-1";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"), "cwd: /tmp");

        var manager = new SessionInteractionManager(this._tempDir, "unused.json", this._stateFile);
        var result = manager.DeleteSession(sessionId);

        Assert.True(result);
        Assert.True(SessionArchiveService.IsDeleted(this._stateFile, sessionId));
        Assert.True(File.Exists(Path.Combine(sessionDir, "workspace.yaml")));
    }

    [Fact]
    public void DeleteSession_NonExistentSession_ReturnsFalse()
    {
        var manager = new SessionInteractionManager(this._tempDir, "unused.json", this._stateFile);
        var result = manager.DeleteSession("no-such-session");

        Assert.False(result);
    }

    [Fact]
    public void DeleteSession_SessionWithoutWorkspaceYaml_StillMarksDeleted()
    {
        var sessionId = "session-no-yaml";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "other-file.txt"), "data");

        var manager = new SessionInteractionManager(this._tempDir, "unused.json", this._stateFile);
        var result = manager.DeleteSession(sessionId);

        Assert.True(result);
        Assert.True(SessionArchiveService.IsDeleted(this._stateFile, sessionId));
    }

    [Fact]
    public void DeleteSession_AlreadyDeleted_ReturnsTrue()
    {
        var sessionId = "session-already-deleted";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"), "cwd: /tmp");

        SessionArchiveService.SetDeleted(this._stateFile, sessionId);

        var manager = new SessionInteractionManager(this._tempDir, "unused.json", this._stateFile);
        var result = manager.DeleteSession(sessionId);

        Assert.True(result);
        Assert.True(SessionArchiveService.IsDeleted(this._stateFile, sessionId));
    }

    [Fact]
    public void LoadSessions_ExcludesDeletedSessions()
    {
        // Active session
        var activeDir = Path.Combine(this._tempDir, "active-session");
        Directory.CreateDirectory(activeDir);
        File.WriteAllText(Path.Combine(activeDir, "workspace.yaml"), "id: active-session\ncwd: /tmp\nsummary: Active");
        File.WriteAllText(Path.Combine(activeDir, "events.jsonl"), "{}");

        // Deleted session — has workspace.yaml but marked deleted in state file
        var deletedDir = Path.Combine(this._tempDir, "deleted-session");
        Directory.CreateDirectory(deletedDir);
        File.WriteAllText(Path.Combine(deletedDir, "workspace.yaml"), "id: deleted-session\ncwd: /tmp\nsummary: Deleted");
        File.WriteAllText(Path.Combine(deletedDir, "events.jsonl"), "{}");

        SessionArchiveService.SetDeleted(this._stateFile, "deleted-session");

        var sessions = SessionService.LoadNamedSessions(this._tempDir, sessionStateFile: this._stateFile);

        Assert.Single(sessions);
        Assert.Equal("active-session", sessions[0].Id);
    }

    [Fact]
    public void GetDeletedIds_ReturnsOnlyDeletedSessions()
    {
        SessionArchiveService.SetDeleted(this._stateFile, "del-1");
        SessionArchiveService.SetDeleted(this._stateFile, "del-2");
        SessionArchiveService.SetTab(this._stateFile, "active-1", "Tab1");

        var deletedIds = SessionArchiveService.GetDeletedIds(this._stateFile);

        Assert.Equal(2, deletedIds.Count);
        Assert.Contains("del-1", deletedIds);
        Assert.Contains("del-2", deletedIds);
        Assert.DoesNotContain("active-1", deletedIds);
    }

    [Fact]
    public void CleanupIfDefault_PreservesDeletedEntries()
    {
        SessionArchiveService.SetDeleted(this._stateFile, "deleted-session");
        // Setting tab to default should NOT remove a deleted entry
        SessionArchiveService.SetTab(this._stateFile, "deleted-session", "");

        Assert.True(SessionArchiveService.IsDeleted(this._stateFile, "deleted-session"));
    }

    [Fact]
    public void GetValidatedSessionCwd_NonExistentDirectory_ReturnsNull()
    {
        var sessionId = "session-bad-cwd";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"), "id: session-bad-cwd\ncwd: Z:\\NonExistent\\FakeDir");

        var manager = new SessionInteractionManager(this._tempDir, "unused.json", this._stateFile);
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

        var manager = new SessionInteractionManager(this._tempDir, "unused.json", this._stateFile);
        var result = manager.GetValidatedSessionCwd(sessionId);

        Assert.Equal(existingDir, result);
    }

    [Fact]
    public void GetValidatedSessionCwd_NoWorkspaceYaml_ReturnsNull()
    {
        var sessionId = "session-no-ws";
        var sessionDir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(sessionDir);

        var manager = new SessionInteractionManager(this._tempDir, "unused.json", this._stateFile);
        var result = manager.GetValidatedSessionCwd(sessionId);

        Assert.Null(result);
    }

    [Fact]
    public void MigrateDeleteMarkers_MigratesMarkerFilesToCache()
    {
        // Session with workspace-deleted.yaml marker
        var markedDir = Path.Combine(this._tempDir, "marked-deleted");
        Directory.CreateDirectory(markedDir);
        File.WriteAllText(Path.Combine(markedDir, "workspace.yaml"), "cwd: /tmp");
        File.WriteAllText(Path.Combine(markedDir, "workspace-deleted.yaml"), "");

        // Normal session without marker
        var normalDir = Path.Combine(this._tempDir, "normal-session");
        Directory.CreateDirectory(normalDir);
        File.WriteAllText(Path.Combine(normalDir, "workspace.yaml"), "cwd: /tmp");

        SessionService.MigrateDeleteMarkers(this._tempDir, this._stateFile, new LauncherSettings { SuppressSave = true });

        Assert.True(SessionArchiveService.IsDeleted(this._stateFile, "marked-deleted"));
        Assert.False(SessionArchiveService.IsDeleted(this._stateFile, "normal-session"));
    }

    [Fact]
    public void MigrateDeleteMarkers_SkipsAlreadyMigratedSessions()
    {
        var markedDir = Path.Combine(this._tempDir, "already-migrated");
        Directory.CreateDirectory(markedDir);
        File.WriteAllText(Path.Combine(markedDir, "workspace.yaml"), "cwd: /tmp");
        File.WriteAllText(Path.Combine(markedDir, "workspace-deleted.yaml"), "");

        // Pre-mark as deleted in cache
        SessionArchiveService.SetDeleted(this._stateFile, "already-migrated");

        // Should not throw or duplicate
        SessionService.MigrateDeleteMarkers(this._tempDir, this._stateFile, new LauncherSettings { SuppressSave = true });

        Assert.True(SessionArchiveService.IsDeleted(this._stateFile, "already-migrated"));
    }

    [Fact]
    public void MigrateDeleteMarkers_NonExistentDir_DoesNotThrow()
    {
        var nonExistentDir = Path.Combine(this._tempDir, "no-such-dir");
        SessionService.MigrateDeleteMarkers(nonExistentDir, this._stateFile, new LauncherSettings { SuppressSave = true });
        // Should complete without error
    }
}
