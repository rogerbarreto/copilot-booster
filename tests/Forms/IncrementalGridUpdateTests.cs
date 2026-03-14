public sealed class IncrementalGridUpdateTests
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

    private static ActiveStatusSnapshot MakeSnapshot(
        Dictionary<string, string>? activeText = null,
        Dictionary<string, string>? statusIcons = null)
    {
        return new ActiveStatusSnapshot(
            activeText ?? [],
            [],
            statusIcons ?? []
        );
    }

    private static void AddRow(DataGridView grid, string sessionId, string statusIcon = "", string activeText = "")
    {
        var rowIndex = grid.Rows.Add(statusIcon, sessionId, "", "", "", activeText);
        grid.Rows[rowIndex].Tag = sessionId;
    }

    [StaFact]
    public void ApplyRowStyling_BellState_SetsBellColors()
    {
        var grid = CreateGrid();
        var tracker = new ActiveStatusTracker();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        AddRow(grid, "s1");

        // Trigger bell styling via UpdateGridIncremental
        var snapshot = MakeSnapshot(statusIcons: new Dictionary<string, string> { ["s1"] = "bell" });
        visuals.UpdateGridIncremental(snapshot);

        var row = grid.Rows[0];
        Assert.Equal("bell", row.Cells[0].Value?.ToString());
        // Bell state sets a non-empty BackColor
        Assert.NotEqual(Color.Empty, row.DefaultCellStyle.BackColor);
    }

    [StaFact]
    public void UpdateGridIncremental_StatusIconChange_UpdatesOnlyChangedCell()
    {
        var grid = CreateGrid();
        var tracker = new ActiveStatusTracker();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        AddRow(grid, "s1", statusIcon: "", activeText: "");
        AddRow(grid, "s2", statusIcon: "", activeText: "");

        // Change status icon for s1 only
        var snapshot = MakeSnapshot(statusIcons: new Dictionary<string, string> { ["s1"] = "working" });
        visuals.UpdateGridIncremental(snapshot);

        // s1 status should be updated
        Assert.Equal("working", grid.Rows[0].Cells[0].Value?.ToString());
        // s2 status should remain unchanged
        Assert.Equal("", grid.Rows[1].Cells[0].Value?.ToString());
    }

    [StaFact]
    public void UpdateGridIncremental_ActiveTextChange_UpdatesCell5()
    {
        var grid = CreateGrid();
        var tracker = new ActiveStatusTracker();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        AddRow(grid, "s1", statusIcon: "", activeText: "");

        var snapshot = MakeSnapshot(activeText: new Dictionary<string, string> { ["s1"] = "doing something" });
        visuals.UpdateGridIncremental(snapshot);

        Assert.Equal("doing something", grid.Rows[0].Cells[5].Value?.ToString());
    }

    [StaFact]
    public void UpdateGridIncremental_NoChanges_NoCellMutations()
    {
        var grid = CreateGrid();
        var tracker = new ActiveStatusTracker();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        AddRow(grid, "s1", statusIcon: "working", activeText: "busy");
        AddRow(grid, "s2", statusIcon: "", activeText: "");

        // Capture original values
        var s1Status = grid.Rows[0].Cells[0].Value;
        var s1Active = grid.Rows[0].Cells[5].Value;
        var s2Status = grid.Rows[1].Cells[0].Value;
        var s2Active = grid.Rows[1].Cells[5].Value;

        // Pass same state
        var snapshot = MakeSnapshot(
            activeText: new Dictionary<string, string> { ["s1"] = "busy" },
            statusIcons: new Dictionary<string, string> { ["s1"] = "working" });
        visuals.UpdateGridIncremental(snapshot);

        // Values should be exactly the same object references (no mutation)
        Assert.Same(s1Status, grid.Rows[0].Cells[0].Value);
        Assert.Same(s1Active, grid.Rows[0].Cells[5].Value);
        Assert.Same(s2Status, grid.Rows[1].Cells[0].Value);
        Assert.Same(s2Active, grid.Rows[1].Cells[5].Value);
    }

    [StaFact]
    public void UpdateGridIncremental_WorkingStatus_SetsActiveRowStyling()
    {
        var grid = CreateGrid();
        var tracker = new ActiveStatusTracker();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        AddRow(grid, "s1");

        var snapshot = MakeSnapshot(statusIcons: new Dictionary<string, string> { ["s1"] = "working" });
        visuals.UpdateGridIncremental(snapshot);

        var row = grid.Rows[0];
        Assert.NotEqual(Color.Empty, row.DefaultCellStyle.BackColor);
        Assert.NotEqual(Color.Empty, row.DefaultCellStyle.ForeColor);
    }

    [StaFact]
    public void UpdateGridIncremental_ClearedStatus_ResetsRowStyling()
    {
        var grid = CreateGrid();
        var tracker = new ActiveStatusTracker();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        // Start with no status, then transition to working so ApplyRowStyling fires
        AddRow(grid, "s1", statusIcon: "", activeText: "");

        var workingSnapshot = MakeSnapshot(
            activeText: new Dictionary<string, string> { ["s1"] = "busy" },
            statusIcons: new Dictionary<string, string> { ["s1"] = "working" });
        visuals.UpdateGridIncremental(workingSnapshot);
        Assert.NotEqual(Color.Empty, grid.Rows[0].DefaultCellStyle.BackColor);

        // Now clear the status
        var clearedSnapshot = MakeSnapshot();
        visuals.UpdateGridIncremental(clearedSnapshot);

        var row = grid.Rows[0];
        Assert.Equal(Color.Empty, row.DefaultCellStyle.BackColor);
        Assert.Equal(Color.Empty, row.DefaultCellStyle.ForeColor);
        Assert.Equal(Color.Empty, row.DefaultCellStyle.SelectionBackColor);
    }
}
