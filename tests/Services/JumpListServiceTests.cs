public class ShouldBackgroundUpdateTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _lastUpdateFile;

    public ShouldBackgroundUpdateTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this._tempDir);
        this._lastUpdateFile = Path.Combine(this._tempDir, "lastupdate.txt");
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    [Fact]
    public void ShouldBackgroundUpdate_NoFile_ReturnsTrue()
    {
        var result = JumpListService.ShouldBackgroundUpdate(TimeSpan.FromMinutes(1), this._lastUpdateFile);
        Assert.True(result);
    }

    [Fact]
    public void ShouldBackgroundUpdate_RecentUpdate_ReturnsFalse()
    {
        File.WriteAllText(this._lastUpdateFile, DateTime.UtcNow.ToString("o"));

        var result = JumpListService.ShouldBackgroundUpdate(TimeSpan.FromMinutes(5), this._lastUpdateFile);

        Assert.False(result);
    }

    [Fact]
    public void ShouldBackgroundUpdate_OldUpdate_ReturnsTrue()
    {
        File.WriteAllText(this._lastUpdateFile, DateTime.UtcNow.AddHours(-2).ToString("o"));

        var result = JumpListService.ShouldBackgroundUpdate(TimeSpan.FromMinutes(1), this._lastUpdateFile);

        Assert.True(result);
    }

    [Fact]
    public void ShouldBackgroundUpdate_InvalidContent_ReturnsTrue()
    {
        File.WriteAllText(this._lastUpdateFile, "not a date");

        var result = JumpListService.ShouldBackgroundUpdate(TimeSpan.FromMinutes(1), this._lastUpdateFile);

        Assert.True(result);
    }
}
