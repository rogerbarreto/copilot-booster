public sealed class SessionGridBellTests
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
        grid.Columns.Add("Session", "Session");
        grid.Columns.Add("CWD", "CWD");
        grid.Columns.Add("Date", "Date");
        grid.Columns.Add("Context", "Context");
        grid.Columns.Add("RunningApps", "Running");
        return grid;
    }

    private static DataGridViewRow AddBellRow(DataGridView grid, string sessionId, string activeText = "")
    {
        var rowIndex = grid.Rows.Add("bell", sessionId, @"C:\test", "2025-01-01", "", activeText);
        var row = grid.Rows[rowIndex];
        row.Tag = sessionId;
        row.DefaultCellStyle.BackColor = Color.Red;
        row.DefaultCellStyle.SelectionBackColor = Color.DarkRed;
        return row;
    }

    [Fact]
    public void DismissBell_ClearsBellStatus_WhenRowHasBell()
    {
        var grid = CreateGrid();
        var tracker = new ActiveStatusTracker();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        var row = AddBellRow(grid, "s1");

        visuals.DismissBell(row);

        Assert.Equal("", row.Cells[0].Value);
    }

    [Fact]
    public void DismissBell_ResetsRowColors_WhenNotActive()
    {
        var grid = CreateGrid();
        var tracker = new ActiveStatusTracker();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        var row = AddBellRow(grid, "s1");

        visuals.DismissBell(row);

        Assert.Equal(Color.Empty, row.DefaultCellStyle.BackColor);
        Assert.Equal(Color.Empty, row.DefaultCellStyle.ForeColor);
        Assert.Equal(Color.Empty, row.DefaultCellStyle.SelectionBackColor);
    }

    [Fact]
    public void DismissBell_SetsActiveColors_WhenSessionHasRunningApps()
    {
        var grid = CreateGrid();
        var tracker = new ActiveStatusTracker();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        var row = AddBellRow(grid, "s1", activeText: "copilot-cli");

        visuals.DismissBell(row);

        Assert.Equal("", row.Cells[0].Value);
        Assert.NotEqual(Color.Empty, row.DefaultCellStyle.BackColor);
        Assert.NotEqual(Color.Red, row.DefaultCellStyle.BackColor);
    }

    [Fact]
    public void DismissBell_NoOp_WhenRowHasNoBell()
    {
        var grid = CreateGrid();
        var tracker = new ActiveStatusTracker();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        var rowIndex = grid.Rows.Add("working", "s1", @"C:\test", "2025-01-01", "", "copilot-cli");
        var row = grid.Rows[rowIndex];
        row.Tag = "s1";
        row.DefaultCellStyle.BackColor = Color.Blue;

        visuals.DismissBell(row);

        // Should not have changed anything
        Assert.Equal("working", row.Cells[0].Value);
        Assert.Equal(Color.Blue, row.DefaultCellStyle.BackColor);
    }

    [Fact]
    public void DismissBell_SuppressesFutureBellNotifications()
    {
        var grid = CreateGrid();
        var tracker = new ActiveStatusTracker();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        var row = AddBellRow(grid, "s1");

        visuals.DismissBell(row);

        // Session should now be suppressed in the tracker
        Assert.True(tracker.IsStartupSuppressed("s1"));
    }
}
