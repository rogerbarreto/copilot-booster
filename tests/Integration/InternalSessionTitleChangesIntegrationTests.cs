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

    [Fact]
    public void OnWindowTitleChanged_TitleMatchesCopilotCliSession_FiresActiveSessionHint()
    {
        // Reproduces Roger's 2026-05-04 21:28 finding: when the user manually
        // switches tabs inside a wt window hosting multiple Copilot CLI sessions,
        // the wt title flips to the new tab's title (e.g., "Process Hi 2 Message"
        // -> "Respond To Greeting"). OnWindowTitleChanged correctly title-matches
        // the new title to its session (live diag: titleMatch=ea9da1be:Copilot CLI)
        // -- but no signal propagates out of the tracker, so the booster grid's
        // active-session highlight never updates. The foreground hook's
        // ResolveSessionForHwnd path doesn't help here because tab switches inside
        // a single wt window don't change the foreground hwnd.
        //
        // Fix surface: tracker exposes ActiveSessionHintChanged event; fired from
        // OnWindowTitleChanged when MatchTrackedWindowTitle resolves a Copilot CLI
        // session. MainForm subscribes and calls SelectSessionById on the booster
        // grid, mirroring the ForegroundChanged handler's existing path.
        var tracker = new ActiveStatusTracker();
        var sessionA = Guid.NewGuid().ToString();
        var sessionB = Guid.NewGuid().ToString();
        var wtHwnd = new IntPtr(0xABCDEF);

        var hints = new List<string>();
        tracker.ActiveSessionHintChanged += sessionId => hints.Add(sessionId);

        // User switches to sessionA's tab -> wt title becomes "Copilot CLI - <sessionA>".
        tracker.OnWindowTitleChanged(wtHwnd, $"Copilot CLI - {sessionA}", sessionSummaries: null);

        // User switches to sessionB's tab -> wt title becomes the session-summary form.
        tracker.OnWindowTitleChanged(
            wtHwnd,
            "Build the auth module",
            sessionSummaries: new Dictionary<string, string> { ["Build the auth module"] = sessionB });

        // Both title-match paths must fire the hint with the matched sessionId in
        // the order the events arrived. Pre-fix: event doesn't exist (compile error)
        // OR exists but never fires (empty list). Post-fix: [sessionA, sessionB].
        Assert.Equal(2, hints.Count);
        Assert.Equal(sessionA, hints[0]);
        Assert.Equal(sessionB, hints[1]);
    }
}
