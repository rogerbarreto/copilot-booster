public sealed class ActiveStatusTrackerEventTests
{
    // ── OnWindowDestroyed ──────────────────────────────────────────────

    [Fact]
    public void OnWindowDestroyed_TrackedProcess_ReturnsAffectedSession()
    {
        var tracker = new ActiveStatusTracker();
        var hwnd = new IntPtr(12345);
        tracker.TrackProcess("session-1", new ActiveProcess("VS Code", 0, null) { Hwnd = hwnd });

        var affected = tracker.OnWindowDestroyed(hwnd);

        Assert.Contains("session-1", affected);
    }

    [Fact]
    public void OnWindowDestroyed_TrackedProcess_RemovesFromTracking()
    {
        var tracker = new ActiveStatusTracker();
        var hwnd = new IntPtr(12345);
        tracker.TrackProcess("session-1", new ActiveProcess("VS Code", 0, null) { Hwnd = hwnd });

        tracker.OnWindowDestroyed(hwnd);

        // Destroying same HWND again should return empty (already removed)
        var second = tracker.OnWindowDestroyed(hwnd);
        Assert.Empty(second);
    }

    [Fact]
    public void OnWindowDestroyed_UnknownHwnd_ReturnsEmpty()
    {
        var tracker = new ActiveStatusTracker();

        var affected = tracker.OnWindowDestroyed(new IntPtr(99999));

        Assert.Empty(affected);
    }

    // ── OnProcessExited ────────────────────────────────────────────────

    [Fact]
    public void OnProcessExited_TrackedPid_ReturnsAffectedSession()
    {
        var tracker = new ActiveStatusTracker();
        tracker.TrackProcess("session-1", new ActiveProcess("VS Code", 1234, null));

        var affected = tracker.OnProcessExited(1234);

        Assert.Contains("session-1", affected);
    }

    [Fact]
    public void OnProcessExited_UnknownPid_ReturnsEmpty()
    {
        var tracker = new ActiveStatusTracker();

        var affected = tracker.OnProcessExited(9999);

        Assert.Empty(affected);
    }

    [Fact]
    public void OnProcessExited_MultipleSessionsSamePid_ReturnsBoth()
    {
        var tracker = new ActiveStatusTracker();
        tracker.TrackProcess("session-a", new ActiveProcess("VS Code", 5555, null));
        tracker.TrackProcess("session-b", new ActiveProcess("VS Code", 5555, null));

        var affected = tracker.OnProcessExited(5555);

        Assert.Contains("session-a", affected);
        Assert.Contains("session-b", affected);
    }

    // ── OnWindowTitleChanged ───────────────────────────────────────────

    [Fact]
    public void OnWindowTitleChanged_TerminalTitle_ReturnsMatchedSession()
    {
        var tracker = new ActiveStatusTracker();
        var hwnd = new IntPtr(100);

        var affected = tracker.OnWindowTitleChanged(hwnd, "Terminal - session-abc", null);

        Assert.Contains("session-abc", affected);
    }

    [Fact]
    public void OnWindowTitleChanged_TitleChangesToDifferentSession_SwapsTracking()
    {
        var tracker = new ActiveStatusTracker();
        var hwnd = new IntPtr(200);

        tracker.OnWindowTitleChanged(hwnd, "Terminal - session-abc", null);
        var affected = tracker.OnWindowTitleChanged(hwnd, "Terminal - session-xyz", null);

        Assert.Contains("session-xyz", affected);
        // Old session should also be in affected (it lost a window)
        Assert.Contains("session-abc", affected);
    }

    [Fact]
    public void OnWindowTitleChanged_NonMatchingTitle_ReturnsEmpty()
    {
        var tracker = new ActiveStatusTracker();
        var hwnd = new IntPtr(300);

        var affected = tracker.OnWindowTitleChanged(hwnd, "Notepad - Untitled", null);

        Assert.Empty(affected);
    }

    // ── BuildSessionSummaryMap ─────────────────────────────────────────

    [Fact]
    public void BuildSessionSummaryMap_SessionsWithSummaries_CreatesMap()
    {
        var sessions = new List<NamedSession>
        {
            new() { Id = "s1", Summary = "My Project" },
            new() { Id = "s2", Summary = "Other Project" }
        };

        var map = ActiveStatusTracker.BuildSessionSummaryMap(sessions);

        Assert.Equal("s1", map["My Project"]);
        Assert.Equal("s2", map["Other Project"]);
    }

    [Fact]
    public void BuildSessionSummaryMap_EmptyOrNullSummaries_Excluded()
    {
        var sessions = new List<NamedSession>
        {
            new() { Id = "s1", Summary = "" },
            new() { Id = "s2", Summary = "  " }
        };

        var map = ActiveStatusTracker.BuildSessionSummaryMap(sessions);

        Assert.Empty(map);
    }

    [Fact]
    public void BuildSessionSummaryMap_IgnoredSummary_Excluded()
    {
        var sessions = new List<NamedSession>
        {
            new() { Id = "s1", Summary = "GitHub Copilot" }
        };

        var map = ActiveStatusTracker.BuildSessionSummaryMap(sessions);

        Assert.Empty(map);
    }

    [Fact]
    public void BuildSessionSummaryMap_DuplicateSummaries_FirstOneWins()
    {
        var sessions = new List<NamedSession>
        {
            new() { Id = "first", Summary = "Shared Name" },
            new() { Id = "second", Summary = "Shared Name" }
        };

        var map = ActiveStatusTracker.BuildSessionSummaryMap(sessions);

        Assert.Equal("first", map["Shared Name"]);
        Assert.Single(map);
    }

    // ── IncrementalRefresh ─────────────────────────────────────────────

    [Fact]
    public void IncrementalRefresh_EmptyState_ReturnsEmptySnapshot()
    {
        var tracker = new ActiveStatusTracker();
        var sessions = new List<NamedSession>
        {
            new() { Id = "s1", Summary = "Test" }
        };

        var snapshot = tracker.IncrementalRefresh(sessions);

        Assert.Empty(snapshot.ActiveTextBySessionId);
    }

    [Fact]
    public void IncrementalRefresh_SessionWithAlias_UsesAliasAsName()
    {
        var tracker = new ActiveStatusTracker();
        var sessions = new List<NamedSession>
        {
            new() { Id = "s1", Summary = "Original", Alias = "My Alias" }
        };

        var snapshot = tracker.IncrementalRefresh(sessions);

        Assert.Equal("My Alias", snapshot.SessionNamesById["s1"]);
    }

    [Fact]
    public void IncrementalRefresh_SessionWithoutAlias_UsesSummaryAsName()
    {
        var tracker = new ActiveStatusTracker();
        var sessions = new List<NamedSession>
        {
            new() { Id = "s1", Summary = "My Summary", Alias = "" }
        };

        var snapshot = tracker.IncrementalRefresh(sessions);

        Assert.Equal("My Summary", snapshot.SessionNamesById["s1"]);
    }

    // ── BuildActiveText ────────────────────────────────────────────────

    [Fact]
    public void BuildActiveText_Empty_ReturnsEmptyString()
    {
        var tracker = new ActiveStatusTracker();

        var result = tracker.BuildActiveText("nonexistent");

        Assert.Equal("", result);
    }

    [Fact]
    public void BuildActiveText_WithTrackedWindows_ReturnsLabels()
    {
        var tracker = new ActiveStatusTracker();
        var hwnd = new IntPtr(500);

        // Add a tracked window via OnWindowTitleChanged
        tracker.OnWindowTitleChanged(hwnd, "Terminal - session-1", null);

        var result = tracker.BuildActiveText("session-1");

        Assert.Contains("Terminal", result);
    }

    [Fact]
    public void BuildActiveText_WithMultipleWindows_ReturnsMultipleLabels()
    {
        var tracker = new ActiveStatusTracker();

        tracker.OnWindowTitleChanged(new IntPtr(501), "Terminal - session-1", null);
        tracker.OnWindowTitleChanged(new IntPtr(502), "Copilot CLI - session-1", null);

        var result = tracker.BuildActiveText("session-1");

        Assert.Contains("Terminal", result);
        Assert.Contains("Copilot CLI", result);
    }

    // ── OnWindowTitleChanged + RefreshActiveSessionIds ──────────────────

    [Fact]
    public void OnWindowTitleChanged_CopilotCliMatch_RefreshesActiveSessionIds()
    {
        var tracker = new ActiveStatusTracker();
        var hwnd = new IntPtr(600);

        // Before title match, _activeSessionIds is empty (no PID registry)
        var textBefore = tracker.BuildActiveText("session-test");
        Assert.Equal("", textBefore);

        // Title match for Copilot CLI triggers RefreshActiveSessionIds
        var affected = tracker.OnWindowTitleChanged(hwnd, "Copilot CLI - session-test", null);

        Assert.Contains("session-test", affected);
        // The window should be tracked via _activeTrackedWindows, so BuildActiveText returns "Copilot CLI"
        var textAfter = tracker.BuildActiveText("session-test");
        Assert.Contains("Copilot CLI", textAfter);
    }

    [Fact]
    public void OnWindowTitleChanged_TerminalMatch_DoesNotRefreshActiveSessionIds()
    {
        var tracker = new ActiveStatusTracker();
        var hwnd = new IntPtr(700);

        // Terminal match should NOT call RefreshActiveSessionIds (only Copilot CLI does)
        var affected = tracker.OnWindowTitleChanged(hwnd, "Terminal - session-test", null);

        Assert.Contains("session-test", affected);
        var text = tracker.BuildActiveText("session-test");
        Assert.Contains("Terminal", text);
    }

    // ── Immediate detection on window creation with title ──────────────

    [Fact]
    public void OnWindowTitleChanged_WindowCreatedWithTitle_ImmediatelyTracked()
    {
        var tracker = new ActiveStatusTracker();
        var hwnd = new IntPtr(800);

        // Simulate what MainForm.WindowCreated now does: read title and call OnWindowTitleChanged
        var affected = tracker.OnWindowTitleChanged(hwnd, "Terminal - session-new", null);

        Assert.Contains("session-new", affected);

        // Verify the session appears in incremental snapshot
        var sessions = new List<NamedSession> { new() { Id = "session-new", Summary = "New Session" } };
        var snapshot = tracker.IncrementalRefresh(sessions);

        Assert.True(snapshot.ActiveTextBySessionId.ContainsKey("session-new"));
        Assert.Contains("Terminal", snapshot.ActiveTextBySessionId["session-new"]);
    }

    [Fact]
    public void OnWindowTitleChanged_CopilotCliCreatedWithTitle_ImmediatelyTracked()
    {
        var tracker = new ActiveStatusTracker();
        var hwnd = new IntPtr(900);

        var affected = tracker.OnWindowTitleChanged(hwnd, "Copilot CLI - session-cli", null);

        Assert.Contains("session-cli", affected);

        var sessions = new List<NamedSession> { new() { Id = "session-cli", Summary = "CLI Session" } };
        var snapshot = tracker.IncrementalRefresh(sessions);

        Assert.True(snapshot.ActiveTextBySessionId.ContainsKey("session-cli"));
        Assert.Contains("Copilot CLI", snapshot.ActiveTextBySessionId["session-cli"]);
    }
}
