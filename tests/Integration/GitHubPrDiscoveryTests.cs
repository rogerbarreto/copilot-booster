namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// Integration tests for GitHubApiService PR discovery using real public GitHub repos.
/// Tests the ListPullRequestsForBranchAsync method which must find PRs
/// regardless of whether they come from the same repo or a fork.
/// </summary>
public sealed class GitHubPrDiscoveryTests
{
    private static GitHubApiService CreateApi() => new(() => null);

    /// <summary>
    /// Discovers a PR from a fork on dotnet/runtime.
    /// PR #125557 has head branch "patch-46" from fork am11/runtime.
    /// The discovery must return a PR whose head.ref matches "patch-46",
    /// NOT just the first PR in the repo.
    /// </summary>
    [Fact]
    public async Task DiscoverPr_ForkBranch_ReturnsPrMatchingBranch()
    {
        var api = CreateApi();

        var doc = await api.ListPullRequestsForBranchAsync("dotnet", "runtime", "patch-46");

        Assert.NotNull(doc);
        using (doc)
        {
            Assert.True(doc.RootElement.GetArrayLength() > 0, "Should find at least one PR");

            // The FIRST element returned must have head.ref == "patch-46"
            // If the API returns the wrong PR (e.g., first PR in repo), this fails.
            var first = doc.RootElement[0];
            var headRef = first.GetProperty("head").GetProperty("ref").GetString();
            Assert.Equal("patch-46", headRef);
        }
    }

    /// <summary>
    /// Discovers a PR from the same org (dotnet/runtime).
    /// The returned PR's head.ref must match the searched branch.
    /// </summary>
    [Fact]
    public async Task DiscoverPr_SameRepoBranch_ReturnsPrMatchingBranch()
    {
        var api = CreateApi();

        var doc = await api.ListPullRequestsForBranchAsync("dotnet", "runtime", "copilot/fix-frozenset-creation-exception");

        Assert.NotNull(doc);
        using (doc)
        {
            Assert.True(doc.RootElement.GetArrayLength() > 0, "Should find at least one PR");

            var first = doc.RootElement[0];
            var headRef = first.GetProperty("head").GetProperty("ref").GetString();
            Assert.Equal("copilot/fix-frozenset-creation-exception", headRef);
        }
    }

    /// <summary>
    /// Discovery for a nonexistent branch returns null or empty array.
    /// </summary>
    [Fact]
    public async Task DiscoverPr_NonexistentBranch_ReturnsNullOrEmpty()
    {
        var api = CreateApi();

        var doc = await api.ListPullRequestsForBranchAsync("dotnet", "runtime", "this-branch-does-not-exist-" + Guid.NewGuid().ToString("N"));

        if (doc != null)
        {
            using (doc)
            {
                Assert.Equal(0, doc.RootElement.GetArrayLength());
            }
        }
    }

    /// <summary>
    /// Fetching a known PR by number returns correct data.
    /// </summary>
    [Fact]
    public async Task GetPr_KnownNumber_ReturnsData()
    {
        var api = CreateApi();

        var doc = await api.GetPullRequestAsync("dotnet", "runtime", 125557);

        Assert.NotNull(doc);
        using (doc)
        {
            Assert.Equal("Add openbsd non-portable probing", doc.RootElement.GetProperty("title").GetString());
            Assert.Equal("open", doc.RootElement.GetProperty("state").GetString());
        }
    }

    /// <summary>
    /// Fetching a known Issue by number returns correct data.
    /// GetIssueAsync should return null for PRs (they have pull_request property).
    /// </summary>
    [Fact]
    public async Task GetIssue_PrNumber_ReturnsNull()
    {
        var api = CreateApi();

        // PR #125557 is a PR, not an issue — GetIssueAsync should return null
        var doc = await api.GetIssueAsync("dotnet", "runtime", 125557);

        Assert.Null(doc);
    }
}
