namespace CopilotBooster.IntegrationTests.Integration.TestTools;

internal sealed class GitTestRepo : IDisposable
{
    private int _changeNumber;

    private GitTestRepo(string rootPath, string sourcePath, string remotePath, string localPath, string workspacesPath)
    {
        this.RootPath = rootPath;
        this.SourcePath = sourcePath;
        this.RemotePath = remotePath;
        this.LocalPath = localPath;
        this.WorkspacesPath = workspacesPath;
    }

    internal string RootPath { get; }

    internal string SourcePath { get; }

    internal string RemotePath { get; }

    internal string LocalPath { get; }

    internal string WorkspacesPath { get; }

    internal static async Task<GitTestRepo> CreateAsync(CancellationToken cancellationToken = default)
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"copilot-booster-git-{Guid.NewGuid():N}");
        var sourcePath = Path.Combine(rootPath, "source");
        var remotePath = Path.Combine(rootPath, "remote.git");
        var localPath = Path.Combine(rootPath, "local");
        var workspacesPath = Path.Combine(rootPath, "workspaces");
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(sourcePath);

        var repo = new GitTestRepo(rootPath, sourcePath, remotePath, localPath, workspacesPath);
        await RunGitAsync(sourcePath, "init -b main", cancellationToken).ConfigureAwait(false);
        await ConfigureUserAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "README.md"), "# Test Repo", cancellationToken).ConfigureAwait(false);
        await RunGitAsync(sourcePath, "add .", cancellationToken).ConfigureAwait(false);
        await RunGitAsync(sourcePath, "commit -m init", cancellationToken).ConfigureAwait(false);
        await RunGitAsync(rootPath, $"clone --bare \"{sourcePath}\" \"{remotePath}\"", cancellationToken).ConfigureAwait(false);
        await RunGitAsync(sourcePath, $"remote add origin \"{remotePath}\"", cancellationToken).ConfigureAwait(false);
        await RunGitAsync(sourcePath, "push -u origin main", cancellationToken).ConfigureAwait(false);
        await RunGitAsync(rootPath, $"clone \"{remotePath}\" \"{localPath}\"", cancellationToken).ConfigureAwait(false);
        await ConfigureUserAsync(localPath, cancellationToken).ConfigureAwait(false);

        return repo;
    }

    internal async Task CreateRemoteBranchAsync(string branchName, CancellationToken cancellationToken = default)
    {
        await RunGitAsync(this.SourcePath, $"checkout -b {branchName}", cancellationToken).ConfigureAwait(false);
        await this.WriteUniqueFileAsync(this.SourcePath, branchName, cancellationToken).ConfigureAwait(false);
        await RunGitAsync(this.SourcePath, "add .", cancellationToken).ConfigureAwait(false);
        await RunGitAsync(this.SourcePath, $"commit -m init-{branchName}", cancellationToken).ConfigureAwait(false);
        await RunGitAsync(this.SourcePath, $"push -u origin {branchName}", cancellationToken).ConfigureAwait(false);
        await RunGitAsync(this.SourcePath, "checkout main", cancellationToken).ConfigureAwait(false);

        await RunGitAsync(this.LocalPath, $"fetch origin {branchName}", cancellationToken).ConfigureAwait(false);
        await RunGitAsync(this.LocalPath, $"checkout -b {branchName} origin/{branchName}", cancellationToken).ConfigureAwait(false);
        await RunGitAsync(this.LocalPath, "checkout main", cancellationToken).ConfigureAwait(false);
    }

    internal async Task<string> CommitAndPushAsync(string branchName, CancellationToken cancellationToken = default)
    {
        await RunGitAsync(this.SourcePath, $"checkout {branchName}", cancellationToken).ConfigureAwait(false);
        await this.WriteUniqueFileAsync(this.SourcePath, branchName, cancellationToken).ConfigureAwait(false);
        await RunGitAsync(this.SourcePath, "add .", cancellationToken).ConfigureAwait(false);
        await RunGitAsync(this.SourcePath, $"commit -m update-{branchName}", cancellationToken).ConfigureAwait(false);
        await RunGitAsync(this.SourcePath, $"push origin {branchName}", cancellationToken).ConfigureAwait(false);
        var tip = await RevParseAsync(this.SourcePath, branchName, cancellationToken).ConfigureAwait(false);
        await RunGitAsync(this.SourcePath, "checkout main", cancellationToken).ConfigureAwait(false);
        return tip;
    }

    internal async Task<string> CreateLocalBranchWithoutUpstreamAsync(string branchName, CancellationToken cancellationToken = default)
    {
        await RunGitAsync(this.LocalPath, $"checkout -b {branchName}", cancellationToken).ConfigureAwait(false);
        await this.WriteUniqueFileAsync(this.LocalPath, branchName, cancellationToken).ConfigureAwait(false);
        await RunGitAsync(this.LocalPath, "add .", cancellationToken).ConfigureAwait(false);
        await RunGitAsync(this.LocalPath, $"commit -m init-{branchName}", cancellationToken).ConfigureAwait(false);
        var tip = await RevParseAsync(this.LocalPath, branchName, cancellationToken).ConfigureAwait(false);
        await RunGitAsync(this.LocalPath, "checkout main", cancellationToken).ConfigureAwait(false);
        return tip;
    }

    internal Task CheckoutAsync(string branchName, CancellationToken cancellationToken = default)
    {
        return RunGitAsync(this.LocalPath, $"checkout {branchName}", cancellationToken);
    }

    internal Task SetOriginUrlAsync(string remoteUrl, CancellationToken cancellationToken = default)
    {
        return RunGitAsync(this.LocalPath, $"remote set-url origin \"{remoteUrl}\"", cancellationToken);
    }

    internal static async Task<string> RevParseAsync(string repoPath, string refName, CancellationToken cancellationToken = default)
    {
        var result = await GitService.RunGitAsync(repoPath, $"rev-parse {refName}", cancellationToken).ConfigureAwait(false);
        Assert.Equal(0, result.exitCode);
        return result.stdout.Trim();
    }

    internal static async Task<string> HeadAsync(string repoPath, CancellationToken cancellationToken = default)
    {
        return await RevParseAsync(repoPath, "HEAD", cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(this.RootPath))
            {
                foreach (var file in Directory.EnumerateFiles(this.RootPath, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(this.RootPath, true);
            }
        }
        catch
        {
        }
    }

    private static async Task ConfigureUserAsync(string repoPath, CancellationToken cancellationToken)
    {
        await RunGitAsync(repoPath, "config user.email tester@example.invalid", cancellationToken).ConfigureAwait(false);
        await RunGitAsync(repoPath, "config user.name \"Test User\"", cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteUniqueFileAsync(string repoPath, string branchName, CancellationToken cancellationToken)
    {
        this._changeNumber++;
        var fileName = $"change-{Sanitize(branchName)}-{this._changeNumber:D3}.txt";
        await File.WriteAllTextAsync(Path.Combine(repoPath, fileName), Guid.NewGuid().ToString("N"), cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunGitAsync(string workingDirectory, string arguments, CancellationToken cancellationToken)
    {
        var result = await GitService.RunGitAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);
        Assert.True(result.exitCode == 0, $"git {arguments} failed in {workingDirectory}: {result.stderr}");
    }

    private static string Sanitize(string branchName)
    {
        return branchName.Replace('/', '-').Replace('\\', '-');
    }
}
