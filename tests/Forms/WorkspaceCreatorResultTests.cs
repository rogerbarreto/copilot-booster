using System.Reflection;

/// <summary>
/// Tier 1: Struct construction and field round-trip tests.
/// Tier 2 (Option B): Reflection contract pin for ShowWorkspaceCreator return type.
/// </summary>
public sealed class WorkspaceCreatorResultTests
{
    // ─── Tier 1: WorkspaceCreatorResult construction ───────────────────────

    [Fact]
    public void WorkspaceCreatorResult_WithNullGitHubLink_FieldsSetCorrectly()
    {
        var result = new WorkspaceCreatorResult
        {
            WorktreePath = @"C:\repos\my-feature",
            SessionName = "my-feature",
            GitHubLink = null,
        };

        Assert.Equal(@"C:\repos\my-feature", result.WorktreePath);
        Assert.Equal("my-feature", result.SessionName);
        Assert.False(result.GitHubLink.HasValue);
    }

    [Fact]
    public void WorkspaceCreatorResult_WithPopulatedGitHubLink_AllFieldsRoundTrip()
    {
        var item = new GitHubTrackedItem
        {
            Type = "pr",
            Number = 42,
            Title = "Add feature",
            State = "open",
            Draft = false,
            Author = "alice",
            HeadBranch = "feature/add-thing",
        };
        var link = new WorkspaceGitHubLink { Owner = "myorg", Repo = "myrepo", Item = item };

        var result = new WorkspaceCreatorResult
        {
            WorktreePath = @"C:\repos\feature",
            SessionName = "feature-session",
            GitHubLink = link,
        };

        Assert.True(result.GitHubLink.HasValue);
        Assert.Equal("myorg", result.GitHubLink.Value.Owner);
        Assert.Equal("myrepo", result.GitHubLink.Value.Repo);
        Assert.Equal("pr", result.GitHubLink.Value.Item.Type);
        Assert.Equal(42, result.GitHubLink.Value.Item.Number);
        Assert.Equal("alice", result.GitHubLink.Value.Item.Author);
        Assert.Equal("feature/add-thing", result.GitHubLink.Value.Item.HeadBranch);
        Assert.Equal(@"C:\repos\feature", result.WorktreePath);
        Assert.Equal("feature-session", result.SessionName);
    }

    [Fact]
    public void WorkspaceCreatorResult_DefaultStruct_WorktreePathNullAndGitHubLinkAbsent()
    {
        var result = default(WorkspaceCreatorResult);

        Assert.Null(result.WorktreePath);
        Assert.Null(result.SessionName);
        Assert.False(result.GitHubLink.HasValue);
    }

    // ─── Tier 2 / Option B: Compile-contract pin via reflection ────────────
    // Asserts that ShowWorkspaceCreator still returns WorkspaceCreatorResult?
    // so that any future signature change fails this test immediately.

    [Fact]
    public void ShowWorkspaceCreator_ReturnType_IsNullableWorkspaceCreatorResult()
    {
        var method = typeof(WorkspaceCreatorVisuals).GetMethod(
            "ShowWorkspaceCreator",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.NotNull(method);

        var expectedReturnType = typeof(WorkspaceCreatorResult?);
        Assert.Equal(expectedReturnType, method!.ReturnType);
    }
}
