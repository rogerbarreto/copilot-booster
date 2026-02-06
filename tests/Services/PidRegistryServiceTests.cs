using System.IO;

public class PidRegistryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _pidFile;

    public PidRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _pidFile = Path.Combine(_tempDir, "active-pids.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void RegisterPid_CreatesRegistryFile()
    {
        PidRegistryService.RegisterPid(1234, _tempDir, _pidFile);

        Assert.True(File.Exists(_pidFile));
        var json = File.ReadAllText(_pidFile);
        Assert.Contains("1234", json);
    }

    [Fact]
    public void RegisterPid_WithCorruptExistingFile_OverwritesCleanly()
    {
        File.WriteAllText(_pidFile, "corrupt json {{{");
        PidRegistryService.RegisterPid(5555, _tempDir, _pidFile);

        Assert.True(File.Exists(_pidFile));
        var json = File.ReadAllText(_pidFile);
        Assert.Contains("5555", json);
    }

    [Fact]
    public void RegisterPid_CreatesDirectory()
    {
        var subDir = Path.Combine(_tempDir, "newsubdir");
        var subFile = Path.Combine(subDir, "pids.json");

        PidRegistryService.RegisterPid(1234, subDir, subFile);

        Assert.True(Directory.Exists(subDir));
        Assert.True(File.Exists(subFile));
    }

    [Fact]
    public void RegisterPid_AddsToExistingRegistry()
    {
        PidRegistryService.RegisterPid(1111, _tempDir, _pidFile);
        PidRegistryService.RegisterPid(2222, _tempDir, _pidFile);

        var json = File.ReadAllText(_pidFile);
        Assert.Contains("1111", json);
        Assert.Contains("2222", json);
    }

    [Fact]
    public void UnregisterPid_RemovesPid()
    {
        PidRegistryService.RegisterPid(1234, _tempDir, _pidFile);
        PidRegistryService.RegisterPid(5678, _tempDir, _pidFile);

        PidRegistryService.UnregisterPid(1234, _pidFile);

        var json = File.ReadAllText(_pidFile);
        Assert.DoesNotContain("1234", json);
        Assert.Contains("5678", json);
    }

    [Fact]
    public void UnregisterPid_NoFileExists_DoesNotThrow()
    {
        var nonExistent = Path.Combine(_tempDir, "no-such-file.json");
        var ex = Record.Exception(() => PidRegistryService.UnregisterPid(1234, nonExistent));
        Assert.Null(ex);
    }

    [Fact]
    public void UpdatePidSessionId_UpdatesExistingPid()
    {
        PidRegistryService.RegisterPid(1234, _tempDir, _pidFile);
        PidRegistryService.UpdatePidSessionId(1234, "session-abc", _pidFile);

        var json = File.ReadAllText(_pidFile);
        Assert.Contains("session-abc", json);
    }

    [Fact]
    public void UpdatePidSessionId_NoFileExists_DoesNotThrow()
    {
        var nonExistent = Path.Combine(_tempDir, "no-such-file.json");
        var ex = Record.Exception(() => PidRegistryService.UpdatePidSessionId(1234, "s1", nonExistent));
        Assert.Null(ex);
    }
}
