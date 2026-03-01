public sealed class SessionGridColumnOrderTests
{
    private static ActiveStatusSnapshot MakeSnapshot(params string[] runningIds)
    {
        var active = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in runningIds)
        {
            active[id] = "running";
            names[id] = id;
            icons[id] = "working";
        }

        return new ActiveStatusSnapshot(active, names, icons);
    }

    private static List<NamedSession> MakeSessions(params (string id, int daysAgo)[] items)
    {
        return items.Select(x => new NamedSession
        {
            Id = x.id,
            Summary = x.id,
            Folder = @"C:\test\" + x.id,
            Tab = "Active",
            LastModified = DateTime.Now.AddDays(-x.daysAgo)
        }).ToList();
    }

    private static DataGridView CreateSessionGrid()
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToOrderColumns = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Status",
            HeaderText = "",
            Width = 30,
            Frozen = true
        });
        grid.Columns.Add("Session", "Session");
        grid.Columns.Add("CWD", "CWD");
        grid.Columns.Add("Date", "Date");
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "RunningApps",
            HeaderText = "Running"
        });

        return grid;
    }

    private static LauncherSettings CreateTestSettings()
    {
        var settings = LauncherSettings.CreateDefault();
        settings.SuppressSave = true;
        return settings;
    }

    [StaFact]
    public void Populate_WorksCorrectly_WhenColumnsHaveCustomDisplayIndex()
    {
        var grid = CreateSessionGrid();
        grid.Columns["Date"]!.DisplayIndex = 2;
        grid.Columns["CWD"]!.DisplayIndex = 3;

        var tracker = new ActiveStatusTracker();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        var sessions = MakeSessions(("s1", 0), ("s2", 1), ("s3", 2));
        var snapshot = MakeSnapshot();
        visuals.Populate(sessions, snapshot, null);

        // Data should still be accessible by column name regardless of display order
        Assert.Equal(3, grid.RowCount);
        Assert.Equal("s1", grid.Rows[0].Tag);
        Assert.Equal("s2", grid.Rows[1].Tag);

        // Verify cells by column name work correctly
        var cwdValue = grid.Rows[0].Cells["CWD"].Value?.ToString();
        Assert.False(string.IsNullOrEmpty(cwdValue));

        // Verify selection still works
        grid.ClearSelection();
        grid.Rows[1].Selected = true;
        grid.CurrentCell = grid.Rows[1].Cells[0];
        Assert.Equal("s2", visuals.GetSelectedSessionId());

        // Verify display indices stayed as we set them
        Assert.Equal(2, grid.Columns["Date"]!.DisplayIndex);
        Assert.Equal(3, grid.Columns["CWD"]!.DisplayIndex);
    }

    [StaFact]
    public void ColumnOrder_StatusColumnFrozen_CannotBeDragged()
    {
        var grid = CreateSessionGrid();

        Assert.True(grid.Columns["Status"]!.Frozen);
        Assert.True(grid.AllowUserToOrderColumns);
        Assert.Equal(0, grid.Columns["Status"]!.DisplayIndex);
    }

    [StaFact]
    public void Populate_PreservesSelection_AfterColumnReorderAndRefresh()
    {
        using var form = new Form { Width = 800, Height = 400 };
        var grid = CreateSessionGrid();
        grid.Dock = DockStyle.Fill;
        form.Controls.Add(grid);
        form.Show();

        var tracker = new ActiveStatusTracker();
        var visuals = new SessionGridVisuals(grid, tracker, CreateTestSettings());

        var sessions = MakeSessions(("s1", 0), ("s2", 1), ("s3", 2));
        var snapshot = MakeSnapshot();
        visuals.Populate(sessions, snapshot, null);

        // Select s2
        grid.ClearSelection();
        grid.Rows[1].Selected = true;
        grid.CurrentCell = grid.Rows[1].Cells[0];
        Assert.Equal("s2", visuals.GetSelectedSessionId());

        // Rearrange columns (simulates user drag)
        grid.Columns["RunningApps"]!.DisplayIndex = 1;

        // Refresh (re-populate)
        var refreshed = MakeSessions(("s1", 0), ("s2", 1), ("s3", 2));
        visuals.Populate(refreshed, snapshot, null);

        // Selection should be preserved
        Assert.Equal("s2", visuals.GetSelectedSessionId());

        // Column order should be preserved
        Assert.Equal(1, grid.Columns["RunningApps"]!.DisplayIndex);

        form.Close();
    }

    [StaFact]
    public void ColumnOrder_PreservedAcrossTabSwitch()
    {
        var settings = CreateTestSettings();
        settings.SessionTabs = ["Active", "Done", "Archived"];
        settings.SessionColumnOrder = ["CWD", "Session", "Date", "RunningApps"];

        using var form = new Form { Width = 800, Height = 400 };
        var panel = new Panel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);

        var grid = CreateSessionGrid();
        grid.Dock = DockStyle.Fill;
        panel.Controls.Add(grid);
        form.Show();

        var tracker = new ActiveStatusTracker();
        var visuals = new ExistingSessionsVisuals(panel, tracker, settings);

        visuals.SessionGrid.Columns["CWD"]!.DisplayIndex = 1;
        visuals.SessionGrid.Columns["Session"]!.DisplayIndex = 2;

        int cwdBefore = visuals.SessionGrid.Columns["CWD"]!.DisplayIndex;
        int sessionBefore = visuals.SessionGrid.Columns["Session"]!.DisplayIndex;

        // Switch to "Done" tab (reparents grid)
        visuals.SessionTabs.SelectedIndex = 1;

        Assert.Equal(cwdBefore, visuals.SessionGrid.Columns["CWD"]!.DisplayIndex);
        Assert.Equal(sessionBefore, visuals.SessionGrid.Columns["Session"]!.DisplayIndex);
        form.Close();
    }

    [StaFact]
    public void ColumnOrder_PreservedAcrossBuildSessionTabs()
    {
        var settings = CreateTestSettings();
        settings.SessionTabs = ["Active", "Archived"];
        settings.SessionColumnOrder = ["CWD", "Session", "Date", "RunningApps"];

        using var form = new Form { Width = 800, Height = 400 };
        var panel = new Panel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);

        var grid = CreateSessionGrid();
        grid.Dock = DockStyle.Fill;
        panel.Controls.Add(grid);
        form.Show();

        var tracker = new ActiveStatusTracker();
        var visuals = new ExistingSessionsVisuals(panel, tracker, settings);

        visuals.SessionGrid.Columns["CWD"]!.DisplayIndex = 1;
        visuals.SessionGrid.Columns["Session"]!.DisplayIndex = 2;

        int cwdBefore = visuals.SessionGrid.Columns["CWD"]!.DisplayIndex;
        int sessionBefore = visuals.SessionGrid.Columns["Session"]!.DisplayIndex;

        // Rebuild tabs (adds a new tab, reparents grid)
        settings.SessionTabs = ["Active", "Archived", "Work"];
        visuals.BuildSessionTabs();

        Assert.Equal(cwdBefore, visuals.SessionGrid.Columns["CWD"]!.DisplayIndex);
        Assert.Equal(sessionBefore, visuals.SessionGrid.Columns["Session"]!.DisplayIndex);
        form.Close();
    }
}
