public class LogTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _logFile;

    public LogTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        this._logFile = Path.Combine(this._tempDir, "sub", "launcher.log");
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    [Fact]
    public void Log_CreatesDirectoryAndAppendsMessage()
    {
        LogService.Log("test message", this._logFile);

        Assert.True(File.Exists(this._logFile));
        var content = File.ReadAllText(this._logFile);
        Assert.Contains("test message", content);
    }

    [Fact]
    public void Log_AppendsMultipleMessages()
    {
        LogService.Log("first", this._logFile);
        LogService.Log("second", this._logFile);

        var content = File.ReadAllText(this._logFile);
        Assert.Contains("first", content);
        Assert.Contains("second", content);
        Assert.Equal(2, content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }
}
