namespace CopilotBooster.Tests.Services;

/// <summary>
/// Tests that ReprojectActiveCopilotHosts evicts a stale host when validator returns false,
/// discriminating between fresh and stale sessions. This is the session-aware eviction logic
/// that catches stale bindings already in _copilotHosts before rescan or active-skip.
/// </summary>
public sealed class RescanExistingSessionsEvictionTests
{
    [Fact]
    public void RescanExistingSessions_TwoHosts_OnlyFreshProjected()
    {
        // Seed two hosts; validator returns true for session-fresh, false for session-stale.
        // After ReprojectActiveCopilotHosts, only the fresh host's badge should be present.
        var tracker = new ActiveStatusTracker(
            new CopilotHostResolver(),
            new WindowsTerminalPaneGateway(),
            new WindowsTerminalPaneCacheService(),
            focusWindowHandle: _ => true,
            isWindowAlive: _ => true,
            isProcessAlive: _ => true,
            isExpectedCopilotProcess: _ => true,
            isSessionLiveForCopilotPid: (sessionId, _) => sessionId == "session-fresh");

        var freshInfo = new CopilotHostInfo(
            HostHwnd: new IntPtr(0x1),
            HostPid: 1000,
            CopilotPid: 1001,
            HostProcessName: "WindowsTerminal",
            HostKindLabel: "Windows Terminal",
            ParentHostHwnd: new IntPtr(0x1),
            PaneRuntimeId: "1.1.1.1",
            PaneRootProcessId: 1002);

        var staleInfo = new CopilotHostInfo(
            HostHwnd: new IntPtr(0x2),
            HostPid: 2000,
            CopilotPid: 2001,
            HostProcessName: "WindowsTerminal",
            HostKindLabel: "Windows Terminal",
            ParentHostHwnd: new IntPtr(0x2),
            PaneRuntimeId: "2.2.2.2",
            PaneRootProcessId: 2002);

        tracker.SetCopilotHost("session-fresh", freshInfo);
        tracker.SetCopilotHost("session-stale", staleInfo);
        tracker.ReprojectActiveCopilotHosts();

        var freshActive = tracker.BuildActiveText("session-fresh");
        var staleActive = tracker.BuildActiveText("session-stale");

        Assert.Contains("Copilot CLI", freshActive, StringComparison.Ordinal);
        Assert.DoesNotContain("Copilot CLI", staleActive, StringComparison.Ordinal);
    }
}
