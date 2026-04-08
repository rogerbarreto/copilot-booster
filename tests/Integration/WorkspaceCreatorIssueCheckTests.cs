namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// Integration tests for the HTML-first issue/PR checking approach.
/// Validates that GitHubApiService can find issues and PRs via HTML scraping
/// (never rate-limited), replacing the previous unauthenticated api.github.com calls
/// that failed when the 60 req/hour limit was exhausted.
/// </summary>
public sealed class WorkspaceCreatorIssueCheckTests
{
    private static GitHubApiService CreateApi()
    {
        return new GitHubApiService();
    }

    /// <summary>
    /// HTML scraping must find a known public issue.
    /// This is the exact scenario that was broken: issue #3081 on microsoft/agent-framework
    /// returned "not found" because the unauthenticated API rate limit was exhausted.
    /// With HTML scraping as primary, this must always succeed for public repos.
    /// </summary>
    [Fact]
    public async Task IssueCheck_ViaService_FindsPublicIssueAsync()
    {
        var api = CreateApi();

        var doc = await api.GetIssueAsync("microsoft", "agent-framework", 3081);

        Assert.NotNull(doc);
        using (doc)
        {
            Assert.Equal(3081, doc.RootElement.GetProperty("number").GetInt32());
            Assert.True(doc.RootElement.TryGetProperty("title", out var titleProp),
                "HTML scraping must extract the issue title");
            Assert.NotEmpty(titleProp.GetString()!);
        }
    }

    /// <summary>
    /// GetIssueAsync must return null when the number is actually a PR.
    /// GitHub's /issues/N endpoint returns PRs too (they have a pull_request property),
    /// and the HTML page at /issues/N redirects to /pull/N for PRs.
    /// The service must detect this and return null.
    /// </summary>
    [Fact]
    public async Task IssueCheck_ViaService_DetectsPrNotIssueAsync()
    {
        var api = CreateApi();

        // PR #5014 on microsoft/agent-framework is a known PR, not an issue
        var doc = await api.GetIssueAsync("microsoft", "agent-framework", 5014);

        Assert.Null(doc);
    }

    /// <summary>
    /// GetIssueAsync must return null for a nonexistent issue.
    /// </summary>
    [Fact]
    public async Task IssueCheck_ViaService_ReturnsNullForNonexistentAsync()
    {
        var api = CreateApi();

        var doc = await api.GetIssueAsync("microsoft", "agent-framework", 999999999);

        Assert.Null(doc);
    }

    /// <summary>
    /// GetPullRequestAsync must find a known public PR via HTML scraping.
    /// </summary>
    [Fact]
    public async Task PrCheck_ViaService_FindsPublicPrAsync()
    {
        var api = CreateApi();

        // PR #5014 on microsoft/agent-framework
        var doc = await api.GetPullRequestAsync("microsoft", "agent-framework", 5014);

        Assert.NotNull(doc);
        using (doc)
        {
            Assert.Equal(5014, doc.RootElement.GetProperty("number").GetInt32());
            Assert.True(doc.RootElement.TryGetProperty("title", out var titleProp),
                "HTML scraping must extract the PR title");
            Assert.NotEmpty(titleProp.GetString()!);
        }
    }

    /// <summary>
    /// GetPullRequestAsync must return null when the number is actually an issue.
    /// GitHub redirects /pull/N to /issues/N for issues.
    /// The service must detect this and return null.
    /// </summary>
    [Fact]
    public async Task PrCheck_ViaService_DetectsIssueNotPrAsync()
    {
        var api = CreateApi();

        // Issue #3081 on microsoft/agent-framework is an issue, not a PR
        var doc = await api.GetPullRequestAsync("microsoft", "agent-framework", 3081);

        Assert.Null(doc);
    }

    /// <summary>
    /// gh api CLI must return check runs data for a known commit.
    /// This validates the GhApiAsync path used by GetCheckRunsAsync.
    /// Requires gh CLI to be authenticated — skips if not available.
    /// </summary>
    [Fact]
    public async Task CheckRuns_ViaGhApi_ReturnsDataAsync()
    {
        var api = CreateApi();

        // Use HEAD of main on a public repo (always valid)
        var doc = await api.GetCheckRunsAsync("rogerbarreto", "copilot-booster", "main");

        // gh api requires authentication — skip if not available
        if (doc == null)
        {
            return;
        }

        using (doc)
        {
            Assert.True(doc.RootElement.TryGetProperty("total_count", out _),
                "Check runs response must include total_count");
        }
    }

    /// <summary>
    /// gh api CLI must return reviews data for a known PR.
    /// Requires gh CLI to be authenticated — skips if not available.
    /// </summary>
    [Fact]
    public async Task Reviews_ViaGhApi_ReturnsDataAsync()
    {
        var api = CreateApi();

        var doc = await api.GetPullRequestReviewsAsync("dotnet", "runtime", 125557);

        if (doc == null)
        {
            return;
        }

        using (doc)
        {
            Assert.True(doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array);
        }
    }
}
