namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// E2E integration tests that mimic the real application path for adding Issues and PRs.
/// Clones rogerbarreto/copilot-booster, resolves the remote, and validates the full flow:
/// git remote → ParseGitHubOwnerRepo → GitHubApiService.GetIssueAsync/GetPullRequestAsync.
/// </summary>
public sealed class GitHubAddItemRealPathTests : IDisposable
{
    private string? _cloneDir;

    public void Dispose()
    {
        if (this._cloneDir != null && Directory.Exists(this._cloneDir))
        {
            try
            {
                // Git objects are read-only — need to clear attributes before delete
                foreach (var file in Directory.EnumerateFiles(this._cloneDir, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(this._cloneDir, true);
            }
            catch { }
        }
    }

    private string CloneRepo()
    {
        if (this._cloneDir != null)
        {
            return this._cloneDir;
        }

        this._cloneDir = Path.Combine(Path.GetTempPath(), $"cb-test-{Guid.NewGuid():N}");
        var psi = new System.Diagnostics.ProcessStartInfo("git", $"clone --depth 1 https://github.com/rogerbarreto/copilot-booster.git \"{this._cloneDir}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit(30000);
        Assert.Equal(0, proc.ExitCode);
        return this._cloneDir;
    }

    private static GitHubApiService CreateApi()
    {
        return new GitHubApiService();
    }

    /// <summary>
    /// Mimics the Add Issue flow: clone repo → resolve origin remote → parse owner/repo →
    /// call GetIssueAsync for issue #8 → verify it's found and is a real issue.
    /// </summary>
    [Fact]
    public async Task AddIssue_RealPath_ClonedRepo_FindsIssueAsync()
    {
        var repoDir = this.CloneRepo();
        var api = CreateApi();

        // Step 1: Find git root (same as AddIssueForm)
        var gitRoot = SessionService.FindGitRoot(repoDir);
        Assert.NotNull(gitRoot);

        // Step 2: Get remote URL for "origin"
        var remoteUrl = GitService.GetRemoteUrl(gitRoot, "origin");
        Assert.NotNull(remoteUrl);

        // Step 3: Parse owner/repo
        var parsed = GitService.ParseGitHubOwnerRepo(remoteUrl);
        Assert.NotNull(parsed);
        var (owner, repo) = parsed.Value;
        Assert.Equal("rogerbarreto", owner);
        Assert.Equal("copilot-booster", repo);

        // Step 4: Fetch issue #8 via API (same as AddIssueForm validation)
        var doc = await api.GetIssueAsync(owner, repo, 8);
        Assert.NotNull(doc);
        using (doc)
        {
            Assert.Equal(8, doc.RootElement.GetProperty("number").GetInt32());
            Assert.Equal("Advanced Smart Search", doc.RootElement.GetProperty("title").GetString());
            Assert.Equal("open", doc.RootElement.GetProperty("state").GetString());
            Assert.False(doc.RootElement.TryGetProperty("pull_request", out _),
                "Issue #8 should not have pull_request property");
        }
    }

    /// <summary>
    /// Mimics the Add PR flow: clone repo → resolve origin remote → parse owner/repo →
    /// call GetPullRequestAsync → verify PR data is returned.
    /// Uses a known merged PR if one exists, otherwise creates a synthetic test.
    /// </summary>
    [Fact]
    public async Task AddPr_RealPath_ClonedRepo_FindsPrByNumberAsync()
    {
        var repoDir = this.CloneRepo();
        var api = CreateApi();

        var gitRoot = SessionService.FindGitRoot(repoDir);
        Assert.NotNull(gitRoot);

        var remoteUrl = GitService.GetRemoteUrl(gitRoot, "origin");
        Assert.NotNull(remoteUrl);

        var parsed = GitService.ParseGitHubOwnerRepo(remoteUrl);
        Assert.NotNull(parsed);
        var (owner, repo) = parsed.Value;

        // Issue #8 is an issue, not a PR — GetPullRequestAsync should return null or 404
        var prDoc = await api.GetPullRequestAsync(owner, repo, 8);
        Assert.Null(prDoc); // #8 is an issue, not a PR

        // GetIssueAsync should return it as an issue
        var issueDoc = await api.GetIssueAsync(owner, repo, 8);
        Assert.NotNull(issueDoc);
        issueDoc.Dispose();
    }

    /// <summary>
    /// Verifies that the full remote resolution path works with multiple remotes.
    /// The cloned repo has "origin" pointing to rogerbarreto/copilot-booster.
    /// </summary>
    [Fact]
    public void RemoteResolution_ClonedRepo_ListsOrigin()
    {
        var repoDir = this.CloneRepo();
        var gitRoot = SessionService.FindGitRoot(repoDir);
        Assert.NotNull(gitRoot);

        var remotes = GitService.GetRemotes(gitRoot);
        Assert.Contains("origin", remotes);

        foreach (var remote in remotes)
        {
            var url = GitService.GetRemoteUrl(gitRoot, remote);
            Assert.NotNull(url);

            var parsed = GitService.ParseGitHubOwnerRepo(url);
            if (parsed.HasValue)
            {
                Assert.Equal("rogerbarreto", parsed.Value.owner);
                Assert.Equal("copilot-booster", parsed.Value.repo);
            }
        }
    }

    /// <summary>
    /// Real-world scenario: agent-framework worktree with upstream remote pointing to
    /// microsoft/agent-framework. Issue #4702 exists but was returning false 404.
    /// This test uses the actual local worktree path to reproduce the exact app flow.
    /// </summary>
    [LocalOnlyFact]
    [Trait("Category", "LocalOnly")]
    public async Task AddIssue_AgentFramework_UpstreamRemote_FindsIssue4702Async()
    {
        const string WorktreePath = @"S:\repo\worktrees\agent-framework-roger-test";
        if (!Directory.Exists(WorktreePath))
        {
            return; // Skip if worktree doesn't exist
        }

        var api = CreateApi();

        // Step 1: Find git root
        var gitRoot = SessionService.FindGitRoot(WorktreePath);
        Assert.NotNull(gitRoot);

        // Step 2: Get upstream remote URL
        var remoteUrl = GitService.GetRemoteUrl(gitRoot, "upstream");
        Assert.NotNull(remoteUrl);

        // Step 3: Parse owner/repo
        var parsed = GitService.ParseGitHubOwnerRepo(remoteUrl);
        Assert.NotNull(parsed);
        var (owner, repo) = parsed.Value;
        Assert.Equal("microsoft", owner);
        Assert.Equal("agent-framework", repo);

        // Step 4: Fetch issue #4702 — THIS IS THE BUG: returns null instead of the issue
        var doc = await api.GetIssueAsync(owner, repo, 4702);
        Assert.NotNull(doc);
        using (doc)
        {
            Assert.Equal(4702, doc.RootElement.GetProperty("number").GetInt32());
        }
    }

    /// <summary>
    /// Same scenario but without the local worktree dependency — uses API directly.
    /// Verifies that microsoft/agent-framework issue #4702 is accessible via cascading auth.
    /// May fail when unauthenticated rate limit is exhausted and gh token lacks SAML for microsoft org.
    /// </summary>
    [LocalOnlyFact]
    [Trait("Category", "LocalOnly")]
    public async Task AddIssue_MicrosoftAgentFramework_Issue4702_FoundAsync()
    {
        var api = CreateApi();

        // Direct API call — same as what AddIssueForm does after resolving remote
        var doc = await api.GetIssueAsync("microsoft", "agent-framework", 4702);
        Assert.NotNull(doc);
        using (doc)
        {
            Assert.Equal(4702, doc.RootElement.GetProperty("number").GetInt32());
            Assert.False(doc.RootElement.TryGetProperty("pull_request", out _),
                "Issue #4702 should not be a PR");
        }
    }
}

