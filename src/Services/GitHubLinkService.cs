using System.Diagnostics;

namespace CopilotBooster.Services;

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
