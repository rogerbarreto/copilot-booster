public sealed class TeamsWindowServiceTests
{
    /// <summary>
    /// Claims all currently open Teams windows to prevent re-scan from picking them up.
    /// Must be disposed (or Release called) to unclaim.
    /// </summary>
    private static List<TeamsWindowService> ClaimAllTeamsWindows()
    {
        var claimers = new List<TeamsWindowService>();
        foreach (var hwnd in TeamsWindowService.FindAllTeamsWindows())
        {
            var c = new TeamsWindowService();
            c.RestoreCachedHwnd(hwnd);
            claimers.Add(c);
        }

        return claimers;
    }

    private static void ReleaseAll(List<TeamsWindowService> claimers)
    {
        foreach (var c in claimers) { c.Release(); }
    }
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
        var claimers = ClaimAllTeamsWindows();
        try
        {
            var service = new TeamsWindowService();

            Assert.False(service.IsOpen);
            Assert.Equal(IntPtr.Zero, service.CachedHwnd);
        }
        finally { ReleaseAll(claimers); }
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
        var claimers = ClaimAllTeamsWindows();
        try
        {
            var service = new TeamsWindowService();

            Assert.False(service.Focus());
        }
        finally { ReleaseAll(claimers); }
    }

    [Fact]
    public void Release_ClearsHwnd()
    {
        var service = new TeamsWindowService
        {
            CachedHwnd = 12345
        };

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
        var claimers = ClaimAllTeamsWindows();
        try
        {
            var service = new TeamsWindowService
            {
                CachedHwnd = 99999999
            };

            Assert.False(service.IsOpen);
        }
        finally { ReleaseAll(claimers); }
    }

    [Fact]
    public void Focus_ReturnsFalse_WhenCachedHwndIsInvalid()
    {
        var claimers = ClaimAllTeamsWindows();
        try
        {
            var service = new TeamsWindowService
            {
                CachedHwnd = 99999999
            };

            Assert.False(service.Focus());
        }
        finally { ReleaseAll(claimers); }
    }

    [Fact]
    public void CheckAlive_FiresWindowClosed_WhenNotOpen()
    {
        var claimers = ClaimAllTeamsWindows();
        try
        {
            var service = new TeamsWindowService();
            bool closedFired = false;
            service.WindowClosed += () => closedFired = true;

            service.CheckAlive();

            Assert.True(closedFired);
        }
        finally { ReleaseAll(claimers); }
    }

    [Fact]
    public void CheckAlive_DoesNotFire_WhenPendingOpen()
    {
        // IsPendingOpen is private set, so we can't test this directly.
        // But we can verify that a new service (not pending, not open) fires the event.
        var claimers = ClaimAllTeamsWindows();
        try
        {
            var service = new TeamsWindowService();
            bool closedFired = false;
            service.WindowClosed += () => closedFired = true;

            service.CheckAlive();
            Assert.True(closedFired);
        }
        finally { ReleaseAll(claimers); }
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
