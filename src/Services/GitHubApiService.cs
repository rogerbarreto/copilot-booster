using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// GitHub REST API client with cascading authentication:
/// 1. Unauthenticated (public repos, 60 req/hour)
/// 2. <c>gh auth token</c> from GitHub CLI (5000 req/hour)
/// 3. Manual PAT from settings (5000 req/hour)
/// </summary>
internal class GitHubApiService
{
    private static readonly HttpClient s_httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private string? _cachedGhToken;
    private bool _ghTokenChecked;
    private readonly Func<string?> _getPatFromSettings;

    /// <summary>
    /// Creates a new GitHub API service.
    /// </summary>
    /// <param name="getPatFromSettings">Callback to retrieve the PAT from settings (decrypted).</param>
    internal GitHubApiService(Func<string?> getPatFromSettings)
    {
        this._getPatFromSettings = getPatFromSettings;
    }

    /// <summary>
    /// Fetches a PR by number. Returns the parsed JSON or null on failure.
    /// </summary>
    internal async Task<JsonDocument?> GetPullRequestAsync(string owner, string repo, int number)
    {
        return await this.GetAsync($"https://api.github.com/repos/{owner}/{repo}/pulls/{number}").ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches an Issue by number. Returns the parsed JSON or null on failure.
    /// Returns null if the issue is actually a PR (has <c>pull_request</c> property).
    /// </summary>
    internal async Task<JsonDocument?> GetIssueAsync(string owner, string repo, int number)
    {
        var doc = await this.GetAsync($"https://api.github.com/repos/{owner}/{repo}/issues/{number}").ConfigureAwait(false);
        if (doc != null && doc.RootElement.TryGetProperty("pull_request", out _))
        {
            doc.Dispose();
            return null; // It's a PR, not an issue
        }

        return doc;
    }

    /// <summary>
    /// Lists open PRs for a branch. Used for PR discovery.
    /// </summary>
    internal async Task<JsonDocument?> ListPullRequestsForBranchAsync(string owner, string repo, string branch)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/pulls?head={owner}:{branch}&state=open";
        return await this.GetAsync(url).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches check runs for a commit SHA.
    /// </summary>
    internal async Task<JsonDocument?> GetCheckRunsAsync(string owner, string repo, string commitSha)
    {
        return await this.GetAsync($"https://api.github.com/repos/{owner}/{repo}/commits/{commitSha}/check-runs").ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches reviews for a PR.
    /// </summary>
    internal async Task<JsonDocument?> GetPullRequestReviewsAsync(string owner, string repo, int number)
    {
        return await this.GetAsync($"https://api.github.com/repos/{owner}/{repo}/pulls/{number}/reviews").ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches a workflow job log by job ID.
    /// </summary>
    internal async Task<string?> GetJobLogAsync(string owner, string repo, long jobId)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/actions/jobs/{jobId}/logs";
        try
        {
            var request = CreateRequest(HttpMethod.Get, url, this.ResolveToken());
            var response = await s_httpClient.SendAsync(request).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("GitHub API job log error: {Error}", ex.Message);
        }

        return null;
    }

    /// <summary>
    /// Core GET with cascading auth: unauthenticated → gh CLI → PAT.
    /// Automatically escalates auth on 403 (rate limit) or 404 (private repo).
    /// </summary>
    private async Task<JsonDocument?> GetAsync(string url)
    {
        // Attempt 1: unauthenticated (works for public repos)
        var result = await TryGetAsync(url, token: null).ConfigureAwait(false);
        if (result.Success)
        {
            return result.Document;
        }

        // Escalate on rate limit (403) or private repo (404)
        if (result.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            // Attempt 2: gh CLI token
            var ghToken = this.GetGhCliToken();
            if (!string.IsNullOrEmpty(ghToken))
            {
                result = await TryGetAsync(url, ghToken).ConfigureAwait(false);
                if (result.Success)
                {
                    return result.Document;
                }
            }

            // Attempt 3: PAT from settings
            var pat = this._getPatFromSettings();
            if (!string.IsNullOrEmpty(pat))
            {
                result = await TryGetAsync(url, pat).ConfigureAwait(false);
                if (result.Success)
                {
                    return result.Document;
                }
            }
        }

        Program.Logger.LogDebug("GitHub API failed for {Url}: {Status}", url, result.StatusCode);
        return null;
    }

    private static async Task<(bool Success, JsonDocument? Document, HttpStatusCode StatusCode)> TryGetAsync(string url, string? token)
    {
        try
        {
            var request = CreateRequest(HttpMethod.Get, url, token);
            var response = await s_httpClient.SendAsync(request).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return (true, JsonDocument.Parse(json), response.StatusCode);
            }

            return (false, null, response.StatusCode);
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("GitHub API error for {Url}: {Error}", url, ex.Message);
            return (false, null, HttpStatusCode.ServiceUnavailable);
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string? token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("User-Agent", "CopilotBooster");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    /// <summary>
    /// Tries to get a token from <c>gh auth token</c>. Cached after first attempt.
    /// </summary>
    private string? GetGhCliToken()
    {
        if (this._ghTokenChecked)
        {
            return this._cachedGhToken;
        }

        this._ghTokenChecked = true;

        try
        {
            var psi = new ProcessStartInfo("gh", "auth token")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return null;
            }

            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(3000);

            if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                this._cachedGhToken = output;
                Program.Logger.LogDebug("GitHub auth: using gh CLI token");
                return this._cachedGhToken;
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("gh CLI not available: {Error}", ex.Message);
        }

        return null;
    }

    /// <summary>
    /// Resolves the best available token (gh CLI → PAT → null).
    /// </summary>
    private string? ResolveToken()
    {
        var ghToken = this.GetGhCliToken();
        if (!string.IsNullOrEmpty(ghToken))
        {
            return ghToken;
        }

        return this._getPatFromSettings();
    }
}
