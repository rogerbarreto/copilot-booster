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
    /// Opens a GitHub URL. If Edge session is enabled: opens in running workspace,
    /// or starts a new workspace and navigates. Falls back to OS default browser.
    /// </summary>
    internal static void OpenUrl(string url, string? sessionId, bool useEdgeSession, ActiveStatusTracker? tracker)
    {
        if (useEdgeSession && sessionId != null && tracker != null)
        {
            if (tracker.TryGetEdge(sessionId, out var ws) && ws.IsOpen)
            {
                // Edge running — focus and navigate
                NavigateInEdge(url, ws);
                return;
            }

            // Edge not running — start workspace and navigate
            _ = Task.Factory.StartNew(async () =>
            {
                var workspace = SessionInteractionManager.CreateEdgeWorkspace(sessionId);
                tracker.TrackEdge(sessionId, workspace);
                if (await workspace.OpenAsync().ConfigureAwait(false))
                {
                    await Task.Delay(1500).ConfigureAwait(false);
                    NavigateInEdge(url, workspace);
                }
            }, System.Threading.CancellationToken.None, TaskCreationOptions.None, StaTaskScheduler.Instance);
            return;
        }

        // Fallback: OS default browser
        OpenInDefaultBrowser(url);
    }

    private static void NavigateInEdge(string url, EdgeWorkspaceService ws)
    {
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

        OpenInDefaultBrowser(url);
    }

    private static void OpenInDefaultBrowser(string url)
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
