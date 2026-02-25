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
}
