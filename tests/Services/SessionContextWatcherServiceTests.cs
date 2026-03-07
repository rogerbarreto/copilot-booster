public sealed class SessionContextWatcherServiceTests : IDisposable
{
    private readonly SessionContextWatcherService _watcher = new();

    public void Dispose()
    {
        this._watcher.Dispose();
    }

    [Fact]
    public void GetCounts_UnknownSession_ReturnsZeros()
    {
        var counts = this._watcher.GetCounts("nonexistent");

        Assert.Equal(0, counts.Files);
        Assert.Equal(0, counts.Tabs);
    }

    [Fact]
    public void UpdateTabCount_UpdatesCacheAndFiresCountsChanged()
    {
        string? changedId = null;
        this._watcher.CountsChanged += id => changedId = id;

        this._watcher.UpdateTabCount("session-1", 5);

        var counts = this._watcher.GetCounts("session-1");
        Assert.Equal(5, counts.Tabs);
        Assert.Equal(0, counts.Files);
        Assert.Equal("session-1", changedId);
    }

    [Fact]
    public void UpdateTabCount_SameValue_DoesNotFireCountsChanged()
    {
        this._watcher.UpdateTabCount("session-1", 5);

        string? changedId = null;
        this._watcher.CountsChanged += id => changedId = id;

        this._watcher.UpdateTabCount("session-1", 5);

        Assert.Null(changedId);
    }

    [Fact]
    public void UpdateTabCount_DifferentValue_FiresCountsChanged()
    {
        this._watcher.UpdateTabCount("session-1", 5);

        string? changedId = null;
        this._watcher.CountsChanged += id => changedId = id;

        this._watcher.UpdateTabCount("session-1", 10);

        var counts = this._watcher.GetCounts("session-1");
        Assert.Equal(10, counts.Tabs);
        Assert.Equal("session-1", changedId);
    }

    [Fact]
    public void UpdateTabCount_MultipleSessions_TrackedIndependently()
    {
        this._watcher.UpdateTabCount("session-a", 3);
        this._watcher.UpdateTabCount("session-b", 7);

        var countsA = this._watcher.GetCounts("session-a");
        var countsB = this._watcher.GetCounts("session-b");

        Assert.Equal(3, countsA.Tabs);
        Assert.Equal(7, countsB.Tabs);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var watcher = new SessionContextWatcherService();
        var ex = Record.Exception(() => watcher.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var watcher = new SessionContextWatcherService();
        watcher.Dispose();
        var ex = Record.Exception(() => watcher.Dispose());
        Assert.Null(ex);
    }
}
