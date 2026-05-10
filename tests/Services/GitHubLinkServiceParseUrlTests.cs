namespace CopilotBooster.Tests.Services;

public sealed class GitHubLinkServiceParseUrlTests
{
    [Fact]
    public void TryParseIssueOrPrUrl_IssuesUrl_ReturnsIssueRef()
    {
        var parsed = GitHubLinkService.TryParseIssueOrPrUrl(
            "https://github.com/microsoft/agent-framework/issues/5633",
            out var result);

        Assert.True(parsed);
        Assert.Equal("microsoft", result.Owner);
        Assert.Equal("agent-framework", result.Repo);
        Assert.Equal(5633, result.Number);
        Assert.Equal(GitHubRefType.Issue, result.Type);
    }

    [Fact]
    public void TryParseIssueOrPrUrl_PullUrl_ReturnsPrRef()
    {
        var parsed = GitHubLinkService.TryParseIssueOrPrUrl(
            "https://github.com/microsoft/agent-framework/pull/5633",
            out var result);

        Assert.True(parsed);
        Assert.Equal("microsoft", result.Owner);
        Assert.Equal("agent-framework", result.Repo);
        Assert.Equal(5633, result.Number);
        Assert.Equal(GitHubRefType.Pr, result.Type);
    }

    [Fact]
    public void TryParseIssueOrPrUrl_TrailingSlash_ReturnsRef()
    {
        var parsed = GitHubLinkService.TryParseIssueOrPrUrl(
            "https://github.com/microsoft/agent-framework/issues/5633/",
            out var result);

        Assert.True(parsed);
        Assert.Equal(5633, result.Number);
        Assert.Equal(GitHubRefType.Issue, result.Type);
    }

    [Fact]
    public void TryParseIssueOrPrUrl_Query_ReturnsRef()
    {
        var parsed = GitHubLinkService.TryParseIssueOrPrUrl(
            "https://github.com/microsoft/agent-framework/issues/5633?foo=bar",
            out var result);

        Assert.True(parsed);
        Assert.Equal(5633, result.Number);
        Assert.Equal(GitHubRefType.Issue, result.Type);
    }

    [Fact]
    public void TryParseIssueOrPrUrl_Fragment_ReturnsRef()
    {
        var parsed = GitHubLinkService.TryParseIssueOrPrUrl(
            "https://github.com/microsoft/agent-framework/pull/5633#discussion",
            out var result);

        Assert.True(parsed);
        Assert.Equal(5633, result.Number);
        Assert.Equal(GitHubRefType.Pr, result.Type);
    }

    [Fact]
    public void TryParseIssueOrPrUrl_SurroundingWhitespace_ReturnsRef()
    {
        var parsed = GitHubLinkService.TryParseIssueOrPrUrl(
            "  https://github.com/microsoft/agent-framework/issues/5633  ",
            out var result);

        Assert.True(parsed);
        Assert.Equal(5633, result.Number);
        Assert.Equal(GitHubRefType.Issue, result.Type);
    }

    [Fact]
    public void TryParseIssueOrPrUrl_SchemeLessUrl_ReturnsRef()
    {
        var parsed = GitHubLinkService.TryParseIssueOrPrUrl(
            "github.com/microsoft/agent-framework/pull/5633",
            out var result);

        Assert.True(parsed);
        Assert.Equal("microsoft", result.Owner);
        Assert.Equal("agent-framework", result.Repo);
        Assert.Equal(5633, result.Number);
        Assert.Equal(GitHubRefType.Pr, result.Type);
    }

    [Fact]
    public void TryParseIssueOrPrUrl_BareInteger_ReturnsFalse()
    {
        var parsed = GitHubLinkService.TryParseIssueOrPrUrl("5633", out var result);

        Assert.False(parsed);
        Assert.Equal(default, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseIssueOrPrUrl_EmptyOrWhitespace_ReturnsFalse(string input)
    {
        var parsed = GitHubLinkService.TryParseIssueOrPrUrl(input, out var result);

        Assert.False(parsed);
        Assert.Equal(default, result);
    }

    [Fact]
    public void TryParseIssueOrPrUrl_WrongHost_ReturnsFalse()
    {
        var parsed = GitHubLinkService.TryParseIssueOrPrUrl(
            "https://gitlab.com/microsoft/agent-framework/issues/5633",
            out var result);

        Assert.False(parsed);
        Assert.Equal(default, result);
    }

    [Fact]
    public void TryParseIssueOrPrUrl_PullsTypo_ReturnsFalse()
    {
        var parsed = GitHubLinkService.TryParseIssueOrPrUrl(
            "https://github.com/microsoft/agent-framework/pulls/5633",
            out var result);

        Assert.False(parsed);
        Assert.Equal(default, result);
    }

    [Fact]
    public void TryParseIssueOrPrUrl_ExtraPathSegments_ReturnsFalse()
    {
        var parsed = GitHubLinkService.TryParseIssueOrPrUrl(
            "https://github.com/microsoft/agent-framework/issues/5633/extra",
            out var result);

        Assert.False(parsed);
        Assert.Equal(default, result);
    }

    [Theory]
    [InlineData("https://github.com/microsoft/agent-framework/issues/0")]
    [InlineData("https://github.com/microsoft/agent-framework/issues/-1")]
    public void TryParseIssueOrPrUrl_NonPositiveNumber_ReturnsFalse(string input)
    {
        var parsed = GitHubLinkService.TryParseIssueOrPrUrl(input, out var result);

        Assert.False(parsed);
        Assert.Equal(default, result);
    }

    [Fact]
    public void TryParseIssueOrPrUrl_HttpUrl_ReturnsFalse()
    {
        var parsed = GitHubLinkService.TryParseIssueOrPrUrl(
            "http://github.com/microsoft/agent-framework/issues/5633",
            out var result);

        Assert.False(parsed);
        Assert.Equal(default, result);
    }
}
