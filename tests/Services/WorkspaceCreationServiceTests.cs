public sealed class WorkspaceCreationServiceTests
{
    [Theory]
    [InlineData("my-repo", "feature/login", "my-repo-feature-login")]
    [InlineData("repo", @"feature\branch", "repo-feature-branch")]
    [InlineData("repo", "feat @#branch name", "repo-featbranchname")]
    [InlineData("repo", "v1.0_hotfix", "repo-v1.0_hotfix")]
    public void SanitizeWorkspaceName_DelegatesToGitService(string repoFolder, string workspace, string expected)
    {
        var result = WorkspaceCreationService.SanitizeWorkspaceName(repoFolder, workspace);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildWorkspacePath_ProducesExpectedPath()
    {
        var result = WorkspaceCreationService.BuildWorkspacePath("my-repo", "feature/login");

        Assert.EndsWith("my-repo-feature-login", result);
        Assert.Contains("Workspaces", result);
    }
}
