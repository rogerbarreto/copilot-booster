namespace CopilotBooster.Tests.Services;

/// <summary>
/// Tests that IsCopilotHostActive is session-aware: EXISTING in-memory stale
/// _copilotHosts entries are evicted when isSessionLiveForCopilotPid returns false.
/// This catches stale bindings that were valid at discovery but became stale later
/// (e.g., after /resume switches sessions inside the same pid).
/// </summary>
public sealed class IsCopilotHostActiveSessionAwareTests
{
    [Fact]
    public void IsCopilotHostActive_SessionStale_DropsBadge()
    {
        // Seed a host that passes all checks (window/process alive, expected process)
        // EXCEPT the session liveness gate. The "Copilot CLI" badge must disappear.
        var tracker = new ActiveStatusTracker(
            new CopilotHostResolver(),
            new WindowsTerminalPaneGateway(),
            new WindowsTerminalPaneCacheService(),
            focusWindowHandle: _ => true,
            isWindowAlive: _ => true,
            isProcessAlive: _ => true,
            isExpectedCopilotProcess: _ => true,
            isSessionLiveForCopilotPid: (_, _) => false);

        var info = new CopilotHostInfo(
            HostHwnd: new IntPtr(0x14062E),
            HostPid: 92132,
            CopilotPid: 91668,
            HostProcessName: "WindowsTerminal",
            HostKindLabel: "Windows Terminal",
            ParentHostHwnd: new IntPtr(0x14062E),
            PaneRuntimeId: "42.4721618.4.13",
            PaneRootProcessId: 76220);

        tracker.SetCopilotHost("X", info);
        tracker.ReprojectActiveCopilotHosts();

        var active = tracker.BuildActiveText("X");

        Assert.DoesNotContain("Copilot CLI", active, StringComparison.Ordinal);
    }

    [Fact]
    public void IsCopilotHostActive_SessionFresh_KeepsBadge()
    {
        // Session liveness returns true; badge stays.
        var tracker = new ActiveStatusTracker(
            new CopilotHostResolver(),
            new WindowsTerminalPaneGateway(),
            new WindowsTerminalPaneCacheService(),
            focusWindowHandle: _ => true,
            isWindowAlive: _ => true,
            isProcessAlive: _ => true,
            isExpectedCopilotProcess: _ => true,
            isSessionLiveForCopilotPid: (_, _) => true);

        var info = new CopilotHostInfo(
            HostHwnd: new IntPtr(0x14062E),
            HostPid: 92132,
            CopilotPid: 91668,
            HostProcessName: "WindowsTerminal",
            HostKindLabel: "Windows Terminal",
            ParentHostHwnd: new IntPtr(0x14062E),
            PaneRuntimeId: "42.4721618.4.13",
            PaneRootProcessId: 76220);

        tracker.SetCopilotHost("Y", info);
        tracker.ReprojectActiveCopilotHosts();

        var active = tracker.BuildActiveText("Y");

        Assert.Contains("Copilot CLI", active, StringComparison.Ordinal);
    }
}
