using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CopilotBooster.Models;
using Microsoft.Extensions.Logging;

namespace CopilotBooster.Services;

/// <summary>
/// Background service that polls GitHub API for updates to tracked PRs/Issues.
/// Uses exponential backoff based on item staleness:
/// - Active (modified &lt; 1h): every 30s
/// - Recent (modified &lt; 24h): every 5min
/// - Stale (modified &gt; 24h): every 30min
/// - Final (merged/closed): stops polling
/// </summary>
internal class GitHubPollingService : IDisposable
{
    private readonly GitHubApiService _api;
    private readonly Func<List<string>> _getSessionIds;
    private Timer? _timer;
    private bool _polling;

    /// <summary>
    /// Fires when a tracked item is updated (sessionId).
    /// </summary>
    internal event Action<string>? ItemUpdated;

    /// <summary>
    /// Fires when a tracked item has new activity (sessionId, type, number, title).
    /// Used for toast/tray notifications.
    /// </summary>
    internal event Action<string, string, int, string>? NewActivityDetected;

    internal GitHubPollingService(GitHubApiService api, Func<List<string>> getSessionIds)
    {
        this._api = api;
        this._getSessionIds = getSessionIds;
    }

    /// <summary>
    /// Starts the background polling timer with an immediate full poll on startup.
    /// </summary>
    internal void Start()
    {
        // Immediate full poll on startup (ignores backoff — items may have changed while app was closed)
        _ = Task.Run(async () =>
        {
            try
            {
                var sessionIds = this._getSessionIds();
                foreach (var sessionId in sessionIds)
                {
                    var data = GitHubTrackingService.Load(sessionId);
                    if (data == null || data.Items.Count == 0)
                    {
                        continue;
                    }

                    foreach (var item in data.Items.ToList())
                    {
                        // Skip truly final items (merged PRs, closed issues with StateReason already set)
                        if (item.IsFinal && (item.IsPr || item.StateReason != null))
                        {
                            continue;
                        }

                        try
                        {
                            if (item.IsPr)
                            {
                                await this.PollPrAsync(sessionId, data.Owner, data.Repo, item).ConfigureAwait(false);
                            }
                            else
                            {
                                await this.PollIssueAsync(sessionId, data.Owner, data.Repo, item).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            Program.Logger.LogDebug("Startup poll error for {Type}#{Number}: {Error}", item.Type, item.Number, ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Logger.LogDebug("Startup poll error: {Error}", ex.Message);
            }
        });

        // Then continue with periodic polling
        this._timer = new Timer(_ => _ = this.PollAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Stops the polling timer.
    /// </summary>
    internal void Stop()
    {
        this._timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void Dispose()
    {
        this._timer?.Dispose();
    }

    /// <summary>
    /// Triggers an immediate poll for a specific session (e.g., after adding a PR/Issue).
    /// </summary>
    internal void PollSessionNow(string sessionId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var data = GitHubTrackingService.Load(sessionId);
                if (data == null || data.Items.Count == 0)
                {
                    return;
                }

                foreach (var item in data.Items.ToList())
                {
                    if (item.IsPr)
                    {
                        await this.PollPrAsync(sessionId, data.Owner, data.Repo, item).ConfigureAwait(false);
                    }
                    else
                    {
                        await this.PollIssueAsync(sessionId, data.Owner, data.Repo, item).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Logger.LogDebug("Immediate poll error for {Session}: {Error}", sessionId, ex.Message);
            }
        });
    }

    private async Task PollAsync()
    {
        if (this._polling)
        {
            return;
        }

        this._polling = true;
        try
        {
            var sessionIds = this._getSessionIds();
            foreach (var sessionId in sessionIds)
            {
                var data = GitHubTrackingService.Load(sessionId);
                if (data == null || data.Items.Count == 0)
                {
                    continue;
                }

                foreach (var item in data.Items.ToList())
                {
                    // Skip final items unless they need StateReason backfill
                    // (issues closed before the StateReason fix have null StateReason)
                    if (item.IsFinal && (item.IsPr || item.StateReason != null))
                    {
                        continue;
                    }

                    if (!ShouldPoll(item))
                    {
                        continue;
                    }

                    try
                    {
                        if (item.IsPr)
                        {
                            await this.PollPrAsync(sessionId, data.Owner, data.Repo, item).ConfigureAwait(false);
                        }
                        else
                        {
                            await this.PollIssueAsync(sessionId, data.Owner, data.Repo, item).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Program.Logger.LogDebug("Poll error for {Type}#{Number}: {Error}", item.Type, item.Number, ex.Message);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Program.Logger.LogDebug("Polling cycle error: {Error}", ex.Message);
        }
        finally
        {
            this._polling = false;
        }
    }

    private async Task PollPrAsync(string sessionId, string owner, string repo, GitHubTrackedItem item)
    {
        var doc = await this._api.GetPullRequestAsync(owner, repo, item.Number).ConfigureAwait(false);
        if (doc == null)
        {
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            var updated = new GitHubTrackedItem
            {
                Type = "pr",
                Number = item.Number,
                State = root.TryGetProperty("merged", out var m) && m.GetBoolean()
                    ? "merged"
                    : root.GetProperty("state").GetString() ?? "open",
                Draft = root.TryGetProperty("draft", out var d) && d.GetBoolean(),
                Title = root.GetProperty("title").GetString() ?? "",
                Author = root.TryGetProperty("user", out var u) && u.TryGetProperty("login", out var l)
                    ? l.GetString() ?? "" : "",
                HeadBranch = root.TryGetProperty("head", out var h) && h.TryGetProperty("ref", out var r)
                    ? r.GetString() ?? "" : "",
                LastModifiedAt = root.TryGetProperty("updated_at", out var ua)
                    ? ua.GetString() ?? "" : ""
            };

            // Fetch reviews for approval count
            var reviewsDoc = await this._api.GetPullRequestReviewsAsync(owner, repo, item.Number).ConfigureAwait(false);
            if (reviewsDoc != null)
            {
                using (reviewsDoc)
                {
                    var approvers = new List<string>();
                    foreach (var review in reviewsDoc.RootElement.EnumerateArray())
                    {
                        var state = review.TryGetProperty("state", out var rs) ? rs.GetString() : "";
                        if (state == "APPROVED")
                        {
                            var reviewer = review.TryGetProperty("user", out var ru) && ru.TryGetProperty("login", out var rl)
                                ? rl.GetString() ?? "" : "";
                            if (!string.IsNullOrEmpty(reviewer) && !approvers.Contains(reviewer))
                            {
                                approvers.Add(reviewer);
                            }
                        }
                    }

                    updated.Approvals = approvers.Count;
                    updated.Approvers = approvers;
                }
            }

            // Fetch check runs for CI status
            var headSha = root.TryGetProperty("head", out var hs) && hs.TryGetProperty("sha", out var sha)
                ? sha.GetString() ?? "" : "";
            if (!string.IsNullOrEmpty(headSha))
            {
                var checksDoc = await this._api.GetCheckRunsAsync(owner, repo, headSha).ConfigureAwait(false);
                if (checksDoc != null)
                {
                    using (checksDoc)
                    {
                        if (checksDoc.RootElement.TryGetProperty("check_runs", out var runs))
                        {
                            bool hasFailure = false, hasPending = false, hasSuccess = false;
                            foreach (var run in runs.EnumerateArray())
                            {
                                var conclusion = run.TryGetProperty("conclusion", out var c) && c.ValueKind != JsonValueKind.Null
                                    ? c.GetString() : null;
                                var status = run.TryGetProperty("status", out var st) ? st.GetString() : "";

                                if (conclusion is "failure" or "timed_out" or "cancelled")
                                {
                                    hasFailure = true;
                                }
                                else if (status != "completed")
                                {
                                    hasPending = true;
                                }
                                else
                                {
                                    hasSuccess = true;
                                }
                            }

                            updated.Checks = hasFailure ? "failure" : hasPending ? "pending" : hasSuccess ? "success" : "";
                        }
                    }
                }
            }

            if (GitHubTrackingService.UpdateItem(sessionId, updated))
            {
                this.NewActivityDetected?.Invoke(sessionId, "pr", item.Number, updated.Title);
            }

            this.ItemUpdated?.Invoke(sessionId);
        }
    }

    private async Task PollIssueAsync(string sessionId, string owner, string repo, GitHubTrackedItem item)
    {
        var doc = await this._api.GetIssueAsync(owner, repo, item.Number).ConfigureAwait(false);
        if (doc == null)
        {
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            var labels = new List<string>();
            if (root.TryGetProperty("labels", out var labelsArr))
            {
                foreach (var lbl in labelsArr.EnumerateArray())
                {
                    if (lbl.TryGetProperty("name", out var n))
                    {
                        labels.Add(n.GetString() ?? "");
                    }
                }
            }

            var stateReason = root.TryGetProperty("state_reason", out var sr) && sr.ValueKind != JsonValueKind.Null
                ? sr.GetString() : null;

            var updated = new GitHubTrackedItem
            {
                Type = "issue",
                Number = item.Number,
                State = root.GetProperty("state").GetString() ?? "open",
                StateReason = stateReason,
                Title = root.GetProperty("title").GetString() ?? "",
                Author = root.TryGetProperty("user", out var u) && u.TryGetProperty("login", out var l)
                    ? l.GetString() ?? "" : "",
                Labels = labels,
                LastModifiedAt = root.TryGetProperty("updated_at", out var ua)
                    ? ua.GetString() ?? "" : ""
            };

            if (GitHubTrackingService.UpdateItem(sessionId, updated))
            {
                this.NewActivityDetected?.Invoke(sessionId, "issue", item.Number, updated.Title);
            }

            this.ItemUpdated?.Invoke(sessionId);
        }
    }

    private static bool ShouldPoll(GitHubTrackedItem item)
    {
        if (string.IsNullOrEmpty(item.LastModifiedAt))
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(item.LastModifiedAt, out var lastModified))
        {
            return true;
        }

        var age = DateTimeOffset.UtcNow - lastModified;

        // Exponential backoff intervals based on staleness
        var interval = age.TotalHours switch
        {
            < 1 => TimeSpan.FromSeconds(30),    // Active: every 30s
            < 24 => TimeSpan.FromMinutes(5),    // Recent: every 5min
            _ => TimeSpan.FromMinutes(30)        // Stale: every 30min
        };

        // Check if enough time has passed since last poll
        if (!DateTimeOffset.TryParse(item.LastSeenAt, out var lastSeen))
        {
            return true;
        }

        return (DateTimeOffset.UtcNow - lastSeen) >= interval;
    }
}
