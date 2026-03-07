namespace CopilotBooster.IntegrationTests.Integration;

public sealed class FileSystemWatcherIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemWatcherIntegrationTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), "copilot-booster-itest-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(this._tempDir))
            {
                Directory.Delete(this._tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    // ── WorkspaceYamlWatcherService ──────────────────────────────────────

    [Fact]
    public void WorkspaceYamlWatcher_FileCreated_FiresWorkspaceChanged()
    {
        using var watcher = new WorkspaceYamlWatcherService(this._tempDir);
        string? changedSessionId = null;
        using var fired = new ManualResetEventSlim();

        watcher.WorkspaceChanged += sid => { changedSessionId = sid; fired.Set(); };
        watcher.StartWatching();

        var sessionDir = Path.Combine(this._tempDir, "test-session-abc");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"), "id: test-session-abc");

        Assert.True(fired.Wait(5000, TestContext.Current.CancellationToken), "WorkspaceChanged should fire when workspace.yaml is created");
        Assert.Equal("test-session-abc", changedSessionId);
    }

    [Fact]
    public void WorkspaceYamlWatcher_FileModified_FiresWorkspaceChanged()
    {
        var sessionDir = Path.Combine(this._tempDir, "test-session-mod");
        Directory.CreateDirectory(sessionDir);
        var filePath = Path.Combine(sessionDir, "workspace.yaml");
        File.WriteAllText(filePath, "initial: true");

        Thread.Sleep(200);

        using var watcher = new WorkspaceYamlWatcherService(this._tempDir);
        using var fired = new ManualResetEventSlim();
        string? changedSessionId = null;

        watcher.WorkspaceChanged += sid => { changedSessionId = sid; fired.Set(); };
        watcher.StartWatching();

        File.AppendAllText(filePath, "\nmodified: true");

        Assert.True(fired.Wait(5000, TestContext.Current.CancellationToken), "WorkspaceChanged should fire when workspace.yaml is modified");
        Assert.Equal("test-session-mod", changedSessionId);
    }

    [Fact]
    public void WorkspaceYamlWatcher_FileDeleted_FiresWorkspaceDeleted()
    {
        var sessionDir = Path.Combine(this._tempDir, "test-session-del");
        Directory.CreateDirectory(sessionDir);
        var filePath = Path.Combine(sessionDir, "workspace.yaml");
        File.WriteAllText(filePath, "delete-me: true");

        Thread.Sleep(200);

        using var watcher = new WorkspaceYamlWatcherService(this._tempDir);
        using var fired = new ManualResetEventSlim();
        string? deletedSessionId = null;

        watcher.WorkspaceDeleted += sid => { deletedSessionId = sid; fired.Set(); };
        watcher.StartWatching();

        File.Delete(filePath);

        Assert.True(fired.Wait(5000, TestContext.Current.CancellationToken), "WorkspaceDeleted should fire when workspace.yaml is deleted");
        Assert.Equal("test-session-del", deletedSessionId);
    }

    // ── SessionContextWatcherService ─────────────────────────────────────

    [Fact]
    public void SessionContextWatcher_NonReservedFileCreated_CountsChanged()
    {
        var sessionDir = Path.Combine(this._tempDir, "test-session-xyz");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"), "id: test-session-xyz");

        using var watcher = new SessionContextWatcherService(this._tempDir);
        string? changedId = null;
        using var fired = new ManualResetEventSlim();

        watcher.CountsChanged += sid => { changedId = sid; fired.Set(); };
        watcher.StartWatching();

        File.WriteAllText(Path.Combine(sessionDir, "plan.md"), "# Plan");

        Assert.True(fired.Wait(5000, TestContext.Current.CancellationToken), "CountsChanged should fire for non-reserved file creation");
        Assert.Equal("test-session-xyz", changedId);

        var counts = watcher.GetCounts("test-session-xyz");
        Assert.Equal(1, counts.Files);
    }

    [Fact]
    public void SessionContextWatcher_ReservedFileCreated_DoesNotFire()
    {
        var sessionDir = Path.Combine(this._tempDir, "test-session-reserved");
        Directory.CreateDirectory(sessionDir);

        using var watcher = new SessionContextWatcherService(this._tempDir);
        using var fired = new ManualResetEventSlim();

        watcher.CountsChanged += _ => fired.Set();
        watcher.StartWatching();

        // Create a reserved file — should be filtered
        File.WriteAllText(Path.Combine(sessionDir, "events.jsonl"), "{}");

        Assert.False(fired.Wait(1000, TestContext.Current.CancellationToken), "CountsChanged should NOT fire for reserved file creation");
    }

    [Fact]
    public void SessionContextWatcher_PrimeCache_PopulatesCounts()
    {
        var sessionDir = Path.Combine(this._tempDir, "test-session-prime");
        Directory.CreateDirectory(sessionDir);

        // Reserved files (should not count)
        File.WriteAllText(Path.Combine(sessionDir, "workspace.yaml"), "id: test-session-prime");
        File.WriteAllText(Path.Combine(sessionDir, "events.jsonl"), "{}");

        // Non-reserved files (should count)
        File.WriteAllText(Path.Combine(sessionDir, "plan.md"), "# Plan");
        File.WriteAllText(Path.Combine(sessionDir, "notes.txt"), "Notes");

        using var watcher = new SessionContextWatcherService(this._tempDir);
        watcher.PrimeCache();

        var counts = watcher.GetCounts("test-session-prime");
        Assert.Equal(2, counts.Files);
        Assert.Equal(0, counts.Tabs);
    }

    [Fact]
    public void SessionContextWatcher_FileInReservedDir_FilteredOut()
    {
        var sessionDir = Path.Combine(this._tempDir, "test-session-resdir");
        Directory.CreateDirectory(sessionDir);

        // Create a file inside a reserved directory
        var reservedDir = Path.Combine(sessionDir, "rewind-snapshots");
        Directory.CreateDirectory(reservedDir);
        File.WriteAllText(Path.Combine(reservedDir, "snapshot.json"), "{}");

        // Also create one non-reserved file for baseline
        File.WriteAllText(Path.Combine(sessionDir, "plan.md"), "# Plan");

        using var watcher = new SessionContextWatcherService(this._tempDir);
        watcher.PrimeCache();

        var counts = watcher.GetCounts("test-session-resdir");
        Assert.Equal(1, counts.Files);
    }

    // ── Session ID extraction (still valid) ──────────────────────────────

    [Theory]
    [InlineData("abc-123-def", "workspace.yaml")]
    [InlineData("session-42", "workspace.yaml")]
    [InlineData("a1b2c3d4-e5f6-7890-abcd-ef1234567890", "workspace.yaml")]
    public void SessionIdExtraction_FromPath_ReturnsParentDirName(string expectedSessionId, string fileName)
    {
        var fullPath = Path.Combine(this._tempDir, expectedSessionId, fileName);
        var sessionId = Path.GetFileName(Path.GetDirectoryName(fullPath));
        Assert.Equal(expectedSessionId, sessionId);
    }

    [Fact]
    public void SessionIdExtraction_FromNestedReservedDir_StillExtractsTopLevelSession()
    {
        var fullPath = Path.Combine(this._tempDir, "my-session-id", "checkpoints", "snapshot.json");
        var relativePath = Path.GetRelativePath(this._tempDir, fullPath);
        var segments = relativePath.Split(Path.DirectorySeparatorChar);

        Assert.Equal("my-session-id", segments[0]);
        Assert.True(segments.Length >= 3);
    }
}
