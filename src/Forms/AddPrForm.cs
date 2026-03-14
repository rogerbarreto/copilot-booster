using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using CopilotBooster.Models;
using CopilotBooster.Services;

namespace CopilotBooster.Forms;

/// <summary>
/// Modal dialog for adding a tracked PR to a session.
/// Supports manual PR number entry and "Discover from Branch" auto-detection.
/// </summary>
internal static class AddPrForm
{
    /// <summary>
    /// Shows the Add PR dialog. Returns the tracked item if added, or null if cancelled.
    /// </summary>
    internal static GitHubTrackedItem? Show(string sessionId, string? cwd, GitHubApiService api)
    {
        var gitRoot = !string.IsNullOrEmpty(cwd) ? SessionService.FindGitRoot(cwd) : null;
        if (gitRoot == null)
        {
            MessageBox.Show("This session is not in a git repository.", "Add PR", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        var remotes = GitService.GetRemotes(gitRoot);
        string? owner = null, repo = null;

        // Resolve owner/repo from first GitHub remote
        foreach (var remote in remotes)
        {
            var url = GitService.GetRemoteUrl(gitRoot, remote);
            if (url != null)
            {
                var parsed = GitService.ParseGitHubOwnerRepo(url);
                if (parsed.HasValue)
                {
                    owner = parsed.Value.owner;
                    repo = parsed.Value.repo;
                    break;
                }
            }
        }

        if (owner == null || repo == null)
        {
            MessageBox.Show("Could not detect a GitHub repository from git remotes.", "Add PR", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        GitHubTrackedItem? result = null;

        var form = new Form
        {
            Text = "Add PR to Session",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Width = 480,
            Height = 280,
            StartPosition = FormStartPosition.CenterParent,
            TopMost = Program._settings.AlwaysOnTop
        };

        var lblRepo = new Label
        {
            Text = $"{owner}/{repo}",
            ForeColor = Color.Gray,
            AutoSize = true,
            Location = new Point(14, 12)
        };

        var lblPr = new Label { Text = "PR Number:", AutoSize = true, Location = new Point(14, 38) };
        var txtPr = new TextBox { PlaceholderText = "e.g., 42", Location = new Point(100, 35), Width = 140 };

        var btnDiscover = new Button
        {
            Text = "Discover from Branch",
            Location = new Point(250, 34),
            Width = 160,
            Height = 25
        };

        var lblInfo = new Label
        {
            Text = "",
            AutoSize = true,
            MaximumSize = new Size(440, 80),
            Location = new Point(14, 70),
            ForeColor = Color.Gray
        };

        var btnAdd = new Button
        {
            Text = "Add PR",
            DialogResult = DialogResult.None,
            Location = new Point(350, 200),
            Width = 80,
            Enabled = false
        };

        var btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(260, 200),
            Width = 80
        };

        bool validated = false;

        async Task ValidateAsync()
        {
            var prText = txtPr.Text.Trim();
            if (!int.TryParse(prText, out var prNum) || prNum <= 0)
            {
                lblInfo.Text = "Enter a valid PR number.";
                lblInfo.ForeColor = Color.OrangeRed;
                btnAdd.Enabled = false;
                validated = false;
                return;
            }

            lblInfo.Text = "Checking...";
            lblInfo.ForeColor = Color.Gray;
            btnAdd.Enabled = false;

            var doc = await api.GetPullRequestAsync(owner, repo, prNum).ConfigureAwait(true);
            if (doc != null)
            {
                using (doc)
                {
                    var root = doc.RootElement;
                    var title = root.GetProperty("title").GetString() ?? "";
                    var state = root.GetProperty("state").GetString() ?? "open";
                    var draft = root.TryGetProperty("draft", out var d) && d.GetBoolean();
                    var author = root.TryGetProperty("user", out var u) && u.TryGetProperty("login", out var l) ? l.GetString() ?? "" : "";
                    var headBranch = root.TryGetProperty("head", out var h) && h.TryGetProperty("ref", out var r) ? r.GetString() ?? "" : "";
                    var merged = root.TryGetProperty("merged", out var m) && m.GetBoolean();
                    var updatedAt = root.TryGetProperty("updated_at", out var ua) ? ua.GetString() ?? "" : "";

                    var effectiveState = merged ? "merged" : state;

                    lblInfo.Text = $"✅ #{prNum} — {title}\nState: {effectiveState}{(draft ? " (draft)" : "")}  |  Branch: {headBranch}\nAuthor: {author}";
                    lblInfo.ForeColor = Color.Green;
                    btnAdd.Enabled = true;
                    validated = true;

                    result = new GitHubTrackedItem
                    {
                        Type = "pr",
                        Number = prNum,
                        State = effectiveState,
                        Draft = draft,
                        Title = title,
                        Author = author,
                        HeadBranch = headBranch,
                        LastModifiedAt = updatedAt,
                        LastSeenAt = DateTime.UtcNow.ToString("o")
                    };
                }
            }
            else
            {
                lblInfo.Text = $"❌ PR #{prNum} not found in {owner}/{repo}";
                lblInfo.ForeColor = Color.OrangeRed;
                btnAdd.Enabled = false;
                validated = false;
            }
        }

        txtPr.KeyDown += async (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await ValidateAsync();
            }
        };

        btnDiscover.Click += async (s, e) =>
        {
            lblInfo.Text = "Discovering PR for current branch...";
            lblInfo.ForeColor = Color.Gray;
            btnAdd.Enabled = false;

            var branch = GitService.GetCurrentBranch(gitRoot);
            var doc = await api.ListPullRequestsForBranchAsync(owner, repo, branch).ConfigureAwait(true);
            if (doc != null)
            {
                using (doc)
                {
                    if (doc.RootElement.GetArrayLength() > 0)
                    {
                        var first = doc.RootElement[0];
                        var prNum = first.GetProperty("number").GetInt32();
                        txtPr.Text = prNum.ToString();
                        await ValidateAsync();
                        return;
                    }
                }
            }

            lblInfo.Text = $"No open PR found for branch \"{branch}\"";
            lblInfo.ForeColor = Color.OrangeRed;
        };

        btnAdd.Click += (s, e) =>
        {
            if (validated && result != null)
            {
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
        };

        form.Controls.AddRange([lblRepo, lblPr, txtPr, btnDiscover, lblInfo, btnAdd, btnCancel]);
        form.AcceptButton = btnAdd;
        form.CancelButton = btnCancel;

        return form.ShowDialog() == DialogResult.OK ? result : null;
    }
}
