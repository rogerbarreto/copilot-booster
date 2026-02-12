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
        Assert.Equal("[project] My session", result[0].Summary);
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
        Assert.Contains(result, s => s.Id == "s2" && s.Summary == "[b]");
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
        var s1 = Path.Combine(this._tempDir, "old-session");
        Directory.CreateDirectory(s1);
        File.WriteAllText(Path.Combine(s1, "workspace.yaml"),
            "id: old\ncwd: C:\\old\nsummary: Old session");
        Directory.SetLastWriteTime(s1, DateTime.Now.AddHours(-2));

        var s2 = Path.Combine(this._tempDir, "new-session");
        Directory.CreateDirectory(s2);
        File.WriteAllText(Path.Combine(s2, "workspace.yaml"),
            "id: new\ncwd: C:\\new\nsummary: New session");
        Directory.SetLastWriteTime(s2, DateTime.Now);

        var result = SessionService.LoadNamedSessions(this._tempDir);

        Assert.Equal(2, result.Count);
        Assert.Equal("new", result[0].Id);
        Assert.Equal("old", result[1].Id);
    }
}
