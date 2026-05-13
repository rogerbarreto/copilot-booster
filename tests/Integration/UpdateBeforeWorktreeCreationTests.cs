using CopilotBooster.IntegrationTests.Integration.TestTools;

namespace CopilotBooster.IntegrationTests.Integration;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class GitIntegrationWorkspaceSettingsCollection
{
    public const string CollectionName = "Git integration workspace settings";
}

[Collection(GitIntegrationWorkspaceSettingsCollection.CollectionName)]
public sealed class UpdateBeforeWorktreeCreationTests : IDisposable
{
    private readonly LauncherSettings _previousSettings;

    public UpdateBeforeWorktreeCreationTests()
    {
        this._previousSettings = Program._settings ?? LauncherSettings.CreateDefault();
    }

    public void Dispose()
    {
        Program._settings = this._previousSettings;
    }

    [Fact]
    public async Task UpdateThenCreate_LocalBehindRemote_WorktreeAtRemoteTip()
    {
        using var repo = await GitTestRepo.CreateAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        ConfigureWorkspaces(repo);
        await repo.CreateRemoteBranchAsync("feature", TestContext.Current.CancellationToken).ConfigureAwait(false);
        var remoteTip = await repo.CommitAndPushAsync("feature", TestContext.Current.CancellationToken).ConfigureAwait(false);

        var update = await WorkspaceCreationService.UpdateSourceBranchAsync(repo.LocalPath, "feature", TestContext.Current.CancellationToken).ConfigureAwait(false);
        var created = await WorkspaceCreationService.CreateWorkspaceFromExistingBranchAsync(repo.LocalPath, "repo", update.effectiveSourceRef, TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.True(update.success, update.error);
        Assert.Null(update.error);
        Assert.Equal("feature", update.effectiveSourceRef);
        Assert.Equal(remoteTip, await GitTestRepo.RevParseAsync(repo.LocalPath, "feature", TestContext.Current.CancellationToken).ConfigureAwait(false));
        Assert.True(created.success, created.error);
        Assert.Equal(remoteTip, await GitTestRepo.HeadAsync(created.path, TestContext.Current.CancellationToken).ConfigureAwait(false));
    }

    [Fact]
    public async Task UpdateThenCreate_BranchCheckedOutInMainRepo_FallsBackToRemoteRef_WorktreeStillFresh()
    {
        using var repo = await GitTestRepo.CreateAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        ConfigureWorkspaces(repo);
        await repo.CreateRemoteBranchAsync("feature", TestContext.Current.CancellationToken).ConfigureAwait(false);
        var localTip = await GitTestRepo.RevParseAsync(repo.LocalPath, "feature", TestContext.Current.CancellationToken).ConfigureAwait(false);
        await repo.CheckoutAsync("feature", TestContext.Current.CancellationToken).ConfigureAwait(false);
        var remoteTip = await repo.CommitAndPushAsync("feature", TestContext.Current.CancellationToken).ConfigureAwait(false);

        var update = await WorkspaceCreationService.UpdateSourceBranchAsync(repo.LocalPath, "feature", TestContext.Current.CancellationToken).ConfigureAwait(false);
        var created = await WorkspaceCreationService.CreateWorkspaceFromExistingBranchAsync(repo.LocalPath, "repo", update.effectiveSourceRef, TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.True(update.success, update.error);
        Assert.Null(update.error);
        Assert.Equal("origin/feature", update.effectiveSourceRef);
        Assert.Equal(localTip, await GitTestRepo.RevParseAsync(repo.LocalPath, "feature", TestContext.Current.CancellationToken).ConfigureAwait(false));
        Assert.True(created.success, created.error);
        Assert.Equal(remoteTip, await GitTestRepo.HeadAsync(created.path, TestContext.Current.CancellationToken).ConfigureAwait(false));
    }

    [Fact]
    public async Task UpdateThenCreate_NoUpstream_LocalBranch_WorktreeAtLocalTip()
    {
        using var repo = await GitTestRepo.CreateAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        ConfigureWorkspaces(repo);
        var localTip = await repo.CreateLocalBranchWithoutUpstreamAsync("local-only", TestContext.Current.CancellationToken).ConfigureAwait(false);

        var update = await WorkspaceCreationService.UpdateSourceBranchAsync(repo.LocalPath, "local-only", TestContext.Current.CancellationToken).ConfigureAwait(false);
        var created = await WorkspaceCreationService.CreateWorkspaceFromExistingBranchAsync(repo.LocalPath, "repo", update.effectiveSourceRef, TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.True(update.success, update.error);
        Assert.Null(update.error);
        Assert.Equal("local-only", update.effectiveSourceRef);
        Assert.Equal(localTip, await GitTestRepo.RevParseAsync(repo.LocalPath, "local-only", TestContext.Current.CancellationToken).ConfigureAwait(false));
        Assert.True(created.success, created.error);
        Assert.Equal(localTip, await GitTestRepo.HeadAsync(created.path, TestContext.Current.CancellationToken).ConfigureAwait(false));
    }

    [Fact]
    public async Task UpdateThenCreate_NetworkFailure_BogusRemote_ReturnsError()
    {
        using var repo = await GitTestRepo.CreateAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        await repo.SetOriginUrlAsync("https://localhost:1/copilot-booster/nonexistent.git", TestContext.Current.CancellationToken).ConfigureAwait(false);

        var update = await WorkspaceCreationService.UpdateSourceBranchAsync(repo.LocalPath, "origin/main", TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.False(update.success);
        Assert.NotNull(update.error);
        Assert.NotEmpty(update.error);
        Assert.Equal("origin/main", update.effectiveSourceRef);
    }

    private static void ConfigureWorkspaces(GitTestRepo repo)
    {
        Program._settings = new LauncherSettings
        {
            WorkspacesDir = repo.WorkspacesPath,
            SuppressSave = true
        };
    }
}
