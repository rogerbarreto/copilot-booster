using System.Diagnostics;

namespace CopilotBooster.Services;

/// <summary>
/// Opens GitHub URLs in the session's Edge workspace or the OS default browser.
/// </summary>
internal static class GitHubLinkService
{
    /// <summary>
    /// Opens a GitHub URL. If <paramref name="useEdgeSession"/> is true and an Edge workspace
    /// is active for the session, opens in that workspace. Otherwise opens in the OS default browser.
    /// </summary>
    internal static void OpenUrl(string url, string? sessionId, bool useEdgeSession, ActiveStatusTracker? tracker)
    {
        // Try opening in the session's Edge workspace if enabled and available
        if (useEdgeSession && sessionId != null && tracker != null
            && tracker.TryGetEdge(sessionId, out var ws) && ws.IsOpen)
        {
            var edgePath = EdgeWorkspaceService.FindEdgePath();
            if (edgePath != null)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = edgePath,
                        Arguments = $"\"{url}\"",
                        UseShellExecute = false
                    });
                    return;
                }
                catch { }
            }
        }

        // Fallback: OS default browser
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
