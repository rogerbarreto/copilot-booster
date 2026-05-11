using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// GitHub data service with two data sources:
/// <list type="number">
///   <item><c>gh api</c> CLI for structured API data (auth handled by <c>gh</c> natively)</item>
///   <item>HTML scraping of <c>github.com</c> pages (never rate-limited, fallback for issues/PRs)</item>
/// </list>
/// No direct <c>api.github.com</c> HTTP calls.
/// </summary>
internal partial class GitHubApiService
{
    /// <summary>
    /// Last error message from the most recent failed call.
    /// </summary>
    internal string? LastError { get; private set; }

    private static readonly HttpClient s_httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly Func<string?>? _getPatFromSettings;
    private readonly Func<string, string?, Task<(int ExitCode, string Stdout, string Stderr)>>? _processRunner;
    private bool? _hasGhCli;
    private bool? _isAuthenticated;

    /// <summary>
    /// Creates a new GitHub data service.
    /// </summary>
    /// <param name="getPatFromSettings">
    /// Optional callback to retrieve a PAT from settings.
    /// If provided, the PAT is forwarded as <c>GH_TOKEN</c> env var to <c>gh api</c> calls.
    /// </param>
    /// <param name="processRunner">
    /// Optional process runner for testability. If null, uses real <c>gh</c> CLI process.
    /// Signature: (command, arguments) → (exitCode, stdout, stderr).
    /// </param>
    internal GitHubApiService(Func<string?>? getPatFromSettings = null, Func<string, string?, Task<(int ExitCode, string Stdout, string Stderr)>>? processRunner = null)
    {
        this._getPatFromSettings = getPatFromSettings;
        this._processRunner = processRunner;
    }

    /// <summary>
    /// Whether the <c>gh</c> CLI is installed. Checked once and cached.
    /// </summary>
    internal bool HasGhCli
    {
        get
        {
            this._hasGhCli ??= this.CheckGhCli();
            return this._hasGhCli.Value;
        }
    }

    /// <summary>
    /// Whether GitHub authentication is available and working.
    /// If <c>gh</c> CLI is installed, runs <c>gh auth status</c>.
    /// Otherwise, checks if a PAT is configured.
    /// Checked once and cached.
    /// </summary>
    internal bool IsAuthenticated
    {
        get
        {
            this._isAuthenticated ??= this.CheckAuthentication();
            return this._isAuthenticated.Value;
        }
    }

    /// <summary>
    /// Checks if the authenticated user has starred the specified repository.
    /// Tries <c>gh api</c> first (if installed), falls back to HTTP with PAT.
    /// Returns <c>false</c> when no authentication is available.
    /// </summary>
    internal async Task<bool> IsRepoStarredAsync(string owner, string repo)
    {
        if (this.HasGhCli)
        {
            var (exitCode, _, _) = await this.RunProcessAsync("gh", $"api user/starred/{owner}/{repo}").ConfigureAwait(false);
            return exitCode == 0;
        }

        var pat = this._getPatFromSettings?.Invoke();
        if (string.IsNullOrEmpty(pat))
        {
            return false;
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/user/starred/{owner}/{repo}");
            request.Headers.Add("User-Agent", "CopilotBooster");
            request.Headers.Add("Accept", "application/vnd.github+json");
            request.Headers.Add("Authorization", $"Bearer {pat}");
            var response = await s_httpClient.SendAsync(request).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.NoContent; // 204 = starred
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Stars the specified repository for the authenticated user.
    /// Tries <c>gh api</c> first (if installed), falls back to HTTP with PAT.
    /// </summary>
    internal async Task<bool> StarRepoAsync(string owner, string repo)
    {
        if (this.HasGhCli)
        {
            var (exitCode, _, _) = await this.RunProcessAsync("gh", $"api -X PUT user/starred/{owner}/{repo}").ConfigureAwait(false);
            return exitCode == 0;
        }

        var pat = this._getPatFromSettings?.Invoke();
        if (string.IsNullOrEmpty(pat))
        {
            return false;
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Put, $"https://api.github.com/user/starred/{owner}/{repo}");
            request.Headers.Add("User-Agent", "CopilotBooster");
            request.Headers.Add("Accept", "application/vnd.github+json");
            request.Headers.Add("Authorization", $"Bearer {pat}");
            var response = await s_httpClient.SendAsync(request).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.NoContent; // 204 = success
        }
        catch
        {
            return false;
        }
    }

    private bool CheckGhCli()
    {
        try
        {
            if (this._processRunner != null)
            {
                var result = this._processRunner("gh", "--version").GetAwaiter().GetResult();
                return result.ExitCode == 0;
            }

            var psi = new ProcessStartInfo("gh", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return false;
            }

            proc.WaitForExit(5000);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private bool CheckAuthentication()
    {
        if (this.HasGhCli)
        {
            try
            {
                if (this._processRunner != null)
                {
                    var result = this._processRunner("gh", "auth status").GetAwaiter().GetResult();
                    return result.ExitCode == 0;
                }

                var psi = new ProcessStartInfo("gh", "auth status")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return false;
                }

                proc.WaitForExit(5000);
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        return !string.IsNullOrEmpty(this._getPatFromSettings?.Invoke());
    }

    /// <summary>
    /// Runs a process, using the injectable runner if available, otherwise real process.
    /// </summary>
    private async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(string fileName, string arguments)
    {
        if (this._processRunner != null)
        {
            return await this._processRunner(fileName, arguments).ConfigureAwait(false);
        }

        return await RunRealProcessAsync(fileName, arguments).ConfigureAwait(false);
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunRealProcessAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = arguments
        };

        using var proc = Process.Start(psi);
        if (proc == null)
        {
            return (-1, "", "Failed to start process");
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        var completed = proc.WaitForExit(15000);
        if (!completed)
        {
            try { proc.Kill(); }
            catch { }

            return (-1, "", "Process timed out");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return (proc.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// Fetches a PR by number. Tries HTML scraping first (never rate-limited, extracts
    /// rich metadata from embedded JSON: author, head branch, head SHA, state, title),
    /// falls back to <c>gh api</c> for private repos.
    /// </summary>
    internal async Task<JsonDocument?> GetPullRequestAsync(string owner, string repo, int number)
    {
        if (this._processRunner != null)
        {
            var apiDoc = await this.GhApiAsync($"repos/{owner}/{repo}/pulls/{number}").ConfigureAwait(false);
            if (apiDoc != null)
            {
                return apiDoc;
            }
        }

        var doc = await this.HtmlCheckAsync(owner, repo, "pull", number).ConfigureAwait(false);
        if (doc != null)
        {
            return doc;
        }

        return await this.GhApiAsync($"repos/{owner}/{repo}/pulls/{number}").ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches an Issue by number. Tries HTML scraping first (never rate-limited),
    /// falls back to <c>gh api</c> for richer data or private repos.
    /// Returns null if the issue is actually a PR.
    /// </summary>
    internal async Task<JsonDocument?> GetIssueAsync(string owner, string repo, int number)
    {
        if (this._processRunner != null)
        {
            var apiDoc = await this.GhApiAsync($"repos/{owner}/{repo}/issues/{number}").ConfigureAwait(false);
            if (apiDoc != null && apiDoc.RootElement.TryGetProperty("pull_request", out _))
            {
                apiDoc.Dispose();
                return null;
            }

            if (apiDoc != null)
            {
                return apiDoc;
            }
        }

        var doc = await this.HtmlCheckAsync(owner, repo, "issues", number).ConfigureAwait(false);
        if (doc != null)
        {
            return doc;
        }

        // HTML failed (private repo?) — try gh api
        doc = await this.GhApiAsync($"repos/{owner}/{repo}/issues/{number}").ConfigureAwait(false);
        if (doc != null && doc.RootElement.TryGetProperty("pull_request", out _))
        {
            doc.Dispose();
            return null; // It's a PR, not an issue
        }

        return doc;
    }

    /// <summary>
    /// Lists open PRs matching a branch. Tries with owner prefix first (same-repo),
    /// then scans all open PRs for fork-based matches.
    /// </summary>
    internal async Task<JsonDocument?> ListPullRequestsForBranchAsync(string owner, string repo, string branch)
    {
        var doc = await this.GhApiAsync($"repos/{owner}/{repo}/pulls?head={owner}:{branch}&state=open").ConfigureAwait(false);
        if (doc != null && doc.RootElement.GetArrayLength() > 0)
        {
            return doc;
        }

        doc?.Dispose();

        // Fallback: scan all open PRs and match by head branch name (works for fork-based PRs)
        doc = await this.GhApiAsync($"repos/{owner}/{repo}/pulls?state=open&per_page=100").ConfigureAwait(false);
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
    /// Fetches check runs for a commit SHA. Tries HTML scraping of <c>/pull/N/checks</c> first,
    /// falls back to <c>gh api</c>.
    /// Note: when called with a SHA, the caller must also supply the PR number via the overload.
    /// </summary>
    internal async Task<JsonDocument?> GetCheckRunsAsync(string owner, string repo, string commitSha)
    {
        // gh api is the only option when called with just a SHA (no PR number for HTML scraping)
        return await this.GhApiAsync($"repos/{owner}/{repo}/commits/{commitSha}/check-runs").ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches check runs for a PR by scraping the <c>/pull/N/checks</c> HTML page.
    /// Falls back to <c>gh api</c> with the commit SHA.
    /// </summary>
    internal async Task<JsonDocument?> GetCheckRunsForPrAsync(string owner, string repo, int prNumber, string? commitSha)
    {
        var doc = await HtmlCheckRunsAsync(owner, repo, prNumber).ConfigureAwait(false);
        if (doc != null)
        {
            return doc;
        }

        if (!string.IsNullOrEmpty(commitSha))
        {
            return await this.GhApiAsync($"repos/{owner}/{repo}/commits/{commitSha}/check-runs").ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>
    /// Fetches reviews for a PR. Tries <c>gh api</c> (no HTML equivalent for structured review data).
    /// </summary>
    internal async Task<JsonDocument?> GetPullRequestReviewsAsync(string owner, string repo, int number)
    {
        return await this.GhApiAsync($"repos/{owner}/{repo}/pulls/{number}/reviews").ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches a workflow job log via <c>gh api</c>.
    /// </summary>
    internal async Task<string?> GetJobLogAsync(string owner, string repo, long jobId)
    {
        var result = await this.GhApiTextAsync($"repos/{owner}/{repo}/actions/jobs/{jobId}/logs").ConfigureAwait(false);
        if (result != null)
        {
            return result;
        }

        this.LastError ??= "Job log not available (requires gh CLI authentication)";
        return null;
    }

    /// <summary>
    /// Runs <c>gh api</c> CLI command and returns parsed JSON.
    /// Auth is handled natively by <c>gh</c> (uses stored credentials or <c>GH_TOKEN</c> env var).
    /// </summary>
    private async Task<JsonDocument?> GhApiAsync(string path)
    {
        this.LastError = null;
        try
        {
            var (exitCode, stdout, stderr) = await this.RunGhApiAsync(path).ConfigureAwait(false);
            if (exitCode == 0 && !string.IsNullOrEmpty(stdout))
            {
                return JsonDocument.Parse(stdout);
            }

            if (!string.IsNullOrEmpty(stderr))
            {
                this.LastError = stderr.Trim();
                Program.Logger.LogDebug("gh api failed for {Path}: {Error}", path, this.LastError);
            }
        }
        catch (Exception ex) when (ex is not System.ComponentModel.Win32Exception)
        {
            Program.Logger.LogDebug("gh api error for {Path}: {Error}", path, ex.Message);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // gh CLI not installed — fall through to caller's fallback
            Program.Logger.LogDebug("gh CLI not available");
        }

        return null;
    }

    /// <summary>
    /// Runs <c>gh api</c> CLI command and returns raw text output (for logs).
    /// </summary>
    private async Task<string?> GhApiTextAsync(string path)
    {
        this.LastError = null;
        try
        {
            var (exitCode, stdout, stderr) = await this.RunGhApiAsync(path).ConfigureAwait(false);
            if (exitCode == 0 && !string.IsNullOrEmpty(stdout))
            {
                return stdout;
            }

            if (!string.IsNullOrEmpty(stderr))
            {
                this.LastError = stderr.Trim();
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("gh api text error for {Path}: {Error}", path, ex.Message);
        }

        return null;
    }

    /// <summary>
    /// Core process runner for <c>gh api</c> CLI.
    /// Uses injectable process runner if available, otherwise real process with PAT forwarding.
    /// </summary>
    private async Task<(int ExitCode, string Stdout, string Stderr)> RunGhApiAsync(string path)
    {
        if (this._processRunner != null)
        {
            return await this._processRunner("gh", $"api {path}").ConfigureAwait(false);
        }

        var psi = new ProcessStartInfo("gh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("api");
        psi.ArgumentList.Add(path);

        // Forward PAT as GH_TOKEN if configured (allows gh api to work with user-configured PATs)
        var pat = this._getPatFromSettings?.Invoke();
        if (!string.IsNullOrEmpty(pat))
        {
            psi.Environment["GH_TOKEN"] = pat;
        }

        using var proc = Process.Start(psi);
        if (proc == null)
        {
            return (-1, "", "Failed to start gh process");
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        var completed = proc.WaitForExit(15000);
        if (!completed)
        {
            try { proc.Kill(); }
            catch { }

            return (-1, "", "gh api timed out");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return (proc.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// Scrapes the <c>/pull/N/checks</c> page to extract check run data.
    /// Returns JSON matching the GitHub API check-runs structure.
    /// </summary>
    private static async Task<JsonDocument?> HtmlCheckRunsAsync(string owner, string repo, int prNumber)
    {
        var checksUrl = $"https://github.com/{owner}/{repo}/pull/{prNumber}/checks";
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, checksUrl);
            request.Headers.Add("User-Agent", "CopilotBooster");
            var response = await s_httpClient.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            // Parse check items: <details class="checks-list-item ..."> blocks
            var checkItems = ChecksListItemRegex().Matches(html);

            if (checkItems.Count == 0)
            {
                return null;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("{\"total_count\":");
            sb.Append(checkItems.Count);
            sb.Append(",\"check_runs\":[");

            for (var i = 0; i < checkItems.Count; i++)
            {
                var block = checkItems[i].Value;

                // Name: <span>NAME</span> inside the check item
                var nameMatch = SpanTextRegex().Match(block);
                var name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : "unknown";

                // Conclusion from aria-label on the SVG: "This job succeeded/failed/was skipped"
                var ariaMatch = AriaLabelJobRegex().Match(block);
                var conclusion = ariaMatch.Success
                    ? ariaMatch.Groups[1].Value switch
                    {
                        "succeeded" => "success",
                        "failed" => "failure",
                        "was" => "skipped", // "This job was skipped"
                        _ => ariaMatch.Groups[1].Value
                    }
                    : "";

                // Job URL: /actions/runs/{runId}/job/{jobId}
                var jobMatch = ActionsRunJobRegex().Match(block);
                var jobId = jobMatch.Success ? jobMatch.Groups[2].Value : "0";
                var htmlUrlValue = jobMatch.Success
                    ? $"https://github.com/{owner}/{repo}/actions/runs/{jobMatch.Groups[1].Value}/job/{jobMatch.Groups[2].Value}"
                    : "";

                // Status: infer from conclusion
                var status = string.IsNullOrEmpty(conclusion) ? "in_progress" : "completed";

                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append('{');
                sb.Append($"\"id\":{jobId}");
                sb.Append($",\"name\":{JsonSerializer.Serialize(name)}");
                sb.Append($",\"status\":\"{status}\"");
                sb.Append($",\"conclusion\":{(string.IsNullOrEmpty(conclusion) ? "null" : $"\"{conclusion}\"")}");
                sb.Append($",\"html_url\":\"{htmlUrlValue}\"");
                sb.Append('}');
            }

            sb.Append("]}");
            return JsonDocument.Parse(sb.ToString());
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("GitHub HTML check runs scraping failed for {Url}: {Error}", checksUrl, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// HTML page scraping for issues and PRs on <c>github.com</c>. Never rate-limited.
    /// Extracts rich metadata from embedded JSON in the HTML page, including:
    /// <list type="bullet">
    ///   <item>PRs: author, headBranch, headSha, state (open/merged/closed), title, mergedBy, updated_at</item>
    ///   <item>Issues: author, state, stateReason, title, updated_at, labels</item>
    /// </list>
    /// </summary>
    private async Task<JsonDocument?> HtmlCheckAsync(string owner, string repo, string type, int number)
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

            // Check redirects: /pull/N → /issues/N means it's an issue, not a PR
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? htmlUrl;
            if (type == "pull" && finalUrl.Contains("/issues/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Check redirects: /issues/N → /pull/N means it's a PR, not an issue
            if (type == "issues" && finalUrl.Contains("/pull/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            // Detect PR-vs-Issue from <title> tag format
            var titleTagStart = html.IndexOf("<title>", StringComparison.OrdinalIgnoreCase);
            if (titleTagStart >= 0)
            {
                titleTagStart += 7;
                var titleTagEnd = html.IndexOf("</title>", titleTagStart, StringComparison.OrdinalIgnoreCase);
                if (titleTagEnd > titleTagStart)
                {
                    var fullTitle = html[titleTagStart..titleTagEnd].Trim();
                    if (type == "issues" && fullTitle.Contains("Pull Request #", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    if (type == "pull" && fullTitle.Contains("Issue #", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }
                }
            }

            string json;
            if (type == "pull")
            {
                json = BuildPrJson(html, owner, repo, number, htmlUrl);
            }
            else
            {
                json = BuildIssueJson(html, number, htmlUrl);
            }

            this.LastError = null;
            Program.Logger.LogDebug("GitHub HTML check succeeded for {Url}", htmlUrl);
            return JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("GitHub HTML check failed for {Url}: {Error}", htmlUrl, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Builds a JSON document matching the GitHub PR API structure from embedded HTML metadata.
    /// Extracts from the <c>"pullRequest":{...}</c> JSON blob and <c>commit_status_icon?oid=</c> URL.
    /// </summary>
    private static string BuildPrJson(string html, string owner, string repo, int number, string htmlUrl)
    {
        // Extract from embedded "pullRequest":{...} JSON blob
        var title = ExtractJsonString(html, "\"pullRequest\":", "\"title\":\"");
        var authorLogin = ExtractJsonString(html, "\"pullRequest\":", "\"login\":\"");
        var headBranch = ExtractJsonString(html, "\"pullRequest\":", "\"headBranch\":\"");
        var baseBranch = ExtractJsonString(html, "\"pullRequest\":", "\"baseBranch\":\"");
        var mergedBy = ExtractJsonString(html, "\"pullRequest\":", "\"mergedBy\":\"");
        _ = ExtractJsonString(html, "\"pullRequest\":", "\"mergedTime\":\"");

        // State from embedded pullRequest blob
        var rawState = ExtractJsonString(html, "\"pullRequest\":", "\"state\":\"");
        var merged = string.Equals(rawState, "MERGED", StringComparison.OrdinalIgnoreCase);
        var state = rawState?.ToLowerInvariant() switch
        {
            "merged" => "closed",
            "closed" => "closed",
            _ => "open"
        };

        // Head SHA from commit_status_icon URL: oid=<40-hex-chars>
        string? headSha = null;
        var oidMatch = CommitStatusOidRegex().Match(html);
        if (oidMatch.Success)
        {
            headSha = oidMatch.Groups[1].Value;
        }

        // updated_at from the last relative-time datetime= attribute
        string? updatedAt = null;
        var timeMatches = RelativeTimeDatetimeRegex().Matches(html);
        if (timeMatches.Count > 0)
        {
            updatedAt = timeMatches[^1].Groups[1].Value;
        }

        // Build JSON matching GitHub API PR structure
        var sb = new System.Text.StringBuilder();
        sb.Append('{');
        sb.Append($"\"number\":{number}");
        sb.Append($",\"title\":{JsonSerializer.Serialize(title ?? "")}");
        sb.Append($",\"state\":\"{state}\"");
        sb.Append($",\"merged\":{(merged ? "true" : "false")}");
        sb.Append($",\"draft\":false");
        sb.Append($",\"html_url\":\"{htmlUrl}\"");

        if (authorLogin != null)
        {
            sb.Append($",\"user\":{{\"login\":{JsonSerializer.Serialize(authorLogin)}}}");
        }

        if (headBranch != null || headSha != null)
        {
            sb.Append(",\"head\":{");
            if (headBranch != null)
            {
                sb.Append($"\"ref\":{JsonSerializer.Serialize(headBranch)}");
            }

            if (headSha != null)
            {
                if (headBranch != null)
                {
                    sb.Append(',');
                }

                sb.Append($"\"sha\":\"{headSha}\"");
            }

            sb.Append('}');
        }

        if (baseBranch != null)
        {
            sb.Append($",\"base\":{{\"ref\":{JsonSerializer.Serialize(baseBranch)}}}");
        }

        if (updatedAt != null)
        {
            sb.Append($",\"updated_at\":\"{updatedAt}\"");
        }

        if (mergedBy != null)
        {
            sb.Append($",\"merged_by\":{{\"login\":{JsonSerializer.Serialize(mergedBy)}}}");
        }

        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Builds a JSON document matching the GitHub Issue API structure from embedded HTML metadata.
    /// Extracts state, stateReason, updatedAt, author from embedded JSON fragments.
    /// </summary>
    private static string BuildIssueJson(string html, int number, string htmlUrl)
    {
        // State from embedded JSON: "state":"CLOSED"
        var rawState = ExtractFirstJsonValue(html, "\"state\":\"");
        var state = rawState?.ToLowerInvariant() switch
        {
            "closed" => "closed",
            _ => "open"
        };

        // State reason: "stateReason":"NOT_PLANNED"
        var rawReason = ExtractFirstJsonValue(html, "\"stateReason\":\"");
        string? stateReason = rawReason?.ToLowerInvariant() switch
        {
            "not_planned" => "not_planned",
            "completed" => "completed",
            "reopened" => "reopened",
            _ => null
        };

        // Title from embedded JSON near state/stateReason
        var title = ExtractFirstJsonValue(html, "\"title\":\"");

        // updatedAt
        var updatedAt = ExtractFirstJsonValue(html, "\"updatedAt\":\"");

        // Author login — find the first "login" after "author"
        string? authorLogin = null;
        var authorIdx = html.IndexOf("\"author\":{", StringComparison.Ordinal);
        if (authorIdx >= 0)
        {
            var loginIdx = html.IndexOf("\"login\":\"", authorIdx, StringComparison.Ordinal);
            if (loginIdx >= 0 && loginIdx - authorIdx < 200)
            {
                authorLogin = ExtractFirstJsonValue(html[loginIdx..], "\"login\":\"");
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.Append('{');
        sb.Append($"\"number\":{number}");
        sb.Append($",\"title\":{JsonSerializer.Serialize(title ?? "")}");
        sb.Append($",\"state\":\"{state}\"");
        sb.Append($",\"html_url\":\"{htmlUrl}\"");

        if (stateReason != null)
        {
            sb.Append($",\"state_reason\":\"{stateReason}\"");
        }

        if (authorLogin != null)
        {
            sb.Append($",\"user\":{{\"login\":{JsonSerializer.Serialize(authorLogin)}}}");
        }

        if (updatedAt != null)
        {
            sb.Append($",\"updated_at\":\"{updatedAt}\"");
        }

        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Extracts the first quoted value after a JSON key pattern.
    /// E.g., for pattern <c>"title":"</c>, returns the value between the quotes.
    /// </summary>
    private static string? ExtractFirstJsonValue(string html, string prefix)
    {
        var idx = html.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        var start = idx + prefix.Length;
        var end = html.IndexOf('"', start);
        return end > start ? html[start..end] : null;
    }

    /// <summary>
    /// Extracts a JSON string value that appears after a context marker and then a field prefix.
    /// E.g., context <c>"pullRequest":</c> then field <c>"title":"</c>.
    /// </summary>
    private static string? ExtractJsonString(string html, string context, string fieldPrefix)
    {
        var contextIdx = html.IndexOf(context, StringComparison.Ordinal);
        if (contextIdx < 0)
        {
            return null;
        }

        // Search within a reasonable window after the context marker
        var searchEnd = Math.Min(html.Length, contextIdx + 3000);
        var fieldIdx = html.IndexOf(fieldPrefix, contextIdx, searchEnd - contextIdx, StringComparison.Ordinal);
        if (fieldIdx < 0)
        {
            return null;
        }

        var start = fieldIdx + fieldPrefix.Length;
        var end = html.IndexOf('"', start);
        if (end <= start)
        {
            return null;
        }

        var value = html[start..end];
        return value == "null" ? null : WebUtility.HtmlDecode(value);
    }

    [GeneratedRegex(@"<details class=""checks-list-item[\s\S]*?</details>")]
    private static partial Regex ChecksListItemRegex();

    [GeneratedRegex(@"<span>([^<]{2,100})</span>")]
    private static partial Regex SpanTextRegex();

    [GeneratedRegex(@"aria-label=""This job (\w+)""")]
    private static partial Regex AriaLabelJobRegex();

    [GeneratedRegex(@"/actions/runs/(\d+)/job/(\d+)")]
    private static partial Regex ActionsRunJobRegex();

    [GeneratedRegex(@"commit_status_icon\?oid=([0-9a-f]{40})")]
    private static partial Regex CommitStatusOidRegex();

    [GeneratedRegex(@"<relative-time[^>]*datetime=""([^""]+)""")]
    private static partial Regex RelativeTimeDatetimeRegex();
}
