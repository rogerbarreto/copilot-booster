using System;
using System.Collections.Generic;
using System.IO;

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

        var (success, errorMsg) = GitService.CheckoutExistingBranchWorktree(repoPath, worktreePath, uniqueBranchName, sourceRef);
        return success
            ? (worktreePath, true, null)
            : (worktreePath, false, errorMsg);
    }

    /// <summary>
    /// Resolves a unique local branch name by appending an incrementing suffix if needed.
    /// Checks both existing branches and active worktrees.
    /// </summary>
    internal static string ResolveUniqueBranchName(string repoPath, string baseName)
    {
        var branches = GitService.GetBranches(repoPath);
        var worktrees = GitService.GetWorktrees(repoPath);
        var remotes = GitService.GetRemotes(repoPath);

        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in branches)
        {
            existingNames.Add(GitService.GetLocalBranchName(b, remotes));
        }

        foreach (var (_, branch) in worktrees)
        {
            existingNames.Add(branch);
        }

        if (!existingNames.Contains(baseName))
        {
            return baseName;
        }

        for (int i = 1; i <= 999; i++)
        {
            var candidate = $"{baseName}-{i:D3}";
            if (!existingNames.Contains(candidate))
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
