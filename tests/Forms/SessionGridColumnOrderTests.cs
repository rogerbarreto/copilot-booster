public sealed class SessionGridColumnOrderTests
{
    /// <summary>
    /// Runs an action on an STA thread (required for WinForms controls).
    /// </summary>
    private static void RunOnSta(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught != null)
        {
            throw caught;
        }
    }

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

    [Fact]
    public void Populate_WorksCorrectly_WhenColumnsHaveCustomDisplayIndex()
    {
        RunOnSta(() =>
        {
            var grid = CreateSessionGrid();

            // Rearrange columns: move CWD to after Date
            // Default DisplayIndex: Status=0, Session=1, CWD=2, Date=3, RunningApps=4
            // New:                  Status=0, Session=1, Date=2, CWD=3, RunningApps=4
            grid.Columns["Date"]!.DisplayIndex = 2;
            grid.Columns["CWD"]!.DisplayIndex = 3;

            var tracker = new ActiveStatusTracker();
            var visuals = new SessionGridVisuals(grid, tracker);

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
        });
    }

    [Fact]
    public void ColumnOrder_StatusColumnFrozen_CannotBeDragged()
    {
        RunOnSta(() =>
        {
            var grid = CreateSessionGrid();

            Assert.True(grid.Columns["Status"]!.Frozen);
            Assert.True(grid.AllowUserToOrderColumns);
            Assert.Equal(0, grid.Columns["Status"]!.DisplayIndex);
        });
    }

    [Fact]
    public void Populate_PreservesSelection_AfterColumnReorderAndRefresh()
    {
        RunOnSta(() =>
        {
            using var form = new Form { Width = 800, Height = 400 };
            var grid = CreateSessionGrid();
            grid.Dock = DockStyle.Fill;
            form.Controls.Add(grid);
            form.Show();

            var tracker = new ActiveStatusTracker();
            var visuals = new SessionGridVisuals(grid, tracker);

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
        });
    }
}
