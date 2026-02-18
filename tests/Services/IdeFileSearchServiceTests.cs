public sealed class IdeFileSearchServiceTests : IDisposable
{
    private readonly string _tempDir;

    public IdeFileSearchServiceTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), $"ide-search-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    private void CreateFile(string relativePath)
    {
        var fullPath = Path.Combine(this._tempDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(fullPath, "");
    }

    [Fact]
    public void Search_WithNullPattern_ReturnsEmpty()
    {
        var result = IdeFileSearchService.Search(this._tempDir, null!, []);
        Assert.Empty(result);
    }

    [Fact]
    public void Search_WithEmptyPattern_ReturnsEmpty()
    {
        var result = IdeFileSearchService.Search(this._tempDir, "", []);
        Assert.Empty(result);
    }

    [Fact]
    public void Search_WithNonExistentDirectory_ReturnsEmpty()
    {
        var result = IdeFileSearchService.Search(@"C:\nonexistent-dir-12345", "*.sln", []);
        Assert.Empty(result);
    }

    [Fact]
    public void Search_WithValidPattern_ReturnsMatchingFiles()
    {
        this.CreateFile("a.sln");
        this.CreateFile(Path.Combine("sub", "b.sln"));
        this.CreateFile("c.txt");

        var result = IdeFileSearchService.Search(this._tempDir, "*.sln", []);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, f => f.EndsWith("a.sln"));
        Assert.Contains(result, f => f.EndsWith("b.sln"));
    }

    [Fact]
    public void Search_WithMultiplePatternsDelimitedBySemicolon_FindsAll()
    {
        this.CreateFile("a.sln");
        this.CreateFile("b.csproj");
        this.CreateFile("c.txt");

        var result = IdeFileSearchService.Search(this._tempDir, "*.sln;*.csproj", []);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, f => f.EndsWith("a.sln"));
        Assert.Contains(result, f => f.EndsWith("b.csproj"));
    }

    [Fact]
    public void Search_ResultsSortedByDepth()
    {
        this.CreateFile("root.sln");
        this.CreateFile(Path.Combine("a", "mid.sln"));
        this.CreateFile(Path.Combine("a", "b", "deep.sln"));

        var result = IdeFileSearchService.Search(this._tempDir, "*.sln", []);

        Assert.True(result.Count >= 2);
        // Shallowest (root.sln) should come first
        Assert.EndsWith("root.sln", result[0]);
    }

    [Fact]
    public void Search_RespectsMaxResults()
    {
        for (int i = 0; i < 8; i++)
        {
            this.CreateFile($"file{i}.sln");
        }

        var result = IdeFileSearchService.Search(this._tempDir, "*.sln", []);

        Assert.True(result.Count <= 5);
    }

    [Fact]
    public void Search_RespectsIgnoredDirs()
    {
        this.CreateFile("root.sln");
        this.CreateFile(Path.Combine("node_modules", "ignored.sln"));

        var result = IdeFileSearchService.Search(this._tempDir, "*.sln", ["node_modules"]);

        Assert.Single(result);
        Assert.EndsWith("root.sln", result[0]);
    }

    [Fact]
    public void Search_WithNoMatchingFiles_ReturnsEmpty()
    {
        this.CreateFile("a.txt");
        this.CreateFile("b.md");

        var result = IdeFileSearchService.Search(this._tempDir, "*.sln", []);

        Assert.Empty(result);
    }
}
