public sealed class ParseWorkspaceTests : IDisposable
{
    private readonly string _tempDir;

    public ParseWorkspaceTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    [Fact]
    public void ParseWorkspace_ValidFile_ReturnsSessionInfo()
    {
        var wsFile = Path.Combine(this._tempDir, "workspace.yaml");
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
        var wsFile = Path.Combine(this._tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "cwd: C:\\myproject\nsummary: Fix the bug");

        var result = SessionService.ParseWorkspace(wsFile, 1);

        Assert.Null(result);
    }

    [Fact]
    public void ParseWorkspace_MissingSummary_ReturnsFolderOnlyBrackets()
    {
        var wsFile = Path.Combine(this._tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "id: session-456\ncwd: C:\\myproject");

        var result = SessionService.ParseWorkspace(wsFile, 1);

        Assert.NotNull(result);
        Assert.Equal("[myproject]", result!.Summary);
    }

    [Fact]
    public void ParseWorkspace_WithSummary_ReturnsFolderAndSummary()
    {
        var wsFile = Path.Combine(this._tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "id: s1\ncwd: D:\\repos\\app\nsummary: Implement feature X");

        var result = SessionService.ParseWorkspace(wsFile, 5);

        Assert.NotNull(result);
        Assert.Equal("[app] Implement feature X", result!.Summary);
    }

    [Fact]
    public void ParseWorkspace_FileNotFound_ReturnsNull()
    {
        var result = SessionService.ParseWorkspace(Path.Combine(this._tempDir, "missing.yaml"), 1);

        Assert.Null(result);
    }

    [Fact]
    public void ParseWorkspace_EmptyCwd_ReturnsEmptyFolder()
    {
        var wsFile = Path.Combine(this._tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "id: s1\ncwd: \nsummary: test");

        var result = SessionService.ParseWorkspace(wsFile, 1);

        Assert.NotNull(result);
        Assert.Equal("[] test", result!.Summary);
    }

    [Fact]
    public void ParseWorkspace_CwdWithTrailingBackslash_StripsIt()
    {
        var wsFile = Path.Combine(this._tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "id: s1\ncwd: C:\\myproject\\\nsummary: test");

        var result = SessionService.ParseWorkspace(wsFile, 1);

        Assert.NotNull(result);
        Assert.Equal("[myproject] test", result!.Summary);
    }

    [Fact]
    public void ParseWorkspace_NoCwd_ReturnsUnknown()
    {
        var wsFile = Path.Combine(this._tempDir, "workspace.yaml");
        File.WriteAllText(wsFile, "id: s1\nsummary: test");

        var result = SessionService.ParseWorkspace(wsFile, 1);

        Assert.NotNull(result);
        Assert.Equal("Unknown", result!.Cwd);
    }
}
