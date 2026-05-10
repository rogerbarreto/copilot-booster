namespace CopilotBooster.Tests.Services;

/// <summary>
/// Locks down the contract that an externally-discovered live copilot.exe still
/// produces a "Copilot CLI" badge through the host-projection path even when the
/// stricter title-scan fallback (PreservePreviouslyTrackedHwnds) would not preserve
/// the title-scan entry.
/// </summary>
public sealed class LiveCopilotHostBadgeTests
{
    [Fact]
    public void BuildActiveText_LiveCopilotHost_StillShowsBadgeWhenTitleScanWouldDrop()
    {
        // Live host: window alive AND copilot pid alive
        var tracker = new ActiveStatusTracker(
            new CopilotHostResolver(),
            new WindowsTerminalPaneGateway(),
            new WindowsTerminalPaneCacheService(),
            focusWindowHandle: _ => true,
            isWindowAlive: _ => true,
            isProcessAlive: _ => true,
            isExpectedCopilotProcess: _ => true);

        const string SessionId = "live-host-session";
        var hostHwnd = new IntPtr(0xABCD);
        var info = new CopilotHostInfo(
            HostHwnd: hostHwnd,
            HostPid: 1000,
            CopilotPid: 91668,
            HostProcessName: "WindowsTerminal",
            HostKindLabel: "Windows Terminal",
            ParentHostHwnd: hostHwnd,
            PaneRuntimeId: "[42.99999.1]",
            PaneRootProcessId: 1000);

        tracker.SetCopilotHost(SessionId, info);

        var active = tracker.BuildActiveText(SessionId);

        Assert.Contains("Copilot CLI", active, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildActiveText_AfterOnProcessExited_BadgeDropsWithProjection()
    {
        // Roger's oh-my-codex symptom: copilot.exe died but the projection lingered.
        // The expected lifecycle is: ProcessExitTracker watches the copilot pid →
        // OnProcessExited(pid) → RemoveCopilotHost → projection unprojected →
        // BuildActiveText returns no Copilot CLI badge.
        var tracker = new ActiveStatusTracker(
            new CopilotHostResolver(),
            new WindowsTerminalPaneGateway(),
            new WindowsTerminalPaneCacheService(),
            focusWindowHandle: _ => true,
            isWindowAlive: _ => true,
            isProcessAlive: _ => false);

        const string SessionId = "dead-copilot-session";
        var hostHwnd = new IntPtr(0xBEEF);
        var info = new CopilotHostInfo(
            HostHwnd: hostHwnd,
            HostPid: 2000,
            CopilotPid: 45700,
            HostProcessName: "WindowsTerminal",
            HostKindLabel: "Windows Terminal",
            ParentHostHwnd: hostHwnd,
            PaneRuntimeId: "[42.99999.2]",
            PaneRootProcessId: 2000);

        tracker.SetCopilotHost(SessionId, info);
        tracker.OnProcessExited(info.CopilotPid);

        var active = tracker.BuildActiveText(SessionId);

        Assert.DoesNotContain("Copilot CLI", active, StringComparison.Ordinal);
    }
}
