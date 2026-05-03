namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// Integration tests for WindowHandleCacheService persistence of copilot-host entries across app restarts.
/// Tests prove that CopilotHostInfo entries survive cache save/load and that PID-revalidation drops stale entries.
/// </summary>
public sealed class WindowHandleCacheRestartTests : IDisposable
{
    private readonly string _tempCacheFile;
    private Form? _testForm;

    public WindowHandleCacheRestartTests()
    {
        this._tempCacheFile = Path.Combine(Path.GetTempPath(), $"cache-test-{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        this._testForm?.Dispose();
        try { File.Delete(this._tempCacheFile); } catch { }
    }

    private IntPtr CreateRealHwnd()
    {
        this._testForm = new Form { Width = 100, Height = 100, ShowInTaskbar = false, Opacity = 0 };
        this._testForm.Show();
        var handle = this._testForm.Handle;
        return handle;
    }

    [Fact]
    public void CopilotHostEntry_RoundTrip_Persists()
    {
        var hwnd = this.CreateRealHwnd();
        var pid = Environment.ProcessId;
        var copilotPid = pid + 1;
        var sessionId = Guid.NewGuid().ToString();

        var hostInfo = new CopilotHostInfo(hwnd, pid, copilotPid, "TestProcess", "Test Host");
        var copilotHosts = new Dictionary<string, CopilotHostInfo> { [sessionId] = hostInfo };

        WindowHandleCacheService.Save(this._tempCacheFile, [], [], [], [], copilotHosts);

        var (_, _, _, _, loadedHosts) = WindowHandleCacheService.Load(this._tempCacheFile);

        Assert.Single(loadedHosts);
        Assert.True(loadedHosts.ContainsKey(sessionId));
        var loaded = loadedHosts[sessionId];
        Assert.Equal(hwnd, loaded.HostHwnd);
        Assert.Equal(pid, loaded.HostPid);
        Assert.Equal(copilotPid, loaded.CopilotPid);
        Assert.Equal("TestProcess", loaded.HostProcessName);
        Assert.Equal("Test Host", loaded.HostKindLabel);
    }

    [Fact]
    public void CopilotHostEntry_StalePidDropped_OnLoad()
    {
        var hwnd = this.CreateRealHwnd();
        var actualPid = Environment.ProcessId;
        var fakePid = 999999;
        var copilotPid = actualPid + 1;
        var sessionId = Guid.NewGuid().ToString();

        var hostInfo = new CopilotHostInfo(hwnd, fakePid, copilotPid, "TestProcess", "Test Host");
        var copilotHosts = new Dictionary<string, CopilotHostInfo> { [sessionId] = hostInfo };

        WindowHandleCacheService.Save(this._tempCacheFile, [], [], [], [], copilotHosts);

        var (_, _, _, _, loadedHosts) = WindowHandleCacheService.Load(this._tempCacheFile);

        Assert.Empty(loadedHosts);
    }

    [Fact]
    public void CopilotHostEntry_DeadHwndDropped_OnLoad()
    {
        var fakeHwnd = new IntPtr(0xDEADBEEF);
        var pid = Environment.ProcessId;
        var copilotPid = pid + 1;
        var sessionId = Guid.NewGuid().ToString();

        var hostInfo = new CopilotHostInfo(fakeHwnd, pid, copilotPid, "TestProcess", "Test Host");
        var copilotHosts = new Dictionary<string, CopilotHostInfo> { [sessionId] = hostInfo };

        WindowHandleCacheService.Save(this._tempCacheFile, [], [], [], [], copilotHosts);

        var (_, _, _, _, loadedHosts) = WindowHandleCacheService.Load(this._tempCacheFile);

        Assert.Empty(loadedHosts);
    }

    [Fact]
    public void BackwardCompat_ExistingNonHostEntries_StillLoad()
    {
        var oldFormatJson = """
            [
                {"SessionId":"s1","Type":"ide","Name":"vscode","FolderPath":"C:\\project","Hwnd":1234567},
                {"SessionId":"s2","Type":"explorer","Name":"Project","FolderPath":null,"Hwnd":2345678}
            ]
            """;

        File.WriteAllText(this._tempCacheFile, oldFormatJson);
        var (_, _, _, _, copilotHosts) = WindowHandleCacheService.Load(this._tempCacheFile);

        Assert.Empty(copilotHosts);
    }

    [Fact]
    public void CopilotHostEntry_MultipleSessions_AllPersist()
    {
        var hwnd = this.CreateRealHwnd();
        var pid = Environment.ProcessId;
        var s1 = Guid.NewGuid().ToString();
        var s2 = Guid.NewGuid().ToString();

        var copilotHosts = new Dictionary<string, CopilotHostInfo>
        {
            [s1] = new CopilotHostInfo(hwnd, pid, pid + 1, "Process1", "Host1"),
            [s2] = new CopilotHostInfo(hwnd, pid, pid + 2, "Process2", "Host2")
        };

        WindowHandleCacheService.Save(this._tempCacheFile, [], [], [], [], copilotHosts);

        var (_, _, _, _, loadedHosts) = WindowHandleCacheService.Load(this._tempCacheFile);

        Assert.Equal(2, loadedHosts.Count);
        Assert.True(loadedHosts.ContainsKey(s1));
        Assert.True(loadedHosts.ContainsKey(s2));
        Assert.Equal("Process1", loadedHosts[s1].HostProcessName);
        Assert.Equal("Process2", loadedHosts[s2].HostProcessName);
    }

    [Fact]
    public void CopilotHostEntry_MixedWithOtherTypes_AllPersist()
    {
        var hwnd = this.CreateRealHwnd();
        var pid = Environment.ProcessId;
        var sessionId = Guid.NewGuid().ToString();

        var copilotHosts = new Dictionary<string, CopilotHostInfo>
        {
            [sessionId] = new CopilotHostInfo(hwnd, pid, pid + 1, "TestProcess", "Test Host")
        };

        var edges = new Dictionary<string, EdgeWorkspaceService>
        {
            [sessionId] = new EdgeWorkspaceService(sessionId) { CachedHwnd = hwnd }
        };

        WindowHandleCacheService.Save(this._tempCacheFile, [], [], edges, [], copilotHosts);

        var (_, _, loadedEdges, _, loadedHosts) = WindowHandleCacheService.Load(this._tempCacheFile);

        Assert.Single(loadedHosts);
        Assert.Single(loadedEdges);
    }

    [Fact]
    public void CopilotHostEntry_MissingHostPid_DroppedOnLoad()
    {
        var hwnd = this.CreateRealHwnd();
        var sessionId = Guid.NewGuid().ToString();

        var malformedJson = $$"""
            [
                {"SessionId":"{{sessionId}}","Type":"copilot-host","Name":"TestProcess","FolderPath":"Test Host","Hwnd":{{hwnd.ToInt64()}},"CopilotPid":12345}
            ]
            """;

        File.WriteAllText(this._tempCacheFile, malformedJson);

        var (_, _, _, _, loadedHosts) = WindowHandleCacheService.Load(this._tempCacheFile);

        Assert.Empty(loadedHosts);
    }

    [Fact]
    public void CopilotHostEntry_MissingCopilotPid_DroppedOnLoad()
    {
        var hwnd = this.CreateRealHwnd();
        var pid = Environment.ProcessId;
        var sessionId = Guid.NewGuid().ToString();

        var malformedJson = $$"""
            [
                {"SessionId":"{{sessionId}}","Type":"copilot-host","Name":"TestProcess","FolderPath":"Test Host","Hwnd":{{hwnd.ToInt64()}},"HostPid":{{pid}}}
            ]
            """;

        File.WriteAllText(this._tempCacheFile, malformedJson);

        var (_, _, _, _, loadedHosts) = WindowHandleCacheService.Load(this._tempCacheFile);

        Assert.Empty(loadedHosts);
    }
}
