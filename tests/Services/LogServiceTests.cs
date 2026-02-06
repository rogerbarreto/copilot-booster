using System;
using System.IO;

public class LogTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _logFile;

    public LogTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _logFile = Path.Combine(_tempDir, "sub", "launcher.log");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void Log_CreatesDirectoryAndAppendsMessage()
    {
        LogService.Log("test message", _logFile);

        Assert.True(File.Exists(_logFile));
        var content = File.ReadAllText(_logFile);
        Assert.Contains("test message", content);
    }

    [Fact]
    public void Log_AppendsMultipleMessages()
    {
        LogService.Log("first", _logFile);
        LogService.Log("second", _logFile);

        var content = File.ReadAllText(_logFile);
        Assert.Contains("first", content);
        Assert.Contains("second", content);
        Assert.Equal(2, content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }
}
