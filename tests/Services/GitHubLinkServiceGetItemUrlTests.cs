namespace CopilotBooster.Tests.Services;

public sealed class GitHubLinkServiceGetItemUrlTests
{
    private static GitHubTrackedItem MakePrItem(int number, string type = "pr") =>
        new() { Type = type, Number = number, Title = "Test PR", State = "open" };

    private static GitHubTrackedItem MakeIssueItem(int number, string type = "issue") =>
        new() { Type = type, Number = number, Title = "Test Issue", State = "open" };

    [Fact]
    public void GetItemUrl_PrItem_ReturnsPrUrl()
    {
        var item = MakePrItem(42);

        var url = GitHubLinkService.GetItemUrl("myorg", "myrepo", item);

        Assert.Contains("/pull/42", url);
    }

    [Fact]
    public void GetItemUrl_IssueItem_ReturnsIssueUrl()
    {
        var item = MakeIssueItem(99);

        var url = GitHubLinkService.GetItemUrl("myorg", "myrepo", item);

        Assert.Contains("/issues/99", url);
    }

    [Fact]
    public void GetItemUrl_PrItemMixedCaseType_ReturnsPrUrl()
    {
        var item = MakePrItem(7, type: "PR");

        var url = GitHubLinkService.GetItemUrl("myorg", "myrepo", item);

        Assert.Contains("/pull/7", url);
        Assert.DoesNotContain("/issues/", url);
    }

    [Fact]
    public void GetItemUrl_OutputMatchesGetPrUrl()
    {
        var item = MakePrItem(123);

        var viaItem = GitHubLinkService.GetItemUrl("owner", "repo", item);
        var viaDirect = GitHubLinkService.GetPrUrl("owner", "repo", 123);

        Assert.Equal(viaDirect, viaItem);
    }

    [Fact]
    public void GetItemUrl_OutputMatchesGetIssueUrl()
    {
        var item = MakeIssueItem(456);

        var viaItem = GitHubLinkService.GetItemUrl("owner", "repo", item);
        var viaDirect = GitHubLinkService.GetIssueUrl("owner", "repo", 456);

        Assert.Equal(viaDirect, viaItem);
    }
}
