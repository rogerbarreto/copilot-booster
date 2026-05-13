using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CopilotBooster.Services;

/// <summary>
/// Encapsulates workspace creation business logic including name sanitization,
/// path construction, and git worktree operations.
/// </summary>
internal static class WorkspaceCreationService
{
    /// <summary>
    /// Sanitizes a workspace name into a safe directory name by combining the repository
    /// folder name with the branch/workspace name.
    /// </summary>
    /// <param name="repoFolderName">The repository folder name.</param>
    /// <param name="workspaceName">The workspace or branch name.</param>
    /// <returns>A sanitized string suitable for use as a directory name.</returns>
    internal static string SanitizeWorkspaceName(string repoFolderName, string workspaceName)
    {
        return GitService.SanitizeWorkspaceDirName(repoFolderName, workspaceName);
    }

    /// <summary>
    /// Builds the full workspace directory path from a workspace name.
    /// </summary>
    /// <param name="repoFolderName">The repository folder name.</param>
    /// <param name="workspaceName">The workspace or branch name.</param>
    /// <returns>The full path where the workspace directory will be created.</returns>
    internal static string BuildWorkspacePath(string repoFolderName, string workspaceName)
    {
        var dirName = SanitizeWorkspaceName(repoFolderName, workspaceName);
        return Path.Combine(GitService.GetWorkspacesDir(), dirName);
    }

    /// <summary>
    /// Creates a new workspace by ensuring the workspaces directory exists and
    /// creating a git worktree with a new branch.
    /// </summary>
    /// <param name="repoPath">The git repository root path.</param>
    /// <param name="repoFolderName">The repository folder name.</param>
    /// <param name="workspaceName">The name for the new workspace (becomes the branch name).</param>
    /// <param name="baseBranch">The branch to base the new workspace on.</param>
    /// <returns>A tuple containing the worktree path, success flag, and optional error message.</returns>
    internal static (string path, bool success, string? error) CreateWorkspace(
        string repoPath, string repoFolderName, string workspaceName, string baseBranch)
    {
        var worktreePath = BuildWorkspacePath(repoFolderName, workspaceName);

        Directory.CreateDirectory(GitService.GetWorkspacesDir());

        var (success, errorMsg) = GitService.CreateWorktree(repoPath, worktreePath, workspaceName, baseBranch);
        return success
            ? (worktreePath, true, null)
            : (worktreePath, false, errorMsg);
    }

    /// <summary>
    /// Asynchronous version of <see cref="CreateWorkspace"/> with cancellation support.
    /// Waits for the git worktree operation to complete naturally — no hard timeout.
    /// </summary>
    internal static async Task<(string path, bool success, string? error)> CreateWorkspaceAsync(
        string repoPath, string repoFolderName, string workspaceName, string baseBranch, CancellationToken cancellationToken = default)
    {
        var worktreePath = BuildWorkspacePath(repoFolderName, workspaceName);

        Directory.CreateDirectory(GitService.GetWorkspacesDir());

        var (success, errorMsg) = await GitService.CreateWorktreeAsync(repoPath, worktreePath, workspaceName, baseBranch, cancellationToken).ConfigureAwait(false);
        return success
            ? (worktreePath, true, null)
            : (worktreePath, false, errorMsg);
    }

    /// <summary>
    /// Creates a new workspace with a local branch tracking the specified ref.
    /// If a local branch with the same name already exists, appends an incrementing suffix (001, 002, etc.).
    /// </summary>
    /// <param name="repoPath">The git repository root path.</param>
    /// <param name="repoFolderName">The repository folder name.</param>
    /// <param name="sourceRef">The source ref to branch from (e.g., "main", "origin/feature").</param>
    /// <returns>A tuple containing the worktree path, success flag, and optional error message.</returns>
    internal static (string path, bool success, string? error) CreateWorkspaceFromExistingBranch(
        string repoPath, string repoFolderName, string sourceRef)
    {
        var remotes = GitService.GetRemotes(repoPath);
        var localBranchName = GitService.GetLocalBranchName(sourceRef, remotes);
        var uniqueBranchName = ResolveUniqueBranchName(repoPath, localBranchName);
        var worktreePath = BuildWorkspacePath(repoFolderName, uniqueBranchName);

        Directory.CreateDirectory(GitService.GetWorkspacesDir());

        // If the local branch already exists, check it out directly; otherwise create it from the source ref.
        var (success, errorMsg) = GitService.LocalBranchExists(repoPath, uniqueBranchName)
            ? GitService.CheckoutLocalBranchWorktree(repoPath, worktreePath, uniqueBranchName)
            : GitService.CheckoutExistingBranchWorktree(repoPath, worktreePath, uniqueBranchName, sourceRef);
        return success
            ? (worktreePath, true, null)
            : (worktreePath, false, errorMsg);
    }

    /// <summary>
    /// Asynchronous version of <see cref="CreateWorkspaceFromExistingBranch"/> with cancellation support.
    /// Waits for the git worktree operation to complete naturally — no hard timeout.
    /// Sync helper calls (GetRemotes, LocalBranchExists, GetLocalBranchName, ResolveUniqueBranchName) remain synchronous.
    /// </summary>
    internal static async Task<(string path, bool success, string? error)> CreateWorkspaceFromExistingBranchAsync(
        string repoPath, string repoFolderName, string sourceRef, CancellationToken cancellationToken = default)
    {
        var remotes = GitService.GetRemotes(repoPath);
        var localBranchName = GitService.GetLocalBranchName(sourceRef, remotes);
        var uniqueBranchName = ResolveUniqueBranchName(repoPath, localBranchName);
        var worktreePath = BuildWorkspacePath(repoFolderName, uniqueBranchName);

        Directory.CreateDirectory(GitService.GetWorkspacesDir());

        // If the local branch already exists, check it out directly; otherwise create it from the source ref.
        var (success, errorMsg) = GitService.LocalBranchExists(repoPath, uniqueBranchName)
            ? await GitService.CheckoutLocalBranchWorktreeAsync(repoPath, worktreePath, uniqueBranchName, cancellationToken).ConfigureAwait(false)
            : await GitService.CheckoutExistingBranchWorktreeAsync(repoPath, worktreePath, uniqueBranchName, sourceRef, cancellationToken).ConfigureAwait(false);
        return success
            ? (worktreePath, true, null)
            : (worktreePath, false, errorMsg);
    }

    /// <summary>
    /// Creates a new workspace from a pull request number by fetching the PR ref and creating a worktree.
    /// </summary>
    internal static (string path, bool success, string? error) CreateWorkspaceFromPr(
        string repoPath, string repoFolderName, string remote, int prNumber, GitService.HostingPlatform platform, string? headBranch = null)
    {
        var baseBranchName = headBranch ?? $"pr-{prNumber}";
        var uniqueBranchName = ResolveUniqueBranchName(repoPath, baseBranchName);
        var worktreePath = BuildWorkspacePath(repoFolderName, uniqueBranchName);

        Directory.CreateDirectory(GitService.GetWorkspacesDir());

        var (fetchSuccess, fetchError) = GitService.FetchPrRef(repoPath, remote, platform, prNumber);
        if (!fetchSuccess)
        {
            return (worktreePath, false, $"Failed to fetch PR #{prNumber}: {fetchError}");
        }

        // If the local branch already exists, check it out directly; otherwise create it from FETCH_HEAD.
        var (success, errorMsg) = GitService.LocalBranchExists(repoPath, uniqueBranchName)
            ? GitService.CheckoutLocalBranchWorktree(repoPath, worktreePath, uniqueBranchName)
            : GitService.CheckoutExistingBranchWorktree(repoPath, worktreePath, uniqueBranchName, "FETCH_HEAD");
        return success
            ? (worktreePath, true, null)
            : (worktreePath, false, errorMsg);
    }

    /// <summary>
    /// Asynchronous version of <see cref="CreateWorkspaceFromPr"/> with cancellation support.
    /// Waits for the git fetch and worktree operations to complete naturally — no hard timeout.
    /// Sync helper calls (LocalBranchExists, ResolveUniqueBranchName) remain synchronous.
    /// </summary>
    internal static async Task<(string path, bool success, string? error)> CreateWorkspaceFromPrAsync(
        string repoPath, string repoFolderName, string remote, int prNumber, GitService.HostingPlatform platform, string? headBranch = null, CancellationToken cancellationToken = default)
    {
        var baseBranchName = headBranch ?? $"pr-{prNumber}";
        var uniqueBranchName = ResolveUniqueBranchName(repoPath, baseBranchName);
        var worktreePath = BuildWorkspacePath(repoFolderName, uniqueBranchName);

        Directory.CreateDirectory(GitService.GetWorkspacesDir());

        var (fetchSuccess, fetchError) = await GitService.FetchPrRefAsync(repoPath, remote, platform, prNumber, cancellationToken).ConfigureAwait(false);
        if (!fetchSuccess)
        {
            return (worktreePath, false, $"Failed to fetch PR #{prNumber}: {fetchError}");
        }

        // If the local branch already exists, check it out directly; otherwise create it from FETCH_HEAD.
        var (success, errorMsg) = GitService.LocalBranchExists(repoPath, uniqueBranchName)
            ? await GitService.CheckoutLocalBranchWorktreeAsync(repoPath, worktreePath, uniqueBranchName, cancellationToken).ConfigureAwait(false)
            : await GitService.CheckoutExistingBranchWorktreeAsync(repoPath, worktreePath, uniqueBranchName, "FETCH_HEAD", cancellationToken).ConfigureAwait(false);
        return success
            ? (worktreePath, true, null)
            : (worktreePath, false, errorMsg);
    }

    /// <summary>
    /// Pulls the current branch using fast-forward-only semantics when it has an upstream.
    /// If the current branch has no upstream, fetches the first configured remote, or <c>origin</c> when no remotes are configured.
    /// </summary>
    /// <returns><c>(true, null)</c> when the pull or fallback fetch succeeds; otherwise <c>(false, error)</c> with git stderr.</returns>
    internal static async Task<(bool success, string? error)> PullCurrentBranchAsync(
        string repoPath, CancellationToken cancellationToken = default)
    {
        var currentBranch = GitService.GetCurrentBranch(repoPath);
        var upstreamRemote = GitService.GetUpstreamRemote(repoPath, currentBranch);
        if (string.IsNullOrWhiteSpace(upstreamRemote))
        {
            var remotes = GitService.GetRemotes(repoPath);
            var fallbackRemote = remotes.Count > 0 ? remotes[0] : "origin";
            var (success, error) = await GitService.FetchRemoteAsync(repoPath, fallbackRemote, cancellationToken).ConfigureAwait(false);
            return success ? (true, null) : (false, error);
        }

        return await GitService.PullFastForwardOnlyAsync(repoPath, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<(bool success, string? error, string effectiveSourceRef)> UpdateSourceBranchAsync(
        string repoPath, string sourceRef, CancellationToken cancellationToken = default)
    {
        var remotes = GitService.GetRemotes(repoPath);
        if (GitService.IsRemoteRef(sourceRef, remotes))
        {
            var remote = sourceRef[..sourceRef.IndexOf('/')];
            var (success, error) = await GitService.FetchRemoteAsync(repoPath, remote, cancellationToken).ConfigureAwait(false);
            return success ? (true, null, sourceRef) : (false, error, sourceRef);
        }

        var localBranch = GitService.GetLocalBranchName(sourceRef, remotes);
        var upstreamRemote = GitService.GetUpstreamRemote(repoPath, localBranch);
        if (string.IsNullOrWhiteSpace(upstreamRemote))
        {
            var fallbackRemote = remotes.Count > 0 ? remotes[0] : "origin";
            var (success, error) = await GitService.FetchRemoteAsync(repoPath, fallbackRemote, cancellationToken).ConfigureAwait(false);
            return success ? (true, null, sourceRef) : (false, error, sourceRef);
        }

        var (result, fastForwardError) = await GitService.FetchAndFastForwardAsync(repoPath, upstreamRemote, localBranch, cancellationToken).ConfigureAwait(false);
        if (result == FastForwardResult.Ok)
        {
            return (true, null, sourceRef);
        }

        if (result is FastForwardResult.BranchCheckedOutElsewhere or FastForwardResult.NonFastForward)
        {
            var (fetchSuccess, fetchError) = await GitService.FetchRemoteAsync(repoPath, upstreamRemote, cancellationToken).ConfigureAwait(false);
            return fetchSuccess ? (true, null, $"{upstreamRemote}/{localBranch}") : (false, fetchError, sourceRef);
        }

        return (false, fastForwardError, sourceRef);
    }

    /// <summary>
    /// Resolves a unique local branch name by appending an incrementing suffix if needed.
    /// Only conflicts with branches that are currently checked out in a worktree,
    /// so the original branch name is preserved for push.default compatibility.
    /// </summary>
    internal static string ResolveUniqueBranchName(string repoPath, string baseName)
    {
        var worktrees = GitService.GetWorktrees(repoPath);

        var worktreeBranches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, branch) in worktrees)
        {
            worktreeBranches.Add(branch);
        }

        if (!worktreeBranches.Contains(baseName))
        {
            return baseName;
        }

        for (int i = 1; i <= 999; i++)
        {
            var candidate = $"{baseName}-{i:D3}";
            if (!worktreeBranches.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseName}-{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Checks whether a branch is already checked out in an existing worktree.
    /// </summary>
    /// <param name="repoPath">The git repository root path.</param>
    /// <param name="sourceRef">The ref to check (e.g., "main", "origin/feature"). Compares against local branch names.</param>
    /// <returns>The worktree path if the branch is in use, or <c>null</c> if available.</returns>
    internal static string? IsBranchInWorktree(string repoPath, string sourceRef)
    {
        var remotes = GitService.GetRemotes(repoPath);
        var localBranchName = GitService.GetLocalBranchName(sourceRef, remotes);
        var worktrees = GitService.GetWorktrees(repoPath);
        foreach (var (path, branch) in worktrees)
        {
            if (string.Equals(branch, localBranchName, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets all local and remote branch names for the specified repository.
    /// </summary>
    /// <param name="repoPath">The root directory of the Git repository.</param>
    /// <returns>A list of branch names.</returns>
    internal static List<string> GetBranches(string repoPath)
    {
        return GitService.GetBranches(repoPath);
    }

    /// <summary>
    /// Gets the current branch name for the specified repository.
    /// </summary>
    /// <param name="repoPath">The root directory of the Git repository.</param>
    /// <returns>The current branch name.</returns>
    internal static string GetCurrentBranch(string repoPath)
    {
        return GitService.GetCurrentBranch(repoPath);
    }
}
