using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using CopilotBooster.Models;
using CopilotBooster.Services;

namespace CopilotBooster.Forms;

/// <summary>
/// Modal dialog for adding a tracked Issue to a session.
/// </summary>
internal static class AddIssueForm
{
    /// <summary>
    /// Shows the Add Issue dialog. Returns the tracked item if added, or null if cancelled.
    /// </summary>
    internal static GitHubTrackedItem? Show(string sessionId, string? cwd, GitHubApiService api)
    {
        var gitRoot = !string.IsNullOrEmpty(cwd) ? SessionService.FindGitRoot(cwd) : null;
        if (gitRoot == null)
        {
            MessageBox.Show("This session is not in a git repository.", "Add Issue", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        var remotes = GitService.GetRemotes(gitRoot);
        string? owner = null, repo = null;

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
            MessageBox.Show("Could not detect a GitHub repository from git remotes.", "Add Issue", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        GitHubTrackedItem? result = null;

        var form = new Form
        {
            Text = "Add Issue to Session",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Width = 480,
            Height = 250,
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

        var lblIssue = new Label { Text = "Issue Number:", AutoSize = true, Location = new Point(14, 38) };
        var txtIssue = new TextBox { PlaceholderText = "e.g., 15", Location = new Point(110, 35), Width = 140 };

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
            Text = "Add Issue",
            DialogResult = DialogResult.None,
            Location = new Point(350, 170),
            Width = 80,
            Enabled = false
        };

        var btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(260, 170),
            Width = 80
        };

        bool validated = false;

        async Task ValidateAsync()
        {
            var issueText = txtIssue.Text.Trim();
            if (!int.TryParse(issueText, out var issueNum) || issueNum <= 0)
            {
                lblInfo.Text = "Enter a valid issue number.";
                lblInfo.ForeColor = Color.OrangeRed;
                btnAdd.Enabled = false;
                validated = false;
                return;
            }

            lblInfo.Text = "Checking...";
            lblInfo.ForeColor = Color.Gray;
            btnAdd.Enabled = false;

            var doc = await api.GetIssueAsync(owner, repo, issueNum).ConfigureAwait(true);
            if (doc != null)
            {
                using (doc)
                {
                    var root = doc.RootElement;
                    var title = root.GetProperty("title").GetString() ?? "";
                    var state = root.GetProperty("state").GetString() ?? "open";
                    var author = root.TryGetProperty("user", out var u) && u.TryGetProperty("login", out var l) ? l.GetString() ?? "" : "";
                    var updatedAt = root.TryGetProperty("updated_at", out var ua) ? ua.GetString() ?? "" : "";

                    var labels = new System.Collections.Generic.List<string>();
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

                    var labelText = labels.Count > 0 ? string.Join(", ", labels) : "(none)";
                    lblInfo.Text = $"✅ #{issueNum} — {title}\nState: {state}  |  Labels: {labelText}\nAuthor: {author}";
                    lblInfo.ForeColor = Color.Green;
                    btnAdd.Enabled = true;
                    validated = true;

                    result = new GitHubTrackedItem
                    {
                        Type = "issue",
                        Number = issueNum,
                        State = state,
                        Title = title,
                        Author = author,
                        Labels = labels,
                        LastModifiedAt = updatedAt,
                        LastSeenAt = DateTime.UtcNow.ToString("o")
                    };
                }
            }
            else
            {
                lblInfo.Text = $"❌ Issue #{issueNum} not found in {owner}/{repo}\n(Note: if this is a PR, use \"Add PR\" instead)";
                lblInfo.ForeColor = Color.OrangeRed;
                btnAdd.Enabled = false;
                validated = false;
            }
        }

        txtIssue.KeyDown += async (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await ValidateAsync();
            }
        };

        btnAdd.Click += (s, e) =>
        {
            if (validated && result != null)
            {
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
        };

        form.Controls.AddRange([lblRepo, lblIssue, txtIssue, lblInfo, btnAdd, btnCancel]);
        form.AcceptButton = btnAdd;
        form.CancelButton = btnCancel;

        return form.ShowDialog() == DialogResult.OK ? result : null;
    }
}
