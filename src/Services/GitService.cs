using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

internal enum GitHubRepoResolution
{
    Resolved,
    NotAGitRepo,
    NoRemote,
    NonGitHubRemote
}

internal readonly record struct GitHubRepoResult(GitHubRepoResolution Status, string? Owner = null, string? Repo = null);

internal enum FastForwardResult
{
    Ok,
    BranchCheckedOutElsewhere,
    NonFastForward,
    NetworkError,
    OtherError
}

/// <summary>
/// Provides Git-related operations such as branch listing, worktree creation, and repository detection.
/// </summary>
internal static partial class GitService
{
    /// <summary>
    /// Returns <c>true</c> if the given path is inside a Git repository.
    /// </summary>
    /// <param name="path">The file-system path to check.</param>
    /// <returns><c>true</c> when a Git root is found; otherwise, <c>false</c>.</returns>
    internal static bool IsGitRepository(string path)
    {
        return SessionService.FindGitRoot(path) != null;
    }

    /// <summary>
    /// Gets all local and remote branch names for the repository at <paramref name="repoPath"/>.
    /// </summary>
    /// <param name="repoPath">The root directory of the Git repository.</param>
    /// <returns>A deduplicated list of branch names, or an empty list on failure.</returns>
    internal static List<string> GetBranches(string repoPath)
    {
        var (exitCode, stdout, _) = RunGit(repoPath, "branch -a --no-color");
        if (exitCode != 0)
        {
            return [];
        }

        var localBranches = new List<string>();
        var remoteBranches = new List<string>();
        var seenLocal = new HashSet<string>(StringComparer.Ordinal);
        var seenRemote = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimStart('*').Trim();

            if (string.IsNullOrEmpty(trimmed) || trimmed.Contains("->"))
            {
                continue;
            }

            if (trimmed.StartsWith("remotes/"))
            {
                // Keep as "origin/branch" for display
                var remoteRef = trimmed["remotes/".Length..];
                if (!string.IsNullOrEmpty(remoteRef) && seenRemote.Add(remoteRef))
                {
                    remoteBranches.Add(remoteRef);
                }
            }
            else if (!string.IsNullOrEmpty(trimmed) && seenLocal.Add(trimmed))
            {
                localBranches.Add(trimmed);
            }
        }

        // Local branches first, then remote-only branches (skip remotes that duplicate a local)
        var result = new List<string>(localBranches);
        foreach (var remote in remoteBranches)
        {
            // Remote refs are "origin/branch" — strip remote prefix for dedup against local
            var slashIdx = remote.IndexOf('/');
            var localName = slashIdx >= 0 ? remote[(slashIdx + 1)..] : remote;
            if (!seenLocal.Contains(localName))
            {
                result.Add(remote);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the current branch name for the repository at <paramref name="repoPath"/>.
    /// </summary>
    /// <param name="repoPath">The root directory of the Git repository.</param>
    /// <returns>The current branch name, or <c>"main"</c> on failure.</returns>
    internal static string GetCurrentBranch(string repoPath)
    {
        var (exitCode, stdout, _) = RunGit(repoPath, "rev-parse --abbrev-ref HEAD");
        if (exitCode != 0)
        {
            return "main";
        }

        var branch = stdout.Trim();
        return string.IsNullOrEmpty(branch) ? "main" : branch;
    }

    /// <summary>
    /// Creates a new Git worktree with a new branch based on <paramref name="baseBranch"/>.
    /// </summary>
    /// <param name="repoPath">The root directory of the Git repository.</param>
    /// <param name="worktreePath">The file-system path for the new worktree.</param>
    /// <param name="branchName">The name of the new branch to create.</param>
    /// <param name="baseBranch">The branch to base the new branch on.</param>
    /// <returns>A tuple indicating success and, on failure, the error message.</returns>
    internal static (bool success, string error) CreateWorktree(string repoPath, string worktreePath, string branchName, string baseBranch)
    {
        var (exitCode, _, stderr) = RunGit(repoPath, $"worktree add -b {branchName} {worktreePath} {baseBranch}");
        return exitCode == 0 ? (true, "") : (false, stderr.Trim());
    }

    /// <summary>
    /// Asynchronous version of <see cref="CreateWorktree"/> with cancellation support.
    /// Waits for the process to complete naturally — no hard timeout.
    /// </summary>
    internal static async Task<(bool success, string error)> CreateWorktreeAsync(
        string repoPath, string worktreePath, string branchName, string baseBranch, CancellationToken cancellationToken = default)
    {
        var (exitCode, _, stderr) = await RunGitAsync(repoPath, $"worktree add -b {branchName} {worktreePath} {baseBranch}", cancellationToken).ConfigureAwait(false);
        return exitCode == 0 ? (true, "") : (false, stderr.Trim());
    }

    /// <summary>
    /// Creates a new Git worktree with a local branch tracking the specified ref.
    /// </summary>
    /// <param name="repoPath">The root directory of the Git repository.</param>
    /// <param name="worktreePath">The file-system path for the new worktree.</param>
    /// <param name="localBranchName">The name for the new local branch.</param>
    /// <param name="sourceRef">The source ref to branch from (e.g., "main", "origin/feature").</param>
    /// <returns>A tuple indicating success and, on failure, the error message.</returns>
    internal static (bool success, string error) CheckoutExistingBranchWorktree(string repoPath, string worktreePath, string localBranchName, string sourceRef)
    {
        var (exitCode, _, stderr) = RunGit(repoPath, $"worktree add -b {localBranchName} \"{worktreePath}\" {sourceRef}");
        return exitCode == 0 ? (true, "") : (false, stderr.Trim());
    }

    /// <summary>
    /// Asynchronous version of <see cref="CheckoutExistingBranchWorktree"/> with cancellation support.
    /// Waits for the process to complete naturally — no hard timeout.
    /// </summary>
    internal static async Task<(bool success, string error)> CheckoutExistingBranchWorktreeAsync(
        string repoPath, string worktreePath, string localBranchName, string sourceRef, CancellationToken cancellationToken = default)
    {
        var (exitCode, _, stderr) = await RunGitAsync(repoPath, $"worktree add -b {localBranchName} \"{worktreePath}\" {sourceRef}", cancellationToken).ConfigureAwait(false);
        return exitCode == 0 ? (true, "") : (false, stderr.Trim());
    }

    /// <summary>
    /// Creates a worktree by checking out an existing local branch.
    /// Use when the local branch already exists and is not in another worktree.
    /// </summary>
    internal static (bool success, string error) CheckoutLocalBranchWorktree(string repoPath, string worktreePath, string localBranchName)
    {
        var (exitCode, _, stderr) = RunGit(repoPath, $"worktree add \"{worktreePath}\" {localBranchName}");
        return exitCode == 0 ? (true, "") : (false, stderr.Trim());
    }

    /// <summary>
    /// Asynchronous version of <see cref="CheckoutLocalBranchWorktree"/> with cancellation support.
    /// Waits for the process to complete naturally — no hard timeout.
    /// </summary>
    internal static async Task<(bool success, string error)> CheckoutLocalBranchWorktreeAsync(
        string repoPath, string worktreePath, string localBranchName, CancellationToken cancellationToken = default)
    {
        var (exitCode, _, stderr) = await RunGitAsync(repoPath, $"worktree add \"{worktreePath}\" {localBranchName}", cancellationToken).ConfigureAwait(false);
        return exitCode == 0 ? (true, "") : (false, stderr.Trim());
    }

    /// <summary>
    /// Checks whether a local branch exists in the repository.
    /// </summary>
    internal static bool LocalBranchExists(string repoPath, string branchName)
    {
        var (exitCode, _, _) = RunGit(repoPath, $"show-ref --verify --quiet refs/heads/{branchName}");
        return exitCode == 0;
    }

    /// <summary>
    /// Checks out an existing branch in the repository (no worktree).
    /// </summary>
    internal static (bool success, string error) CheckoutBranch(string repoPath, string branchName)
    {
        var (exitCode, _, stderr) = RunGit(repoPath, $"checkout {branchName}");
        return exitCode == 0 ? (true, "") : (false, stderr.Trim());
    }

    /// <summary>
    /// Creates and checks out a new branch from a base ref (no worktree).
    /// </summary>
    internal static (bool success, string error) CheckoutNewBranch(string repoPath, string branchName, string baseBranch)
    {
        var (exitCode, _, stderr) = RunGit(repoPath, $"checkout -b {branchName} {baseBranch}");
        return exitCode == 0 ? (true, "") : (false, stderr.Trim());
    }

    /// <summary>
    /// Fetches a PR ref and checks out a local branch from it (no worktree).
    /// </summary>
    internal static (bool success, string error) FetchAndCheckoutPr(string repoPath, string remote, HostingPlatform platform, int prNumber, string localBranchName)
    {
        var (fetchSuccess, fetchError) = FetchPrRef(repoPath, remote, platform, prNumber);
        if (!fetchSuccess)
        {
            return (false, fetchError);
        }

        // If branch already exists locally, just check it out; otherwise create from FETCH_HEAD.
        var (exitCode, _, stderr) = LocalBranchExists(repoPath, localBranchName)
            ? RunGit(repoPath, $"checkout {localBranchName}")
            : RunGit(repoPath, $"checkout -b {localBranchName} FETCH_HEAD");
        return exitCode == 0 ? (true, "") : (false, stderr.Trim());
    }

    /// <summary>
    /// Lists all active worktrees and their checked-out branches.
    /// </summary>
    /// <param name="repoPath">The root directory of the Git repository.</param>
    /// <returns>A list of tuples containing the worktree path and branch name.</returns>
    internal static List<(string path, string branch)> GetWorktrees(string repoPath)
    {
        var (exitCode, stdout, _) = RunGit(repoPath, "worktree list --porcelain");
        if (exitCode != 0)
        {
            return [];
        }

        return ParseWorktreeList(stdout);
    }

    /// <summary>
    /// Parses the porcelain output of <c>git worktree list</c> into path/branch tuples.
    /// </summary>
    internal static List<(string path, string branch)> ParseWorktreeList(string porcelainOutput)
    {
        var result = new List<(string path, string branch)>();
        string? currentPath = null;

        foreach (var line in porcelainOutput.Split('\n', StringSplitOptions.None))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("worktree "))
            {
                currentPath = trimmed["worktree ".Length..];
            }
            else if (trimmed.StartsWith("branch ") && currentPath != null)
            {
                var branch = trimmed["branch ".Length..];
                // Strip refs/heads/ prefix
                if (branch.StartsWith("refs/heads/"))
                {
                    branch = branch["refs/heads/".Length..];
                }

                result.Add((currentPath, branch));
            }
            else if (trimmed.Length == 0)
            {
                currentPath = null;
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the list of configured remote names for the repository.
    /// </summary>
    internal static List<string> GetRemotes(string repoPath)
    {
        var (exitCode, stdout, _) = RunGit(repoPath, "remote");
        if (exitCode != 0)
        {
            return ["origin"];
        }

        var remotes = new List<string>();
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                remotes.Add(trimmed);
            }
        }

        return remotes.Count > 0 ? remotes : ["origin"];
    }

    /// <summary>
    /// Extracts the local branch name from a ref, stripping any remote prefix.
    /// For example, "origin/feature/login" becomes "feature/login"; "main" stays "main";
    /// "feature/login" stays "feature/login" (no remote prefix).
    /// </summary>
    internal static string GetLocalBranchName(string refName)
    {
        return refName;
    }

    /// <summary>
    /// Extracts the local branch name from a ref, stripping the remote prefix if it matches a known remote.
    /// </summary>
    internal static string GetLocalBranchName(string refName, List<string> remotes)
    {
        var slashIndex = refName.IndexOf('/');
        if (slashIndex < 0)
        {
            return refName;
        }

        var prefix = refName[..slashIndex];
        if (remotes.Contains(prefix))
        {
            return refName[(slashIndex + 1)..];
        }

        return refName;
    }

    /// <summary>
    /// Returns <c>true</c> if the ref looks like a remote branch (e.g., "origin/main").
    /// </summary>
    internal static bool IsRemoteRef(string refName, List<string> remotes)
    {
        var slashIndex = refName.IndexOf('/');
        if (slashIndex < 0)
        {
            return false;
        }

        var prefix = refName[..slashIndex];
        return remotes.Contains(prefix);
    }

    /// <summary>
    /// Combines a repository folder name and branch name into a safe directory name.
    /// </summary>
    /// <param name="repoName">The repository folder name.</param>
    /// <param name="branchName">The branch name.</param>
    /// <returns>A sanitized string suitable for use as a directory name.</returns>
    internal static string SanitizeWorkspaceDirName(string repoName, string branchName)
    {
        var combined = $"{repoName}-{branchName}";
        combined = MyRegex().Replace(combined, "-");
        combined = ConsecutiveHyphensRegex().Replace(combined, "-");
        combined = combined.Trim('-');

        // Truncate branch portion to 3 words (segments separated by '-')
        var repoSanitized = MyRegex().Replace(repoName, "-");
        repoSanitized = ConsecutiveHyphensRegex().Replace(repoSanitized, "-").Trim('-');
        var prefix = repoSanitized + "-";
        if (combined.StartsWith(prefix) && combined.Length > prefix.Length)
        {
            var branchPart = combined[prefix.Length..];
            var segments = branchPart.Split('-');
            if (segments.Length > 3)
            {
                combined = repoSanitized + "-" + string.Join("-", segments[..3]);
            }
        }

        return combined;
    }

    /// <summary>
    /// Gets the root directory for CopilotBooster workspaces.
    /// </summary>
    /// <returns>The full path to the workspaces directory under the user's application data folder.</returns>
    internal static string GetWorkspacesDir()
    {
        var configured = Program._settings?.WorkspacesDir;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return GetDefaultWorkspacesDir();
    }

    /// <summary>
    /// Gets the default root directory for CopilotBooster workspaces.
    /// </summary>
    /// <returns>The full path to the default workspaces directory under the user's application data folder.</returns>
    internal static string GetDefaultWorkspacesDir()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CopilotBooster", "Workspaces");
    }

    /// <summary>
    /// Hosting platforms supported for PR ref fetching.
    /// </summary>
    internal enum HostingPlatform
    {
        Unknown,
        GitHub,
        GitLab,
        Bitbucket,
        AzureDevOps
    }

    /// <summary>
    /// Gets the URL for the specified remote.
    /// </summary>
    internal static string? GetRemoteUrl(string repoPath, string remoteName)
    {
        var (exitCode, stdout, _) = RunGit(repoPath, $"remote get-url {remoteName}");
        return exitCode == 0 ? stdout.Trim() : null;
    }

    internal static GitHubRepoResult ResolveGitHubRepo(string cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd))
        {
            return new GitHubRepoResult(GitHubRepoResolution.NotAGitRepo, null, null);
        }

        var (gitExitCode, _, _) = RunGitAtCwd(cwd, ["rev-parse", "--is-inside-work-tree"]);
        if (gitExitCode != 0)
        {
            return new GitHubRepoResult(GitHubRepoResolution.NotAGitRepo, null, null);
        }

        var upstream = TryResolveRemote(cwd, "upstream");
        if (upstream.HasValue)
        {
            return upstream.Value;
        }

        var origin = TryResolveRemote(cwd, "origin");
        if (origin.HasValue)
        {
            return origin.Value;
        }

        return new GitHubRepoResult(GitHubRepoResolution.NoRemote, null, null);
    }

    internal static (string Owner, string Repo)? TryResolveGitHubRepo(string cwd)
    {
        var result = ResolveGitHubRepo(cwd);
        return result.Status == GitHubRepoResolution.Resolved && result.Owner != null && result.Repo != null
            ? (result.Owner, result.Repo)
            : null;
    }

    /// <summary>
    /// Detects the hosting platform from a remote URL.
    /// </summary>
    internal static HostingPlatform DetectHostingPlatform(string remoteUrl)
    {
        var lower = remoteUrl.ToLowerInvariant();
        if (lower.Contains("github.com"))
        {
            return HostingPlatform.GitHub;
        }

        if (lower.Contains("gitlab.com") || lower.Contains("gitlab"))
        {
            return HostingPlatform.GitLab;
        }

        if (lower.Contains("bitbucket.org") || lower.Contains("bitbucket"))
        {
            return HostingPlatform.Bitbucket;
        }

        if (lower.Contains("dev.azure.com") || lower.Contains("visualstudio.com"))
        {
            return HostingPlatform.AzureDevOps;
        }

        return HostingPlatform.Unknown;
    }

    /// <summary>
    /// Returns the git ref pattern for a PR/MR number on the specified platform.
    /// </summary>
    internal static string? GetPrRefPattern(HostingPlatform platform, int prNumber)
    {
        return platform switch
        {
            HostingPlatform.GitHub => $"refs/pull/{prNumber}/head",
            HostingPlatform.AzureDevOps => $"refs/pull/{prNumber}/head",
            HostingPlatform.GitLab => $"refs/merge-requests/{prNumber}/head",
            HostingPlatform.Bitbucket => $"refs/pull-requests/{prNumber}/from",
            _ => null
        };
    }

    /// <summary>
    /// Validates that a PR ref exists on the remote using <c>git ls-remote</c>.
    /// </summary>
    /// <returns><c>true</c> if the ref exists on the remote.</returns>
    internal static bool ValidatePrRef(string repoPath, string remote, HostingPlatform platform, int prNumber)
    {
        var refPattern = GetPrRefPattern(platform, prNumber);
        if (refPattern is null)
        {
            return false;
        }

        var (exitCode, stdout, _) = RunGit(repoPath, $"ls-remote {remote} {refPattern}", timeoutMs: 30_000);
        return exitCode == 0 && !string.IsNullOrWhiteSpace(stdout);
    }

    internal static string? GetUpstreamRemote(string repoPath, string localBranch)
    {
        var (exitCode, stdout, _) = RunGit(repoPath, $"rev-parse --abbrev-ref \"{localBranch}@{{upstream}}\"");
        if (exitCode != 0)
        {
            return null;
        }

        var upstream = stdout.Trim();
        var slashIndex = upstream.IndexOf('/');
        if (slashIndex <= 0)
        {
            return null;
        }

        return upstream[..slashIndex];
    }

    internal static async Task<(bool success, string error)> FetchRemoteAsync(
        string repoPath, string remote, CancellationToken cancellationToken = default)
    {
        var (exitCode, _, stderr) = await RunGitAsync(repoPath, $"fetch {remote}", cancellationToken).ConfigureAwait(false);
        return exitCode == 0 ? (true, string.Empty) : (false, stderr.Trim());
    }

    internal static async Task<(FastForwardResult result, string error)> FetchAndFastForwardAsync(
        string repoPath, string remote, string localBranch, CancellationToken cancellationToken = default)
    {
        var (exitCode, _, stderr) = await RunGitAsync(repoPath, $"fetch {remote} {localBranch}:{localBranch}", cancellationToken).ConfigureAwait(false);
        var error = stderr.Trim();
        if (exitCode == 0)
        {
            return (FastForwardResult.Ok, string.Empty);
        }

        return (ClassifyFastForwardError(error), error);
    }

    /// <summary>
    /// Fetches a PR ref from the remote. Must be called before creating a worktree from FETCH_HEAD.
    /// </summary>
    /// <returns>A tuple indicating success and, on failure, the error message.</returns>
    internal static (bool success, string error) FetchPrRef(string repoPath, string remote, HostingPlatform platform, int prNumber)
    {
        var refPattern = GetPrRefPattern(platform, prNumber);
        if (refPattern is null)
        {
            return (false, "Unsupported hosting platform.");
        }

        var (exitCode, _, stderr) = RunGit(repoPath, $"fetch {remote} {refPattern}", timeoutMs: 60_000);
        return exitCode == 0 ? (true, "") : (false, stderr.Trim());
    }

    /// <summary>
    /// Asynchronous version of <see cref="FetchPrRef"/> with cancellation support.
    /// Waits for the fetch to complete naturally — no hard timeout (replaces the 60s sync timeout).
    /// </summary>
    internal static async Task<(bool success, string error)> FetchPrRefAsync(
        string repoPath, string remote, HostingPlatform platform, int prNumber, CancellationToken cancellationToken = default)
    {
        var refPattern = GetPrRefPattern(platform, prNumber);
        if (refPattern is null)
        {
            return (false, "Unsupported hosting platform.");
        }

        var (exitCode, _, stderr) = await RunGitAsync(repoPath, $"fetch {remote} {refPattern}", cancellationToken).ConfigureAwait(false);
        return exitCode == 0 ? (true, "") : (false, stderr.Trim());
    }

    /// <summary>
    /// Parses the owner and repo from a GitHub remote URL.
    /// Supports HTTPS and SSH formats.
    /// </summary>
    /// <returns>A tuple of (owner, repo), or <c>null</c> if parsing fails.</returns>
    internal static (string owner, string repo)? ParseGitHubOwnerRepo(string remoteUrl)
    {
        return TryParseGitHubRemote(remoteUrl) is { } repo ? (repo.Owner, repo.Repo) : null;
    }

    private static FastForwardResult ClassifyFastForwardError(string stderr)
    {
        if (stderr.Contains("refusing to fetch into branch", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("checked out at", StringComparison.OrdinalIgnoreCase))
        {
            return FastForwardResult.BranchCheckedOutElsewhere;
        }

        if (stderr.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("not fast-forward", StringComparison.OrdinalIgnoreCase))
        {
            return FastForwardResult.NonFastForward;
        }

        if (stderr.Contains("Could not resolve host", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("unable to access", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("Failed to connect", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("Connection", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("Network is unreachable", StringComparison.OrdinalIgnoreCase))
        {
            return FastForwardResult.NetworkError;
        }

        return FastForwardResult.OtherError;
    }

    private static GitHubRepoResult? TryResolveRemote(string cwd, string remoteName)
    {
        var (exitCode, stdout, _) = RunGitAtCwd(cwd, ["remote", "get-url", remoteName]);
        if (exitCode != 0)
        {
            return null;
        }

        var remoteUrl = stdout.Trim();
        if (TryParseGitHubRemote(remoteUrl) is not { } repo)
        {
            return new GitHubRepoResult(GitHubRepoResolution.NonGitHubRemote, null, null);
        }

        var parent = TryResolveForkParent(repo.Owner, repo.Repo);
        return new GitHubRepoResult(GitHubRepoResolution.Resolved, parent.Owner, parent.Repo);
    }

    private static (string Owner, string Repo)? TryParseGitHubRemote(string remoteUrl)
    {
        var url = remoteUrl.Trim().TrimEnd('/');
        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^4];
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("ssh", StringComparison.OrdinalIgnoreCase)
                ? ParseOwnerRepoPath(uri.AbsolutePath.Trim('/'))
                : null;
        }

        var match = ScpGitHubRemoteRegex().Match(url);
        if (!match.Success || !match.Groups["host"].Value.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return (match.Groups["owner"].Value, StripGitSuffix(match.Groups["repo"].Value));
    }

    private static (string Owner, string Repo)? ParseOwnerRepoPath(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        return (parts[0], StripGitSuffix(parts[1]));
    }

    private static string StripGitSuffix(string repo)
    {
        return repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? repo[..^4] : repo;
    }

    private static (string Owner, string Repo) TryResolveForkParent(string owner, string repo)
    {
        try
        {
            var ghPath = Environment.GetEnvironmentVariable("GH_PATH");
            if (string.IsNullOrWhiteSpace(ghPath))
            {
                ghPath = "gh";
            }

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = ghPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add("repo");
            process.StartInfo.ArgumentList.Add("view");
            process.StartInfo.ArgumentList.Add($"{owner}/{repo}");
            process.StartInfo.ArgumentList.Add("--json");
            process.StartInfo.ArgumentList.Add("parent");
            process.StartInfo.ArgumentList.Add("--jq");
            process.StartInfo.ArgumentList.Add(".parent.nameWithOwner");

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(5_000))
            {
                try { process.Kill(true); } catch (Exception ex) { Program.Logger.LogDebug("Failed to kill gh process: {Error}", ex.Message); }
                return (owner, repo);
            }

            var parent = stdoutTask.GetAwaiter().GetResult().Trim();
            _ = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(parent) || parent.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                return (owner, repo);
            }

            var parts = parent.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 2 ? (parts[0], parts[1]) : (owner, repo);
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("Failed to resolve GitHub fork parent for {Owner}/{Repo}: {Error}", owner, repo, ex.Message);
            return (owner, repo);
        }
    }

    private static (int exitCode, string stdout, string stderr) RunGitAtCwd(string cwd, IReadOnlyList<string> args, int timeoutMs = 10_000)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add("-C");
            process.StartInfo.ArgumentList.Add(cwd);
            foreach (var arg in args)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(true); } catch (Exception ex) { Program.Logger.LogDebug("Failed to kill git process: {Error}", ex.Message); }
                return (-1, "", "Git command timed out.");
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();

            return (process.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    /// <summary>
    /// Runs a Git command in the specified repository directory.
    /// </summary>
    /// <param name="repoPath">The working directory for the Git process.</param>
    /// <param name="arguments">The arguments to pass to the <c>git</c> executable.</param>
    /// <param name="timeoutMs">Maximum time in milliseconds to wait for the process to exit.</param>
    /// <returns>A tuple containing the exit code, standard output, and standard error.</returns>
    private static (int exitCode, string stdout, string stderr) RunGit(string repoPath, string arguments, int timeoutMs = 10_000)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();

            // Read both streams asynchronously to prevent deadlock when
            // a git command (e.g. fetch) fills the stderr buffer with progress output.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(true); } catch (Exception ex) { Program.Logger.LogDebug("Failed to kill git process: {Error}", ex.Message); }
                return (-1, "", "Git command timed out.");
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();

            return (process.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    /// <summary>
    /// Asynchronous version of <see cref="RunGit"/> with cancellation support.
    /// Waits for the process to complete naturally — no hard timeout.
    /// On cancellation, kills the entire process tree.
    /// </summary>
    /// <param name="repoPath">The working directory for the Git process.</param>
    /// <param name="arguments">The arguments to pass to the <c>git</c> executable.</param>
    /// <param name="cancellationToken">Token to cancel the operation and kill the process.</param>
    /// <returns>A tuple containing the exit code, standard output, and standard error.</returns>
    internal static async Task<(int exitCode, string stdout, string stderr)> RunGitAsync(
        string repoPath, string arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();

            // Register cancellation callback to kill the process tree BEFORE awaiting,
            // so cancellation is handled even if the process is blocked.
            using var registration = cancellationToken.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (Exception ex) { Program.Logger.LogDebug("Failed to kill git process on cancellation: {Error}", ex.Message); }
            });

            // Read both streams concurrently to prevent deadlock when
            // a git command (e.g. fetch) fills the stderr buffer with progress output.
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            return (process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    [GeneratedRegex(@"[^a-zA-Z0-9\-_.]")]
    private static partial Regex MyRegex();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex ConsecutiveHyphensRegex();

    [GeneratedRegex("^git@(?<host>[^:]+):(?<owner>[^/]+)/(?<repo>[^/]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ScpGitHubRemoteRegex();
}
