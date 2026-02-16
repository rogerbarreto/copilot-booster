using Microsoft.Extensions.Logging;

public sealed class LogTests : IDisposable
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
        var logger = new FileLogger(this._logFile);
        logger.LogInformation("test message");

        Assert.True(File.Exists(this._logFile));
        var content = File.ReadAllText(this._logFile);
        Assert.Contains("test message", content);
        Assert.Contains("[INF]", content);
    }

    [Fact]
    public void Log_AppendsMultipleMessages()
    {
        var logger = new FileLogger(this._logFile);
        logger.LogInformation("first");
        logger.LogInformation("second");

        var content = File.ReadAllText(this._logFile);
        Assert.Contains("first", content);
        Assert.Contains("second", content);
        Assert.Equal(2, content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Log_DebugLevel_RespectedByMinLevel()
    {
        var logger = new FileLogger(this._logFile, LogLevel.Information);
        logger.LogDebug("should not appear");

        Assert.False(File.Exists(this._logFile));

        var debugLogger = new FileLogger(this._logFile, LogLevel.Debug);
        debugLogger.LogDebug("should appear");

        Assert.True(File.Exists(this._logFile));
        var content = File.ReadAllText(this._logFile);
        Assert.Contains("should appear", content);
        Assert.Contains("[DBG]", content);
    }
}
