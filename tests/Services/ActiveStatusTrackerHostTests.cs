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

    [Fact]
    public void ResolveSessionForHwnd_MultipleSessionsShareWtHwnd_ReturnsSessionWithSelectedPane()
    {
        // Reproduces Roger's 2026-05-04 finding: when a wt window hosts multiple
        // copilot tabs (after FullRefresh title-scan rebind binds both sessions to
        // the same wtHwnd in _copilotHosts → ProjectCopilotHostToActiveWindows
        // adds a "Copilot CLI" entry with hwnd=wtHwnd to BOTH sessions in
        // _activeTrackedWindows), the foreground-window hook fires
        // OnWindowFocused(wtHwnd). MainForm calls ResolveSessionForHwnd(wtHwnd) to
        // determine which session row to highlight. Pre-fix: returns whichever
        // sessionId iterates first — for Roger that flipped to "Run Test 2" when
        // he clicked the "Run Tests" link. Post-fix: when multiple candidates
        // exist, disambiguate by the currently-selected pane's RuntimeId against
        // each candidate session's CopilotHostInfo.PaneRuntimeId. Pane runtime ids
        // are unique within a wt window so this cannot false-positive.
        var wtHwnd = new IntPtr(0x900FBC);
        var wtMonarchPid = 18144;
        var runTestsSessionId = "fe873d35-af00-423d-a088-554eca62c38e";
        var runTest2SessionId = "ea9da1be-8992-4f58-939a-1433797e4f3a";
        const string runTestsRuntimeId = "42.9179664.4.2978";
        const string runTest2RuntimeId = "42.9179664.4.2983";

        var tree = new FakeProcessTree()
            .Add(wtMonarchPid, null, "WindowsTerminal", wtHwnd)
            .AddWindows(wtMonarchPid, wtHwnd);

        // User just clicked "Run Tests" link → that pane is currently selected.
        var paneRunTests = new WindowsTerminalPaneInfo(
            Name: "Run Tests",
            Hwnd: IntPtr.Zero,
            ProcessId: wtMonarchPid,
            IsSelected: true,
            Select: () => { },
            RuntimeId: runTestsRuntimeId,
            PaneRootProcessId: null);
        var paneRunTest2 = new WindowsTerminalPaneInfo(
            Name: "Run Test 2",
            Hwnd: IntPtr.Zero,
            ProcessId: wtMonarchPid,
            IsSelected: false,
            Select: () => { },
            RuntimeId: runTest2RuntimeId,
            PaneRootProcessId: null);
        var gateway = FakeWindowsTerminalPaneGateway.PerHwnd(new Dictionary<IntPtr, IReadOnlyList<WindowsTerminalPaneInfo>>
        {
            [wtHwnd] = new[] { paneRunTests, paneRunTest2 }
        });
        var tracker = CreateTracker(tree, gateway);

        // Insert "Run Test 2" FIRST so it iterates first in _activeTrackedWindows.
        // This locks in the bug deterministically: pre-fix, ResolveSessionForHwnd
        // walks the dict in insertion order and returns the first match.
        tracker.SetCopilotHost(runTest2SessionId, new CopilotHostInfo(
            HostHwnd: wtHwnd,
            HostPid: wtMonarchPid,
            CopilotPid: 88860,
            HostProcessName: "WindowsTerminal",
            HostKindLabel: "Windows Terminal",
            ParentHostHwnd: wtHwnd,
            PaneTitle: "Run Test 2",
            PaneRuntimeId: runTest2RuntimeId,
            PaneRootProcessId: null));
        tracker.SetCopilotHost(runTestsSessionId, new CopilotHostInfo(
            HostHwnd: wtHwnd,
            HostPid: wtMonarchPid,
            CopilotPid: 101056,
            HostProcessName: "WindowsTerminal",
            HostKindLabel: "Windows Terminal",
            ParentHostHwnd: wtHwnd,
            PaneTitle: "Run Tests",
            PaneRuntimeId: runTestsRuntimeId,
            PaneRootProcessId: null));

        var resolved = tracker.ResolveSessionForHwnd(wtHwnd);

        Assert.Equal(runTestsSessionId, resolved);
    }

    [Fact]
    public void ResolveSessionForHwnd_MultipleSessionsShareWtHwnd_OtherPaneSelected_ReturnsThatSession()
    {
        // Symmetric inverse of the disambiguation test: when the OTHER pane is
        // selected, we must return THAT session — proving the disambiguation is
        // driven by the runtime-id match, not by insertion order or stable-but-
        // wrong logic. Same setup, only IsSelected flipped.
        var wtHwnd = new IntPtr(0x900FBC);
        var wtMonarchPid = 18144;
        var sessionA = "session-a-runtime";
        var sessionB = "session-b-runtime";
        const string runtimeA = "42.9179664.4.2978";
        const string runtimeB = "42.9179664.4.2983";

        var tree = new FakeProcessTree()
            .Add(wtMonarchPid, null, "WindowsTerminal", wtHwnd)
            .AddWindows(wtMonarchPid, wtHwnd);

        var paneA = new WindowsTerminalPaneInfo(
            Name: "Pane A", Hwnd: IntPtr.Zero, ProcessId: wtMonarchPid,
            IsSelected: false, Select: () => { },
            RuntimeId: runtimeA, PaneRootProcessId: null);
        var paneB = new WindowsTerminalPaneInfo(
            Name: "Pane B", Hwnd: IntPtr.Zero, ProcessId: wtMonarchPid,
            IsSelected: true, Select: () => { },
            RuntimeId: runtimeB, PaneRootProcessId: null);
        var gateway = FakeWindowsTerminalPaneGateway.PerHwnd(new Dictionary<IntPtr, IReadOnlyList<WindowsTerminalPaneInfo>>
        {
            [wtHwnd] = new[] { paneA, paneB }
        });
        var tracker = CreateTracker(tree, gateway);

        // Insert sessionA first (iterates first) — but sessionB's pane is selected.
        tracker.SetCopilotHost(sessionA, new CopilotHostInfo(
            HostHwnd: wtHwnd, HostPid: wtMonarchPid, CopilotPid: 1001,
            HostProcessName: "WindowsTerminal", HostKindLabel: "Windows Terminal",
            ParentHostHwnd: wtHwnd, PaneTitle: "Pane A",
            PaneRuntimeId: runtimeA, PaneRootProcessId: null));
        tracker.SetCopilotHost(sessionB, new CopilotHostInfo(
            HostHwnd: wtHwnd, HostPid: wtMonarchPid, CopilotPid: 2002,
            HostProcessName: "WindowsTerminal", HostKindLabel: "Windows Terminal",
            ParentHostHwnd: wtHwnd, PaneTitle: "Pane B",
            PaneRuntimeId: runtimeB, PaneRootProcessId: null));

        var resolved = tracker.ResolveSessionForHwnd(wtHwnd);

        Assert.Equal(sessionB, resolved);
    }

    [Fact]
    public void ResolveSessionForHwnd_MultipleSessionsShareWtHwnd_NullRuntimeIds_DisambiguatesByPaneRootPid()
    {
        // Reproduces Roger's 2026-05-04 follow-up finding (live diag.log):
        //   FocusCopilotHost session=59add766 ... host=13369638 runtimeId=null paneRootPid=24180
        //   FocusCopilotHost session=fd5a52ff ... host=13369638 runtimeId=null paneRootPid=114552
        // Two NEW tabs in an existing wt window — ResolveWindowsTerminalPane's
        // FindMatchingPane returns null for both (UIA only exposes PaneRootProcessId
        // for the SELECTED tab's content; inactive tabs return null/wt-pid), so both
        // hosts end up with PaneRuntimeId=null. The runtime-id-only disambiguation
        // can't tell them apart and falls back to first-match — same bug as before.
        // PaneRootProcessId IS reliably set on each host (from wtContext.PaneRootPid
        // at initial resolve time) and IS reliably set on the SELECTED pane, so we
        // can disambiguate via that field instead.
        var wtHwnd = new IntPtr(13369638);
        var wtPid = 18144;
        var sessionA = "59add766-de3f-44e6-8aec-82dc4fe01f8c";
        var sessionB = "fd5a52ff-0896-4ff1-a24e-a4c40f6ec02d";
        var pwshPidA = 24180;
        var pwshPidB = 114552;

        var tree = new FakeProcessTree()
            .Add(wtPid, null, "WindowsTerminal", wtHwnd)
            .AddWindows(wtPid, wtHwnd);

        // Selected pane is sessionB's tab (user just clicked the fd5a52ff link).
        // Its PaneRootProcessId is reliably the pwsh pid (114552). RuntimeId is set
        // to the UIA runtime id but neither host's stored PaneRuntimeId equals it,
        // so runtime-id matching alone can't pick the right session.
        var paneSelected = new WindowsTerminalPaneInfo(
            Name: "Process Hi 2 Message",
            Hwnd: IntPtr.Zero,
            ProcessId: wtPid,
            IsSelected: true,
            Select: () => { },
            RuntimeId: "live-runtime-from-uia-not-stored-anywhere",
            PaneRootProcessId: pwshPidB);
        var paneInactive = new WindowsTerminalPaneInfo(
            Name: "Respond To Greeting",
            Hwnd: IntPtr.Zero,
            ProcessId: wtPid,
            IsSelected: false,
            Select: () => { },
            RuntimeId: "another-live-runtime",
            PaneRootProcessId: null); // inactive panes typically have no descendants
        var gateway = FakeWindowsTerminalPaneGateway.PerHwnd(new Dictionary<IntPtr, IReadOnlyList<WindowsTerminalPaneInfo>>
        {
            [wtHwnd] = new[] { paneSelected, paneInactive }
        });
        var tracker = CreateTracker(tree, gateway);

        // Insert sessionA FIRST (iterates first → pre-fix bug returns this one).
        // Both hosts have PaneRuntimeId=null exactly like the live diag.
        tracker.SetCopilotHost(sessionA, new CopilotHostInfo(
            HostHwnd: wtHwnd, HostPid: wtPid, CopilotPid: 67592,
            HostProcessName: "WindowsTerminal", HostKindLabel: "Windows Terminal",
            ParentHostHwnd: wtHwnd, PaneTitle: null,
            PaneRuntimeId: null,
            PaneRootProcessId: pwshPidA));
        tracker.SetCopilotHost(sessionB, new CopilotHostInfo(
            HostHwnd: wtHwnd, HostPid: wtPid, CopilotPid: 32996,
            HostProcessName: "WindowsTerminal", HostKindLabel: "Windows Terminal",
            ParentHostHwnd: wtHwnd, PaneTitle: null,
            PaneRuntimeId: null,
            PaneRootProcessId: pwshPidB));

        var resolved = tracker.ResolveSessionForHwnd(wtHwnd);

        Assert.Equal(sessionB, resolved);
    }

    [Fact]
    public void OnWindowTitleChanged_TitleMatch_UpdatesPaneTitleOnCopilotHost()
    {
        // Closes the second-order bug Roger reported 2026-05-04 21:16:
        // clicking "Respond To Greeting" focuses the correct wt window but the tab
        // stays on "Process Hi 2 Message". Root cause: TrySelectWindowsTerminalPane
        // falls back to FindMatchingPane(panes, copilotPid, terms, hostInfo.PaneTitle,
        // hostInfo.PaneRootProcessId). For two new tabs in the same wt:
        //   - PaneRuntimeId is null on both hosts (FindMatchingPane returned null at
        //     resolve time because UIA only exposes content for the SELECTED tab)
        //   - PaneRootProcessId for the CLICKED inactive pane is null in the gateway
        //     enumeration (same UIA quirk)
        //   - terms built with empty sessionId yield no title match
        //   - PaneTitle on the host is also null
        // -> FindMatchingPane returns null -> Select never called -> tab stays.
        //
        // OnWindowTitleChanged already title-matches each tab title to its session
        // (via session-summary equality or "Copilot CLI - <sessionId>" parse). The
        // fix: persist that title onto _copilotHosts[sessionId].PaneTitle so future
        // TrySelectWindowsTerminalPane calls have a preferred-title hint that UIA
        // exposes on every tab regardless of selection.
        var sessionId = "59add766-de3f-44e6-8aec-82dc4fe01f8c";
        var copilotPid = 67592;
        var pwshPid = 24180;
        var wtPid = 18144;
        var wtHwnd = new IntPtr(13369638);

        var tree = new FakeProcessTree()
            .Add(copilotPid, pwshPid, "copilot", IntPtr.Zero)
            .Add(pwshPid, wtPid, "pwsh", IntPtr.Zero)
            .Add(wtPid, null, "WindowsTerminal", wtHwnd)
            .AddWindows(wtPid, wtHwnd);

        // No pane in the enumeration matches the session — exactly mirrors the live
        // wt state when FindMatchingPane returns null (different user-set tab name,
        // no PaneRootProcessId on the inactive pane). The title-match path runs
        // entirely on the title hook, not on UIA pane enumeration.
        var unrelatedPane = new WindowsTerminalPaneInfo(
            Name: "unrelated",
            Hwnd: IntPtr.Zero,
            ProcessId: wtPid,
            IsSelected: true,
            Select: () => { },
            RuntimeId: "rt-unrelated",
            PaneRootProcessId: 999999);
        var gateway = FakeWindowsTerminalPaneGateway.PerHwnd(new Dictionary<IntPtr, IReadOnlyList<WindowsTerminalPaneInfo>>
        {
            [wtHwnd] = new[] { unrelatedPane }
        });
        var tracker = CreateTracker(tree, gateway);

        // Seed the host with PaneTitle=null (the production state when the resolver
        // failed to pin a unique pane for this session).
        tracker.SetCopilotHost(sessionId, new CopilotHostInfo(
            HostHwnd: wtHwnd, HostPid: wtPid, CopilotPid: copilotPid,
            HostProcessName: "WindowsTerminal", HostKindLabel: "Windows Terminal",
            ParentHostHwnd: wtHwnd, PaneTitle: null,
            PaneRuntimeId: null,
            PaneRootProcessId: pwshPid));

        // Title hook fires when wt's tab title becomes the session's summary.
        // MatchTrackedWindowTitle parses "Copilot CLI - <sessionId>" too -- using
        // the explicit form here to lock the assertion onto title-match (not session-
        // summary equality which would also match in this contrived test).
        tracker.OnWindowTitleChanged(wtHwnd, $"Copilot CLI - {sessionId}", sessionSummaries: null);

        var host = tracker.GetCopilotHost(sessionId);
        Assert.NotNull(host);
        // Pre-fix: PaneTitle stays null because OnWindowTitleChanged never propagates
        // the observed title into _copilotHosts. Post-fix: title is persisted so the
        // next FindMatchingPane(...) for this host has a preferredTitle hint that
        // UIA reliably exposes for both selected and unselected tabs.
        Assert.Equal($"Copilot CLI - {sessionId}", host!.PaneTitle);
    }

    [Fact]
    public void OnWindowTitleChanged_SessionSummaryMatch_UpdatesPaneTitleOnCopilotHost()
    {
        // Inverse case driving the actual production scenario: user renames a wt
        // tab to a string equal to the session summary (e.g., "Respond To Greeting"
        // matching session 59add766 whose summary is "Respond To Greeting").
        // MatchTrackedWindowTitle resolves via session-summary equality, NOT the
        // "Copilot CLI - <sessionId>" parse path. Both paths must update PaneTitle.
        var sessionId = "59add766-de3f-44e6-8aec-82dc4fe01f8c";
        var copilotPid = 67592;
        var pwshPid = 24180;
        var wtPid = 18144;
        var wtHwnd = new IntPtr(13369638);
        const string sessionSummary = "Respond To Greeting";

        var tree = new FakeProcessTree()
            .Add(copilotPid, pwshPid, "copilot", IntPtr.Zero)
            .Add(pwshPid, wtPid, "pwsh", IntPtr.Zero)
            .Add(wtPid, null, "WindowsTerminal", wtHwnd)
            .AddWindows(wtPid, wtHwnd);

        var gateway = new FakeWindowsTerminalPaneGateway([]);
        var tracker = CreateTracker(tree, gateway);

        tracker.SetCopilotHost(sessionId, new CopilotHostInfo(
            HostHwnd: wtHwnd, HostPid: wtPid, CopilotPid: copilotPid,
            HostProcessName: "WindowsTerminal", HostKindLabel: "Windows Terminal",
            ParentHostHwnd: wtHwnd, PaneTitle: null,
            PaneRuntimeId: null,
            PaneRootProcessId: pwshPid));

        tracker.OnWindowTitleChanged(
            wtHwnd,
            sessionSummary,
            sessionSummaries: new Dictionary<string, string> { [sessionSummary] = sessionId });

        var host = tracker.GetCopilotHost(sessionId);
        Assert.NotNull(host);
        Assert.Equal(sessionSummary, host!.PaneTitle);
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
