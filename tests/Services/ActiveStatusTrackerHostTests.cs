namespace CopilotBooster.Tests.Services;

/// <summary>
/// Unit tests for ActiveStatusTracker's Copilot Host dictionary methods.
/// Tests the scaffolding added in Phase 3: SetCopilotHost, GetCopilotHost, RemoveCopilotHost,
/// projection into _activeTrackedWindows, and event firing.
/// </summary>
public sealed class ActiveStatusTrackerHostTests
{
    [Fact]
    public void SetCopilotHost_FollowedByGet_RoundTrips()
    {
        var tracker = new ActiveStatusTracker();
        var sessionId = "test-session-1";
        var hwnd = new IntPtr(12345);
        var hostInfo = new CopilotHostInfo(hwnd, 100, 200, "WindowsTerminal", "Windows Terminal");

        tracker.SetCopilotHost(sessionId, hostInfo);
        var retrieved = tracker.GetCopilotHost(sessionId);

        Assert.NotNull(retrieved);
        Assert.Equal(hwnd, retrieved!.HostHwnd);
        Assert.Equal(100, retrieved.HostPid);
        Assert.Equal(200, retrieved.CopilotPid);
        Assert.Equal("WindowsTerminal", retrieved.HostProcessName);
        Assert.Equal("Windows Terminal", retrieved.HostKindLabel);
    }

    [Fact]
    public void GetCopilotHost_MissingSession_ReturnsNull()
    {
        var tracker = new ActiveStatusTracker();

        var result = tracker.GetCopilotHost("non-existent-session");

        Assert.Null(result);
    }

    [Fact]
    public void SetCopilotHost_Idempotent_SameDataNoOp()
    {
        var tracker = new ActiveStatusTracker();
        var sessionId = "test-session-2";
        var hwnd = new IntPtr(12345);
        var hostInfo = new CopilotHostInfo(hwnd, 100, 200, "pwsh", "PowerShell");
        int eventFireCount = 0;
        tracker.CopilotHostResolved += (_, _) => eventFireCount++;

        tracker.SetCopilotHost(sessionId, hostInfo);
        tracker.SetCopilotHost(sessionId, hostInfo);

        Assert.Equal(1, eventFireCount);
    }

    [Fact]
    public void SetCopilotHost_DifferentHwnd_Updates()
    {
        var tracker = new ActiveStatusTracker();
        var sessionId = "test-session-3";
        var hostInfo1 = new CopilotHostInfo(new IntPtr(111), 100, 200, "pwsh", "PowerShell");
        var hostInfo2 = new CopilotHostInfo(new IntPtr(222), 100, 200, "pwsh", "PowerShell");
        int eventFireCount = 0;
        tracker.CopilotHostResolved += (_, _) => eventFireCount++;

        tracker.SetCopilotHost(sessionId, hostInfo1);
        tracker.SetCopilotHost(sessionId, hostInfo2);

        var retrieved = tracker.GetCopilotHost(sessionId);
        Assert.Equal(new IntPtr(222), retrieved!.HostHwnd);
        Assert.Equal(2, eventFireCount);
    }

    [Fact]
    public void RemoveCopilotHost_RemovesFromDict()
    {
        var tracker = new ActiveStatusTracker();
        var sessionId = "test-session-4";
        var hwnd = new IntPtr(12345);
        var hostInfo = new CopilotHostInfo(hwnd, 100, 200, "cmd", "Console");

        tracker.SetCopilotHost(sessionId, hostInfo);
        tracker.RemoveCopilotHost(sessionId);

        Assert.Null(tracker.GetCopilotHost(sessionId));
    }

    [Fact]
    public void RemoveCopilotHost_FiresEvent()
    {
        var tracker = new ActiveStatusTracker();
        var sessionId = "test-session-5";
        var hostInfo = new CopilotHostInfo(new IntPtr(12345), 100, 200, "cmd", "Console");
        string? removedSessionId = null;
        tracker.CopilotHostRemoved += (sid) => removedSessionId = sid;

        tracker.SetCopilotHost(sessionId, hostInfo);
        tracker.RemoveCopilotHost(sessionId);

        Assert.Equal(sessionId, removedSessionId);
    }

    [Fact]
    public void RemoveCopilotHost_MissingSession_NoOp()
    {
        var tracker = new ActiveStatusTracker();
        int eventFireCount = 0;
        tracker.CopilotHostRemoved += (_) => eventFireCount++;

        tracker.RemoveCopilotHost("non-existent");

        Assert.Equal(0, eventFireCount);
    }

    [Fact]
    public void SetCopilotHost_ProjectsIntoBuildActiveText()
    {
        var tracker = new ActiveStatusTracker();
        var sessionId = "test-session-6";
        var hwnd = new IntPtr(12345);
        var hostInfo = new CopilotHostInfo(hwnd, 100, 200, "WindowsTerminal", "Windows Terminal");

        tracker.SetCopilotHost(sessionId, hostInfo);
        var activeText = tracker.BuildActiveText(sessionId);

        Assert.Contains("Copilot CLI", activeText);
    }

    [Fact]
    public void RemoveCopilotHost_UnprojectsFromBuildActiveText()
    {
        var tracker = new ActiveStatusTracker();
        var sessionId = "test-session-7";
        var hwnd = new IntPtr(12345);
        var hostInfo = new CopilotHostInfo(hwnd, 100, 200, "WindowsTerminal", "Windows Terminal");

        tracker.SetCopilotHost(sessionId, hostInfo);
        tracker.RemoveCopilotHost(sessionId);
        var activeText = tracker.BuildActiveText(sessionId);

        Assert.Empty(activeText);
    }

    [Fact]
    public void SetCopilotHost_DedupByHwnd_NoDoubleProjection()
    {
        var tracker = new ActiveStatusTracker();
        var sessionId = "test-session-8";
        var hwnd = new IntPtr(12345);
        var hostInfo = new CopilotHostInfo(hwnd, 100, 200, "WindowsTerminal", "Windows Terminal");

        tracker.SetCopilotHost(sessionId, hostInfo);
        tracker.SetCopilotHost(sessionId, hostInfo);
        var activeText = tracker.BuildActiveText(sessionId);

        var lineCount = activeText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.Equal(1, lineCount);
    }

    [Fact]
    public void SetCopilotHost_FiresEventWithCorrectData()
    {
        var tracker = new ActiveStatusTracker();
        var sessionId = "test-session-9";
        var hwnd = new IntPtr(12345);
        var hostInfo = new CopilotHostInfo(hwnd, 100, 200, "WindowsTerminal", "Windows Terminal");
        string? firedSessionId = null;
        CopilotHostInfo? firedInfo = null;
        tracker.CopilotHostResolved += (sid, info) => { firedSessionId = sid; firedInfo = info; };

        tracker.SetCopilotHost(sessionId, hostInfo);

        Assert.Equal(sessionId, firedSessionId);
        Assert.Equal(hwnd, firedInfo!.HostHwnd);
        Assert.Equal(100, firedInfo.HostPid);
        Assert.Equal(200, firedInfo.CopilotPid);
    }
}
