using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using CopilotBooster.Services;

namespace CopilotBooster.Forms;

/// <summary>
/// Modal window showing CI check run results for a PR.
/// Groups checks into PR Checks and Merge Queue Checks.
/// Failed checks show logs in a searchable text area.
/// </summary>
internal static class CiInformationForm
{
    internal record CheckRunInfo(
        string Name,
        string Status,      // "success", "failure", "neutral", "cancelled", "timed_out", "action_required"
        string Conclusion,
        long RunId,
        long JobId,
        string HtmlUrl,
        bool IsMergeQueue);

    /// <summary>
    /// Shows CI check results for a PR.
    /// </summary>
    internal static async Task ShowAsync(
        string owner,
        string repo,
        int prNumber,
        string commitSha,
        string? sessionId,
        GitHubApiService api,
        ActiveStatusTracker? tracker)
    {
        var checks = new List<CheckRunInfo>();

        // Fetch check runs for the commit
        var doc = await api.GetCheckRunsAsync(owner, repo, commitSha).ConfigureAwait(true);
        if (doc != null)
        {
            using (doc)
            {
                if (doc.RootElement.TryGetProperty("check_runs", out var runs))
                {
                    foreach (var run in runs.EnumerateArray())
                    {
                        var name = run.GetProperty("name").GetString() ?? "";
                        var status = run.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                        var conclusion = run.TryGetProperty("conclusion", out var c) && c.ValueKind != JsonValueKind.Null ? c.GetString() ?? "" : "";
                        var htmlUrl = run.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
                        var jobId = run.TryGetProperty("id", out var jid) ? jid.GetInt64() : 0;

                        // Determine run ID from details_url or html_url
                        long runId = 0;
                        if (run.TryGetProperty("details_url", out var du))
                        {
                            var durl = du.GetString() ?? "";
                            var runsIdx = durl.IndexOf("/runs/", StringComparison.Ordinal);
                            if (runsIdx > 0)
                            {
                                var afterRuns = durl[(runsIdx + 6)..];
                                var slashIdx = afterRuns.IndexOf('/');
                                var numStr = slashIdx > 0 ? afterRuns[..slashIdx] : afterRuns;
                                _ = long.TryParse(numStr, out runId);
                            }
                        }

                        var isMergeQueue = name.Contains("merge", StringComparison.OrdinalIgnoreCase)
                            && name.Contains("queue", StringComparison.OrdinalIgnoreCase);

                        checks.Add(new CheckRunInfo(name, status, conclusion, runId, jobId, htmlUrl, isMergeQueue));
                    }
                }
            }
        }

        // Build the form
        var form = new Form
        {
            Text = $"CI Check Results — PR #{prNumber}",
            Width = 700,
            Height = 550,
            FormBorderStyle = FormBorderStyle.Sizable,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            TopMost = Program._settings.AlwaysOnTop
        };

        var splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 200
        };

        // Top panel: check run list
        var checkList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
        checkList.Columns.Add("Status", 40);
        checkList.Columns.Add("Check Name", 350);
        checkList.Columns.Add("Group", 120);

        var prChecks = checks.Where(c => !c.IsMergeQueue).OrderBy(c => c.Conclusion == "success" ? 1 : 0).ToList();
        var mqChecks = checks.Where(c => c.IsMergeQueue).OrderBy(c => c.Conclusion == "success" ? 1 : 0).ToList();

        foreach (var check in prChecks.Concat(mqChecks))
        {
            var icon = check.Conclusion switch
            {
                "success" => "✅",
                "failure" or "timed_out" or "cancelled" => "❌",
                _ => check.Status == "completed" ? "⚪" : "⏳"
            };

            var group = check.IsMergeQueue ? "Merge Queue" : "PR Check";
            var item = new ListViewItem([icon, check.Name, group])
            {
                Tag = check,
                ForeColor = check.Conclusion == "failure" ? Color.Red : Color.Empty
            };
            checkList.Items.Add(item);
        }

        splitContainer.Panel1.Controls.Add(checkList);

        // Bottom panel: log viewer
        var logPanel = new Panel { Dock = DockStyle.Fill };

        var searchBox = new TextBox
        {
            PlaceholderText = "Search log...",
            Dock = DockStyle.Top,
            Height = 25
        };

        var logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9f)
        };

        CheckRunInfo? selectedCheck = null;
        string? fullLog = null;

        checkList.SelectedIndexChanged += async (s, e) =>
        {
            if (checkList.SelectedItems.Count == 0)
            {
                return;
            }

            selectedCheck = checkList.SelectedItems[0].Tag as CheckRunInfo;
            if (selectedCheck == null)
            {
                return;
            }

            logBox.Text = "Loading log...";
            fullLog = await api.GetJobLogAsync(owner, repo, selectedCheck.JobId);
            logBox.Text = fullLog ?? "(Log not available)";
        };

        searchBox.TextChanged += (s, e) =>
        {
            if (string.IsNullOrEmpty(fullLog))
            {
                return;
            }

            var query = searchBox.Text;
            if (string.IsNullOrWhiteSpace(query))
            {
                logBox.Text = fullLog;
                return;
            }

            var idx = logBox.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                logBox.SelectionStart = idx;
                logBox.SelectionLength = query.Length;
                logBox.ScrollToCaret();
            }
        };

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 35,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(4, 2, 4, 2)
        };

        var btnOpenPr = new Button
        {
            Text = "Open PR in Browser",
            Width = 140,
            Height = 28
        };
        btnOpenPr.Click += (s, e) =>
        {
            var prUrl = GitHubLinkService.GetPrUrl(owner, repo, prNumber);
            GitHubLinkService.OpenUrl(prUrl, sessionId, Program._settings.OpenLinksInEdgeSession, tracker);
        };

        var btnOpenJob = new Button
        {
            Text = "Open Job/Run in Browser",
            Width = 160,
            Height = 28,
            Enabled = false
        };
        btnOpenJob.Click += (s, e) =>
        {
            if (selectedCheck != null && !string.IsNullOrEmpty(selectedCheck.HtmlUrl))
            {
                GitHubLinkService.OpenUrl(selectedCheck.HtmlUrl, sessionId, Program._settings.OpenLinksInEdgeSession, tracker);
            }
        };

        checkList.SelectedIndexChanged += (s2, e2) =>
        {
            btnOpenJob.Enabled = checkList.SelectedItems.Count > 0;
        };

        btnPanel.Controls.Add(btnOpenPr);
        btnPanel.Controls.Add(btnOpenJob);

        logPanel.Controls.Add(logBox);
        logPanel.Controls.Add(searchBox);
        splitContainer.Panel2.Controls.Add(logPanel);
        splitContainer.Panel2.Controls.Add(btnPanel);

        form.Controls.Add(splitContainer);

        if (checks.Count == 0)
        {
            logBox.Text = "No check runs found for this commit.";
        }

        form.ShowDialog();
    }
}
