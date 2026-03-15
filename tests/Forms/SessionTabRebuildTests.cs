public sealed class SessionTabRebuildTests
{
    /// <summary>
    /// Creates a fresh <see cref="LauncherSettings"/> with <see cref="LauncherSettings.SuppressSave"/> enabled.
    /// Each test gets its own isolated instance - no shared state.
    /// </summary>
    private static LauncherSettings CreateTestSettings()
    {
        var settings = LauncherSettings.CreateDefault();
        settings.SuppressSave = true;
        return settings;
    }

    [StaFact]
    public void BuildSessionTabs_AfterAddingTab_DoesNotThrow()
    {
        var settings = CreateTestSettings();
        settings.SessionTabs = ["Active", "Archived"];

        using var form = new Form();
        var panel = new Panel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);
        form.Show();

        var tracker = new ActiveStatusTracker();
        var visuals = new ExistingSessionsVisuals(panel, tracker, settings);

        settings.SessionTabs = ["Active", "Archived", "Work"];
        visuals.BuildSessionTabs();

        Assert.Equal(4, visuals.SessionTabs.TabPages.Count);
        Assert.Equal("Work", visuals.SessionTabs.TabPages[2].Tag);

        visuals.SessionGrid.Dispose();

        visuals.SessionTabs.Dispose();

        form.Close();
    }

    [StaFact]
    public void BuildSessionTabs_PreservesSelectedTab_AfterRebuild()
    {
        var settings = CreateTestSettings();
        settings.SessionTabs = ["Active", "Archived"];

        using var form = new Form();
        var panel = new Panel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);
        form.Show();

        var tracker = new ActiveStatusTracker();
        var visuals = new ExistingSessionsVisuals(panel, tracker, settings);

        visuals.SessionTabs.SelectedIndex = 1;
        Assert.Equal("Archived", visuals.SelectedTabName);

        settings.SessionTabs = ["Active", "Archived", "Work"];
        visuals.BuildSessionTabs();

        Assert.Equal("Archived", visuals.SelectedTabName);
        visuals.SessionGrid.Dispose();

        visuals.SessionTabs.Dispose();

        form.Close();
    }

    [StaFact]
    public void GridIsParentedOnSelectedTab_AfterConstruction()
    {
        var settings = CreateTestSettings();
        settings.SessionTabs = ["Active", "Archived"];

        using var form = new Form();
        var panel = new Panel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);
        form.Show();

        var tracker = new ActiveStatusTracker();
        var visuals = new ExistingSessionsVisuals(panel, tracker, settings);

        var selectedTab = visuals.SessionTabs.SelectedTab;
        Assert.NotNull(selectedTab);
        Assert.Equal("Active", selectedTab.Tag);
        Assert.True(
            selectedTab.Controls.Contains(visuals.SessionGrid),
            "Grid should be parented on the first tab after construction");
        visuals.SessionGrid.Dispose();

        visuals.SessionTabs.Dispose();

        form.Close();
    }

    [StaFact]
    public void GridIsParentedOnSelectedTab_AfterRebuild()
    {
        var settings = CreateTestSettings();
        settings.SessionTabs = ["Active", "Archived"];

        using var form = new Form();
        var panel = new Panel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);
        form.Show();

        var tracker = new ActiveStatusTracker();
        var visuals = new ExistingSessionsVisuals(panel, tracker, settings);

        visuals.SessionTabs.SelectedIndex = 1;

        settings.SessionTabs = ["Active", "Archived", "Work"];
        visuals.BuildSessionTabs();

        var selectedTab = visuals.SessionTabs.SelectedTab;
        Assert.NotNull(selectedTab);
        Assert.Equal("Archived", selectedTab.Tag);
        Assert.True(
            selectedTab.Controls.Contains(visuals.SessionGrid),
            "Grid should be parented on the previously selected tab after rebuild");
        visuals.SessionGrid.Dispose();

        visuals.SessionTabs.Dispose();

        form.Close();
    }

    [StaFact]
    public void BuildSessionTabs_AddsPlusTab_WhenBelowMaxTabs()
    {
        var settings = CreateTestSettings();
        settings.SessionTabs = ["Active", "Archived"];
        settings.MaxSessionTabs = 10;

        using var form = new Form();
        var panel = new Panel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);
        form.Show();

        var tracker = new ActiveStatusTracker();
        var visuals = new ExistingSessionsVisuals(panel, tracker, settings);

        Assert.Equal(3, visuals.SessionTabs.TabPages.Count);
        var plusTab = visuals.SessionTabs.TabPages[2];
        Assert.Equal("+", plusTab.Text);
        Assert.Null(plusTab.Tag);
        visuals.SessionGrid.Dispose();

        visuals.SessionTabs.Dispose();

        form.Close();
    }

    [StaFact]
    public void BuildSessionTabs_NoPlusTab_WhenAtMaxTabs()
    {
        var settings = CreateTestSettings();
        settings.SessionTabs = ["Tab1", "Tab2"];
        settings.MaxSessionTabs = 2;

        using var form = new Form();
        var panel = new Panel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);
        form.Show();

        var tracker = new ActiveStatusTracker();
        var visuals = new ExistingSessionsVisuals(panel, tracker, settings);

        Assert.Equal(2, visuals.SessionTabs.TabPages.Count);
        Assert.All(visuals.SessionTabs.TabPages.Cast<TabPage>(), p => Assert.NotNull(p.Tag));
        visuals.SessionGrid.Dispose();

        visuals.SessionTabs.Dispose();

        form.Close();
    }

    [StaFact]
    public void UpdateTabCounts_SkipsPlusTab()
    {
        var settings = CreateTestSettings();
        settings.SessionTabs = ["Active"];

        using var form = new Form();
        var panel = new Panel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);
        form.Show();

        var tracker = new ActiveStatusTracker();
        var visuals = new ExistingSessionsVisuals(panel, tracker, settings);

        var counts = new Dictionary<string, int> { ["Active"] = 5 };
        visuals.UpdateTabCounts(counts);

        Assert.Equal("Active (5)", visuals.SessionTabs.TabPages[0].Text);

        var plusTab = visuals.SessionTabs.TabPages[1];
        Assert.Equal("+", plusTab.Text);
        visuals.SessionGrid.Dispose();

        visuals.SessionTabs.Dispose();

        form.Close();
    }

    [StaFact]
    public void SelectedTabName_ReturnsFallback_WhenPlusTabSelected()
    {
        var settings = CreateTestSettings();
        settings.SessionTabs = ["Active", "Archived"];

        using var form = new Form();
        var panel = new Panel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);
        form.Show();

        var tracker = new ActiveStatusTracker();
        var visuals = new ExistingSessionsVisuals(panel, tracker, settings);

        Assert.Equal("Active", visuals.SelectedTabName);
        visuals.SessionGrid.Dispose();

        visuals.SessionTabs.Dispose();

        form.Close();
    }

    [Fact]
    public void ApplySessionStates_AutoAddsMissingTabs_WhenSessionsReferenceUnknownTabs()
    {
        var settings = CreateTestSettings();
        settings.SessionTabs = ["Active", "Archived"];

        var sessions = new List<NamedSession>
        {
            new() { Id = "s1", Summary = "Session 1" },
            new() { Id = "s2", Summary = "Session 2" },
            new() { Id = "s3", Summary = "Session 3" },
            new() { Id = "s4", Summary = "Session 4" },
        };

        var states = new Dictionary<string, SessionArchiveService.SessionState>
        {
            ["s1"] = new() { Tab = "Active" },
            ["s2"] = new() { Tab = "Done" },
            ["s3"] = new() { Tab = "Todo" },
            ["s4"] = new() { Tab = "Archived" },
        };

        MainForm.ApplySessionStates(sessions, states, "Active", settings, persistChanges: false);

        Assert.Equal("Active", sessions[0].Tab);
        Assert.Equal("Done", sessions[1].Tab);
        Assert.Equal("Todo", sessions[2].Tab);
        Assert.Equal("Archived", sessions[3].Tab);

        Assert.Contains("Done", settings.SessionTabs);
        Assert.Contains("Todo", settings.SessionTabs);
        Assert.Equal(4, settings.SessionTabs.Count);
    }

    [Fact]
    public void ApplySessionStates_DoesNotAddDuplicateTabs()
    {
        var settings = CreateTestSettings();
        settings.SessionTabs = ["Active", "Done", "Archived"];

        var sessions = new List<NamedSession>
        {
            new() { Id = "s1", Summary = "Session 1" },
            new() { Id = "s2", Summary = "Session 2" },
        };

        var states = new Dictionary<string, SessionArchiveService.SessionState>
        {
            ["s1"] = new() { Tab = "Active" },
            ["s2"] = new() { Tab = "Done" },
        };

        MainForm.ApplySessionStates(sessions, states, "Active", settings, persistChanges: false);

        Assert.Equal(3, settings.SessionTabs.Count);
    }

    [Fact]
    public void ApplySessionStates_AssignsDefaultTab_WhenNoStateExists()
    {
        var settings = CreateTestSettings();
        settings.SessionTabs = ["Active", "Archived"];

        var sessions = new List<NamedSession>
        {
            new() { Id = "s1", Summary = "Session 1" },
        };

        var states = new Dictionary<string, SessionArchiveService.SessionState>();

        MainForm.ApplySessionStates(sessions, states, "Active", settings, persistChanges: false);

        Assert.Equal("Active", sessions[0].Tab);
        Assert.Equal(2, settings.SessionTabs.Count);
    }

    [StaFact]
    public void BuildSessionTabs_ReflectsSettingsOrder()
    {
        var settings = CreateTestSettings();
        settings.SessionTabs = ["Active", "Done", "Todo", "Archived"];
        settings.MaxSessionTabs = 10;

        using var form = new Form();
        var panel = new Panel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);
        form.Show();

        var tracker = new ActiveStatusTracker();
        var visuals = new ExistingSessionsVisuals(panel, tracker, settings);

        Assert.Equal(5, visuals.SessionTabs.TabPages.Count);
        Assert.Equal("Active", visuals.SessionTabs.TabPages[0].Tag);
        Assert.Equal("Done", visuals.SessionTabs.TabPages[1].Tag);
        Assert.Equal("Todo", visuals.SessionTabs.TabPages[2].Tag);
        Assert.Equal("Archived", visuals.SessionTabs.TabPages[3].Tag);
        Assert.Null(visuals.SessionTabs.TabPages[4].Tag);
        visuals.SessionGrid.Dispose();

        visuals.SessionTabs.Dispose();

        form.Close();
    }

    [StaFact]
    public void BuildSessionTabs_PreservesTabOrder_AfterRebuild()
    {
        var settings = CreateTestSettings();
        settings.SessionTabs = ["Todo", "Active", "Done"];

        using var form = new Form();
        var panel = new Panel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);
        form.Show();

        var tracker = new ActiveStatusTracker();
        var visuals = new ExistingSessionsVisuals(panel, tracker, settings);

        Assert.Equal("Todo", visuals.SessionTabs.TabPages[0].Tag);
        Assert.Equal("Active", visuals.SessionTabs.TabPages[1].Tag);
        Assert.Equal("Done", visuals.SessionTabs.TabPages[2].Tag);

        settings.SessionTabs = ["Todo", "Active", "Done", "Archived"];
        visuals.BuildSessionTabs();

        Assert.Equal("Todo", visuals.SessionTabs.TabPages[0].Tag);
        Assert.Equal("Active", visuals.SessionTabs.TabPages[1].Tag);
        Assert.Equal("Done", visuals.SessionTabs.TabPages[2].Tag);
        Assert.Equal("Archived", visuals.SessionTabs.TabPages[3].Tag);
        visuals.SessionGrid.Dispose();

        visuals.SessionTabs.Dispose();

        form.Close();
    }

    [Fact]
    public void ApplySessionStates_AutoRecoveredTabs_AppendedWithoutCorruptingOrder()
    {
        var settings = CreateTestSettings();
        settings.SessionTabs = ["Active", "Archived"];

        var sessions = new List<NamedSession>
        {
            new() { Id = "s1", Summary = "S1" },
            new() { Id = "s2", Summary = "S2" },
            new() { Id = "s3", Summary = "S3" },
        };

        var states = new Dictionary<string, SessionArchiveService.SessionState>
        {
            ["s1"] = new() { Tab = "Active" },
            ["s2"] = new() { Tab = "Done" },
            ["s3"] = new() { Tab = "Todo" },
        };

        MainForm.ApplySessionStates(sessions, states, "Active", settings, persistChanges: false);

        Assert.Equal("Active", settings.SessionTabs[0]);
        Assert.Equal("Archived", settings.SessionTabs[1]);
        Assert.Contains("Done", settings.SessionTabs);
        Assert.Contains("Todo", settings.SessionTabs);
    }
}

