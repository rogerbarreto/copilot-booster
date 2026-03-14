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
    /// <summary>
    /// Last error message from the most recent failed API call.
    /// </summary>
    internal string? LastError { get; private set; }

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
    /// Falls back to HTML page check if API is rate limited/SAML blocked.
    /// </summary>
    internal async Task<JsonDocument?> GetPullRequestAsync(string owner, string repo, int number)
    {
        var doc = await this.GetAsync($"https://api.github.com/repos/{owner}/{repo}/pulls/{number}").ConfigureAwait(false);
        if (doc != null)
        {
            return doc;
        }

        // Fallback: check if the PR page exists on github.com (not rate limited)
        return await this.FallbackHtmlCheckAsync(owner, repo, "pull", number).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches an Issue by number. Returns the parsed JSON or null on failure.
    /// Returns null if the issue is actually a PR (has <c>pull_request</c> property).
    /// Falls back to HTML page check if API is rate limited/SAML blocked.
    /// </summary>
    internal async Task<JsonDocument?> GetIssueAsync(string owner, string repo, int number)
    {
        var doc = await this.GetAsync($"https://api.github.com/repos/{owner}/{repo}/issues/{number}").ConfigureAwait(false);
        if (doc != null && doc.RootElement.TryGetProperty("pull_request", out _))
        {
            doc.Dispose();
            return null; // It's a PR, not an issue
        }

        if (doc != null)
        {
            return doc;
        }

        // Fallback: check if the issue page exists on github.com (not rate limited)
        return await this.FallbackHtmlCheckAsync(owner, repo, "issues", number).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists open PRs matching a branch. Tries with owner prefix first (same-repo),
    /// then scans all open PRs for fork-based matches.
    /// Returns a JSON document where [0] is the matched PR.
    /// </summary>
    internal async Task<JsonDocument?> ListPullRequestsForBranchAsync(string owner, string repo, string branch)
    {
        // Try with owner prefix (same-repo PRs)
        var doc = await this.GetAsync($"https://api.github.com/repos/{owner}/{repo}/pulls?head={owner}:{branch}&state=open").ConfigureAwait(false);
        if (doc != null && doc.RootElement.GetArrayLength() > 0)
        {
            return doc;
        }

        doc?.Dispose();

        // Fallback: scan all open PRs and match by head branch name
        // (works for fork-based PRs where the head owner differs)
        doc = await this.GetAsync($"https://api.github.com/repos/{owner}/{repo}/pulls?state=open&per_page=100").ConfigureAwait(false);
        if (doc != null)
        {
            using (doc)
            {
                foreach (var pr in doc.RootElement.EnumerateArray())
                {
                    if (pr.TryGetProperty("head", out var head) && head.TryGetProperty("ref", out var refProp))
                    {
                        var headRef = refProp.GetString();
                        if (string.Equals(headRef, branch, StringComparison.OrdinalIgnoreCase))
                        {
                            // Re-wrap the single matched PR as a JSON array so [0] is correct
                            var matchedJson = $"[{pr.GetRawText()}]";
                            return JsonDocument.Parse(matchedJson);
                        }
                    }
                }
            }
        }

        return null;
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
        this.LastError = null;
        // Attempt 1: unauthenticated (works for public repos)
        var result = await TryGetAsync(url, token: null).ConfigureAwait(false);
        if (result.Success)
        {
            return result.Document;
        }

        // Only escalate auth on rate limit (403 with X-RateLimit-Remaining: 0) or private repo (404)
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

                // If gh token got 403 (SAML enforcement), the repo is public but the token
                // doesn't have org access. Wait briefly and retry unauthenticated.
                if (result.StatusCode == HttpStatusCode.Forbidden)
                {
                    await Task.Delay(1000).ConfigureAwait(false);
                    result = await TryGetAsync(url, token: null).ConfigureAwait(false);
                    if (result.Success)
                    {
                        return result.Document;
                    }
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

        // Extract error message from GitHub API response
        if (!string.IsNullOrEmpty(result.ErrorBody))
        {
            try
            {
                using var errDoc = JsonDocument.Parse(result.ErrorBody);
                if (errDoc.RootElement.TryGetProperty("message", out var msg))
                {
                    this.LastError = $"{(int)result.StatusCode} {result.StatusCode}: {msg.GetString()}";
                    return null;
                }
            }
            catch { }

            this.LastError = $"{(int)result.StatusCode} {result.StatusCode}";
        }
        else
        {
            this.LastError = $"{(int)result.StatusCode} {result.StatusCode}";
        }

        return null;
    }

    /// <summary>
    /// Last-resort fallback: check if a PR/Issue exists by fetching the HTML page on github.com.
    /// This is never rate limited. If the page returns 200, builds a minimal JSON document
    /// with the number and title extracted from the HTML &lt;title&gt; tag.
    /// </summary>
    private async Task<JsonDocument?> FallbackHtmlCheckAsync(string owner, string repo, string type, int number)
    {
        var htmlUrl = $"https://github.com/{owner}/{repo}/{type}/{number}";
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, htmlUrl);
            request.Headers.Add("User-Agent", "CopilotBooster");
            var response = await s_httpClient.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            // Check if we were redirected (e.g., /pull/N → /issues/N means it's not a PR)
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? htmlUrl;
            if (type == "pull" && finalUrl.Contains("/issues/", StringComparison.OrdinalIgnoreCase))
            {
                return null; // Redirected to issues — this is an issue, not a PR
            }

            // Extract title from HTML <title> tag: "Title · Issue #N · owner/repo"
            var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var title = "";
            var titleStart = html.IndexOf("<title>", StringComparison.OrdinalIgnoreCase);
            if (titleStart >= 0)
            {
                titleStart += 7;
                var titleEnd = html.IndexOf("</title>", titleStart, StringComparison.OrdinalIgnoreCase);
                if (titleEnd > titleStart)
                {
                    var fullTitle = html[titleStart..titleEnd].Trim();
                    // Format: "Title · Issue #N · owner/repo · GitHub"
                    var middleDot = fullTitle.IndexOf(" · ", StringComparison.Ordinal);
                    title = middleDot > 0 ? WebUtility.HtmlDecode(fullTitle[..middleDot]) : fullTitle;
                }
            }

            // Build a minimal JSON that matches the API structure
            var state = "open";
            var escapedTitle = JsonSerializer.Serialize(title); // Properly JSON-escapes the string
            var json = $"{{\"number\":{number},\"title\":{escapedTitle},\"state\":\"{state}\",\"html_url\":\"{htmlUrl}\"}}";
            this.LastError = null;
            Program.Logger.LogDebug("GitHub HTML fallback succeeded for {Url}", htmlUrl);
            return JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("GitHub HTML fallback failed for {Url}: {Error}", htmlUrl, ex.Message);
            return null;
        }
    }

    private static async Task<(bool Success, JsonDocument? Document, HttpStatusCode StatusCode, string? ErrorBody)> TryGetAsync(string url, string? token)
    {
        try
        {
            var request = CreateRequest(HttpMethod.Get, url, token);
            var response = await s_httpClient.SendAsync(request).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return (true, JsonDocument.Parse(json), response.StatusCode, null);
            }

            var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return (false, null, response.StatusCode, errorBody);
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("GitHub API error for {Url}: {Error}", url, ex.Message);
            return (false, null, HttpStatusCode.ServiceUnavailable, ex.Message);
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
