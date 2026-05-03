namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// Integration tests proving that the Copilot Host focus path is robust against window title changes.
/// This is THE hardest existing bug: Copilot CLI rewrites its window title mid-conversation,
/// breaking the legacy title-pattern path. The Host path (HWND-based) must survive this.
/// </summary>
public sealed class InternalSessionTitleChangesIntegrationTests : IDisposable
{
    private readonly string _tempCacheFile;
    private Form? _testForm;

    public InternalSessionTitleChangesIntegrationTests()
    {
        this._tempCacheFile = Path.Combine(Path.GetTempPath(), $"cache-test-{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        this._testForm?.Dispose();
        try { File.Delete(this._tempCacheFile); } catch { }
    }

    [Fact]
    public void CopilotHost_TitleChanged_StillResolvable()
    {
        var sessionId = Guid.NewGuid().ToString();
        this._testForm = new Form
        {
            Width = 100,
            Height = 100,
            ShowInTaskbar = false,
            Opacity = 0,
            Text = $"Copilot CLI - {sessionId}"
        };
        this._testForm.Show();
        var hwnd = this._testForm.Handle;
        var pid = Environment.ProcessId;

        var hostInfo = new CopilotHostInfo(hwnd, pid, pid + 1, "TestHost", "Test");
        var copilotHosts = new Dictionary<string, CopilotHostInfo> { [sessionId] = hostInfo };

        WindowHandleCacheService.Save(this._tempCacheFile, [], [], [], [], copilotHosts);

        this._testForm.Text = "Completely Different Title";

        var (_, _, _, _, loadedHosts) = WindowHandleCacheService.Load(this._tempCacheFile);

        Assert.Single(loadedHosts);
        Assert.True(loadedHosts.ContainsKey(sessionId));
        var loaded = loadedHosts[sessionId];
        Assert.Equal(hwnd, loaded.HostHwnd);
    }

    [Fact]
    public void CopilotHost_TitleChanged_WindowStillAlive()
    {
        this._testForm = new Form
        {
            Width = 100,
            Height = 100,
            ShowInTaskbar = false,
            Opacity = 0,
            Text = "Original Title"
        };
        this._testForm.Show();
        var hwnd = this._testForm.Handle;

        this._testForm.Text = "Changed Title";

        var isAlive = WindowFocusService.IsWindowAlive(hwnd);
        Assert.True(isAlive);
    }

    [Fact]
    public void CopilotHost_TitleChangedMultipleTimes_PersistsCorrectly()
    {
        var sessionId = Guid.NewGuid().ToString();
        this._testForm = new Form
        {
            Width = 100,
            Height = 100,
            ShowInTaskbar = false,
            Opacity = 0,
            Text = $"Copilot CLI - {sessionId}"
        };
        this._testForm.Show();
        var hwnd = this._testForm.Handle;
        var pid = Environment.ProcessId;

        var hostInfo = new CopilotHostInfo(hwnd, pid, pid + 1, "TestHost", "Test");
        var copilotHosts = new Dictionary<string, CopilotHostInfo> { [sessionId] = hostInfo };

        WindowHandleCacheService.Save(this._tempCacheFile, [], [], [], [], copilotHosts);

        this._testForm.Text = "First Change";
        this._testForm.Text = "Second Change";
        this._testForm.Text = "Third Change";

        var (_, _, _, _, loadedHosts) = WindowHandleCacheService.Load(this._tempCacheFile);

        Assert.Single(loadedHosts);
        Assert.True(loadedHosts.ContainsKey(sessionId));
        var loaded = loadedHosts[sessionId];
        Assert.Equal(hwnd, loaded.HostHwnd);
        Assert.True(WindowFocusService.IsWindowAlive(hwnd));
    }

    [Fact]
    public void CopilotHost_HwndBasedCache_IndependentOfTitle()
    {
        var sessionId = Guid.NewGuid().ToString();
        this._testForm = new Form
        {
            Width = 100,
            Height = 100,
            ShowInTaskbar = false,
            Opacity = 0,
            Text = "Any Title"
        };
        this._testForm.Show();
        var hwnd = this._testForm.Handle;
        var pid = Environment.ProcessId;

        var hostInfo = new CopilotHostInfo(hwnd, pid, pid + 1, "TestHost", "Test");
        var copilotHosts = new Dictionary<string, CopilotHostInfo> { [sessionId] = hostInfo };

        WindowHandleCacheService.Save(this._tempCacheFile, [], [], [], [], copilotHosts);

        var (_, _, _, _, loadedHosts) = WindowHandleCacheService.Load(this._tempCacheFile);

        Assert.Single(loadedHosts);
        var loaded = loadedHosts[sessionId];
        Assert.Equal(hwnd, loaded.HostHwnd);
    }
}
