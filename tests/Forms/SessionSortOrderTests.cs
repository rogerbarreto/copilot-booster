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

    private static NamedSession MakeSession(string id, bool isPinned = false, int daysAgo = 0, string alias = "")
    {
        return new NamedSession
        {
            Id = id,
            Summary = id,
            Alias = alias,
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
}
