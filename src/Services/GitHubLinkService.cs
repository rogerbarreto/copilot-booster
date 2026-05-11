using System;
using System.Diagnostics;

using CopilotBooster.Models;

namespace CopilotBooster.Services;

internal enum GitHubRefType
{
    Issue,
    Pr,
}

internal readonly record struct GitHubRef(string Owner, string Repo, int Number, GitHubRefType Type);

/// <summary>
/// Opens GitHub URLs in the OS default browser and provides URL builder helpers.
/// </summary>
internal static class GitHubLinkService
{
    /// <summary>
    /// Opens a GitHub URL in the OS default browser.
    /// </summary>
    internal static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>
    /// Builds a GitHub PR URL.
    /// </summary>
    internal static string GetPrUrl(string owner, string repo, int number) =>
        $"https://github.com/{owner}/{repo}/pull/{number}";

    /// <summary>
    /// Builds a GitHub Issue URL.
    /// </summary>
    internal static string GetIssueUrl(string owner, string repo, int number) =>
        $"https://github.com/{owner}/{repo}/issues/{number}";

    /// <summary>
    /// Builds a GitHub PR or Issue URL from a tracked item, dispatching on <see cref="GitHubTrackedItem.IsPr"/>.
    /// </summary>
    internal static string GetItemUrl(string owner, string repo, GitHubTrackedItem item) =>
        item.IsPr
            ? GetPrUrl(owner, repo, item.Number)
            : GetIssueUrl(owner, repo, item.Number);

    internal static bool TryParseIssueOrPrUrl(string input, out GitHubRef result)
    {
        result = default;

        var trimmed = input.Trim();
        if (string.IsNullOrEmpty(trimmed)
            || !trimmed.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase)
                ? $"https://{trimmed}"
                : null;

        if (normalized == null
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length != 4
            || !int.TryParse(segments[3], out var number)
            || number <= 0)
        {
            return false;
        }

        var type = segments[2] switch
        {
            "issues" => GitHubRefType.Issue,
            "pull" => GitHubRefType.Pr,
            _ => (GitHubRefType?)null,
        };

        if (type == null)
        {
            return false;
        }

        result = new GitHubRef(segments[0], segments[1], number, type.Value);
        return true;
    }

    /// <summary>
    /// Builds a GitHub Actions run URL.
    /// </summary>
    internal static string GetRunUrl(string owner, string repo, long runId) =>
        $"https://github.com/{owner}/{repo}/actions/runs/{runId}";

    /// <summary>
    /// Builds a GitHub Actions job URL.
    /// </summary>
    internal static string GetJobUrl(string owner, string repo, long runId, long jobId) =>
        $"https://github.com/{owner}/{repo}/actions/runs/{runId}/job/{jobId}";
}
