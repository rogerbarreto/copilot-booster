public sealed class GitServiceTests : IDisposable
{
    private readonly string _tempDir;

    public GitServiceTests()
    {
        this._tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this._tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    [Fact]
    public void SanitizeWorkspaceDirName_BasicCase()
    {
        var result = GitService.SanitizeWorkspaceDirName("agent-framework", "issues/12312-fix-abcd");

        Assert.Equal("agent-framework-issues-12312-fix-abcd", result);
    }

    [Fact]
    public void SanitizeWorkspaceDirName_BackslashReplaced()
    {
        var result = GitService.SanitizeWorkspaceDirName("repo", @"feature\branch");

        Assert.Equal("repo-feature-branch", result);
    }

    [Fact]
    public void SanitizeWorkspaceDirName_SpecialCharsReplacedWithDash()
    {
        var result = GitService.SanitizeWorkspaceDirName("repo", "feat @#branch name");

        Assert.Equal("repo-feat-branch-name", result);
    }

    [Fact]
    public void SanitizeWorkspaceDirName_DotsAndUnderscoresPreserved()
    {
        var result = GitService.SanitizeWorkspaceDirName("repo", "v1.0_hotfix");

        Assert.Equal("repo-v1.0_hotfix", result);
    }

    [Fact]
    public void GetWorkspacesDir_ReturnsExpectedPath()
    {
        var result = GitService.GetWorkspacesDir();

        Assert.Contains("CopilotBooster", result);
        Assert.Contains("Workspaces", result);
    }

    [Fact]
    public void IsGitRepository_ReturnsTrueForGitRepo()
    {
        var gitDir = Path.Combine(this._tempDir, "repo");
        Directory.CreateDirectory(Path.Combine(gitDir, ".git"));

        var result = GitService.IsGitRepository(gitDir);

        Assert.True(result);
    }

    [Fact]
    public void IsGitRepository_ReturnsFalseForNonRepo()
    {
        var result = GitService.IsGitRepository(this._tempDir);

        Assert.False(result);
    }

    [Fact]
    public void ParseWorktreeList_ParsesBranchesCorrectly()
    {
        var porcelain = "worktree C:\\repos\\main\nbranch refs/heads/main\nHEAD abc123\n\nworktree C:\\repos\\feature\nbranch refs/heads/feature/login\nHEAD def456\n\n";

        var result = GitService.ParseWorktreeList(porcelain);

        Assert.Equal(2, result.Count);
        Assert.Equal(("C:\\repos\\main", "main"), result[0]);
        Assert.Equal(("C:\\repos\\feature", "feature/login"), result[1]);
    }

    [Fact]
    public void ParseWorktreeList_EmptyOutput_ReturnsEmpty()
    {
        var result = GitService.ParseWorktreeList("");

        Assert.Empty(result);
    }

    [Fact]
    public void ParseWorktreeList_SkipsDetachedHead()
    {
        var porcelain = "worktree C:\\repos\\main\nbranch refs/heads/main\nHEAD abc123\n\nworktree C:\\repos\\detached\nHEAD def456\ndetached\n\n";

        var result = GitService.ParseWorktreeList(porcelain);

        Assert.Single(result);
        Assert.Equal(("C:\\repos\\main", "main"), result[0]);
    }

    [Fact]
    public void GetDefaultWorkspacesDir_ReturnsExpectedPath()
    {
        var result = GitService.GetDefaultWorkspacesDir();

        Assert.Contains("CopilotBooster", result);
        Assert.Contains("Workspaces", result);
    }

    [Theory]
    [InlineData("origin/main", "main")]
    [InlineData("origin/feature/login", "feature/login")]
    [InlineData("upstream/hotfix", "hotfix")]
    [InlineData("main", "main")]
    [InlineData("feature/login", "feature/login")]
    public void GetLocalBranchName_StripsRemotePrefix(string refName, string expected)
    {
        var remotes = new List<string> { "origin", "upstream" };
        var result = GitService.GetLocalBranchName(refName, remotes);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("origin/main", true)]
    [InlineData("upstream/feature", true)]
    [InlineData("main", false)]
    [InlineData("feature/login", false)]
    public void IsRemoteRef_DetectsRemotePrefixes(string refName, bool expected)
    {
        var remotes = new List<string> { "origin", "upstream" };
        var result = GitService.IsRemoteRef(refName, remotes);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("https://github.com/owner/repo.git", "GitHub")]
    [InlineData("git@github.com:owner/repo.git", "GitHub")]
    [InlineData("https://gitlab.com/owner/repo.git", "GitLab")]
    [InlineData("https://bitbucket.org/owner/repo.git", "Bitbucket")]
    [InlineData("https://dev.azure.com/org/project/_git/repo", "AzureDevOps")]
    [InlineData("https://owner.visualstudio.com/project/_git/repo", "AzureDevOps")]
    [InlineData("https://self-hosted.example.com/repo.git", "Unknown")]
    public void DetectHostingPlatform_ReturnsCorrectPlatform(string url, string expectedPlatform)
    {
        var result = GitService.DetectHostingPlatform(url);
        Assert.Equal(expectedPlatform, result.ToString());
    }

    [Fact]
    public void GetPrRefPattern_ReturnsCorrectPatternForEachPlatform()
    {
        Assert.Equal("refs/pull/42/head", GitService.GetPrRefPattern(GitService.HostingPlatform.GitHub, 42));
        Assert.Equal("refs/pull/99/head", GitService.GetPrRefPattern(GitService.HostingPlatform.AzureDevOps, 99));
        Assert.Equal("refs/merge-requests/7/head", GitService.GetPrRefPattern(GitService.HostingPlatform.GitLab, 7));
        Assert.Equal("refs/pull-requests/15/from", GitService.GetPrRefPattern(GitService.HostingPlatform.Bitbucket, 15));
        Assert.Null(GitService.GetPrRefPattern(GitService.HostingPlatform.Unknown, 1));
    }

    [Theory]
    [InlineData("https://github.com/owner/repo.git", "owner", "repo")]
    [InlineData("https://github.com/owner/repo", "owner", "repo")]
    [InlineData("git@github.com:owner/repo.git", "owner", "repo")]
    [InlineData("git@github.com:owner/repo", "owner", "repo")]
    [InlineData("https://github.com/microsoft/semantic-kernel.git", "microsoft", "semantic-kernel")]
    public void ParseGitHubOwnerRepo_ReturnsCorrectParts(string url, string expectedOwner, string expectedRepo)
    {
        var result = GitService.ParseGitHubOwnerRepo(url);
        Assert.NotNull(result);
        Assert.Equal(expectedOwner, result.Value.owner);
        Assert.Equal(expectedRepo, result.Value.repo);
    }

    [Theory]
    [InlineData("https://gitlab.com/owner/repo.git")]
    [InlineData("https://bitbucket.org/owner/repo.git")]
    public void ParseGitHubOwnerRepo_ReturnsNullForNonGitHub(string url)
    {
        var result = GitService.ParseGitHubOwnerRepo(url);
        Assert.Null(result);
    }

    [Fact]
    public void ResolveUniqueBranchName_ReturnBaseNameWhenNoWorktreeConflict()
    {
        // Even if a branch with the same name exists, if no worktree uses it, return the base name.
        // We test with a temp git repo where no worktrees exist beyond the main one.
        var repoPath = this.InitBareGitRepo();
        var result = WorkspaceCreationService.ResolveUniqueBranchName(repoPath, "feature-x");
        Assert.Equal("feature-x", result);
    }

    [Fact]
    public void ResolveUniqueBranchName_AppendsSuffixWhenWorktreeUsesName()
    {
        var repoPath = this.InitBareGitRepo();
        // The main worktree uses "main" as its branch
        var result = WorkspaceCreationService.ResolveUniqueBranchName(repoPath, "main");
        Assert.Equal("main-001", result);
    }

    [Fact]
    public void LocalBranchExists_ReturnsFalseForNonExistentBranch()
    {
        var repoPath = this.InitBareGitRepo();
        Assert.False(GitService.LocalBranchExists(repoPath, "nonexistent-branch"));
    }

    [Fact]
    public void LocalBranchExists_ReturnsTrueForExistingBranch()
    {
        var repoPath = this.InitBareGitRepo();
        Assert.True(GitService.LocalBranchExists(repoPath, "main"));
    }

    private string InitBareGitRepo()
    {
        var repoPath = Path.Combine(this._tempDir, Path.GetRandomFileName());
        Directory.CreateDirectory(repoPath);

        RunGitCmd(repoPath, "init -b main");
        RunGitCmd(repoPath, "config user.email test@test.com");
        RunGitCmd(repoPath, "config user.name Test");
        File.WriteAllText(Path.Combine(repoPath, "README.md"), "# Test");
        RunGitCmd(repoPath, "add .");
        RunGitCmd(repoPath, "commit -m \"init\"");

        return repoPath;
    }

    private static void RunGitCmd(string workDir, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit(10_000);
    }
}
