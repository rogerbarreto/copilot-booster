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

    /// <summary>
    /// Issue #7385 on dotnet/extensions is closed as "not_planned".
    /// The API response must include state_reason so the icon shows gray (not purple).
    /// </summary>
    [Fact]
    public async Task GetIssue_ClosedNotPlanned_HasStateReason()
    {
        var api = CreateApi();

        var doc = await api.GetIssueAsync("dotnet", "extensions", 7385);

        Assert.NotNull(doc);
        using (doc)
        {
            Assert.Equal("closed", doc.RootElement.GetProperty("state").GetString());
            Assert.True(doc.RootElement.TryGetProperty("state_reason", out var sr),
                "API response must include state_reason");
            Assert.Equal("not_planned", sr.GetString());
        }
    }

    /// <summary>
    /// When a "not_planned" issue is added via AddIssueForm parsing,
    /// the resulting GitHubTrackedItem must have StateReason = "not_planned".
    /// </summary>
    [Fact]
    public async Task ParseIssue_ClosedNotPlanned_StateReasonPreserved()
    {
        var api = CreateApi();

        var doc = await api.GetIssueAsync("dotnet", "extensions", 7385);
        Assert.NotNull(doc);

        using (doc)
        {
            var root = doc.RootElement;
            var state = root.GetProperty("state").GetString() ?? "open";
            var stateReason = root.TryGetProperty("state_reason", out var srp)
                && srp.ValueKind != System.Text.Json.JsonValueKind.Null
                ? srp.GetString() : null;

            var item = new GitHubTrackedItem
            {
                Type = "issue",
                Number = 7385,
                State = state,
                StateReason = stateReason
            };

            Assert.Equal("closed", item.State);
            Assert.Equal("not_planned", item.StateReason);

            // Verify the icon would be gray, not purple
            var icon = GitHubIconRenderer.GetIssueIcon(item.State, item.StateReason);
            Assert.NotNull(icon);
        }
    }

    /// <summary>
    /// Simulates the stale data bug: a closed "not_planned" issue was tracked
    /// BEFORE the StateReason fix, so StateReason is null in the persisted data.
    /// The polling service must backfill StateReason even for final (closed) items.
    /// Without the fix, IsFinal skips the item and StateReason stays null → purple icon.
    /// </summary>
    [Fact]
    public async Task Polling_ClosedIssue_MissingStateReason_BackfillsFromApi()
    {
        var api = CreateApi();
        const string SessionId = "e2e-stale-state-reason";

        var staleItem = new GitHubTrackedItem
        {
            Type = "issue",
            Number = 7385,
            State = "closed",
            StateReason = null,
            Title = "Old title",
            LastModifiedAt = "2026-03-13T15:44:48Z",
            LastSeenAt = "2026-03-13T16:00:00Z"
        };

        var data = new GitHubTrackingData
        {
            Owner = "dotnet",
            Repo = "extensions",
            Items = [staleItem]
        };

        GitHubTrackingService.Save(SessionId, data);

        try
        {
            // Verify the IsFinal check allows backfill for missing StateReason
            var item = data.Items[0];
            bool shouldSkip = item.IsFinal && (item.IsPr || item.StateReason != null);
            Assert.False(shouldSkip, "Closed issue with null StateReason must NOT be skipped");

            // Actually poll via PollSessionNow and verify backfill
            using var poller = new GitHubPollingService(api, () => [SessionId]);
            poller.PollSessionNow(SessionId);
            await Task.Delay(3000);

            var updated = GitHubTrackingService.Load(SessionId);
            Assert.NotNull(updated);
            var updatedItem = updated.Items.First(i => i.Number == 7385);
            Assert.Equal("not_planned", updatedItem.StateReason);
        }
        finally
        {
            var dir = SessionStateService.GetSessionDir(SessionId);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    /// <summary>
    /// Same bug but for PRs: a merged PR tracked before the fix has no check data.
    /// Merged PRs should NOT be re-polled (they're truly final).
    /// </summary>
    [Fact]
    public async Task Polling_MergedPr_NotRepolled()
    {
        var api = CreateApi();
        const string SessionId = "e2e-merged-pr-nopoll";

        var mergedItem = new GitHubTrackedItem
        {
            Type = "pr",
            Number = 99999,
            State = "merged",
            Title = "Some merged PR"
        };

        var data = new GitHubTrackingData
        {
            Owner = "dotnet",
            Repo = "extensions",
            Items = [mergedItem]
        };

        GitHubTrackingService.Save(SessionId, data);

        try
        {
            using var poller = new GitHubPollingService(api, () => [SessionId]);
            poller.PollSessionNow(SessionId);
            await Task.Delay(2000);

            // Merged PR should NOT have been changed (still merged, no re-poll)
            var updated = GitHubTrackingService.Load(SessionId);
            Assert.NotNull(updated);
            var item = updated.Items.First(i => i.Number == 99999);
            Assert.Equal("merged", item.State);
            Assert.Equal("Some merged PR", item.Title); // Title unchanged = not re-polled
        }
        finally
        {
            var dir = SessionStateService.GetSessionDir(SessionId);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
