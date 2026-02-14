public sealed class UpdateSessionTests : IDisposable
{
    private readonly string _tempDir;

    public UpdateSessionTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    [Fact]
    public void UpdateSession_UpdatesExistingFields()
    {
        var sessionDir = Path.Combine(this._tempDir, "session1");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            "id: session1\ncwd: C:\\old-path\nsummary: Old name\nname: Old name");

        var result = SessionService.UpdateSession(sessionDir, "New name", "C:\\new-path");

        Assert.True(result);
        var lines = File.ReadAllLines(Path.Combine(sessionDir, "workspace.yaml"));
        Assert.Contains("id: session1", lines);
        Assert.Contains("summary: New name", lines);
        Assert.Contains("name: New name", lines);
        Assert.Contains("cwd: C:\\new-path", lines);
    }

    [Fact]
    public void UpdateSession_AppendsMissingFields()
    {
        var sessionDir = Path.Combine(this._tempDir, "session2");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            "id: session2");

        var result = SessionService.UpdateSession(sessionDir, "Added summary", "C:\\added");

        Assert.True(result);
        var lines = File.ReadAllLines(Path.Combine(sessionDir, "workspace.yaml"));
        Assert.Contains("id: session2", lines);
        Assert.Contains("summary: Added summary", lines);
        Assert.Contains("name: Added summary", lines);
        Assert.Contains("cwd: C:\\added", lines);
    }

    [Fact]
    public void UpdateSession_PreservesOtherLines()
    {
        var sessionDir = Path.Combine(this._tempDir, "session3");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            "id: session3\ncwd: C:\\old\nsummary: Old\nname: Old\ncustomField: keep-me");

        var result = SessionService.UpdateSession(sessionDir, "Updated", "C:\\updated");

        Assert.True(result);
        var lines = File.ReadAllLines(Path.Combine(sessionDir, "workspace.yaml"));
        Assert.Contains("customField: keep-me", lines);
        Assert.Contains("summary: Updated", lines);
        Assert.Contains("name: Updated", lines);
        Assert.Contains("cwd: C:\\updated", lines);
    }

    [Fact]
    public void UpdateSession_CreatesFileWhenMissing()
    {
        var sessionDir = Path.Combine(this._tempDir, "missing");
        Directory.CreateDirectory(sessionDir);

        var result = SessionService.UpdateSession(sessionDir, "My Session", @"C:\work");

        Assert.True(result);
        var wsFile = Path.Combine(sessionDir, "workspace.yaml");
        Assert.True(File.Exists(wsFile));
        var content = File.ReadAllText(wsFile);
        Assert.Contains("summary: My Session", content);
        Assert.Contains(@"cwd: C:\work", content);
    }

    [Fact]
    public void UpdateSession_ReturnsFalseForMissingDirectory()
    {
        var sessionDir = Path.Combine(this._tempDir, "nonexistent");

        var result = SessionService.UpdateSession(sessionDir, "x", "y");

        Assert.False(result);
    }
}
