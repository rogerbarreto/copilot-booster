using System;
using System.Diagnostics;
using System.Threading.Tasks;

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
        if (useEdgeSession && sessionId != null && tracker != null)
        {
            if (tracker.TryGetEdge(sessionId, out var ws) && ws.IsOpen)
            {
                // Focus the session's Edge workspace first so the new tab opens in it
                if (ws.CachedHwnd != IntPtr.Zero)
                {
                    WindowFocusService.TryFocusWindowHandle(ws.CachedHwnd);
                    WindowFocusService.WaitForForeground(ws.CachedHwnd, 500);
                }

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
            else
            {
                // No Edge workspace running — start one and navigate to the URL
                _ = Task.Run(async () =>
                {
                    if (await ws.OpenAsync().ConfigureAwait(false))
                    {
                        // Wait a moment for the workspace to fully load
                        await Task.Delay(1500).ConfigureAwait(false);

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
                            }
                            catch { }
                        }
                    }
                });
                return;
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
