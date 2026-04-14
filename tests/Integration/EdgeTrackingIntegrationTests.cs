namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// Integration tests for Edge browser session tracking.
/// Validates that when "Open in Edge" is triggered for a session,
/// the Edge browser link appears in the sessions grid Active column.
/// </summary>
public sealed class EdgeTrackingIntegrationTests
{
    private static LauncherSettings CreateTestSettings()
    {
        var settings = LauncherSettings.CreateDefault();
        settings.SuppressSave = true;
        return settings;
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        grid.Columns.Add("Status", "");
        grid.Columns.Add("Name", "Name");
        grid.Columns.Add("CWD", "CWD");
        grid.Columns.Add("LastModified", "LastModified");
        grid.Columns.Add("Context", "Context");
        grid.Columns.Add("Active", "Active");
        grid.Columns.Add("GitHub", "GitHub");
        return grid;
    }

    private static void AddRow(DataGridView grid, string sessionId)
    {
        var rowIndex = grid.Rows.Add("", sessionId, "", "", "", "");
        grid.Rows[rowIndex].Tag = sessionId;
    }

    private static string GetActiveCell(DataGridView grid, int rowIndex) =>
        grid.Rows[rowIndex].Cells[5].Value?.ToString() ?? "";

    private static void RefreshGrid(
        ActiveStatusTracker tracker,
        SessionGridVisuals visuals,
        List<NamedSession> sessions)
    {
        var snapshot = tracker.IncrementalRefresh(sessions);
        visuals.UpdateGridIncremental(snapshot);
    }

    /// <summary>
    /// RED TEST: When "Open in Edge" is triggered for a session, the Edge browser
    /// link should appear in the session's Active column in the sessions grid.
    ///
    /// This test replicates the MainForm.OnOpenEdge flow:
    /// 1. Create EdgeWorkspaceService for the session
    /// 2. Track it in ActiveStatusTracker (same as MainForm does)
    /// 3. Refresh the grid
    /// 4. Assert "Edge" appears in the Active column
    ///
    /// Currently RED: BuildActiveText only shows "Edge" when IsOpen returns true,
    /// which requires a real Edge window with a matching tab title. After tracking
    /// the workspace, the browser link for the session does not appear in the
    /// sessions list until the OS-level window is detected.
    /// </summary>
    [StaFact]
    public void OpenInEdge_TrackedWorkspace_EdgeLinkAppearsInActiveColumn()
    {
        const string SessionId = "edge-tracking-test";
        const string SessionName = "Edge Tracking Test";

        var tracker = new ActiveStatusTracker();
        var grid = CreateGrid();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        AddRow(grid, SessionId);
        var sessions = new List<NamedSession>
        {
            new() { Id = SessionId, Summary = SessionName }
        };

        // ── Step 1: Session starts with no Edge ──
        RefreshGrid(tracker, visuals, sessions);
        Assert.Equal("", GetActiveCell(grid, 0));
        Assert.False(tracker.HasEdgeWorkspace(SessionId));

        // ── Step 2: Simulate "Open in Edge" — create and track workspace ──
        // This mirrors the flow in MainForm.ContextMenu.cs OnOpenEdge handler:
        //   var workspace = SessionInteractionManager.CreateEdgeWorkspace(sid);
        //   tracker.TrackEdge(sid, workspace);
        var workspace = SessionInteractionManager.CreateEdgeWorkspace(SessionId);
        tracker.TrackEdge(SessionId, workspace);

        // Verify workspace is registered in the tracker
        Assert.True(tracker.HasEdgeWorkspace(SessionId));
        Assert.True(tracker.TryGetEdge(SessionId, out var retrievedWs));
        Assert.Equal(SessionId, retrievedWs!.WorkspaceId);

        // Verify the session link URL can be built for this workspace
        var sessionHtml = Path.Combine(AppContext.BaseDirectory, "session.html");
        var sessionLink = EdgeWorkspaceService.BuildSessionUrl(sessionHtml, SessionId, SessionName);
        Assert.Contains(SessionId, sessionLink);

        // ── Step 3: Refresh grid — Edge link should appear in Active column ──
        RefreshGrid(tracker, visuals, sessions);
        var activeText = GetActiveCell(grid, 0);

        // RED: The Edge browser link should show in the Active column after tracking.
        // Currently fails because BuildActiveText requires ws.IsOpen (a real Edge
        // window detected via UI Automation) to include "Edge" in the output.
        Assert.Contains("Edge", activeText);
    }
}
