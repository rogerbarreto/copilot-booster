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
    public void UpdateSessionCwd_UpdatesExistingCwd()
    {
        var sessionDir = Path.Combine(this._tempDir, "session1");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            "id: session1\ncwd: C:\\old-path\nsummary: Old name\nname: Old name");

        var result = SessionService.UpdateSessionCwd(sessionDir, "C:\\new-path");

        Assert.True(result);
        var lines = File.ReadAllLines(Path.Combine(sessionDir, "workspace.yaml"));
        Assert.Contains("id: session1", lines);
        Assert.Contains("summary: Old name", lines);
        Assert.Contains("name: Old name", lines);
        Assert.Contains("cwd: C:\\new-path", lines);
    }

    [Fact]
    public void UpdateSessionCwd_AppendsMissingCwd()
    {
        var sessionDir = Path.Combine(this._tempDir, "session2");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            "id: session2");

        var result = SessionService.UpdateSessionCwd(sessionDir, "C:\\added");

        Assert.True(result);
        var lines = File.ReadAllLines(Path.Combine(sessionDir, "workspace.yaml"));
        Assert.Contains("id: session2", lines);
        Assert.Contains("cwd: C:\\added", lines);
    }

    [Fact]
    public void UpdateSessionCwd_PreservesOtherLines()
    {
        var sessionDir = Path.Combine(this._tempDir, "session3");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"),
            "id: session3\ncwd: C:\\old\nsummary: Old\nname: Old\ncustomField: keep-me");

        var result = SessionService.UpdateSessionCwd(sessionDir, "C:\\updated");

        Assert.True(result);
        var lines = File.ReadAllLines(Path.Combine(sessionDir, "workspace.yaml"));
        Assert.Contains("customField: keep-me", lines);
        Assert.Contains("summary: Old", lines);
        Assert.Contains("name: Old", lines);
        Assert.Contains("cwd: C:\\updated", lines);
    }

    [Fact]
    public void UpdateSessionCwd_ReturnsFalseForMissingFile()
    {
        var sessionDir = Path.Combine(this._tempDir, "missing");
        Directory.CreateDirectory(sessionDir);

        var result = SessionService.UpdateSessionCwd(sessionDir, "y");

        Assert.False(result);
    }

    [Fact]
    public void UpdateSessionCwd_ReturnsFalseForMissingDirectory()
    {
        var sessionDir = Path.Combine(this._tempDir, "nonexistent");

        var result = SessionService.UpdateSessionCwd(sessionDir, "y");

        Assert.False(result);
    }
}
