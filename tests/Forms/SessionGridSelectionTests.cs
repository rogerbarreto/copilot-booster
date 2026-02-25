public sealed class SessionGridSelectionTests
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
            Tab = "Active",
            LastModified = DateTime.Now.AddDays(-x.daysAgo)
        }).ToList();
    }

    [Fact]
    public void Populate_AfterResort_CurrentRowMatchesSelectedSession()
    {
        RunOnSta(() =>
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
            grid.Columns.Add("Active", "Active");

            var tracker = new ActiveStatusTracker();
            var visuals = new SessionGridVisuals(grid, tracker);

            // Initial state: session-A (newest), session-B, session-C (oldest) — none running
            var sessions = MakeSessions(("session-A", 0), ("session-B", 1), ("session-C", 2));
            var emptySnapshot = MakeSnapshot();

            // Sort: by date desc (no running)
            MainForm.SortSessions(sessions, emptySnapshot, "running");
            visuals.Populate(sessions, emptySnapshot, null);

            // User selects session-B (row index 1)
            grid.ClearSelection();
            grid.Rows[1].Selected = true;
            grid.CurrentCell = grid.Rows[1].Cells[0];

            Assert.Equal("session-B", visuals.GetSelectedSessionId());

            // Now session-C becomes running — re-sort puts it first
            var newSnapshot = MakeSnapshot("session-C");
            var resorted = MakeSessions(("session-A", 0), ("session-B", 1), ("session-C", 2));
            MainForm.SortSessions(resorted, newSnapshot, "running");

            // Verify the sort moved session-C to position 0
            Assert.Equal("session-C", resorted[0].Id);

            // Re-populate with new order (simulates 3s refresh)
            visuals.Populate(resorted, newSnapshot, null);

            // The grid should now have session-C at row 0, session-A at row 1, session-B at row 2
            Assert.Equal("session-C", grid.Rows[0].Tag);
            Assert.Equal("session-A", grid.Rows[1].Tag);
            Assert.Equal("session-B", grid.Rows[2].Tag);

            // BUG CHECK: GetSelectedSessionId() should still return session-B
            // because that's what the user had selected before the refresh.
            // But CurrentRow snaps to row 0 after Rows.Clear() + Add, so it
            // returns session-C instead.
            var selectedAfterRefresh = visuals.GetSelectedSessionId();
            Assert.Equal("session-B", selectedAfterRefresh);
        });
    }

    [Fact]
    public void Populate_SelectedRowsContainCorrectSession_AfterResort()
    {
        RunOnSta(() =>
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
            grid.Columns.Add("Active", "Active");

            var tracker = new ActiveStatusTracker();
            var visuals = new SessionGridVisuals(grid, tracker);

            // Initial: 3 sessions, none running
            var sessions = MakeSessions(("s1", 0), ("s2", 1), ("s3", 2));
            MainForm.SortSessions(sessions, MakeSnapshot(), "running");
            visuals.Populate(sessions, MakeSnapshot(), null);

            // User selects s3 (last row)
            grid.ClearSelection();
            grid.Rows[2].Selected = true;
            grid.CurrentCell = grid.Rows[2].Cells[0];
            Assert.Equal("s3", visuals.GetSelectedSessionId());

            // s3 becomes running → jumps to row 0
            var snapshot = MakeSnapshot("s3");
            var resorted = MakeSessions(("s1", 0), ("s2", 1), ("s3", 2));
            MainForm.SortSessions(resorted, snapshot, "running");
            visuals.Populate(resorted, snapshot, null);

            // s3 is now at row 0, but the selected ID should still track it
            var selectedIds = visuals.GetSelectedSessionIds();
            Assert.Contains("s3", selectedIds);

            // And GetSelectedSessionId (CurrentRow) should also be s3
            Assert.Equal("s3", visuals.GetSelectedSessionId());
        });
    }

    [Fact]
    public void Populate_MultipleSelections_PreservedAfterRefresh()
    {
        RunOnSta(() =>
        {
            using var form = new Form { Width = 800, Height = 400 };
            var grid = new DataGridView
            {
                AllowUserToAddRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                Dock = DockStyle.Fill
            };
            grid.Columns.Add("Status", "");
            grid.Columns.Add("Name", "Name");
            grid.Columns.Add("CWD", "CWD");
            grid.Columns.Add("LastModified", "LastModified");
            grid.Columns.Add("Active", "Active");

            form.Controls.Add(grid);
            form.Show();

            var tracker = new ActiveStatusTracker();
            var visuals = new SessionGridVisuals(grid, tracker);

            // 5 sessions, none running
            var sessions = MakeSessions(
                ("s1", 0), ("s2", 1), ("s3", 2), ("s4", 3), ("s5", 4));
            var snapshot = MakeSnapshot();
            MainForm.SortSessions(sessions, snapshot, "running");
            visuals.Populate(sessions, snapshot, null);

            // User selects s1, s3, s5 (Ctrl-click multi-select)
            // Must set CurrentCell first (clears selection), then add others
            grid.ClearSelection();
            grid.CurrentCell = grid.Rows[2].Cells[0]; // CurrentRow = s3
            grid.Rows[0].Selected = true; // s1
            grid.Rows[2].Selected = true; // s3
            grid.Rows[4].Selected = true; // s5

            var beforeIds = visuals.GetSelectedSessionIds();
            Assert.Equal(3, beforeIds.Count);
            Assert.Contains("s1", beforeIds);
            Assert.Contains("s3", beforeIds);
            Assert.Contains("s5", beforeIds);
            Assert.Equal("s3", visuals.GetSelectedSessionId());

            // Refresh with same data (simulates 3s timer, no sort change)
            var refreshed = MakeSessions(
                ("s1", 0), ("s2", 1), ("s3", 2), ("s4", 3), ("s5", 4));
            MainForm.SortSessions(refreshed, snapshot, "running");
            visuals.Populate(refreshed, snapshot, null);

            // All 3 selections must be preserved
            var afterIds = visuals.GetSelectedSessionIds();
            Assert.Equal(3, afterIds.Count);
            Assert.Contains("s1", afterIds);
            Assert.Contains("s3", afterIds);
            Assert.Contains("s5", afterIds);

            // CurrentRow should still point to s3
            Assert.Equal("s3", visuals.GetSelectedSessionId());

            form.Close();
        });
    }

    [Fact]
    public void Populate_MultipleSelections_PreservedAfterResort()
    {
        RunOnSta(() =>
        {
            using var form = new Form { Width = 800, Height = 400 };
            var grid = new DataGridView
            {
                AllowUserToAddRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                Dock = DockStyle.Fill
            };
            grid.Columns.Add("Status", "");
            grid.Columns.Add("Name", "Name");
            grid.Columns.Add("CWD", "CWD");
            grid.Columns.Add("LastModified", "LastModified");
            grid.Columns.Add("Active", "Active");

            form.Controls.Add(grid);
            form.Show();

            var tracker = new ActiveStatusTracker();
            var visuals = new SessionGridVisuals(grid, tracker);

            // 5 sessions, none running
            var sessions = MakeSessions(
                ("s1", 0), ("s2", 1), ("s3", 2), ("s4", 3), ("s5", 4));
            var snapshot = MakeSnapshot();
            MainForm.SortSessions(sessions, snapshot, "running");
            visuals.Populate(sessions, snapshot, null);

            // User selects s2 and s4
            // Must set CurrentCell first (clears selection), then add others
            grid.ClearSelection();
            grid.CurrentCell = grid.Rows[3].Cells[0]; // CurrentRow = s4
            grid.Rows[1].Selected = true; // s2
            grid.Rows[3].Selected = true; // s4

            Assert.Equal(2, visuals.GetSelectedSessionIds().Count);
            Assert.Equal("s4", visuals.GetSelectedSessionId());

            // s5 becomes running → jumps to top; s3 becomes running too
            var newSnapshot = MakeSnapshot("s5", "s3");
            var resorted = MakeSessions(
                ("s1", 0), ("s2", 1), ("s3", 2), ("s4", 3), ("s5", 4));
            MainForm.SortSessions(resorted, newSnapshot, "running");

            // Running first: s3, s5 (date desc among running), then s1, s2, s4
            Assert.Equal("s3", resorted[0].Id);
            Assert.Equal("s5", resorted[1].Id);

            visuals.Populate(resorted, newSnapshot, null);

            // s2 and s4 should still be selected despite row indices changing
            var afterIds = visuals.GetSelectedSessionIds();
            Assert.Equal(2, afterIds.Count);
            Assert.Contains("s2", afterIds);
            Assert.Contains("s4", afterIds);
            Assert.Equal("s4", visuals.GetSelectedSessionId());

            form.Close();
        });
    }

    [Fact]
    public void Populate_ScrollPosition_PreservedAfterRefresh()
    {
        RunOnSta(() =>
        {
            using var form = new Form { Width = 800, Height = 200 };
            var grid = new DataGridView
            {
                AllowUserToAddRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Vertical
            };
            grid.Columns.Add("Status", "");
            grid.Columns.Add("Name", "Name");
            grid.Columns.Add("CWD", "CWD");
            grid.Columns.Add("LastModified", "LastModified");
            grid.Columns.Add("Active", "Active");

            form.Controls.Add(grid);
            form.Show();

            var tracker = new ActiveStatusTracker();
            var visuals = new SessionGridVisuals(grid, tracker);

            // Create enough sessions to require scrolling (small form, many rows)
            var items = Enumerable.Range(1, 30)
                .Select(i => (id: $"s{i}", daysAgo: i))
                .ToArray();
            var sessions = MakeSessions(items);
            var snapshot = MakeSnapshot();
            MainForm.SortSessions(sessions, snapshot, "running");
            visuals.Populate(sessions, snapshot, null);

            // Ensure we have enough rows to scroll
            Assert.True(grid.RowCount > grid.DisplayedRowCount(false),
                "Grid should have more rows than visible to test scrolling");

            // Scroll down to row 15
            grid.FirstDisplayedScrollingRowIndex = 15;
            var scrollBefore = grid.FirstDisplayedScrollingRowIndex;
            Assert.True(scrollBefore >= 15, "Scroll should be at or past row 15");

            // Refresh with same data (simulates 3s timer)
            var refreshed = MakeSessions(items);
            MainForm.SortSessions(refreshed, snapshot, "running");
            visuals.Populate(refreshed, snapshot, null);

            // Scroll position must be preserved
            var scrollAfter = grid.FirstDisplayedScrollingRowIndex;
            Assert.Equal(scrollBefore, scrollAfter);

            form.Close();
        });
    }
}
