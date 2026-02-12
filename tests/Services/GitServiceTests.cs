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
    public void SanitizeWorkspaceDirName_SpecialCharsRemoved()
    {
        var result = GitService.SanitizeWorkspaceDirName("repo", "feat @#branch name");

        Assert.Equal("repo-featbranchname", result);
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

        Assert.Contains("CopilotApp", result);
        Assert.Contains("Workspaces", result);
    }

    [Fact]
    public void IsGitRepository_ReturnsTrueForGitRepo()
    {
        var result = GitService.IsGitRepository(@"D:\repo\community\copilot-app");

        Assert.True(result);
    }

    [Fact]
    public void IsGitRepository_ReturnsFalseForNonRepo()
    {
        var result = GitService.IsGitRepository(this._tempDir);

        Assert.False(result);
    }
}
