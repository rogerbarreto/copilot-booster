using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        var branches = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimStart('*').Trim();

            if (string.IsNullOrEmpty(trimmed) || trimmed.Contains("->"))
            {
                continue;
            }

            if (trimmed.StartsWith("remotes/origin/"))
            {
                trimmed = trimmed["remotes/origin/".Length..];
            }

            if (!string.IsNullOrEmpty(trimmed))
            {
                branches.Add(trimmed);
            }
        }

        return branches.ToList();
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
    /// Combines a repository folder name and branch name into a safe directory name.
    /// </summary>
    /// <param name="repoName">The repository folder name.</param>
    /// <param name="branchName">The branch name.</param>
    /// <returns>A sanitized string suitable for use as a directory name.</returns>
    internal static string SanitizeWorkspaceDirName(string repoName, string branchName)
    {
        var combined = $"{repoName}-{branchName}";
        combined = combined.Replace('/', '-').Replace('\\', '-');
        combined = Regex.Replace(combined, @"[^a-zA-Z0-9\-_.]", "");
        return combined;
    }

    /// <summary>
    /// Gets the root directory for CopilotBooster workspaces.
    /// </summary>
    /// <returns>The full path to the workspaces directory under the user's application data folder.</returns>
    internal static string GetWorkspacesDir()
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
