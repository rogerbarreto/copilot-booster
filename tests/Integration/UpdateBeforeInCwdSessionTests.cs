using CopilotBooster.IntegrationTests.Integration.TestTools;

namespace CopilotBooster.IntegrationTests.Integration;

[Collection(GitIntegrationWorkspaceSettingsCollection.CollectionName)]
public sealed class UpdateBeforeInCwdSessionTests
{
    [Fact]
    public async Task UpdateThenCheckout_LocalBehindRemote_WorkingTreeAtRemoteTipAsync()
    {
        using var repo = await GitTestRepo.CreateAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        await repo.CreateRemoteBranchAsync("feature", TestContext.Current.CancellationToken).ConfigureAwait(false);
        var remoteTip = await repo.CommitAndPushAsync("feature", TestContext.Current.CancellationToken).ConfigureAwait(false);

        var update = await WorkspaceCreationService.UpdateSourceBranchAsync(repo.LocalPath, "feature", TestContext.Current.CancellationToken).ConfigureAwait(false);
        var checkout = GitService.CheckoutBranch(repo.LocalPath, update.effectiveSourceRef);

        Assert.True(update.success, update.error);
        Assert.Null(update.error);
        Assert.Equal("feature", update.effectiveSourceRef);
        Assert.True(checkout.success, checkout.error);
        Assert.Equal(remoteTip, await GitTestRepo.HeadAsync(repo.LocalPath, TestContext.Current.CancellationToken).ConfigureAwait(false));
    }
}
