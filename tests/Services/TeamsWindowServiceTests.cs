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
    public void Focus_ReturnsFalse_WhenNotOpen()
    {
        var service = new TeamsWindowService();

        Assert.False(service.Focus());
    }
}
