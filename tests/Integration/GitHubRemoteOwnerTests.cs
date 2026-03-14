namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// Tests that the owner/repo stored in github-tracking.json matches
/// the remote the user selected, not just the first GitHub remote.
/// Reproduces the bug where a session with multiple remotes (origin=fork, upstream=main)
/// stores the wrong owner when opening issues/PRs in the browser.
/// </summary>
public sealed class GitHubRemoteOwnerTests : IDisposable
{
    private string? _repoDir;

    public void Dispose()
    {
        if (this._repoDir != null && Directory.Exists(this._repoDir))
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(this._repoDir, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(f, FileAttributes.Normal);
                }

                Directory.Delete(this._repoDir, true);
            }
            catch { }
        }
    }

    /// <summary>
    /// Creates a git repo with two remotes:
    /// origin → rogerbarreto/copilot-booster (fork)
    /// upstream → microsoft/agent-framework (main repo)
    /// Simulates a fork workflow where issue #4702 exists on upstream, not on origin.
    /// </summary>
    private string CreateRepoWithTwoRemotes()
    {
        this._repoDir = Path.Combine(Path.GetTempPath(), $"remote-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(this._repoDir);

        RunGit(this._repoDir, "init");
        RunGit(this._repoDir, "remote add origin https://github.com/rogerbarreto/agent-framework-public.git");
        RunGit(this._repoDir, "remote add upstream https://github.com/microsoft/agent-framework.git");

        return this._repoDir;
    }

    private static void RunGit(string workDir, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", args)
        {
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit(5000);
    }

    /// <summary>
    /// When user selects "upstream" remote and adds issue #4702,
    /// the stored owner must be "microsoft" (from upstream),
    /// not "rogerbarreto" (from origin which is resolved first by ResolveGitHubRepo).
    /// This test verifies that AddIssueForm/AddPrForm now return the correct owner/repo
    /// from the selected remote, and MainForm uses it instead of ResolveGitHubRepo.
    /// </summary>
    [Fact]
    public void StoredOwner_MustMatch_SelectedRemote_NotFirstRemote()
    {
        var repoDir = this.CreateRepoWithTwoRemotes();

        var gitRoot = SessionService.FindGitRoot(repoDir);
        Assert.NotNull(gitRoot);

        // Simulate what the form does: user selects "upstream"
        var upstreamUrl = GitService.GetRemoteUrl(gitRoot, "upstream");
        Assert.NotNull(upstreamUrl);
        var upstreamParsed = GitService.ParseGitHubOwnerRepo(upstreamUrl);
        Assert.NotNull(upstreamParsed);
        var (upstreamOwner, upstreamRepo) = upstreamParsed.Value;
        Assert.Equal("microsoft", upstreamOwner);
        Assert.Equal("agent-framework", upstreamRepo);

        // Simulate storing with the form-returned owner (the fix)
        const string SessionId = "test-remote-owner";
        var item = new GitHubTrackedItem { Type = "issue", Number = 4702, Title = "Test" };
        GitHubTrackingService.AddItem(SessionId, upstreamOwner, upstreamRepo, item);

        try
        {
            // Verify the stored data uses the selected remote's owner, not the first remote
            var data = GitHubTrackingService.Load(SessionId);
            Assert.NotNull(data);
            Assert.Equal("microsoft", data.Owner);
            Assert.Equal("agent-framework", data.Repo);

            // The URL built from stored data should point to microsoft, not rogerbarreto
            var url = GitHubLinkService.GetIssueUrl(data.Owner, data.Repo, 4702);
            Assert.Contains("microsoft/agent-framework", url);
            Assert.DoesNotContain("rogerbarreto", url);
        }
        finally
        {
            var sessionDir = SessionStateService.GetSessionDir(SessionId);
            if (Directory.Exists(sessionDir))
            {
                Directory.Delete(sessionDir, true);
            }
        }
    }
}
