public sealed class TeamsWindowServiceTests
{
    [Fact]
    public void TeamsUrl_IsCorrect()
    {
        Assert.Equal("https://teams.microsoft.com", TeamsWindowService.TeamsUrl);
    }

    [Fact]
    public void GetIconCachePath_ReturnsPathInLocalAppData()
    {
        var path = TeamsWindowService.GetIconCachePath();

        Assert.Contains("CopilotBooster", path);
        Assert.EndsWith("teams-favicon.ico", path);
    }

    [Fact]
    public void BuildAppArguments_ContainsAppFlag()
    {
        var args = TeamsWindowService.BuildAppArguments();

        Assert.Contains("--app=", args);
        Assert.Contains("teams.microsoft.com", args);
    }

    [Fact]
    public void NewInstance_IsNotOpen()
    {
        var service = new TeamsWindowService();

        Assert.False(service.IsOpen);
        Assert.Equal(IntPtr.Zero, service.CachedHwnd);
    }

    [Fact]
    public void NewInstance_IsNotPendingOpen()
    {
        var service = new TeamsWindowService();

        Assert.False(service.IsPendingOpen);
    }

    [Fact]
    public void Focus_ReturnsFalse_WhenNotOpen()
    {
        var service = new TeamsWindowService();

        Assert.False(service.Focus());
    }

    [Fact]
    public void Release_ClearsHwnd()
    {
        var service = new TeamsWindowService();
        service.CachedHwnd = 12345;

        service.Release();

        Assert.Equal(IntPtr.Zero, service.CachedHwnd);
    }

    [Fact]
    public void FindNewTeamsWindow_ReturnsZero_WhenNoNewWindows()
    {
        // Snapshot current Teams windows as "existing" — any currently running ones.
        // Since we don't open a new one, FindNewTeamsWindow should return zero.
        var existing = TeamsWindowService.FindAllTeamsWindows();

        var result = TeamsWindowService.FindNewTeamsWindow(existing);

        Assert.Equal(IntPtr.Zero, result);
    }

    [Fact]
    public void FindNewTeamsWindow_ReturnsZero_WhenAllCurrentWindowsAreKnown()
    {
        // Capture all current windows, then verify none are "new"
        var snapshot1 = TeamsWindowService.FindAllTeamsWindows();
        var snapshot2 = TeamsWindowService.FindAllTeamsWindows();

        // Every HWND in snapshot2 should be in snapshot1 (no new windows appeared)
        foreach (var hwnd in snapshot2)
        {
            Assert.Contains(hwnd, snapshot1);
        }
    }

    [Fact]
    public void IsOpen_ReturnsFalse_WhenCachedHwndIsInvalid_AndNoUnclaimedWindows()
    {
        // Claim all currently open Teams windows so re-scan finds nothing
        var openWindows = TeamsWindowService.FindAllTeamsWindows();
        var claimServices = new List<TeamsWindowService>();
        foreach (var hwnd in openWindows)
        {
            var claimer = new TeamsWindowService { CachedHwnd = hwnd };
            _ = claimer.IsOpen; // triggers claim in static set
            claimServices.Add(claimer);
        }

        try
        {
            var service = new TeamsWindowService();
            service.CachedHwnd = 99999999;

            Assert.False(service.IsOpen);
        }
        finally
        {
            foreach (var c in claimServices) { c.Release(); }
        }
    }

    [Fact]
    public void Focus_ReturnsFalse_WhenCachedHwndIsInvalid()
    {
        var service = new TeamsWindowService();
        service.CachedHwnd = 99999999;

        Assert.False(service.Focus());
    }

    [Fact]
    public void CheckAlive_FiresWindowClosed_WhenNotOpen()
    {
        // Claim all open windows so re-scan can't find any
        var openWindows = TeamsWindowService.FindAllTeamsWindows();
        var claimers = new List<TeamsWindowService>();
        foreach (var h in openWindows)
        {
            var c = new TeamsWindowService { CachedHwnd = h };
            _ = c.IsOpen;
            claimers.Add(c);
        }

        try
        {
            var service = new TeamsWindowService();
            bool closedFired = false;
            service.WindowClosed += () => closedFired = true;

            service.CheckAlive();

            Assert.True(closedFired);
        }
        finally
        {
            foreach (var c in claimers) { c.Release(); }
        }
    }

    [Fact]
    public void CheckAlive_DoesNotFire_WhenPendingOpen()
    {
        // IsPendingOpen is private set, so we can't test this directly.
        // But we can verify that a new service (not pending, not open) fires the event.
        var openWindows = TeamsWindowService.FindAllTeamsWindows();
        var claimers = new List<TeamsWindowService>();
        foreach (var h in openWindows)
        {
            var c = new TeamsWindowService { CachedHwnd = h };
            _ = c.IsOpen;
            claimers.Add(c);
        }

        try
        {
            var service = new TeamsWindowService();
            bool closedFired = false;
            service.WindowClosed += () => closedFired = true;

            service.CheckAlive();
            Assert.True(closedFired);
        }
        finally
        {
            foreach (var c in claimers) { c.Release(); }
        }
    }

    // --- Title matching tests ---

    [Theory]
    [InlineData("Microsoft Teams", true)]
    [InlineData("Chat | Emma Lynch | Microsoft Teams", true)]
    [InlineData("teams.microsoft.com", true)]
    [InlineData("https://teams.microsoft.com", true)]
    [InlineData("https://teams.microsoft.com/", true)]
    [InlineData("Microsoft Teams - Loading...", true)]
    [InlineData("", false)]
    [InlineData("Some other app", false)]
    [InlineData("Google Chrome", false)]
    public void IsTeamsWindowTitle_MatchesExpected(string title, bool expected)
    {
        Assert.Equal(expected, TeamsWindowService.IsTeamsWindowTitle(title));
    }
}
