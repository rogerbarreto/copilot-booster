using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Provides Git-related operations such as branch listing, worktree creation, and repository detection.
/// </summary>
internal static class GitService
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
            return new List<string>();
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
        combined = Regex.Replace(combined, @"[^a-zA-Z0-9\-_.]", "-");
        combined = Regex.Replace(combined, @"-{2,}", "-");
        combined = combined.Trim('-');
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
    /// Runs a Git command in the specified repository directory.
    /// </summary>
    /// <param name="repoPath">The working directory for the Git process.</param>
    /// <param name="arguments">The arguments to pass to the <c>git</c> executable.</param>
    /// <returns>A tuple containing the exit code, standard output, and standard error.</returns>
    private static (int exitCode, string stdout, string stderr) RunGit(string repoPath, string arguments)
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

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(10_000))
            {
                try { process.Kill(); } catch (Exception ex) { Program.Logger.LogDebug("Failed to kill git process: {Error}", ex.Message); }
                return (-1, "", "Git command timed out.");
            }

            return (process.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }
}
