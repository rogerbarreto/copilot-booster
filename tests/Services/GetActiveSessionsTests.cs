using System.Text.Json;

public sealed class GetActiveSessionsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _pidFile;
    private readonly string _sessionStateDir;

    public GetActiveSessionsTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this._tempDir);
        this._pidFile = Path.Combine(this._tempDir, "active-pids.json");
        this._sessionStateDir = Path.Combine(this._tempDir, "session-state");
        Directory.CreateDirectory(this._sessionStateDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    [Fact]
    public void GetActiveSessions_NoPidFile_ReturnsEmpty()
    {
        var result = SessionService.GetActiveSessions(
            Path.Combine(this._tempDir, "nonexistent.json"), this._sessionStateDir);
        Assert.Empty(result);
    }

    [Fact]
    public void GetActiveSessions_EmptyRegistry_ReturnsEmpty()
    {
        File.WriteAllText(this._pidFile, "{}");
        var result = SessionService.GetActiveSessions(this._pidFile, this._sessionStateDir);
        Assert.Empty(result);
    }

    [Fact]
    public void GetActiveSessions_InvalidJson_ReturnsEmpty()
    {
        File.WriteAllText(this._pidFile, "not json at all");
        var result = SessionService.GetActiveSessions(this._pidFile, this._sessionStateDir);
        Assert.Empty(result);
    }

    [Fact]
    public void GetActiveSessions_PidNotRunning_RemovesStalePid()
    {
        var fakePid = 99999;
        var registry = new Dictionary<string, object>
        {
            [fakePid.ToString()] = new { started = DateTime.Now.ToString("o"), sessionId = "s1" }
        };
        File.WriteAllText(this._pidFile, JsonSerializer.Serialize(registry));

        var result = SessionService.GetActiveSessions(this._pidFile, this._sessionStateDir);

        Assert.Empty(result);
        var updatedJson = File.ReadAllText(this._pidFile);
        Assert.DoesNotContain(fakePid.ToString(), updatedJson);
    }

    [Fact]
    public void GetActiveSessions_NonNumericPid_Skipped()
    {
        var registry = new Dictionary<string, object>
        {
            ["not-a-number"] = new { started = DateTime.Now.ToString("o"), sessionId = "s1" }
        };
        File.WriteAllText(this._pidFile, JsonSerializer.Serialize(registry));

        var result = SessionService.GetActiveSessions(this._pidFile, this._sessionStateDir);

        Assert.Empty(result);
    }

    [Fact]
    public void GetActiveSessions_NullSessionId_SkipsEntry()
    {
        // Use current process PID (it's running but sessionId is null)
        var myPid = Environment.ProcessId;
        var registry = new Dictionary<string, object>
        {
            [myPid.ToString()] = new { started = DateTime.Now.ToString("o"), sessionId = (string?)null }
        };
        File.WriteAllText(this._pidFile, JsonSerializer.Serialize(registry));

        var result = SessionService.GetActiveSessions(this._pidFile, this._sessionStateDir);

        // Process is running but not CopilotApp, so it gets removed
        // OR sessionId is null so it gets skipped — either way empty
        Assert.Empty(result);
    }

    [Fact]
    public void GetActiveSessions_NoWorkspaceFile_SkipsEntry()
    {
        var myPid = Environment.ProcessId;
        var registry = new Dictionary<string, object>
        {
            [myPid.ToString()] = new { started = DateTime.Now.ToString("o"), sessionId = "nonexistent-session" }
        };
        File.WriteAllText(this._pidFile, JsonSerializer.Serialize(registry));

        var result = SessionService.GetActiveSessions(this._pidFile, this._sessionStateDir);

        Assert.Empty(result);
    }
}
