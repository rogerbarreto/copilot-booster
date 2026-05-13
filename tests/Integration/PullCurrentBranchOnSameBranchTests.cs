#pragma warning disable IDE1006

using CopilotBooster.IntegrationTests.Integration.TestTools;

namespace CopilotBooster.IntegrationTests.Integration;

[Collection(GitIntegrationWorkspaceSettingsCollection.CollectionName)]
public sealed class PullCurrentBranchOnSameBranchTests
{
    [Fact]
    public async Task PullCurrentBranchAsync_RealRepoWithRemote_AdvancesWorkingTreeHeadToRemoteTip()
    {
        using var repo = await GitTestRepo.CreateAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        var remoteTip = await repo.CommitAndPushAsync("main", TestContext.Current.CancellationToken).ConfigureAwait(false);

        var (success, error) = await WorkspaceCreationService.PullCurrentBranchAsync(repo.LocalPath, TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.True(success, error);
        Assert.Null(error);
        Assert.Equal(remoteTip, await GitTestRepo.HeadAsync(repo.LocalPath, TestContext.Current.CancellationToken).ConfigureAwait(false));
    }

    [Fact]
    public async Task PullCurrentBranchAsync_DirtyTrackedFile_PullFailsAndHeadUnchanged()
    {
        using var repo = await GitTestRepo.CreateAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        var localTip = await GitTestRepo.HeadAsync(repo.LocalPath, TestContext.Current.CancellationToken).ConfigureAwait(false);
        _ = await CommitReadmeAndPushAsync(repo.SourcePath, "main", TestContext.Current.CancellationToken).ConfigureAwait(false);
        await File.AppendAllTextAsync(Path.Combine(repo.LocalPath, "README.md"), Environment.NewLine + "local dirty change", TestContext.Current.CancellationToken).ConfigureAwait(false);

        var (success, error) = await WorkspaceCreationService.PullCurrentBranchAsync(repo.LocalPath, TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.False(success);
        Assert.NotNull(error);
        Assert.NotEmpty(error);
        Assert.Contains("overwritten", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(localTip, await GitTestRepo.HeadAsync(repo.LocalPath, TestContext.Current.CancellationToken).ConfigureAwait(false));
    }

    [Fact]
    public async Task PullCurrentBranchAsync_NoUpstream_ReturnsSuccessFallbackFetch()
    {
        using var repo = await GitTestRepo.CreateAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        var localTip = await repo.CreateLocalBranchWithoutUpstreamAsync("local-only", TestContext.Current.CancellationToken).ConfigureAwait(false);
        await repo.CheckoutAsync("local-only", TestContext.Current.CancellationToken).ConfigureAwait(false);
        var remoteTip = await repo.CommitAndPushAsync("main", TestContext.Current.CancellationToken).ConfigureAwait(false);

        var (success, error) = await WorkspaceCreationService.PullCurrentBranchAsync(repo.LocalPath, TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.True(success, error);
        Assert.Null(error);
        Assert.Equal(localTip, await GitTestRepo.HeadAsync(repo.LocalPath, TestContext.Current.CancellationToken).ConfigureAwait(false));
        Assert.Equal(remoteTip, await GitTestRepo.RevParseAsync(repo.LocalPath, "origin/main", TestContext.Current.CancellationToken).ConfigureAwait(false));
    }

    private static async Task<string> CommitReadmeAndPushAsync(string repoPath, string branchName, CancellationToken cancellationToken)
    {
        await RunGitAsync(repoPath, $"checkout {branchName}", cancellationToken).ConfigureAwait(false);
        await File.AppendAllTextAsync(Path.Combine(repoPath, "README.md"), Environment.NewLine + Guid.NewGuid().ToString("N"), cancellationToken).ConfigureAwait(false);
        await RunGitAsync(repoPath, "add README.md", cancellationToken).ConfigureAwait(false);
        await RunGitAsync(repoPath, $"commit -m update-readme-{branchName}", cancellationToken).ConfigureAwait(false);
        await RunGitAsync(repoPath, $"push origin {branchName}", cancellationToken).ConfigureAwait(false);
        var tip = await GitTestRepo.RevParseAsync(repoPath, branchName, cancellationToken).ConfigureAwait(false);
        await RunGitAsync(repoPath, "checkout main", cancellationToken).ConfigureAwait(false);
        return tip;
    }

    private static async Task RunGitAsync(string repoPath, string arguments, CancellationToken cancellationToken)
    {
        var result = await GitService.RunGitAsync(repoPath, arguments, cancellationToken).ConfigureAwait(false);
        Assert.True(result.exitCode == 0, $"git {arguments} failed in {repoPath}: {result.stderr}");
    }
}
