namespace CopilotBooster.Tests.Services;

/// <summary>
/// Reproduces Roger's live-state scenario where session <c>3ff5e1a7-...</c> shows a
/// phantom "Copilot CLI" badge after copilot.exe died and Windows recycled the PID
/// for an unrelated msedge process. <c>IsCopilotHostActive</c> returns true purely
/// because <c>_isProcessAlive(copilotPid)</c> returns true, with no validation that
/// the process is actually copilot.exe.
///
/// Live state at confirmation time (booster diag.log + Get-Process):
///   _copilotHosts[3ff5e1a7] = { CopilotPid = 45700, HostHwnd = 7143450, HostPid = 45448 }
///   PID 45700 → msedge.exe (alive, but NOT copilot.exe — PID reuse after copilot died)
///   HWND 7143450 → invisible "Picture in picture" Edge window (pid 45448 = msedge)
///   IsCopilotHostActive returns true (BUG); FocusActiveProcess builds a target list
///   with [#0:Copilot CLI] which calls FocusCopilotHost on the dead binding.
///
/// These tests pin the contract: a copilot host is only "active" when the CopilotPid
/// actually points to a copilot.exe process. PID reuse must not satisfy liveness.
/// </summary>
public sealed class IsCopilotHostActivePidReuseTests
{
    [Fact]
    public void IsCopilotHostActive_PidNowPointsToWrongProcess_DropsBadge()
    {
        // Mirrors session 3ff5e1a7's stale binding. The PID is alive but it's an
        // msedge process, not copilot.exe. The host MUST be considered inactive
        // so the phantom badge disappears.
        var tracker = new ActiveStatusTracker(
            new CopilotHostResolver(),
            new WindowsTerminalPaneGateway(),
            new WindowsTerminalPaneCacheService(),
            focusWindowHandle: _ => true,
            isWindowAlive: _ => true,
            isProcessAlive: _ => true,
            isExpectedCopilotProcess: _ => false);

        var info = new CopilotHostInfo(
            HostHwnd: new IntPtr(0x6D11A),
            HostPid: 45448,
            CopilotPid: 45700,
            HostProcessName: "msedge",
            HostKindLabel: "Edge");

        tracker.SetCopilotHost("3ff5e1a7-adeb-4c62-b03d-1a819edb79cb", info);
        tracker.ReprojectActiveCopilotHosts();

        var active = tracker.BuildActiveText("3ff5e1a7-adeb-4c62-b03d-1a819edb79cb");

        Assert.DoesNotContain("Copilot CLI", active, StringComparison.Ordinal);
    }

    [Fact]
    public void IsCopilotHostActive_PidIsRealCopilot_KeepsBadge()
    {
        // Healthy binding — process identity check passes. Badge stays.
        var tracker = new ActiveStatusTracker(
            new CopilotHostResolver(),
            new WindowsTerminalPaneGateway(),
            new WindowsTerminalPaneCacheService(),
            focusWindowHandle: _ => true,
            isWindowAlive: _ => true,
            isProcessAlive: _ => true,
            isExpectedCopilotProcess: _ => true);

        var info = new CopilotHostInfo(
            HostHwnd: new IntPtr(0x14062E),
            HostPid: 92132,
            CopilotPid: 91668,
            HostProcessName: "WindowsTerminal",
            HostKindLabel: "Windows Terminal",
            ParentHostHwnd: new IntPtr(0x14062E),
            PaneRuntimeId: "42.4721618.4.13",
            PaneRootProcessId: 76220);

        tracker.SetCopilotHost("be6b9891-d461-488c-9fd6-432228602480", info);
        tracker.ReprojectActiveCopilotHosts();

        var active = tracker.BuildActiveText("be6b9891-d461-488c-9fd6-432228602480");

        Assert.Contains("Copilot CLI", active, StringComparison.Ordinal);
    }
}
