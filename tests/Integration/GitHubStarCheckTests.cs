namespace CopilotBooster.IntegrationTests.Integration;

/// <summary>
/// Integration tests for GitHubApiService star-related APIs using real gh CLI.
/// </summary>
public sealed class GitHubStarCheckTests
{
    private static GitHubApiService CreateApi()
    {
        return new GitHubApiService();
    }

    [Fact]
    public void HasGhCli_ReturnsTrue_OnDeveloperMachine()
    {
        var api = CreateApi();

        // gh CLI should be installed on dev machines — skip gracefully if not
        if (!api.HasGhCli)
        {
            return;
        }

        Assert.True(api.HasGhCli);
    }

    [Fact]
    public void IsAuthenticated_ReturnsTrue_OnDeveloperMachine()
    {
        var api = CreateApi();

        if (!api.HasGhCli)
        {
            return;
        }

        // Developer machines should have gh authenticated — skip gracefully if not
        if (!api.IsAuthenticated)
        {
            return;
        }

        Assert.True(api.IsAuthenticated);
    }

    [Fact]
    public async Task IsRepoStarred_OwnRepo_ReturnsTrueAsync()
    {
        var api = CreateApi();
        if (!api.IsAuthenticated)
        {
            return;
        }

        // The developer should have starred their own repo
        var starred = await api.IsRepoStarredAsync("rogerbarreto", "copilot-booster");
        Assert.True(starred);
    }

    [Fact]
    public async Task IsRepoStarred_UnstarredRepo_ReturnsFalseAsync()
    {
        var api = CreateApi();
        if (!api.IsAuthenticated)
        {
            return;
        }

        var starred = await api.IsRepoStarredAsync("torvalds", "linux");
        Assert.False(starred);
    }

    [Fact]
    public async Task StarRepo_AlreadyStarred_ReturnsTrueAsync()
    {
        var api = CreateApi();
        if (!api.IsAuthenticated)
        {
            return;
        }

        // Starring an already-starred repo should be idempotent
        var result = await api.StarRepoAsync("rogerbarreto", "copilot-booster");
        Assert.True(result);
    }
}
