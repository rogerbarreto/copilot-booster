namespace CopilotBooster.Tests.Services;

/// <summary>
/// Unit tests for GitHubApiService star-related APIs (HasGhCli, IsAuthenticated,
/// IsRepoStarredAsync, StarRepoAsync) using injectable process runner.
/// </summary>
public sealed class GitHubApiServiceStarTests
{
    private static Task<(int ExitCode, string Stdout, string Stderr)> FakeProcess(
        Dictionary<string, (int ExitCode, string Stdout, string Stderr)> responses,
        string command, string? args)
    {
        var key = args != null ? $"{command} {args}" : command;
        if (responses.TryGetValue(key, out var response))
        {
            return Task.FromResult(response);
        }

        return Task.FromResult((-1, "", $"Unknown command: {key}"));
    }

    [Fact]
    public void HasGhCli_ReturnsTrue_WhenGhInstalled()
    {
        var responses = new Dictionary<string, (int, string, string)>
        {
            ["gh --version"] = (0, "gh version 2.50.0", "")
        };
        var api = new GitHubApiService(processRunner: (cmd, args) => FakeProcess(responses, cmd, args));

        Assert.True(api.HasGhCli);
    }

    [Fact]
    public void HasGhCli_ReturnsFalse_WhenGhNotInstalled()
    {
        var responses = new Dictionary<string, (int, string, string)>();
        var api = new GitHubApiService(processRunner: (cmd, args) => FakeProcess(responses, cmd, args));

        Assert.False(api.HasGhCli);
    }

    [Fact]
    public void HasGhCli_CachesResult()
    {
        int callCount = 0;
        var api = new GitHubApiService(processRunner: (cmd, args) =>
        {
            if (args == "--version")
            {
                callCount++;
            }

            return Task.FromResult((0, "gh version 2.50.0", ""));
        });

        _ = api.HasGhCli;
        _ = api.HasGhCli;
        _ = api.HasGhCli;

        Assert.Equal(1, callCount);
    }

    [Fact]
    public void IsAuthenticated_WithGhCli_RunsGhAuthStatus()
    {
        var responses = new Dictionary<string, (int, string, string)>
        {
            ["gh --version"] = (0, "gh version 2.50.0", ""),
            ["gh auth status"] = (0, "Logged in", "")
        };
        var api = new GitHubApiService(processRunner: (cmd, args) => FakeProcess(responses, cmd, args));

        Assert.True(api.IsAuthenticated);
    }

    [Fact]
    public void IsAuthenticated_WithGhCli_ReturnsFalse_WhenNotLoggedIn()
    {
        var responses = new Dictionary<string, (int, string, string)>
        {
            ["gh --version"] = (0, "gh version 2.50.0", ""),
            ["gh auth status"] = (1, "", "not logged in")
        };
        var api = new GitHubApiService(processRunner: (cmd, args) => FakeProcess(responses, cmd, args));

        Assert.False(api.IsAuthenticated);
    }

    [Fact]
    public void IsAuthenticated_WithoutGhCli_ChecksPat()
    {
        var responses = new Dictionary<string, (int, string, string)>();
        var api = new GitHubApiService(
            getPatFromSettings: () => "ghp_test_token",
            processRunner: (cmd, args) => FakeProcess(responses, cmd, args));

        Assert.True(api.IsAuthenticated);
    }

    [Fact]
    public void IsAuthenticated_WithoutGhCli_NoPat_ReturnsFalse()
    {
        var responses = new Dictionary<string, (int, string, string)>();
        var api = new GitHubApiService(
            getPatFromSettings: () => null,
            processRunner: (cmd, args) => FakeProcess(responses, cmd, args));

        Assert.False(api.IsAuthenticated);
    }

    [Fact]
    public void IsAuthenticated_CachesResult()
    {
        int authCallCount = 0;
        var api = new GitHubApiService(processRunner: (cmd, args) =>
        {
            if (args == "auth status")
            {
                authCallCount++;
            }

            return Task.FromResult((0, "ok", ""));
        });

        _ = api.IsAuthenticated;
        _ = api.IsAuthenticated;

        Assert.Equal(1, authCallCount);
    }

    [Fact]
    public async Task IsRepoStarred_WithGhCli_ReturnsTrue_OnExitCode0Async()
    {
        var responses = new Dictionary<string, (int, string, string)>
        {
            ["gh --version"] = (0, "gh version 2.50.0", ""),
            ["gh api user/starred/owner/repo"] = (0, "", "")
        };
        var api = new GitHubApiService(processRunner: (cmd, args) => FakeProcess(responses, cmd, args));

        Assert.True(await api.IsRepoStarredAsync("owner", "repo").ConfigureAwait(false));
    }

    [Fact]
    public async Task IsRepoStarred_WithGhCli_ReturnsFalse_OnNonZeroExitAsync()
    {
        var responses = new Dictionary<string, (int, string, string)>
        {
            ["gh --version"] = (0, "gh version 2.50.0", ""),
            ["gh api user/starred/owner/repo"] = (1, "", "404")
        };
        var api = new GitHubApiService(processRunner: (cmd, args) => FakeProcess(responses, cmd, args));

        Assert.False(await api.IsRepoStarredAsync("owner", "repo").ConfigureAwait(false));
    }

    [Fact]
    public async Task IsRepoStarred_NoAuth_ReturnsFalseAsync()
    {
        var responses = new Dictionary<string, (int, string, string)>();
        var api = new GitHubApiService(
            getPatFromSettings: () => null,
            processRunner: (cmd, args) => FakeProcess(responses, cmd, args));

        Assert.False(await api.IsRepoStarredAsync("owner", "repo").ConfigureAwait(false));
    }

    [Fact]
    public async Task StarRepo_WithGhCli_ReturnsTrue_OnSuccessAsync()
    {
        var responses = new Dictionary<string, (int, string, string)>
        {
            ["gh --version"] = (0, "gh version 2.50.0", ""),
            ["gh api -X PUT user/starred/owner/repo"] = (0, "", "")
        };
        var api = new GitHubApiService(processRunner: (cmd, args) => FakeProcess(responses, cmd, args));

        Assert.True(await api.StarRepoAsync("owner", "repo").ConfigureAwait(false));
    }

    [Fact]
    public async Task StarRepo_NoAuth_ReturnsFalseAsync()
    {
        var responses = new Dictionary<string, (int, string, string)>();
        var api = new GitHubApiService(
            getPatFromSettings: () => null,
            processRunner: (cmd, args) => FakeProcess(responses, cmd, args));

        Assert.False(await api.StarRepoAsync("owner", "repo").ConfigureAwait(false));
    }
}
