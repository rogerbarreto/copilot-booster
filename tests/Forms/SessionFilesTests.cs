public sealed class SessionFilesTests : IDisposable
{
    private readonly string _tempDir;

    public SessionFilesTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), $"session-files-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(this._tempDir))
        {
            Directory.Delete(this._tempDir, true);
        }
    }

    private string CreateSessionDir(string sessionId)
    {
        var dir = Path.Combine(this._tempDir, sessionId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CreateFile(string dir, string relativePath)
    {
        var fullPath = Path.Combine(dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "test");
    }

    [Fact]
    public void GetSessionFiles_ReturnsEmpty_WhenSessionDirDoesNotExist()
    {
        var result = MainForm.GetSessionFiles(this._tempDir, "nonexistent");
        Assert.Empty(result);
    }

    [Fact]
    public void GetSessionFiles_ExcludesReservedFiles()
    {
        var sid = "test-session";
        var dir = this.CreateSessionDir(sid);

        CreateFile(dir, "events.jsonl");
        CreateFile(dir, "workspace.yaml");
        CreateFile(dir, "session.db");
        CreateFile(dir, "plan.md");

        var result = MainForm.GetSessionFiles(this._tempDir, sid);

        // Only plan.md should be included — the rest are reserved
        Assert.Single(result);
        Assert.Equal("plan.md", result[0].Name);
    }

    [Fact]
    public void GetSessionFiles_ExcludesRewindSnapshotsFolder()
    {
        var sid = "test-session";
        var dir = this.CreateSessionDir(sid);

        CreateFile(dir, Path.Combine("rewind-snapshots", "index.json"));
        CreateFile(dir, Path.Combine("rewind-snapshots", "backups", "abc123-456"));
        CreateFile(dir, "plan.md");

        var result = MainForm.GetSessionFiles(this._tempDir, sid);

        Assert.Single(result);
        Assert.Equal("plan.md", result[0].Name);
    }

    [Fact]
    public void GetSessionFiles_IncludesFilesSubfolder_WithRelativePaths()
    {
        var sid = "test-session";
        var dir = this.CreateSessionDir(sid);

        CreateFile(dir, Path.Combine("files", "linkedin-cv.md"));
        CreateFile(dir, Path.Combine("files", "meeting-prep.md"));

        var result = MainForm.GetSessionFiles(this._tempDir, sid);

        Assert.Equal(2, result.Count);
        var names = result.Select(f => f.Name).OrderBy(n => n).ToList();
        Assert.Equal(Path.Combine("files", "linkedin-cv.md"), names[0]);
        Assert.Equal(Path.Combine("files", "meeting-prep.md"), names[1]);
    }

    [Fact]
    public void GetSessionFiles_IncludesNestedSubfolders()
    {
        var sid = "test-session";
        var dir = this.CreateSessionDir(sid);

        CreateFile(dir, Path.Combine("files", "research", "notes.md"));
        CreateFile(dir, Path.Combine("files", "research", "data.csv"));

        var result = MainForm.GetSessionFiles(this._tempDir, sid);

        Assert.Equal(2, result.Count);
        var names = result.Select(f => f.Name).OrderBy(n => n).ToList();
        Assert.Equal(Path.Combine("files", "research", "data.csv"), names[0]);
        Assert.Equal(Path.Combine("files", "research", "notes.md"), names[1]);
    }

    [Fact]
    public void GetSessionFiles_MixedContent_OnlyReturnsUserFiles()
    {
        var sid = "test-session";
        var dir = this.CreateSessionDir(sid);

        // Reserved files
        CreateFile(dir, "events.jsonl");
        CreateFile(dir, "workspace.yaml");
        CreateFile(dir, "session.db");

        // Reserved folder
        CreateFile(dir, Path.Combine("rewind-snapshots", "index.json"));
        CreateFile(dir, Path.Combine("rewind-snapshots", "backups", "snap1"));

        // User files
        CreateFile(dir, "plan.md");
        CreateFile(dir, Path.Combine("files", "cv.md"));
        CreateFile(dir, Path.Combine("files", "research", "deep-dive.txt"));

        var result = MainForm.GetSessionFiles(this._tempDir, sid);

        Assert.Equal(3, result.Count);
        var names = result.Select(f => f.Name).OrderBy(n => n).ToList();
        Assert.Equal(Path.Combine("files", "cv.md"), names[0]);
        Assert.Equal(Path.Combine("files", "research", "deep-dive.txt"), names[1]);
        Assert.Equal("plan.md", names[2]);
    }

    [Fact]
    public void GetSessionFiles_ReturnsFullPaths()
    {
        var sid = "test-session";
        var dir = this.CreateSessionDir(sid);

        CreateFile(dir, "plan.md");
        CreateFile(dir, Path.Combine("files", "notes.txt"));

        var result = MainForm.GetSessionFiles(this._tempDir, sid);

        foreach (var (name, fullPath) in result)
        {
            Assert.True(File.Exists(fullPath), $"Full path should exist: {fullPath}");
            Assert.True(fullPath.EndsWith(name), $"Full path should end with relative name: {fullPath} -> {name}");
        }
    }

    [Fact]
    public void GetSessionFiles_ReservedFilenames_CaseInsensitive()
    {
        var sid = "test-session";
        var dir = this.CreateSessionDir(sid);

        CreateFile(dir, "Events.JSONL");
        CreateFile(dir, "WORKSPACE.yaml");
        CreateFile(dir, "Session.DB");

        var result = MainForm.GetSessionFiles(this._tempDir, sid);

        Assert.Empty(result);
    }

    [Fact]
    public void GetSessionFiles_RewindSnapshots_CaseInsensitive()
    {
        var sid = "test-session";
        var dir = this.CreateSessionDir(sid);

        CreateFile(dir, Path.Combine("Rewind-Snapshots", "index.json"));

        var result = MainForm.GetSessionFiles(this._tempDir, sid);

        Assert.Empty(result);
    }
}
