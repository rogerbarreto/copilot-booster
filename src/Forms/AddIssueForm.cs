using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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
    internal static (GitHubTrackedItem? item, string? owner, string? repo) Show(string sessionId, string? cwd, GitHubApiService api)
    {
        var gitRoot = !string.IsNullOrEmpty(cwd) ? SessionService.FindGitRoot(cwd) : null;
        if (gitRoot == null)
        {
            MessageBox.Show("This session is not in a git repository.", "Add Issue", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return (null, null, null);
        }

        var remotes = GitService.GetRemotes(gitRoot);
        var remoteMap = new Dictionary<string, (string owner, string repo)>();
        foreach (var remote in remotes)
        {
            var url = GitService.GetRemoteUrl(gitRoot, remote);
            if (url != null)
            {
                var parsed = GitService.ParseGitHubOwnerRepo(url);
                if (parsed.HasValue)
                {
                    remoteMap[remote] = parsed.Value;
                }
            }
        }

        if (remoteMap.Count == 0)
        {
            MessageBox.Show("Could not detect a GitHub repository from git remotes.", "Add Issue", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return (null, null, null);
        }

        GitHubTrackedItem? result = null;

        var form = new Form
        {
            Text = "Add Issue to Session",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Width = 480,
            Height = 280,
            StartPosition = FormStartPosition.CenterParent,
            TopMost = Program._settings.AlwaysOnTop
        };

        int y = 12;

        var cmbRemote = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(100, y),
            Width = 340
        };
        foreach (var kv in remoteMap)
        {
            cmbRemote.Items.Add($"{kv.Key} ({kv.Value.owner}/{kv.Value.repo})");
        }

        if (cmbRemote.Items.Count > 0)
        {
            var preferred = remoteMap.Keys.FirstOrDefault(r => r.Equals("upstream", StringComparison.OrdinalIgnoreCase))
                ?? remoteMap.Keys.FirstOrDefault(r => r.Equals("origin", StringComparison.OrdinalIgnoreCase))
                ?? remoteMap.Keys.First();
            cmbRemote.SelectedIndex = remoteMap.Keys.ToList().IndexOf(preferred);
        }

        var lblRemote = new Label { Text = "Remote:", AutoSize = true, Location = new Point(14, y + 3) };

        if (remoteMap.Count > 1)
        {
            form.Controls.AddRange([lblRemote, cmbRemote]);
            y += 32;
        }

        (string owner, string repo) GetSelectedRemote()
        {
            var idx = Math.Max(0, cmbRemote.SelectedIndex);
            return remoteMap.Values.ElementAt(idx);
        }

        var (initialOwner, initialRepo) = GetSelectedRemote();
        var lblRepo = new Label
        {
            Text = $"{initialOwner}/{initialRepo}",
            ForeColor = Color.Gray,
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblRepo);
        y += 22;

        cmbRemote.SelectedIndexChanged += (s, e) =>
        {
            var (o, r) = GetSelectedRemote();
            lblRepo.Text = $"{o}/{r}";
        };

        var lblIssue = new Label { Text = "Issue Number:", AutoSize = true, Location = new Point(14, y + 3) };
        var txtIssue = new TextBox { PlaceholderText = "e.g., 15", Location = new Point(110, y), Width = 140 };
        y += 32;

        var lblInfo = new Label
        {
            Text = "",
            AutoSize = true,
            MaximumSize = new Size(440, 80),
            Location = new Point(14, y),
            ForeColor = Color.Gray
        };

        var btnAdd = new Button
        {
            Text = "Add Issue",
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
            var (owner, repo) = GetSelectedRemote();
            var issueText = txtIssue.Text.Trim();
            if (!int.TryParse(issueText, out var issueNum) || issueNum <= 0)
            {
                lblInfo.Text = "Enter a valid issue number.";
                lblInfo.ForeColor = Color.OrangeRed;
                btnAdd.Enabled = false;
                validated = false;
                return;
            }

            // Check for duplicate
            var existing = GitHubTrackingService.Load(sessionId);
            if (existing?.Items.Any(i => i.Type == "issue" && i.Number == issueNum) == true)
            {
                lblInfo.Text = $"⚠ Issue #{issueNum} is already tracked in this session.";
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

                    var stateReason = root.TryGetProperty("state_reason", out var srp) && srp.ValueKind != System.Text.Json.JsonValueKind.Null
                        ? srp.GetString() : null;

                    var labelText = labels.Count > 0 ? string.Join(", ", labels) : "(none)";
                    lblInfo.Text = $"✅ #{issueNum} — {title}\nState: {state}{(stateReason != null ? $" ({stateReason})" : "")}  |  Labels: {labelText}\nAuthor: {author}";
                    lblInfo.ForeColor = Color.Green;
                    btnAdd.Enabled = true;
                    validated = true;

                    result = new GitHubTrackedItem
                    {
                        Type = "issue",
                        Number = issueNum,
                        State = state,
                        StateReason = stateReason,
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
                var errorDetail = api.LastError != null ? $"\n({api.LastError})" : "";
                lblInfo.Text = $"❌ Issue #{issueNum} not found in {owner}/{repo}{errorDetail}\n(Note: if this is a PR, use \"Add PR\" instead)";
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

        txtIssue.Leave += async (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(txtIssue.Text) && !validated)
            {
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

        form.Controls.AddRange([lblIssue, txtIssue, lblInfo, btnAdd, btnCancel]);
        form.AcceptButton = btnAdd;
        form.CancelButton = btnCancel;

        if (form.ShowDialog() == DialogResult.OK && result != null)
        {
            var (selectedOwner, selectedRepo) = GetSelectedRemote();
            return (result, selectedOwner, selectedRepo);
        }

        return (null, null, null);
    }
}
