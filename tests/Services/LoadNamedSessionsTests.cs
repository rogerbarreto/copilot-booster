public sealed class LoadNamedSessionsTests : IDisposable
{
    private readonly string _tempDir;

    public LoadNamedSessionsTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    [Fact]
    public void LoadNamedSessions_NoSessionStateDir_ReturnsEmpty()
    {
        var nonExistent = Path.Combine(this._tempDir, "nonexistent");
        var result = SessionService.LoadNamedSessions(nonExistent);
        Assert.Empty(result);
    }

    [Fact]
    public void LoadNamedSessions_WithValidSessions_ReturnsParsedSessions()
    {
        var sessionDir = Path.Combine(this._tempDir, "session1");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            "id: session1\ncwd: C:\\project\nsummary: My session");

        var result = SessionService.LoadNamedSessions(this._tempDir);

        Assert.Single(result);
        Assert.Equal("session1", result[0].Id);
        Assert.Equal("My session", result[0].Summary);
        Assert.Equal("project", result[0].Folder);
    }

    [Fact]
    public void LoadNamedSessions_IncludesSessionsWithoutSummary()
    {
        var s1 = Path.Combine(this._tempDir, "s1");
        Directory.CreateDirectory(s1);
        File.WriteAllText(Path.Combine(s1, "workspace.yaml"), "id: s1\ncwd: C:\\a\nsummary: Has summary");

        var s2 = Path.Combine(this._tempDir, "s2");
        Directory.CreateDirectory(s2);
        File.WriteAllText(Path.Combine(s2, "workspace.yaml"), "id: s2\ncwd: C:\\b");

        var result = SessionService.LoadNamedSessions(this._tempDir);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.Id == "s1" && s.Summary.Contains("Has summary"));
        Assert.Contains(result, s => s.Id == "s2" && s.Summary == "Session s2" && s.Folder == "b");
    }

    [Fact]
    public void LoadNamedSessions_ReturnsAllSessions()
    {
        for (int i = 0; i < 60; i++)
        {
            var dir = Path.Combine(this._tempDir, $"session-{i:D3}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "workspace.yaml"),
                $"id: session-{i:D3}\ncwd: C:\\proj{i}\nsummary: Session {i}");
        }

        var result = SessionService.LoadNamedSessions(this._tempDir);

        Assert.Equal(60, result.Count);
    }

    [Fact]
    public void LoadNamedSessions_OrderedByLastModified()
    {
        var s1 = Path.Combine(this._tempDir, "old");
        Directory.CreateDirectory(s1);
        File.WriteAllText(Path.Combine(s1, "workspace.yaml"),
            "id: old\ncwd: C:\\old\nsummary: Old session");
        Directory.SetLastWriteTime(s1, DateTime.Now.AddHours(-2));

        var s2 = Path.Combine(this._tempDir, "new");
        Directory.CreateDirectory(s2);
        File.WriteAllText(Path.Combine(s2, "workspace.yaml"),
            "id: new\ncwd: C:\\new\nsummary: New session");
        Directory.SetLastWriteTime(s2, DateTime.Now);

        var result = SessionService.LoadNamedSessions(this._tempDir);

        Assert.Equal(2, result.Count);
        Assert.Equal("new", result[0].Id);
        Assert.Equal("old", result[1].Id);
    }

    [Fact]
    public void LoadNamedSessions_QuotedEmptySummary_TreatsAsEmpty()
    {
        var sessionDir = Path.Combine(this._tempDir, "session-quoted");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            "id: session-quoted\ncwd: C:\\myproject\nsummary: \"\"");

        var result = SessionService.LoadNamedSessions(this._tempDir);

        Assert.Single(result);
        Assert.Equal("Session session-", result[0].Summary);
        Assert.Equal("myproject", result[0].Folder);
    }

    [Fact]
    public void LoadNamedSessions_BackupFolders_AreSkipped()
    {
        // Copilot CLI creates backup folders during YAML migrations like
        // <session-id>-backup-pre-strip-20260509-172120. The workspace.yaml inside
        // retains the original `id:` (without the backup suffix). The grid must
        // not surface them as duplicate sessions of the real one.
        var realId = "2d76b3fe-9909-4750-853c-d510cbdfd58d";
        var realDir = Path.Combine(this._tempDir, realId);
        Directory.CreateDirectory(realDir);
        File.WriteAllText(Path.Combine(realDir, "workspace.yaml"),
            $"id: {realId}\ncwd: S:\\repo\\rkti\\mari-sali\nname: Fact Check Wave 3 Codebase");

        // Three backup folders, all with the SAME id in their YAML
        var backupSuffixes = new[] { "-backup-20260509-160646", "-backup-pre-ephemeral-20260509-171823", "-backup-pre-strip-20260509-172120" };
        foreach (var suffix in backupSuffixes)
        {
            var backupDir = Path.Combine(this._tempDir, realId + suffix);
            Directory.CreateDirectory(backupDir);
            File.WriteAllText(Path.Combine(backupDir, "workspace.yaml"),
                $"id: {realId}\ncwd: S:\\repo\\rkti\\mari-sali\nname: Fact Check Wave 3 Codebase");
        }

        var sessionStateFile = Path.Combine(this._tempDir, "sessions.json");
        var result = SessionService.LoadNamedSessions(this._tempDir, sessionStateFile: sessionStateFile);

        Assert.Single(result);
        Assert.Equal(realId, result[0].Id);
    }

    [Fact]
    public void LoadNamedSessions_FolderNameDoesNotMatchYamlId_IsSkipped()
    {
        // General invariant: a session's folder name must equal its workspace.yaml `id:`.
        // Anything else is a stray directory (backup, manual copy, leftover migration artifact).
        var sessionDir = Path.Combine(this._tempDir, "renamed-by-user");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            "id: original-id\ncwd: C:\\proj\nsummary: Mismatched");

        var sessionStateFile = Path.Combine(this._tempDir, "sessions.json");
        var result = SessionService.LoadNamedSessions(this._tempDir, sessionStateFile: sessionStateFile);

        Assert.Empty(result);
    }

    [Fact]
    public void LoadNamedSessions_BareNullSummary_TreatsAsEmpty()
    {
        var sessionDir = Path.Combine(this._tempDir, "session-null");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            "id: session-null\ncwd: C:\\myproject\nsummary:");

        var result = SessionService.LoadNamedSessions(this._tempDir);

        Assert.Single(result);
        Assert.Equal("Session session-", result[0].Summary);
        Assert.Equal("myproject", result[0].Folder);
    }
}
