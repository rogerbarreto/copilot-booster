using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace CopilotBooster.Services;

/// <summary>
/// Searches for files matching IDE file patterns using git ls-files (primary)
/// or recursive directory walk (fallback for non-git repos).
/// </summary>
internal static class IdeFileSearchService
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(1);
    private const int MaxResults = 5;

    /// <summary>
    /// Searches for files matching the given patterns in the specified directory.
    /// Returns relative paths sorted by depth (shallowest first).
    /// </summary>
    internal static List<string> Search(string directory, string filePattern, IReadOnlyList<string> ignoredDirs)
    {
        if (string.IsNullOrWhiteSpace(filePattern) || string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        var patterns = filePattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (patterns.Length == 0)
        {
            return [];
        }

        // Try git ls-files first (respects .gitignore)
        var results = TryGitSearch(directory, patterns);
        if (results != null)
        {
            return results;
        }

        // Fallback: directory walk with ignored dirs
        return WalkSearch(directory, patterns, ignoredDirs);
    }

    private static List<string>? TryGitSearch(string directory, string[] patterns)
    {
        try
        {
            var args = "ls-files " + string.Join(" ", patterns.Select(p => $"\"{p}\""));
            using var cts = new CancellationTokenSource(s_timeout);
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return null;
            }

            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit((int)s_timeout.TotalMilliseconds) || proc.ExitCode != 0)
            {
                return null;
            }

            var files = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(f => !string.IsNullOrEmpty(f))
                .OrderBy(f => f.Count(c => c == '/' || c == '\\'))
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Take(MaxResults)
                .Select(f => f.Replace('/', '\\'))
                .ToList();

            return files;
        }
        catch
        {
            return null;
        }
    }

    private static List<string> WalkSearch(string directory, string[] patterns, IReadOnlyList<string> ignoredDirs)
    {
        var ignoredSet = new HashSet<string>(ignoredDirs, StringComparer.OrdinalIgnoreCase);
        var results = new List<string>();
        var sw = Stopwatch.StartNew();

        try
        {
            WalkDirectory(directory, directory, patterns, ignoredSet, results, sw);
        }
        catch (OperationCanceledException) { }

        results.Sort((a, b) =>
        {
            int depthA = a.Count(c => c == '\\');
            int depthB = b.Count(c => c == '\\');
            int cmp = depthA.CompareTo(depthB);
            return cmp != 0 ? cmp : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        });

        return results.Take(MaxResults).ToList();
    }

    private static void WalkDirectory(
        string root, string current, string[] patterns,
        HashSet<string> ignoredSet, List<string> results, Stopwatch sw)
    {
        if (sw.Elapsed > s_timeout || results.Count >= MaxResults)
        {
            return;
        }

        try
        {
            foreach (var pattern in patterns)
            {
                foreach (var file in Directory.EnumerateFiles(current, pattern))
                {
                    results.Add(Path.GetRelativePath(root, file));
                    if (results.Count >= MaxResults)
                    {
                        return;
                    }
                }
            }

            foreach (var dir in Directory.EnumerateDirectories(current))
            {
                if (sw.Elapsed > s_timeout || results.Count >= MaxResults)
                {
                    return;
                }

                var dirName = Path.GetFileName(dir);
                if (!ignoredSet.Contains(dirName))
                {
                    WalkDirectory(root, dir, patterns, ignoredSet, results, sw);
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
    }
}
