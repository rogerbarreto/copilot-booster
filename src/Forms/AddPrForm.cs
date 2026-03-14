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
/// Modal dialog for adding a tracked PR to a session.
/// Supports manual PR number entry and "Discover from Branch" auto-detection.
/// </summary>
internal static class AddPrForm
{
    /// <summary>
    /// Shows the Add PR dialog. Returns the tracked item if added, or null if cancelled.
    /// </summary>
    internal static (GitHubTrackedItem? item, string? owner, string? repo) Show(string sessionId, string? cwd, GitHubApiService api)
    {
        var gitRoot = !string.IsNullOrEmpty(cwd) ? SessionService.FindGitRoot(cwd) : null;
        if (gitRoot == null)
        {
            MessageBox.Show("This session is not in a git repository.", "Add PR", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return (null, null, null);
        }

        // Build remote → (owner, repo) mapping (only GitHub remotes)
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
            MessageBox.Show("Could not detect a GitHub repository from git remotes.", "Add PR", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return (null, null, null);
        }

        GitHubTrackedItem? result = null;

        var form = new Form
        {
            Text = "Add PR to Session",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Width = 480,
            Height = 310,
            StartPosition = FormStartPosition.CenterParent,
            TopMost = Program._settings.AlwaysOnTop
        };

        int y = 12;

        // Remote dropdown (shown only if multiple remotes)
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
            // Prefer "upstream", then "origin", then first
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

        var lblPr = new Label { Text = "PR Number:", AutoSize = true, Location = new Point(14, y + 3) };
        var txtPr = new TextBox { PlaceholderText = "e.g., 42", Location = new Point(100, y), Width = 140 };

        var btnDiscover = new Button
        {
            Text = "Discover from Branch",
            Location = new Point(250, y - 1),
            Width = 160,
            Height = 25
        };
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
            Text = "Add PR",
            DialogResult = DialogResult.None,
            Location = new Point(350, 230),
            Width = 80,
            Enabled = false
        };

        var btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(260, 230),
            Width = 80
        };

        bool validated = false;

        async Task ValidateAsync()
        {
            var (owner, repo) = GetSelectedRemote();
            var prText = txtPr.Text.Trim();
            if (!int.TryParse(prText, out var prNum) || prNum <= 0)
            {
                lblInfo.Text = "Enter a valid PR number.";
                lblInfo.ForeColor = Color.OrangeRed;
                btnAdd.Enabled = false;
                validated = false;
                return;
            }

            // Check for duplicate
            var existing = GitHubTrackingService.Load(sessionId);
            if (existing?.Items.Any(i => i.Type == "pr" && i.Number == prNum) == true)
            {
                lblInfo.Text = $"⚠ PR #{prNum} is already tracked in this session.";
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
                var errorDetail = api.LastError != null ? $"\n({api.LastError})" : "";
                lblInfo.Text = $"❌ PR #{prNum} not found in {owner}/{repo}{errorDetail}";
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

        txtPr.Leave += async (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(txtPr.Text) && !validated)
            {
                await ValidateAsync();
            }
        };

        btnDiscover.Click += async (s, e) =>
        {
            var (discOwner, discRepo) = GetSelectedRemote();
            var branch = GitService.GetCurrentBranch(gitRoot);

            var doc = await api.ListPullRequestsForBranchAsync(discOwner, discRepo, branch).ConfigureAwait(true);
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

        form.Controls.AddRange([lblPr, txtPr, btnDiscover, lblInfo, btnAdd, btnCancel]);
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
