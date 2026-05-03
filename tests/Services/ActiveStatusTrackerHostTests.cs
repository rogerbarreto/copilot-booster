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

    [Fact]
    public void HandleInternalCopilotPidRegistered_WindowsTerminal_StoresMatchedPaneHwnd()
    {
        var sessionId = "session-pane-match";
        var parentHwnd = new IntPtr(0xAAA);
        var paneHwnd = new IntPtr(0xBBB);
        var tree = new FakeProcessTree()
            .Add(1000, 900, "copilot", IntPtr.Zero)
            .Add(900, null, "WindowsTerminal", parentHwnd);
        var gateway = new FakeWindowsTerminalPaneGateway([
            new WindowsTerminalPaneInfo("Unrelated", new IntPtr(0x111), 900, false, () => { }),
            new WindowsTerminalPaneInfo($"Copilot CLI - {sessionId}", paneHwnd, 900, false, () => { })
        ]);
        var tracker = CreateTracker(tree, gateway);

        tracker.HandleInternalCopilotPidRegistered(sessionId, 1000);

        var host = tracker.GetCopilotHost(sessionId);
        Assert.NotNull(host);
        Assert.Equal(paneHwnd, host!.HostHwnd);
        Assert.Equal(parentHwnd, host.ParentHostHwnd);
        Assert.Equal($"Copilot CLI - {sessionId}", host.PaneTitle);
        Assert.Equal(1, gateway.EnumerateCount);
    }

    [Fact]
    public void HandleInternalCopilotPidRegistered_NonWindowsTerminal_DoesNotEnumeratePanes()
    {
        var tree = new FakeProcessTree()
            .Add(1000, 900, "copilot", IntPtr.Zero)
            .Add(900, null, "pwsh", new IntPtr(0x123));
        var gateway = new FakeWindowsTerminalPaneGateway([]);
        var tracker = CreateTracker(tree, gateway);

        tracker.HandleInternalCopilotPidRegistered("session-pwsh", 1000);

        var host = tracker.GetCopilotHost("session-pwsh");
        Assert.NotNull(host);
        Assert.Equal(new IntPtr(0x123), host!.HostHwnd);
        Assert.Equal(0, gateway.EnumerateCount);
    }

    [Fact]
    public void HandleInternalCopilotPidRegistered_WindowsTerminalEmptyGateway_FallsBackToParentHwnd()
    {
        var tree = new FakeProcessTree()
            .Add(1000, 900, "copilot", IntPtr.Zero)
            .Add(900, null, "WindowsTerminal", new IntPtr(0xAAA));
        var gateway = new FakeWindowsTerminalPaneGateway([]);
        var tracker = CreateTracker(tree, gateway);

        tracker.HandleInternalCopilotPidRegistered("session-empty", 1000);

        var host = tracker.GetCopilotHost("session-empty");
        Assert.NotNull(host);
        Assert.Equal(new IntPtr(0xAAA), host!.HostHwnd);
        Assert.Equal(new IntPtr(0xAAA), host.ParentHostHwnd);
        Assert.Null(host.PaneTitle);
    }

    [Fact]
    public void HandleWindowNameChanged_WindowsTerminalParent_InvalidatesHostForRefresh()
    {
        var sessionId = "session-title-change";
        var parentHwnd = new IntPtr(0xAAA);
        var tree = new FakeProcessTree()
            .Add(1000, 900, "copilot", IntPtr.Zero)
            .Add(900, null, "WindowsTerminal", parentHwnd);
        var gateway = new FakeWindowsTerminalPaneGateway([
            new WindowsTerminalPaneInfo($"Copilot CLI - {sessionId}", new IntPtr(0xBBB), 900, false, () => { })
        ]);
        var tracker = CreateTracker(tree, gateway);
        tracker.HandleInternalCopilotPidRegistered(sessionId, 1000);

        var affected = tracker.HandleWindowNameChanged(parentHwnd);

        Assert.Contains(sessionId, affected);
        Assert.Null(tracker.GetCopilotHost(sessionId));
    }

    [Fact]
    public void OnWindowTitleChanged_WindowsTerminalSameParentHwnd_KeepsBothCopilotHostsActive()
    {
        var firstSessionId = "session-run-tests";
        var secondSessionId = "session-run-test-2";
        var parentHwnd = new IntPtr(0xAAA);
        var tree = new FakeProcessTree()
            .Add(1000, 900, "copilot", IntPtr.Zero)
            .Add(1001, 900, "copilot", IntPtr.Zero)
            .Add(900, null, "WindowsTerminal", parentHwnd);
        var gateway = new FakeWindowsTerminalPaneGateway([
            new WindowsTerminalPaneInfo($"Run Tests - {firstSessionId}", IntPtr.Zero, 900, false, () => { }, "runtime-1"),
            new WindowsTerminalPaneInfo($"Run Test 2 - {secondSessionId}", IntPtr.Zero, 900, true, () => { }, "runtime-2")
        ]);
        var tracker = CreateTracker(tree, gateway);
        tracker.HandleInternalCopilotPidRegistered(firstSessionId, 1000);
        tracker.HandleInternalCopilotPidRegistered(secondSessionId, 1001);

        var firstHost = tracker.GetCopilotHost(firstSessionId);
        var secondHost = tracker.GetCopilotHost(secondSessionId);
        Assert.NotNull(firstHost);
        Assert.NotNull(secondHost);
        Assert.Equal(parentHwnd, firstHost!.HostHwnd);
        Assert.Equal(parentHwnd, secondHost!.HostHwnd);
        Assert.Equal("runtime-1", firstHost.PaneRuntimeId);
        Assert.Equal("runtime-2", secondHost.PaneRuntimeId);
        Assert.Contains("Copilot CLI", tracker.BuildActiveText(firstSessionId));
        Assert.Contains("Copilot CLI", tracker.BuildActiveText(secondSessionId));

        tracker.OnWindowTitleChanged(
            parentHwnd,
            "Run Test 2",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Run Tests"] = firstSessionId,
                ["Run Test 2"] = secondSessionId
            });

        Assert.Contains("Copilot CLI", tracker.BuildActiveText(firstSessionId));
        Assert.Contains("Copilot CLI", tracker.BuildActiveText(secondSessionId));
    }

    [Fact]
    public void FocusActiveProcess_WindowsTerminalHostWithRuntimeId_FocusesPaneBeforeParentWindow()
    {
        var sessionId = "session-run-test-2";
        var parentHwnd = new IntPtr(0xAAA);
        var expectedHwnd = parentHwnd.ToInt64().ToString("X");
        var calls = new List<string>();
        var tree = new FakeProcessTree()
            .Add(1001, 900, "copilot", IntPtr.Zero)
            .Add(900, null, "WindowsTerminal", parentHwnd);
        var gateway = new FakeWindowsTerminalPaneGateway([])
        {
            OnFocusPane = (hwnd, runtimeId) => calls.Add($"pane:{hwnd.ToInt64():X}:{runtimeId}")
        };
        var tracker = new ActiveStatusTracker(
            new CopilotHostResolver(tree, ownPid: 0),
            gateway,
            new WindowsTerminalPaneCacheService(),
            hwnd =>
            {
                calls.Add($"foreground:{hwnd.ToInt64():X}");
                return true;
            },
            _ => true);
        var hostInfo = new CopilotHostInfo(
            parentHwnd,
            900,
            1001,
            "WindowsTerminal",
            "Windows Terminal",
            ParentHostHwnd: parentHwnd,
            PaneRuntimeId: "runtime-2");
        tracker.SetCopilotHost(sessionId, hostInfo);
        var previousSettings = Program._settings;
        Program._settings = LauncherSettings.CreateDefault();

        try
        {
            tracker.FocusActiveProcess(sessionId, clickedLineIndex: 0);
        }
        finally
        {
            Program._settings = previousSettings;
        }

        Assert.Equal([$"foreground:{expectedHwnd}", $"pane:{expectedHwnd}:runtime-2"], calls);
    }

    private static ActiveStatusTracker CreateTracker(FakeProcessTree tree, IWindowsTerminalPaneGateway gateway)
    {
        return new ActiveStatusTracker(new CopilotHostResolver(tree, ownPid: 0), gateway, new WindowsTerminalPaneCacheService());
    }

    private sealed class FakeProcessTree : IProcessTreeProvider
    {
        private readonly Dictionary<int, int?> _parents = [];
        private readonly Dictionary<int, string?> _names = [];
        private readonly Dictionary<int, IntPtr> _windows = [];

        internal FakeProcessTree Add(int pid, int? parentPid, string? name, IntPtr window)
        {
            this._parents[pid] = parentPid;
            this._names[pid] = name;
            this._windows[pid] = window;
            return this;
        }

        public int? GetParentPid(int pid) => this._parents.TryGetValue(pid, out var parent) ? parent : null;
        public string? GetProcessName(int pid) => this._names.TryGetValue(pid, out var name) ? name : null;
        public IntPtr GetTopLevelWindow(int pid) => this._windows.TryGetValue(pid, out var hwnd) ? hwnd : IntPtr.Zero;
    }

    private sealed class FakeWindowsTerminalPaneGateway : IWindowsTerminalPaneGateway
    {
        private readonly IReadOnlyList<WindowsTerminalPaneInfo> _panes;

        internal FakeWindowsTerminalPaneGateway(IReadOnlyList<WindowsTerminalPaneInfo> panes)
        {
            this._panes = panes;
        }

        internal int EnumerateCount { get; private set; }
        internal Action<IntPtr, string>? OnFocusPane { get; init; }

        public bool FocusPane(IntPtr wtHwnd, string paneRuntimeId)
        {
            this.OnFocusPane?.Invoke(wtHwnd, paneRuntimeId);
            return true;
        }

        public WindowsTerminalPaneEnumeration EnumeratePanes(IntPtr wtHwnd)
        {
            this.EnumerateCount++;
            return new WindowsTerminalPaneEnumeration(this._panes, IsPartial: false);
        }

        public IReadOnlyList<(string Name, Action Select)> EnumerateTabs(IntPtr wtHwnd)
        {
            return this._panes.Select(pane => (pane.Name, pane.Select)).ToList();
        }
    }
}
