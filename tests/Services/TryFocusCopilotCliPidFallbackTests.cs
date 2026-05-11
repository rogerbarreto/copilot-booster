namespace CopilotBooster.Tests.Services;

/// <summary>
/// Tests that the TryFocusCopilotCli PID fallback respects the session-aware liveness gate.
/// When isSessionLiveForCopilotPid returns false, the focus path must NOT invoke the
/// focusWindowHandle callback (critique #4).
/// </summary>
public sealed class TryFocusCopilotCliPidFallbackTests
{
    [Fact]
    public void TryFocusCopilotCli_StaleSession_DoesNotFocus()
    {
        // Seed a host that fails the session liveness check. When trying to focus,
        // the focusWindowHandle spy should NOT be invoked because the binding is stale.
        bool focusInvoked = false;
        var tracker = new ActiveStatusTracker(
            new CopilotHostResolver(),
            new WindowsTerminalPaneGateway(),
            new WindowsTerminalPaneCacheService(),
            focusWindowHandle: hwnd =>
            {
                focusInvoked = true;
                return true;
            },
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

        tracker.SetCopilotHost("stale-session", info);

        // The TryFocusCopilotCli method (or equivalent focus path) should check liveness
        // before invoking focusWindowHandle. If IsCopilotHostActive is used correctly,
        // the focus callback will not be invoked.
        var result = tracker.TryFocusCopilotCli("stale-session");

        Assert.False(focusInvoked);
        Assert.False(result);
    }

    [Fact]
    public void TryFocusCopilotCli_FreshSession_InvokesFocus()
    {
        // Fresh session; focus callback should be invoked.
        bool focusInvoked = false;
        var tracker = new ActiveStatusTracker(
            new CopilotHostResolver(),
            new WindowsTerminalPaneGateway(),
            new WindowsTerminalPaneCacheService(),
            focusWindowHandle: hwnd =>
            {
                focusInvoked = true;
                return true;
            },
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

        tracker.SetCopilotHost("fresh-session", info);

        var result = tracker.TryFocusCopilotCli("fresh-session");

        Assert.True(focusInvoked);
        Assert.True(result);
    }
}
