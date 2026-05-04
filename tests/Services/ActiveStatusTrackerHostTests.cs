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
    public void HandleWindowNameChanged_WindowsTerminalParent_PreservesHostsAndLinks()
    {
        var firstSessionId = "session-run-test";
        var secondSessionId = "session-run-test-2";
        var parentHwnd = new IntPtr(0xAAA);
        var tree = new FakeProcessTree()
            .Add(1000, 900, "copilot", IntPtr.Zero)
            .Add(1001, 900, "copilot", IntPtr.Zero)
            .Add(900, null, "WindowsTerminal", parentHwnd);
        var gateway = new FakeWindowsTerminalPaneGateway([
            new WindowsTerminalPaneInfo($"Copilot CLI - {firstSessionId}", IntPtr.Zero, 900, false, () => { }, "runtime-1"),
            new WindowsTerminalPaneInfo($"Copilot CLI - {secondSessionId}", IntPtr.Zero, 900, true, () => { }, "runtime-2")
        ]);
        var tracker = CreateTracker(tree, gateway);
        tracker.HandleInternalCopilotPidRegistered(firstSessionId, 1000);
        tracker.HandleInternalCopilotPidRegistered(secondSessionId, 1001);

        var affected = tracker.HandleWindowNameChanged(parentHwnd);

        Assert.Contains(firstSessionId, affected);
        Assert.Contains(secondSessionId, affected);
        Assert.NotNull(tracker.GetCopilotHost(firstSessionId));
        Assert.NotNull(tracker.GetCopilotHost(secondSessionId));
        Assert.Contains("Copilot CLI", tracker.BuildActiveText(firstSessionId));
        Assert.Contains("Copilot CLI", tracker.BuildActiveText(secondSessionId));
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
    public void HandleInternalCopilotPidRegistered_WindowsTerminalIdenticalTitles_MapsByPaneRootProcess()
    {
        var firstSessionId = "session-run-tests";
        var secondSessionId = "session-run-test-2";
        var parentHwnd = new IntPtr(0xAAA);
        var tree = new FakeProcessTree()
            .Add(1000, 700, "copilot", IntPtr.Zero)
            .Add(1001, 701, "copilot", IntPtr.Zero)
            .Add(700, 900, "OpenConsole", IntPtr.Zero)
            .Add(701, 900, "OpenConsole", IntPtr.Zero)
            .Add(900, null, "WindowsTerminal", parentHwnd);
        var gateway = new FakeWindowsTerminalPaneGateway([
            new WindowsTerminalPaneInfo("Copilot CLI", IntPtr.Zero, 900, false, () => { }, "runtime-1", PaneRootProcessId: 700),
            new WindowsTerminalPaneInfo("Copilot CLI", IntPtr.Zero, 900, true, () => { }, "runtime-2", PaneRootProcessId: 701)
        ]);
        var tracker = CreateTracker(tree, gateway);

        tracker.HandleInternalCopilotPidRegistered(firstSessionId, 1000);
        tracker.HandleInternalCopilotPidRegistered(secondSessionId, 1001);

        var firstHost = tracker.GetCopilotHost(firstSessionId);
        var secondHost = tracker.GetCopilotHost(secondSessionId);
        Assert.NotNull(firstHost);
        Assert.NotNull(secondHost);
        Assert.Equal("runtime-1", firstHost!.PaneRuntimeId);
        Assert.Equal("runtime-2", secondHost!.PaneRuntimeId);
        Assert.NotEqual(firstHost.PaneRuntimeId, secondHost.PaneRuntimeId);
    }

    [Fact]
    public void FocusActiveProcess_WindowsTerminalHostWithRuntimeId_FocusesParentBeforePaneSelection()
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
            _ => true,
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

    [Fact]
    public void OnWindowTitleChanged_TitleMatchIdentifiesDifferentWtHwnd_RebindsCopilotHost()
    {
        // Reproduces the bug Roger surfaced 2026-05-04 (image: _copilotHosts watch with two
        // sessions sharing one host hwnd while the visible "Run Tests" wt is a different
        // hwnd). Production scenario: Sun Valley wt monarch hosts multiple wt windows under
        // a single WindowsTerminal.exe pid; CopilotHostResolver's pane-term match fails
        // when a user's tab labels don't include the session sessionId/summary; the tracker
        // falls back to the first candidate hwnd by Z-order. The OnWindowTitleChanged hook
        // later observes the right wt hwnd via "Copilot CLI - <sessionId>" — but pre-fix
        // that information only updates _activeTrackedWindows, never propagating into
        // _copilotHosts where FocusCopilotHost reads from. Click-to-focus then targets the
        // wrong wt window, exactly as Roger saw.
        var sessionId = "session-pwsh-100856";
        var copilotPid = 101056;
        var pwshPid = 100856;
        var wtMonarchPid = 18144;

        // Two wt hwnds: 0x90768 (the "wrong" candidate the resolver falls back to) and
        // 0x900FBC (the actually-correct hwnd whose tab title becomes "Copilot CLI -
        // <sessionId>" once copilot starts up).
        var wrongWtHwnd = new IntPtr(0x90768);
        var correctWtHwnd = new IntPtr(0x900FBC);

        var tree = new FakeProcessTree()
            .Add(copilotPid, pwshPid, "copilot", IntPtr.Zero)
            .Add(pwshPid, wtMonarchPid, "pwsh", IntPtr.Zero)
            .Add(wtMonarchPid, null, "WindowsTerminal", IntPtr.Zero)
            .AddWindows(wtMonarchPid, wrongWtHwnd, correctWtHwnd);

        // Both wt windows return panes whose names DON'T match the session sessionId or
        // any term in BuildWindowsTerminalPaneMatchTerms — like real users naming tabs
        // "Add Grill With Docs Route" or "Run Tests" without any session metadata.
        // Neither pane has PaneRootProcessId populated (also matching production
        // behaviour where UIA only exposes pane-content hwnds for the foreground tab).
        var paneInWrongWt = new WindowsTerminalPaneInfo(
            Name: "Add Grill With Docs Route",
            Hwnd: IntPtr.Zero,
            ProcessId: wtMonarchPid,
            IsSelected: true,
            Select: () => { },
            RuntimeId: "runtime-wrong",
            PaneRootProcessId: null);
        var paneInCorrectWt = new WindowsTerminalPaneInfo(
            Name: "Run Tests",
            Hwnd: IntPtr.Zero,
            ProcessId: wtMonarchPid,
            IsSelected: true,
            Select: () => { },
            RuntimeId: "runtime-correct",
            PaneRootProcessId: null);
        var gateway = FakeWindowsTerminalPaneGateway.PerHwnd(new Dictionary<IntPtr, IReadOnlyList<WindowsTerminalPaneInfo>>
        {
            [wrongWtHwnd] = new[] { paneInWrongWt },
            [correctWtHwnd] = new[] { paneInCorrectWt }
        });
        var tracker = CreateTracker(tree, gateway);

        // Discovery: resolver iterates candidates [wrongWtHwnd, correctWtHwnd], no pane
        // matches via term/paneRootPid for either, so IsRealPaneMatch returns false on
        // both → falls back to firstAttempt = wrongWtHwnd. This is the production bug.
        tracker.HandleExternalSessionDiscovered(sessionId, copilotPid);
        var hostAfterDiscovery = tracker.GetCopilotHost(sessionId);
        Assert.NotNull(hostAfterDiscovery);
        Assert.Equal(wrongWtHwnd, hostAfterDiscovery!.ParentHostHwnd);

        // The WindowEventHookService later fires a title-change event when copilot CLI
        // updates the wt tab title. MatchTrackedWindowTitle parses
        // "Copilot CLI - <sessionId>" → (sessionId, "Copilot CLI"). This is the
        // strongest-possible per-session identifier — it cannot false-positive across
        // wt windows. The tracker MUST treat this signal as authoritative and rebind
        // _copilotHosts[sessionId].ParentHostHwnd to the title-source hwnd.
        tracker.OnWindowTitleChanged(correctWtHwnd, $"Copilot CLI - {sessionId}", sessionSummaries: null);

        var hostAfterTitleMatch = tracker.GetCopilotHost(sessionId);
        Assert.NotNull(hostAfterTitleMatch);
        Assert.Equal(correctWtHwnd, hostAfterTitleMatch!.ParentHostHwnd);
        // HostHwnd may be the pane hwnd or the wt window hwnd — but ParentHostHwnd must
        // be the wt window hwnd identified by title-match.
        Assert.Equal(wtMonarchPid, hostAfterTitleMatch.HostPid);
    }

    [Fact]
    public void OnWindowTitleChanged_TitleMatchAgreesWithCurrentHwnd_NoRebindEvent()
    {
        // Inverse case: when title-match identifies the SAME hwnd that's already stored,
        // we must not churn — no SetCopilotHost call, no event fire. Otherwise every
        // title tick (copilot CLI updates its title frequently while working) would
        // refire CopilotHostResolved on the UI bus and re-paint the grid.
        var sessionId = "session-agreement";
        var copilotPid = 200;
        var pwshPid = 300;
        var wtMonarchPid = 400;
        var wtHwnd = new IntPtr(0xCCC);

        var tree = new FakeProcessTree()
            .Add(copilotPid, pwshPid, "copilot", IntPtr.Zero)
            .Add(pwshPid, wtMonarchPid, "pwsh", IntPtr.Zero)
            .Add(wtMonarchPid, null, "WindowsTerminal", wtHwnd)
            .AddWindows(wtMonarchPid, wtHwnd);

        var pane = new WindowsTerminalPaneInfo(
            Name: $"Copilot CLI - {sessionId}",
            Hwnd: IntPtr.Zero,
            ProcessId: wtMonarchPid,
            IsSelected: true,
            Select: () => { },
            RuntimeId: "runtime-only",
            PaneRootProcessId: pwshPid);
        var gateway = new FakeWindowsTerminalPaneGateway([pane]);
        var tracker = CreateTracker(tree, gateway);

        tracker.HandleExternalSessionDiscovered(sessionId, copilotPid);
        int eventFireCount = 0;
        tracker.CopilotHostResolved += (_, _) => eventFireCount++;

        tracker.OnWindowTitleChanged(wtHwnd, $"Copilot CLI - {sessionId}", sessionSummaries: null);

        Assert.Equal(0, eventFireCount);
        Assert.Equal(wtHwnd, tracker.GetCopilotHost(sessionId)!.ParentHostHwnd);
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
        private readonly Dictionary<int, List<IntPtr>> _multiWindows = [];

        internal FakeProcessTree Add(int pid, int? parentPid, string? name, IntPtr window)
        {
            this._parents[pid] = parentPid;
            this._names[pid] = name;
            this._windows[pid] = window;
            return this;
        }

        internal FakeProcessTree AddWindows(int pid, params IntPtr[] hwnds)
        {
            this._multiWindows[pid] = [.. hwnds];
            return this;
        }

        public int? GetParentPid(int pid) => this._parents.TryGetValue(pid, out var parent) ? parent : null;
        public string? GetProcessName(int pid) => this._names.TryGetValue(pid, out var name) ? name : null;
        public IntPtr GetTopLevelWindow(int pid) => this._windows.TryGetValue(pid, out var hwnd) ? hwnd : IntPtr.Zero;
        public IReadOnlyList<IntPtr> EnumerateTopLevelWindows(int pid)
        {
            if (this._multiWindows.TryGetValue(pid, out var multi))
            {
                return multi;
            }
            return this._windows.TryGetValue(pid, out var hwnd) && hwnd != IntPtr.Zero
                ? [hwnd]
                : Array.Empty<IntPtr>();
        }
    }

    private sealed class FakeWindowsTerminalPaneGateway : IWindowsTerminalPaneGateway
    {
        private readonly IReadOnlyList<WindowsTerminalPaneInfo> _panes;
        private readonly Dictionary<IntPtr, IReadOnlyList<WindowsTerminalPaneInfo>>? _panesByHwnd;

        internal FakeWindowsTerminalPaneGateway(IReadOnlyList<WindowsTerminalPaneInfo> panes)
        {
            this._panes = panes;
        }

        private FakeWindowsTerminalPaneGateway(Dictionary<IntPtr, IReadOnlyList<WindowsTerminalPaneInfo>> panesByHwnd, bool _)
        {
            this._panes = Array.Empty<WindowsTerminalPaneInfo>();
            this._panesByHwnd = panesByHwnd;
        }

        internal static FakeWindowsTerminalPaneGateway PerHwnd(Dictionary<IntPtr, IReadOnlyList<WindowsTerminalPaneInfo>> panesByHwnd)
        {
            return new FakeWindowsTerminalPaneGateway(panesByHwnd, false);
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
            if (this._panesByHwnd != null)
            {
                return this._panesByHwnd.TryGetValue(wtHwnd, out var panes)
                    ? new WindowsTerminalPaneEnumeration(panes, IsPartial: false)
                    : new WindowsTerminalPaneEnumeration(Array.Empty<WindowsTerminalPaneInfo>(), IsPartial: false);
            }
            return new WindowsTerminalPaneEnumeration(this._panes, IsPartial: false);
        }

        public IReadOnlyList<(string Name, Action Select)> EnumerateTabs(IntPtr wtHwnd)
        {
            if (this._panesByHwnd != null && this._panesByHwnd.TryGetValue(wtHwnd, out var panes))
            {
                return panes.Select(pane => (pane.Name, pane.Select)).ToList();
            }
            return this._panes.Select(pane => (pane.Name, pane.Select)).ToList();
        }
    }
}
