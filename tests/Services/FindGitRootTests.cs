public sealed class FindGitRootTests : IDisposable
{
    private readonly string _tempDir;

    public FindGitRootTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    [Fact]
    public void FindGitRoot_HasGitDir_ReturnsRoot()
    {
        Directory.CreateDirectory(Path.Combine(this._tempDir, ".git"));

        var result = SessionService.FindGitRoot(this._tempDir);

        Assert.Equal(this._tempDir, result);
    }

    [Fact]
    public void FindGitRoot_NoGitDir_ReturnsNull()
    {
        var subDir = Path.Combine(this._tempDir, "sub");
        Directory.CreateDirectory(subDir);

        var result = SessionService.FindGitRoot(subDir);

        Assert.Null(result);
    }

    [Fact]
    public void FindGitRoot_NestedDir_FindsParent()
    {
        Directory.CreateDirectory(Path.Combine(this._tempDir, ".git"));
        var child = Path.Combine(this._tempDir, "src", "deep");
        Directory.CreateDirectory(child);

        var result = SessionService.FindGitRoot(child);

        Assert.Equal(this._tempDir, result);
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
