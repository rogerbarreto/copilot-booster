[CollectionDefinition("Git workspace settings", DisableParallelization = true)]
public sealed class GitWorkspaceSettingsCollection;
[Collection("Git workspace settings")]
public sealed class WorkspaceCreationServiceTests : IDisposable
{
    private readonly LauncherSettings _previousSettings;
    private readonly string _tempDir;

    public WorkspaceCreationServiceTests()
    {
        this._previousSettings = Program._settings ?? new LauncherSettings();
        this._tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this._tempDir);
        Program._settings = new LauncherSettings
        {
            WorkspacesDir = Path.Combine(this._tempDir, "workspaces")
        };
    }

    public void Dispose()
    {
        Program._settings = this._previousSettings;
        try { Directory.Delete(this._tempDir, true); } catch { }
    }

    [Theory]
    [InlineData("my-repo", "feature/login", "my-repo-feature-login")]
    [InlineData("repo", @"feature\branch", "repo-feature-branch")]
    [InlineData("repo", "feat @#branch name", "repo-feat-branch-name")]
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
        Assert.Contains("Workspaces", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateSourceBranchAsync_FetchesRemoteTrackingRefAsync()
    {
        var (sourcePath, repoPath) = this.CreateRemoteBackedRepo();
        var expected = CommitAndPush(sourcePath, "main");

        var (updated, error, effectiveSourceRef) = await WorkspaceCreationService.UpdateSourceBranchAsync(repoPath, "origin/main", TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.True(updated, error);
        Assert.Null(error);
        Assert.Equal("origin/main", effectiveSourceRef);
        Assert.Equal(expected, RunGitOutput(repoPath, "rev-parse origin/main"));
    }

    [Fact]
    public async Task UpdateSourceBranchAsync_FastForwardsLocalBranchWithUpstreamAsync()
    {
        var (sourcePath, repoPath) = this.CreateRemoteBackedRepo();
        CreateRemoteBranch(sourcePath, repoPath, "feature");
        var expected = CommitAndPush(sourcePath, "feature");

        var (updated, error, effectiveSourceRef) = await WorkspaceCreationService.UpdateSourceBranchAsync(repoPath, "feature", TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.True(updated, error);
        Assert.Null(error);
        Assert.Equal("feature", effectiveSourceRef);
        Assert.Equal(expected, RunGitOutput(repoPath, "rev-parse feature"));
    }

    [Fact]
    public async Task UpdateSourceBranchAsync_FetchesFallbackRemote_WhenLocalBranchHasNoUpstreamAsync()
    {
        var (_, repoPath) = this.CreateRemoteBackedRepo();
        RunGitCmd(repoPath, "checkout -b local-only");
        RunGitCmd(repoPath, "checkout main");

        var (updated, error, effectiveSourceRef) = await WorkspaceCreationService.UpdateSourceBranchAsync(repoPath, "local-only", TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.True(updated, error);
        Assert.Null(error);
        Assert.Equal("local-only", effectiveSourceRef);
    }

    [Fact]
    public async Task UpdateSourceBranchAsync_FallsBackToRemoteRef_WhenFastForwardBlockedAsync()
    {
        var (sourcePath, repoPath) = this.CreateRemoteBackedRepo();
        CreateRemoteBranch(sourcePath, repoPath, "feature");
        var worktreePath = Path.Combine(this._tempDir, "wt-" + Path.GetRandomFileName());
        RunGitCmd(repoPath, $"worktree add -q \"{worktreePath}\" feature");
        _ = CommitAndPush(sourcePath, "feature");

        var (updated, error, effectiveSourceRef) = await WorkspaceCreationService.UpdateSourceBranchAsync(repoPath, "feature", TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.True(updated, error);
        Assert.Null(error);
        Assert.Equal("origin/feature", effectiveSourceRef);
    }

#pragma warning disable IDE1006

    [Fact]
    public async Task PullCurrentBranchAsync_HappyPath_AdvancesLocalToRemoteTip()
    {
        var (sourcePath, repoPath) = this.CreateRemoteBackedRepo();
        var expected = CommitAndPush(sourcePath, "main");

        var (success, error) = await WorkspaceCreationService.PullCurrentBranchAsync(repoPath, TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.True(success, error);
        Assert.Null(error);
        Assert.Equal(expected, RunGitOutput(repoPath, "rev-parse HEAD"));
    }

    [Fact]
    public async Task PullCurrentBranchAsync_NoUpstream_FallsBackToFetchAndReturnsSuccess()
    {
        var (_, repoPath) = this.CreateRemoteBackedRepo();
        RunGitCmd(repoPath, "checkout -b local-only");
        var localTip = RunGitOutput(repoPath, "rev-parse HEAD");

        var (success, error) = await WorkspaceCreationService.PullCurrentBranchAsync(repoPath, TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.True(success, error);
        Assert.Null(error);
        Assert.Equal(localTip, RunGitOutput(repoPath, "rev-parse HEAD"));
    }

    [Fact]
    public async Task PullCurrentBranchAsync_DirtyWorkingTree_ReturnsFailureSurfacingGitError()
    {
        var (sourcePath, repoPath) = this.CreateRemoteBackedRepo();
        var localTip = RunGitOutput(repoPath, "rev-parse HEAD");
        CommitReadmeAndPush(sourcePath, "main");
        File.AppendAllText(Path.Combine(repoPath, "README.md"), Environment.NewLine + "local dirty change");

        var (success, error) = await WorkspaceCreationService.PullCurrentBranchAsync(repoPath, TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.False(success);
        Assert.NotNull(error);
        Assert.NotEmpty(error);
        Assert.Equal(localTip, RunGitOutput(repoPath, "rev-parse HEAD"));
    }

    [Fact]
    public async Task PullCurrentBranchAsync_AlreadyUpToDate_ReturnsSuccess()
    {
        var (_, repoPath) = this.CreateRemoteBackedRepo();
        var localTip = RunGitOutput(repoPath, "rev-parse HEAD");

        var (success, error) = await WorkspaceCreationService.PullCurrentBranchAsync(repoPath, TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.True(success, error);
        Assert.Null(error);
        Assert.Equal(localTip, RunGitOutput(repoPath, "rev-parse HEAD"));
    }

    [Fact]
    public async Task PullCurrentBranchAsync_NonFastForward_ReturnsFailureWithError()
    {
        var (sourcePath, repoPath) = this.CreateRemoteBackedRepo();
        CommitAndPush(sourcePath, "main");
        File.WriteAllText(Path.Combine(repoPath, "local-change.txt"), Guid.NewGuid().ToString("N"));
        RunGitCmd(repoPath, "add local-change.txt");
        RunGitCmd(repoPath, "commit -m local-change");
        var localTip = RunGitOutput(repoPath, "rev-parse HEAD");

        var (success, error) = await WorkspaceCreationService.PullCurrentBranchAsync(repoPath, TestContext.Current.CancellationToken).ConfigureAwait(false);

        Assert.False(success);
        Assert.NotNull(error);
        Assert.NotEmpty(error);
        Assert.Equal(localTip, RunGitOutput(repoPath, "rev-parse HEAD"));
    }

#pragma warning restore IDE1006

    private string InitGitRepo()
    {
        var repoPath = Path.Combine(this._tempDir, Path.GetRandomFileName());
        Directory.CreateDirectory(repoPath);

        RunGitCmd(repoPath, "init -b main");
        RunGitCmd(repoPath, "config user.email test@test.com");
        RunGitCmd(repoPath, "config user.name Test");
        File.WriteAllText(Path.Combine(repoPath, "README.md"), "# Test");
        RunGitCmd(repoPath, "add .");
        RunGitCmd(repoPath, "commit -m init");

        return repoPath;
    }

    private (string sourcePath, string repoPath) CreateRemoteBackedRepo()
    {
        var sourcePath = this.InitGitRepo();
        var remotePath = Path.Combine(this._tempDir, "remote-" + Path.GetRandomFileName() + ".git");
        var repoPath = Path.Combine(this._tempDir, "clone-" + Path.GetRandomFileName());

        RunGitCmd(this._tempDir, $"clone --bare \"{sourcePath}\" \"{remotePath}\"");
        RunGitCmd(sourcePath, $"remote add origin \"{remotePath}\"");
        RunGitCmd(sourcePath, "push -u origin main");
        RunGitCmd(this._tempDir, $"clone \"{remotePath}\" \"{repoPath}\"");
        RunGitCmd(repoPath, "config user.email test@test.com");
        RunGitCmd(repoPath, "config user.name Test");

        return (sourcePath, repoPath);
    }

    private static void CreateRemoteBranch(string sourcePath, string repoPath, string branchName)
    {
        RunGitCmd(sourcePath, $"checkout -b {branchName}");
        File.WriteAllText(Path.Combine(sourcePath, branchName + ".txt"), Guid.NewGuid().ToString("N"));
        RunGitCmd(sourcePath, "add .");
        RunGitCmd(sourcePath, $"commit -m init-{branchName}");
        RunGitCmd(sourcePath, $"push -u origin {branchName}");
        RunGitCmd(sourcePath, "checkout main");

        RunGitCmd(repoPath, $"fetch origin {branchName}");
        RunGitCmd(repoPath, $"checkout -b {branchName} origin/{branchName}");
        RunGitCmd(repoPath, "checkout main");
    }

    private static string CommitAndPush(string sourcePath, string branchName)
    {
        RunGitCmd(sourcePath, $"checkout {branchName}");
        File.WriteAllText(Path.Combine(sourcePath, "change-" + Guid.NewGuid().ToString("N") + ".txt"), Guid.NewGuid().ToString("N"));
        RunGitCmd(sourcePath, "add .");
        RunGitCmd(sourcePath, $"commit -m update-{branchName}");
        RunGitCmd(sourcePath, $"push origin {branchName}");
        var rev = RunGitOutput(sourcePath, $"rev-parse {branchName}");
        RunGitCmd(sourcePath, "checkout main");
        return rev;
    }

    private static string CommitReadmeAndPush(string sourcePath, string branchName)
    {
        RunGitCmd(sourcePath, $"checkout {branchName}");
        File.AppendAllText(Path.Combine(sourcePath, "README.md"), Environment.NewLine + Guid.NewGuid().ToString("N"));
        RunGitCmd(sourcePath, "add README.md");
        RunGitCmd(sourcePath, $"commit -m update-readme-{branchName}");
        RunGitCmd(sourcePath, $"push origin {branchName}");
        var rev = RunGitOutput(sourcePath, $"rev-parse {branchName}");
        RunGitCmd(sourcePath, "checkout main");
        return rev;
    }

    private static string RunGitOutput(string workDir, string args)
    {
        const int TimeoutMs = 10_000;
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
        using var proc = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git.");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        var exited = proc.WaitForExit(TimeoutMs);
        if (!exited)
        {
            proc.Kill(entireProcessTree: true);
        }

        Assert.True(exited, $"git {args} timed out after {TimeoutMs}ms");
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        Assert.True(proc.ExitCode == 0, $"git {args} failed: {stderr}");
        return stdout.Trim();
    }

    private static void RunGitCmd(string workDir, string args)
    {
        const int TimeoutMs = 10_000;
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
        using var proc = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git.");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        var exited = proc.WaitForExit(TimeoutMs);
        if (!exited)
        {
            proc.Kill(entireProcessTree: true);
        }

        Assert.True(exited, $"git {args} timed out after {TimeoutMs}ms");
        _ = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        Assert.True(proc.ExitCode == 0, $"git {args} failed: {stderr}");
    }
}
