public sealed class SessionSortOrderTests
{
    private static ActiveStatusSnapshot MakeSnapshot(params string[] runningSessionIds)
    {
        var active = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in runningSessionIds)
        {
            active[id] = "running";
            icons[id] = "working";
        }

        return new ActiveStatusSnapshot(active, names, icons);
    }

    private static NamedSession MakeSession(string id, bool isPinned = false, int daysAgo = 0, string alias = "", string folder = "")
    {
        return new NamedSession
        {
            Id = id,
            Summary = id,
            Alias = alias,
            Folder = string.IsNullOrEmpty(folder) ? id : folder,
            IsPinned = isPinned,
            Tab = "Active",
            LastModified = DateTime.Now.AddDays(-daysAgo)
        };
    }

    [Fact]
    public void RunningNonPinned_SortBeforeIdle()
    {
        var sessions = new List<NamedSession>
        {
            MakeSession("idle-1", daysAgo: 0),
            MakeSession("running-1", daysAgo: 5),
            MakeSession("idle-2", daysAgo: 1),
        };
        var snapshot = MakeSnapshot("running-1");

        MainForm.SortSessions(sessions, snapshot, "running");

        Assert.Equal("running-1", sessions[0].Id);
        Assert.Equal("idle-1", sessions[1].Id);
        Assert.Equal("idle-2", sessions[2].Id);
    }

    [Fact]
    public void PinnedAlwaysBeforeNonPinned()
    {
        var sessions = new List<NamedSession>
        {
            MakeSession("running-unpinned", daysAgo: 0),
            MakeSession("pinned-idle", isPinned: true, daysAgo: 10),
        };
        var snapshot = MakeSnapshot("running-unpinned");

        MainForm.SortSessions(sessions, snapshot, "running");

        Assert.Equal("pinned-idle", sessions[0].Id);
        Assert.Equal("running-unpinned", sessions[1].Id);
    }

    [Fact]
    public void RunningPinned_SortBeforeIdlePinned()
    {
        var sessions = new List<NamedSession>
        {
            MakeSession("pinned-idle", isPinned: true, daysAgo: 0),
            MakeSession("pinned-running", isPinned: true, daysAgo: 5),
        };
        var snapshot = MakeSnapshot("pinned-running");

        MainForm.SortSessions(sessions, snapshot, "running");

        Assert.Equal("pinned-running", sessions[0].Id);
        Assert.Equal("pinned-idle", sessions[1].Id);
    }

    [Fact]
    public void IdleSessions_SortByDateDescending()
    {
        var sessions = new List<NamedSession>
        {
            MakeSession("old", daysAgo: 10),
            MakeSession("newest", daysAgo: 0),
            MakeSession("middle", daysAgo: 5),
        };
        var snapshot = MakeSnapshot();

        MainForm.SortSessions(sessions, snapshot, "running");

        Assert.Equal("newest", sessions[0].Id);
        Assert.Equal("middle", sessions[1].Id);
        Assert.Equal("old", sessions[2].Id);
    }

    [Fact]
    public void MultipleRunning_SortByDateAmongThemselves()
    {
        var sessions = new List<NamedSession>
        {
            MakeSession("running-old", daysAgo: 10),
            MakeSession("idle", daysAgo: 0),
            MakeSession("running-new", daysAgo: 1),
        };
        var snapshot = MakeSnapshot("running-old", "running-new");

        MainForm.SortSessions(sessions, snapshot, "running");

        Assert.Equal("running-new", sessions[0].Id);
        Assert.Equal("running-old", sessions[1].Id);
        Assert.Equal("idle", sessions[2].Id);
    }

    [Fact]
    public void FullMix_PinnedRunning_PinnedIdle_RunningUnpinned_IdleUnpinned()
    {
        var sessions = new List<NamedSession>
        {
            MakeSession("idle-unpinned", daysAgo: 0),
            MakeSession("running-unpinned", daysAgo: 2),
            MakeSession("pinned-idle", isPinned: true, daysAgo: 1),
            MakeSession("pinned-running", isPinned: true, daysAgo: 3),
        };
        var snapshot = MakeSnapshot("running-unpinned", "pinned-running");

        MainForm.SortSessions(sessions, snapshot, "running");

        // Pinned first: running pinned, then idle pinned
        Assert.Equal("pinned-running", sessions[0].Id);
        Assert.Equal("pinned-idle", sessions[1].Id);
        // Then non-pinned: running, then idle
        Assert.Equal("running-unpinned", sessions[2].Id);
        Assert.Equal("idle-unpinned", sessions[3].Id);
    }

    [Fact]
    public void PinnedOrder_Alias_SortsPinnedAlphabetically()
    {
        var sessions = new List<NamedSession>
        {
            MakeSession("c", isPinned: true, alias: "Charlie"),
            MakeSession("a", isPinned: true, alias: "Alpha"),
            MakeSession("b", isPinned: true, alias: "Bravo"),
        };
        var snapshot = MakeSnapshot("c");

        MainForm.SortSessions(sessions, snapshot, "alias");

        Assert.Equal("a", sessions[0].Id);
        Assert.Equal("b", sessions[1].Id);
        Assert.Equal("c", sessions[2].Id);
    }

    [Fact]
    public void PinnedOrder_Created_SortsPinnedByDate()
    {
        var sessions = new List<NamedSession>
        {
            MakeSession("old-pinned", isPinned: true, daysAgo: 10),
            MakeSession("running-pinned", isPinned: true, daysAgo: 5),
            MakeSession("new-pinned", isPinned: true, daysAgo: 0),
        };
        var snapshot = MakeSnapshot("running-pinned");

        MainForm.SortSessions(sessions, snapshot, "created");

        // Ignores running status, sorts by date only
        Assert.Equal("new-pinned", sessions[0].Id);
        Assert.Equal("running-pinned", sessions[1].Id);
        Assert.Equal("old-pinned", sessions[2].Id);
    }

    [Fact]
    public void NullSnapshot_FallsBackToDateOrder()
    {
        var sessions = new List<NamedSession>
        {
            MakeSession("old", daysAgo: 5),
            MakeSession("new", daysAgo: 0),
        };

        MainForm.SortSessions(sessions, null, "running");

        Assert.Equal("new", sessions[0].Id);
        Assert.Equal("old", sessions[1].Id);
    }

    [Fact]
    public void ColumnSort_SessionAscending_OverridesRunningOrder()
    {
        var sessions = new List<NamedSession>
        {
            MakeSession("zebra", daysAgo: 0),
            MakeSession("alpha", daysAgo: 5),
            MakeSession("middle", daysAgo: 1),
        };
        var snapshot = MakeSnapshot("zebra");

        MainForm.SortSessions(sessions, snapshot, "running", "Session", SortOrder.Ascending);

        Assert.Equal("alpha", sessions[0].Id);
        Assert.Equal("middle", sessions[1].Id);
        Assert.Equal("zebra", sessions[2].Id);
    }

    [Fact]
    public void ColumnSort_SessionDescending()
    {
        var sessions = new List<NamedSession>
        {
            MakeSession("alpha", daysAgo: 0),
            MakeSession("zebra", daysAgo: 5),
        };

        MainForm.SortSessions(sessions, MakeSnapshot(), "running", "Session", SortOrder.Descending);

        Assert.Equal("zebra", sessions[0].Id);
        Assert.Equal("alpha", sessions[1].Id);
    }

    [Fact]
    public void ColumnSort_DateAscending_OldestFirst()
    {
        var sessions = new List<NamedSession>
        {
            MakeSession("newest", daysAgo: 0),
            MakeSession("oldest", daysAgo: 10),
            MakeSession("middle", daysAgo: 5),
        };

        MainForm.SortSessions(sessions, MakeSnapshot(), "running", "Date", SortOrder.Ascending);

        Assert.Equal("oldest", sessions[0].Id);
        Assert.Equal("middle", sessions[1].Id);
        Assert.Equal("newest", sessions[2].Id);
    }

    [Fact]
    public void ColumnSort_DateDescending_NewestFirst()
    {
        var sessions = new List<NamedSession>
        {
            MakeSession("oldest", daysAgo: 10),
            MakeSession("newest", daysAgo: 0),
        };

        MainForm.SortSessions(sessions, MakeSnapshot(), "running", "Date", SortOrder.Descending);

        Assert.Equal("newest", sessions[0].Id);
        Assert.Equal("oldest", sessions[1].Id);
    }

    [Fact]
    public void ColumnSort_CwdAscending()
    {
        var sessions = new List<NamedSession>
        {
            MakeSession("s2", folder: "zoo"),
            MakeSession("s1", folder: "alpha"),
            MakeSession("s3", folder: "middle"),
        };

        MainForm.SortSessions(sessions, MakeSnapshot(), "running", "CWD", SortOrder.Ascending);

        Assert.Equal("s1", sessions[0].Id);
        Assert.Equal("s3", sessions[1].Id);
        Assert.Equal("s2", sessions[2].Id);
    }

    [Fact]
    public void ColumnSort_RunningDescending_RunningFirst()
    {
        var sessions = new List<NamedSession>
        {
            MakeSession("idle-1", daysAgo: 0),
            MakeSession("running-1", daysAgo: 5),
            MakeSession("idle-2", daysAgo: 1),
        };
        var snapshot = MakeSnapshot("running-1");

        MainForm.SortSessions(sessions, snapshot, "running", "RunningApps", SortOrder.Descending);

        Assert.Equal("running-1", sessions[0].Id);
    }

    [Fact]
    public void ColumnSort_PinnedAlwaysFirst_RegardlessOfColumnSort()
    {
        var sessions = new List<NamedSession>
        {
            MakeSession("zebra-unpinned", daysAgo: 0),
            MakeSession("alpha-pinned", isPinned: true, daysAgo: 10),
        };

        MainForm.SortSessions(sessions, MakeSnapshot(), "running", "Session", SortOrder.Ascending);

        // Pinned always first, even though "alpha" < "zebra" alphabetically
        Assert.Equal("alpha-pinned", sessions[0].Id);
        Assert.Equal("zebra-unpinned", sessions[1].Id);
    }

    [Fact]
    public void ColumnSort_Tiebreak_UsesSettingAlias()
    {
        var sessions = new List<NamedSession>
        {
            MakeSession("s2", daysAgo: 5, alias: "Beta", folder: "same"),
            MakeSession("s1", daysAgo: 0, alias: "Alpha", folder: "same"),
        };

        MainForm.SortSessions(sessions, MakeSnapshot(), "alias", "CWD", SortOrder.Ascending);

        // Same CWD → tiebreak by alias alphabetically
        Assert.Equal("s1", sessions[0].Id);
        Assert.Equal("s2", sessions[1].Id);
    }

    [Fact]
    public void ColumnSort_Tiebreak_UsesSettingDate()
    {
        var sessions = new List<NamedSession>
        {
            MakeSession("old", daysAgo: 10, folder: "same"),
            MakeSession("new", daysAgo: 0, folder: "same"),
        };

        MainForm.SortSessions(sessions, MakeSnapshot(), "running", "CWD", SortOrder.Ascending);

        // Same CWD → tiebreak by date descending (newest first)
        Assert.Equal("new", sessions[0].Id);
        Assert.Equal("old", sessions[1].Id);
    }
}
