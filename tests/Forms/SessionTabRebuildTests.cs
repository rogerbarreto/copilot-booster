public sealed class SessionTabRebuildTests
{
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

    [Fact]
    public void BuildSessionTabs_AfterAddingTab_DoesNotThrow()
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

            // Use a Form so the TabControl is properly hosted and fires events
            using var form = new Form();
            var panel = new Panel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            panel.Controls.Add(grid);
            form.Show();

            // Save original settings and set up test tabs
            var originalTabs = Program._settings?.SessionTabs;
            try
            {
                if (Program._settings == null)
                {
                    Program._settings = LauncherSettings.CreateDefault();
                }

                Program._settings.SessionTabs = ["Active", "Archived"];

                var visuals = new ExistingSessionsVisuals(panel, tracker);

                // Simulate user adding a tab in settings and clicking Save
                Program._settings.SessionTabs = ["Active", "Archived", "Work"];

                // This should NOT throw NullReferenceException
                visuals.BuildSessionTabs();

                Assert.Equal(3, visuals.SessionTabs.TabPages.Count);
                Assert.Equal("Work", visuals.SessionTabs.TabPages[2].Tag);
            }
            finally
            {
                form.Close();
                if (Program._settings != null && originalTabs != null)
                {
                    Program._settings.SessionTabs = originalTabs;
                }
            }
        });
    }

    [Fact]
    public void BuildSessionTabs_PreservesSelectedTab_AfterRebuild()
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

            using var form = new Form();
            var panel = new Panel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            panel.Controls.Add(grid);
            form.Show();

            var originalTabs = Program._settings?.SessionTabs;
            try
            {
                if (Program._settings == null)
                {
                    Program._settings = LauncherSettings.CreateDefault();
                }

                Program._settings.SessionTabs = ["Active", "Archived"];

                var visuals = new ExistingSessionsVisuals(panel, tracker);

                // Select "Archived" tab
                visuals.SessionTabs.SelectedIndex = 1;
                Assert.Equal("Archived", visuals.SelectedTabName);

                // Add a new tab and rebuild
                Program._settings.SessionTabs = ["Active", "Archived", "Work"];
                visuals.BuildSessionTabs();

                // "Archived" should still be selected
                Assert.Equal("Archived", visuals.SelectedTabName);
            }
            finally
            {
                form.Close();
                if (Program._settings != null && originalTabs != null)
                {
                    Program._settings.SessionTabs = originalTabs;
                }
            }
        });
    }

    [Fact]
    public void GridIsParentedOnSelectedTab_AfterConstruction()
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

            using var form = new Form();
            var panel = new Panel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            panel.Controls.Add(grid);
            form.Show();

            var originalTabs = Program._settings?.SessionTabs;
            try
            {
                if (Program._settings == null)
                {
                    Program._settings = LauncherSettings.CreateDefault();
                }

                Program._settings.SessionTabs = ["Active", "Archived"];

                var visuals = new ExistingSessionsVisuals(panel, tracker);

                // After construction, the grid must be a child of the selected (first) tab
                var selectedTab = visuals.SessionTabs.SelectedTab;
                Assert.NotNull(selectedTab);
                Assert.Equal("Active", selectedTab.Tag);
                Assert.True(
                    selectedTab.Controls.Contains(visuals.SessionGrid),
                    "Grid should be parented on the first tab after construction");
            }
            finally
            {
                form.Close();
                if (Program._settings != null && originalTabs != null)
                {
                    Program._settings.SessionTabs = originalTabs;
                }
            }
        });
    }

    [Fact]
    public void GridIsParentedOnSelectedTab_AfterRebuild()
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

            using var form = new Form();
            var panel = new Panel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            panel.Controls.Add(grid);
            form.Show();

            var originalTabs = Program._settings?.SessionTabs;
            try
            {
                if (Program._settings == null)
                {
                    Program._settings = LauncherSettings.CreateDefault();
                }

                Program._settings.SessionTabs = ["Active", "Archived"];

                var visuals = new ExistingSessionsVisuals(panel, tracker);

                // Select Archived tab
                visuals.SessionTabs.SelectedIndex = 1;

                // Add a tab and rebuild
                Program._settings.SessionTabs = ["Active", "Archived", "Work"];
                visuals.BuildSessionTabs();

                // Grid should be on the restored "Archived" tab
                var selectedTab = visuals.SessionTabs.SelectedTab;
                Assert.NotNull(selectedTab);
                Assert.Equal("Archived", selectedTab.Tag);
                Assert.True(
                    selectedTab.Controls.Contains(visuals.SessionGrid),
                    "Grid should be parented on the previously selected tab after rebuild");
            }
            finally
            {
                form.Close();
                if (Program._settings != null && originalTabs != null)
                {
                    Program._settings.SessionTabs = originalTabs;
                }
            }
        });
    }
}
