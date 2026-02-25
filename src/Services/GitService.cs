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
    /// Creates a worktree by checking out an existing local branch.
    /// Use when the local branch already exists and is not in another worktree.
    /// </summary>
    internal static (bool success, string error) CheckoutLocalBranchWorktree(string repoPath, string worktreePath, string localBranchName)
    {
        var (exitCode, _, stderr) = RunGit(repoPath, $"worktree add \"{worktreePath}\" {localBranchName}");
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
    /// Parses the owner and repo from a GitHub remote URL.
    /// Supports HTTPS and SSH formats.
    /// </summary>
    /// <returns>A tuple of (owner, repo), or <c>null</c> if parsing fails.</returns>
    internal static (string owner, string repo)? ParseGitHubOwnerRepo(string remoteUrl)
    {
        var url = remoteUrl.Trim().TrimEnd('/');
        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^4];
        }

        if (url.Contains("github.com"))
        {
            var httpsIdx = url.IndexOf("github.com/", StringComparison.OrdinalIgnoreCase);
            if (httpsIdx >= 0)
            {
                var path = url[(httpsIdx + "github.com/".Length)..];
                var parts = path.Split('/');
                if (parts.Length >= 2)
                {
                    return (parts[0], parts[1]);
                }
            }

            var sshIdx = url.IndexOf("github.com:", StringComparison.OrdinalIgnoreCase);
            if (sshIdx >= 0)
            {
                var path = url[(sshIdx + "github.com:".Length)..];
                var parts = path.Split('/');
                if (parts.Length >= 2)
                {
                    return (parts[0], parts[1]);
                }
            }
        }

        return null;
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
}
