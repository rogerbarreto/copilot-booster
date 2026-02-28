using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using CopilotBooster.Services;

namespace CopilotBooster.Forms;

/// <summary>
/// Provides a modal dialog for creating a new git worktree workspace.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class WorkspaceCreatorVisuals
{
    private static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Displays a modal dialog for creating a git worktree workspace from the specified repository.
    /// </summary>
    /// <param name="repoPath">The git repository root path.</param>
    /// <returns>A tuple of worktree path and optional session name on success, or <c>null</c> if the user cancels.</returns>
    internal static (string WorktreePath, string? SessionName, string? GitHubUrl)? ShowWorkspaceCreator(string repoPath)
    {
        (string WorktreePath, string? SessionName, string? GitHubUrl)? result = null;
        var repoFolderName = Path.GetFileName(repoPath);

        const int FormWidthValue = 500;
        const int CollapsedHeight = 340;
        const int ExpandedHeight = 410;

        var branches = WorkspaceCreationService.GetBranches(repoPath);
        var remotes = GitService.GetRemotes(repoPath);
        var currentBranch = GitService.GetCurrentBranch(repoPath);

        // Detect hosting platforms for each remote
        var remotePlatforms = new Dictionary<string, GitService.HostingPlatform>();
        foreach (var remote in remotes)
        {
            var url = GitService.GetRemoteUrl(repoPath, remote);
            if (!string.IsNullOrEmpty(url))
            {
                var platform = GitService.DetectHostingPlatform(url);
                if (platform != GitService.HostingPlatform.Unknown)
                {
                    remotePlatforms[remote] = platform;
                }
            }
        }

        var form = new Form
        {
            Text = "Create New Workspace",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Width = FormWidthValue,
            Height = CollapsedHeight,
            TopMost = Program._settings.AlwaysOnTop
        };

        if (Program.AppIcon != null)
        {
            form.Icon = Program.AppIcon;
        }

        int y = 12;

        // Subtitle
        var lblSubtitle = new Label
        {
            Text = "Set up a new isolated workspace for your coding session",
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 8.5f),
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblSubtitle);
        y += 28;

        // Session Name
        var lblSessionName = new Label
        {
            Text = "Session Name",
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblSessionName);
        y += 20;

        var txtSessionName = new TextBox
        {
            PlaceholderText = "e.g., Feature: User Authentication",
            Location = new Point(14, y),
            Width = 450
        };
        form.Controls.Add(SettingsVisuals.WrapWithBorder(txtSessionName));
        y += 26;

        var lblSessionNameHelper = new Label
        {
            Text = "A descriptive name for your session (optional)",
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f),
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblSessionNameHelper);
        y += 22;

        // Radio buttons — horizontal layout
        var rdoExistingBranch = new RadioButton
        {
            Text = "Existing branch",
            AutoSize = true,
            Location = new Point(14, y),
            Checked = true
        };
        form.Controls.Add(rdoExistingBranch);

        var rdoNewBranch = new RadioButton
        {
            Text = "New branch",
            AutoSize = true,
            Location = new Point(160, y)
        };
        form.Controls.Add(rdoNewBranch);

        var rdoFromPr = new RadioButton
        {
            Text = "From PR #",
            AutoSize = true,
            Location = new Point(290, y),
            Visible = remotePlatforms.Count > 0
        };
        form.Controls.Add(rdoFromPr);
        y += 26;

        // Second row of radio buttons (Issue)
        var rdoFromIssue = new RadioButton
        {
            Text = "From Issue #",
            AutoSize = true,
            Location = new Point(14, y),
            Visible = remotePlatforms.Values.Any(p => p == GitService.HostingPlatform.GitHub)
        };
        form.Controls.Add(rdoFromIssue);
        if (rdoFromIssue.Visible)
        {
            y += 26;
        }

        // Current branch info label
        var lblCurrentBranch = new Label
        {
            Text = $"Current branch: {currentBranch}",
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f),
            AutoSize = true,
            Location = new Point(14, y),
            Visible = true
        };
        form.Controls.Add(lblCurrentBranch);

        // --- New Branch controls (hidden by default) ---
        var modeStartY = y;

        var lblName = new Label
        {
            Text = "New branch name *",
            AutoSize = true,
            Location = new Point(14, y),
            Visible = false
        };
        form.Controls.Add(lblName);

        var txtName = new TextBox
        {
            PlaceholderText = "i.e: issues/123-new-issue",
            Location = new Point(14, y + 20),
            Width = 450,
            Visible = false
        };
        var txtNameWrapper = SettingsVisuals.WrapWithBorder(txtName);
        txtNameWrapper.Visible = false;
        form.Controls.Add(txtNameWrapper);

        var lblNameHelper = new Label
        {
            Text = "A descriptive name for your workspace (becomes the branch name)",
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f),
            AutoSize = true,
            Location = new Point(14, y + 46),
            Visible = false
        };
        form.Controls.Add(lblNameHelper);

        const int BranchFieldHeight = 68;

        // --- Shared branch dropdown (used in Existing Branch & New Branch modes) ---
        var lblBranch = new Label
        {
            Text = "Branch",
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblBranch);

        var cmbBranch = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(14, y + 20),
            Width = 450
        };

        foreach (var b in branches)
        {
            cmbBranch.Items.Add(b == currentBranch ? $"* {b}" : b);
        }

        if (!string.IsNullOrEmpty(currentBranch) && cmbBranch.Items.Contains($"* {currentBranch}"))
        {
            cmbBranch.SelectedItem = $"* {currentBranch}";
        }
        else if (cmbBranch.Items.Contains("main"))
        {
            cmbBranch.SelectedItem = "main";
        }
        else if (cmbBranch.Items.Count > 0)
        {
            cmbBranch.SelectedIndex = 0;
        }

        form.Controls.Add(cmbBranch);

        var lblBranchHelper = new Label
        {
            Text = "The branch to create the workspace from",
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f),
            AutoSize = true,
            Location = new Point(14, y + 46)
        };
        form.Controls.Add(lblBranchHelper);

        // --- PR mode controls (hidden by default) ---
        var lblRemote = new Label
        {
            Text = "Remote",
            AutoSize = true,
            Location = new Point(14, y),
            Visible = false
        };
        form.Controls.Add(lblRemote);

        var cmbRemote = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(14, y + 20),
            Width = 450,
            Visible = false
        };
        foreach (var kv in remotePlatforms)
        {
            cmbRemote.Items.Add(kv.Key);
        }
        if (cmbRemote.Items.Contains("origin"))
        {
            cmbRemote.SelectedItem = "origin";
        }
        else if (cmbRemote.Items.Count > 0)
        {
            cmbRemote.SelectedIndex = 0;
        }
        form.Controls.Add(cmbRemote);

        var lblPrNumber = new Label
        {
            Text = "PR Number *",
            AutoSize = true,
            Location = new Point(14, y),
            Visible = false
        };
        form.Controls.Add(lblPrNumber);

        var txtPrNumber = new TextBox
        {
            PlaceholderText = "e.g., 42",
            Location = new Point(14, y + 20),
            Width = 360,
            Visible = false
        };
        var txtPrNumberWrapper = SettingsVisuals.WrapWithBorder(txtPrNumber);
        txtPrNumberWrapper.Visible = false;
        form.Controls.Add(txtPrNumberWrapper);

        var btnCheck = new Button
        {
            Text = "Check",
            Width = 80,
            Visible = false
        };
        form.Controls.Add(btnCheck);

        var lblPrValidation = new Label
        {
            Text = "",
            AutoSize = true,
            MaximumSize = new Size(450, 0),
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f),
            Location = new Point(14, y),
            Visible = false
        };
        form.Controls.Add(lblPrValidation);

        var chkUsePrTitle = new CheckBox
        {
            Text = "Use PR title as session name",
            AutoSize = true,
            Location = new Point(14, y),
            Visible = false
        };
        form.Controls.Add(chkUsePrTitle);

        // Track PR validation state
        bool prValidated = false;
        string? fetchedPrTitle = null;
        string? fetchedPrHeadBranch = null;

        // --- Issue mode controls (hidden by default) ---
        var lblIssueRemote = new Label
        {
            Text = "Remote",
            AutoSize = true,
            Location = new Point(14, y),
            Visible = false
        };
        form.Controls.Add(lblIssueRemote);

        var cmbIssueRemote = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(14, y + 20),
            Width = 450,
            Visible = false
        };
        foreach (var kv in remotePlatforms)
        {
            if (kv.Value == GitService.HostingPlatform.GitHub)
            {
                cmbIssueRemote.Items.Add(kv.Key);
            }
        }
        if (cmbIssueRemote.Items.Contains("origin"))
        {
            cmbIssueRemote.SelectedItem = "origin";
        }
        else if (cmbIssueRemote.Items.Count > 0)
        {
            cmbIssueRemote.SelectedIndex = 0;
        }
        form.Controls.Add(cmbIssueRemote);

        var lblIssueNumber = new Label
        {
            Text = "Issue Number *",
            AutoSize = true,
            Location = new Point(14, y),
            Visible = false
        };
        form.Controls.Add(lblIssueNumber);

        var txtIssueNumber = new TextBox
        {
            PlaceholderText = "e.g., 42",
            Location = new Point(14, y + 20),
            Width = 360,
            Visible = false
        };
        var txtIssueNumberWrapper = SettingsVisuals.WrapWithBorder(txtIssueNumber);
        txtIssueNumberWrapper.Visible = false;
        form.Controls.Add(txtIssueNumberWrapper);

        var btnCheckIssue = new Button
        {
            Text = "Check",
            Width = 80,
            Visible = false
        };
        form.Controls.Add(btnCheckIssue);

        var lblIssueValidation = new Label
        {
            Text = "",
            AutoSize = true,
            MaximumSize = new Size(450, 0),
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f),
            Location = new Point(14, y),
            Visible = false
        };
        form.Controls.Add(lblIssueValidation);

        var chkUseIssueTitle = new CheckBox
        {
            Text = "Use issue title as session name",
            AutoSize = true,
            Location = new Point(14, y),
            Visible = false
        };
        form.Controls.Add(chkUseIssueTitle);

        var lblIssueBaseBranch = new Label
        {
            Text = "Base Branch",
            AutoSize = true,
            Location = new Point(14, y),
            Visible = false
        };
        form.Controls.Add(lblIssueBaseBranch);

        var cmbIssueBaseBranch = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(14, y + 20),
            Width = 450,
            Visible = false
        };
        foreach (var b in branches)
        {
            cmbIssueBaseBranch.Items.Add(b);
        }
        if (cmbIssueBaseBranch.Items.Contains("main"))
        {
            cmbIssueBaseBranch.SelectedItem = "main";
        }
        else if (cmbIssueBaseBranch.Items.Contains("master"))
        {
            cmbIssueBaseBranch.SelectedItem = "master";
        }
        else if (cmbIssueBaseBranch.Items.Count > 0)
        {
            cmbIssueBaseBranch.SelectedIndex = 0;
        }
        form.Controls.Add(cmbIssueBaseBranch);

        // Track Issue validation state
        bool issueValidated = false;
        string? fetchedIssueTitle = null;
        string? issueGitHubUrl = null;

        // Preview label
        var lblPreview = new Label
        {
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f, FontStyle.Italic),
            AutoSize = true,
            Location = new Point(14, y + 68),
            MaximumSize = new Size(460, 0)
        };
        form.Controls.Add(lblPreview);

        // Buttons
        var btnCreate = new Button
        {
            Text = "Create",
            DialogResult = DialogResult.None,
            Width = 80
        };

        var btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 80
        };

        form.Controls.Add(btnCreate);
        form.Controls.Add(btnCancel);

        // Layout helper — repositions controls based on selected mode
        void RelayoutControls()
        {
            int cy = modeStartY;
            bool isNewBranch = rdoNewBranch.Checked;
            bool isPrMode = rdoFromPr.Checked;
            bool isIssueMode = rdoFromIssue.Checked;
            bool isExistingBranch = rdoExistingBranch.Checked;

            // Current branch label — only visible in Existing Branch mode
            lblCurrentBranch.Visible = isExistingBranch;
            if (isExistingBranch)
            {
                lblCurrentBranch.Location = new Point(14, cy);
                cy += 18;
            }

            // New branch name fields
            lblName.Visible = isNewBranch;
            txtName.Visible = isNewBranch;
            txtNameWrapper.Visible = isNewBranch;
            lblNameHelper.Visible = isNewBranch;

            // Branch dropdown (visible in Existing Branch & New Branch modes)
            lblBranch.Visible = !isPrMode && !isIssueMode;
            cmbBranch.Visible = !isPrMode && !isIssueMode;
            lblBranchHelper.Visible = !isPrMode && !isIssueMode;

            // PR mode controls
            lblRemote.Visible = isPrMode;
            cmbRemote.Visible = isPrMode;
            lblPrNumber.Visible = isPrMode;
            txtPrNumber.Visible = isPrMode;
            txtPrNumberWrapper.Visible = isPrMode;
            btnCheck.Visible = isPrMode;
            lblPrValidation.Visible = isPrMode;

            if (!isPrMode)
            {
                chkUsePrTitle.Visible = false;
            }

            // Issue mode controls
            lblIssueRemote.Visible = isIssueMode;
            cmbIssueRemote.Visible = isIssueMode;
            lblIssueNumber.Visible = isIssueMode;
            txtIssueNumber.Visible = isIssueMode;
            txtIssueNumberWrapper.Visible = isIssueMode;
            btnCheckIssue.Visible = isIssueMode;
            lblIssueValidation.Visible = isIssueMode;
            lblIssueBaseBranch.Visible = isIssueMode;
            cmbIssueBaseBranch.Visible = isIssueMode;

            if (!isIssueMode)
            {
                chkUseIssueTitle.Visible = false;
            }

            if (isPrMode)
            {
                // Remote dropdown
                lblRemote.Location = new Point(14, cy);
                cmbRemote.Location = new Point(14, cy + 20);
                cy += 50;

                // PR number + Check button
                lblPrNumber.Location = new Point(14, cy);
                txtPrNumber.Location = new Point(14, cy + 20);
                txtPrNumberWrapper.Location = new Point(14, cy + 20);
                btnCheck.Location = new Point(384, cy + 19);
                cy += 48;

                // Validation label
                lblPrValidation.Location = new Point(14, cy);
                cy += Math.Max(20, lblPrValidation.PreferredHeight + 4);

                // PR title checkbox
                chkUsePrTitle.Location = new Point(14, cy);
                if (chkUsePrTitle.Visible)
                {
                    cy += 24;
                }

                // Preview
                lblPreview.Location = new Point(14, cy);
                cy += 32;

                // Buttons
                btnCreate.Location = new Point(300, cy);
                btnCancel.Location = new Point(390, cy);

                btnCreate.Enabled = prValidated;
                form.Height = cy + 70;
            }
            else if (isIssueMode)
            {
                // Remote dropdown
                lblIssueRemote.Location = new Point(14, cy);
                cmbIssueRemote.Location = new Point(14, cy + 20);
                cy += 50;

                // Issue number + Check button
                lblIssueNumber.Location = new Point(14, cy);
                txtIssueNumber.Location = new Point(14, cy + 20);
                txtIssueNumberWrapper.Location = new Point(14, cy + 20);
                btnCheckIssue.Location = new Point(384, cy + 19);
                cy += 48;

                // Validation label
                lblIssueValidation.Location = new Point(14, cy);
                cy += Math.Max(20, lblIssueValidation.PreferredHeight + 4);

                // Issue title checkbox
                chkUseIssueTitle.Location = new Point(14, cy);
                if (chkUseIssueTitle.Visible)
                {
                    cy += 24;
                }

                // Base branch
                lblIssueBaseBranch.Location = new Point(14, cy);
                cmbIssueBaseBranch.Location = new Point(14, cy + 20);
                cy += 50;

                // Preview
                lblPreview.Location = new Point(14, cy);
                cy += 32;

                // Buttons
                btnCreate.Location = new Point(300, cy);
                btnCancel.Location = new Point(390, cy);

                btnCreate.Enabled = issueValidated;
                form.Height = cy + 70;
            }
            else if (isNewBranch)
            {
                lblName.Location = new Point(14, cy);
                txtName.Location = new Point(14, cy + 20);
                txtNameWrapper.Location = new Point(14, cy + 20);
                lblNameHelper.Location = new Point(14, cy + 46);
                cy += BranchFieldHeight;

                lblBranch.Text = "Base Branch";
                lblBranchHelper.Text = "The branch to create the new branch from";

                lblBranch.Location = new Point(14, cy);
                cmbBranch.Location = new Point(14, cy + 20);
                lblBranchHelper.Location = new Point(14, cy + 46);
                lblPreview.Location = new Point(14, cy + 68);

                int buttonY = cy + 100;
                btnCreate.Location = new Point(300, buttonY);
                btnCancel.Location = new Point(390, buttonY);

                btnCreate.Enabled = true;
                form.Height = ExpandedHeight;
            }
            else
            {
                lblBranch.Text = "Branch";
                lblBranchHelper.Text = "The existing branch to check out";

                lblBranch.Location = new Point(14, cy);
                cmbBranch.Location = new Point(14, cy + 20);
                lblBranchHelper.Location = new Point(14, cy + 46);
                lblPreview.Location = new Point(14, cy + 68);

                int buttonY = cy + 100;
                btnCreate.Location = new Point(300, buttonY);
                btnCancel.Location = new Point(390, buttonY);

                btnCreate.Enabled = true;
                form.Height = CollapsedHeight;
            }
        }

        void UpdatePreview()
        {
            if (rdoFromPr.Checked)
            {
                var prText = txtPrNumber.Text.Trim();
                if (int.TryParse(prText, out var prNum) && prNum > 0)
                {
                    lblPreview.Text = WorkspaceCreationService.BuildWorkspacePath(repoFolderName!, $"pr-{prNum}");
                }
                else
                {
                    lblPreview.Text = "";
                }
            }
            else if (rdoFromIssue.Checked)
            {
                var issueText = txtIssueNumber.Text.Trim();
                if (int.TryParse(issueText, out var issueNum) && issueNum > 0)
                {
                    var branchName = Models.LauncherSettings.FormatBranchName(
                        Program._settings.IssueBranchPattern, issueNum, txtSessionName.Text.Trim());
                    lblPreview.Text = WorkspaceCreationService.BuildWorkspacePath(repoFolderName!, branchName);
                }
                else
                {
                    lblPreview.Text = "";
                }
            }
            else if (rdoNewBranch.Checked)
            {
                var name = txtName.Text.Trim();
                lblPreview.Text = string.IsNullOrEmpty(name)
                    ? ""
                    : WorkspaceCreationService.BuildWorkspacePath(repoFolderName!, name);
            }
            else
            {
                var branch = cmbBranch.SelectedItem?.ToString()?.TrimStart('*', ' ');
                if (string.IsNullOrEmpty(branch))
                {
                    lblPreview.Text = "";
                }
                else
                {
                    var localName = GitService.GetLocalBranchName(branch, remotes);
                    lblPreview.Text = WorkspaceCreationService.BuildWorkspacePath(repoFolderName!, localName);
                }
            }
        }

        void ResetPrValidation()
        {
            prValidated = false;
            fetchedPrTitle = null;
            fetchedPrHeadBranch = null;
            lblPrValidation.Text = "";
            lblPrValidation.ForeColor = Color.Black;
            chkUsePrTitle.Visible = false;
            chkUsePrTitle.Checked = false;
            txtSessionName.ReadOnly = false;
            if (rdoFromPr.Checked)
            {
                btnCreate.Enabled = false;
            }
        }

        void ResetIssueValidation()
        {
            issueValidated = false;
            fetchedIssueTitle = null;
            issueGitHubUrl = null;
            lblIssueValidation.Text = "";
            lblIssueValidation.ForeColor = Color.Black;
            chkUseIssueTitle.Visible = false;
            chkUseIssueTitle.Checked = false;
            txtSessionName.ReadOnly = false;
            if (rdoFromIssue.Checked)
            {
                btnCreate.Enabled = false;
            }
        }

        bool isValidating = false;

        async Task ValidatePrAsync()
        {
            if (isValidating)
            {
                return;
            }

            var remoteName = cmbRemote.SelectedItem?.ToString();
            var prText = txtPrNumber.Text.Trim();
            if (string.IsNullOrEmpty(remoteName) || !int.TryParse(prText, out var prNum) || prNum <= 0)
            {
                lblPrValidation.Text = "Enter a valid PR number.";
                lblPrValidation.ForeColor = Color.Red;
                prValidated = false;
                btnCreate.Enabled = false;
                return;
            }

            if (!remotePlatforms.TryGetValue(remoteName, out var platform))
            {
                return;
            }

            isValidating = true;
            lblPrValidation.Text = "Checking...";
            lblPrValidation.ForeColor = Color.Gray;
            btnCheck.Enabled = false;

            bool found = false;
            string? prTitle = null;
            string? prHeadBranch = null;
            try
            {
                // Run git ls-remote and optional GitHub API title fetch entirely on background thread
                (found, prTitle, prHeadBranch) = await Task.Run(async () =>
                {
                    var valid = GitService.ValidatePrRef(repoPath, remoteName, platform, prNum);
                    string? title = null;
                    string? headRef = null;

                    if (valid && platform == GitService.HostingPlatform.GitHub)
                    {
                        try
                        {
                            var remoteUrl = GitService.GetRemoteUrl(repoPath, remoteName);
                            if (!string.IsNullOrEmpty(remoteUrl))
                            {
                                var parsed = GitService.ParseGitHubOwnerRepo(remoteUrl);
                                if (parsed.HasValue)
                                {
                                    var (owner, repo) = parsed.Value;
                                    var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/pulls/{prNum}";
                                    var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                                    request.Headers.Add("User-Agent", "CopilotBooster");
                                    var response = await s_httpClient.SendAsync(request).ConfigureAwait(false);
                                    if (response.IsSuccessStatusCode)
                                    {
                                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                        using var doc = JsonDocument.Parse(json);
                                        if (doc.RootElement.TryGetProperty("title", out var titleProp))
                                        {
                                            title = titleProp.GetString();
                                        }

                                        if (doc.RootElement.TryGetProperty("head", out var headProp) &&
                                            headProp.TryGetProperty("ref", out var refProp))
                                        {
                                            headRef = refProp.GetString();
                                        }
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // API failure — don't affect the flow
                        }
                    }

                    return (valid, title, headRef);
                }).ConfigureAwait(true);
            }
            catch
            {
                found = false;
            }

            if (found)
            {
                lblPrValidation.Text = prTitle != null
                    ? $"✅ PR #{prNum}: {prTitle}"
                    : $"✅ PR #{prNum} found";
                lblPrValidation.ForeColor = Color.Green;
                prValidated = true;
                btnCreate.Enabled = true;

                if (prTitle != null)
                {
                    fetchedPrTitle = prTitle;
                    chkUsePrTitle.Visible = true;
                    RelayoutControls();
                }

                fetchedPrHeadBranch = prHeadBranch;
            }
            else
            {
                lblPrValidation.Text = $"❌ PR #{prNum} not found";
                lblPrValidation.ForeColor = Color.Red;
                prValidated = false;
                btnCreate.Enabled = false;
            }

            btnCheck.Enabled = true;
            isValidating = false;
            UpdatePreview();
        }

        bool isValidatingIssue = false;

        async Task ValidateIssueAsync()
        {
            if (isValidatingIssue)
            {
                return;
            }

            var remoteName = cmbIssueRemote.SelectedItem?.ToString();
            var issueText = txtIssueNumber.Text.Trim();
            if (string.IsNullOrEmpty(remoteName) || !int.TryParse(issueText, out var issueNum) || issueNum <= 0)
            {
                lblIssueValidation.Text = "Enter a valid issue number.";
                lblIssueValidation.ForeColor = Color.Red;
                issueValidated = false;
                btnCreate.Enabled = false;
                return;
            }

            isValidatingIssue = true;
            lblIssueValidation.Text = "Checking...";
            lblIssueValidation.ForeColor = Color.Gray;
            btnCheckIssue.Enabled = false;

            bool found = false;
            string? issueTitle = null;
            string? ghUrl = null;
            try
            {
                (found, issueTitle, ghUrl) = await Task.Run(async () =>
                {
                    string? title = null;
                    string? url = null;
                    bool valid = false;

                    try
                    {
                        var remoteUrl = GitService.GetRemoteUrl(repoPath, remoteName);
                        if (!string.IsNullOrEmpty(remoteUrl))
                        {
                            var parsed = GitService.ParseGitHubOwnerRepo(remoteUrl);
                            if (parsed.HasValue)
                            {
                                var (owner, repo) = parsed.Value;
                                var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/issues/{issueNum}";
                                var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                                request.Headers.Add("User-Agent", "CopilotBooster");
                                var response = await s_httpClient.SendAsync(request).ConfigureAwait(false);
                                if (response.IsSuccessStatusCode)
                                {
                                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                    using var doc = JsonDocument.Parse(json);

                                    // Ensure it's actually an issue, not a PR
                                    if (!doc.RootElement.TryGetProperty("pull_request", out _))
                                    {
                                        valid = true;
                                        if (doc.RootElement.TryGetProperty("title", out var titleProp))
                                        {
                                            title = titleProp.GetString();
                                        }

                                        url = $"https://github.com/{owner}/{repo}/issues/{issueNum}";
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // API failure
                    }

                    return (valid, title, url);
                }).ConfigureAwait(true);
            }
            catch
            {
                found = false;
            }

            if (found)
            {
                lblIssueValidation.Text = issueTitle != null
                    ? $"✅ Issue #{issueNum}: {issueTitle}"
                    : $"✅ Issue #{issueNum} found";
                lblIssueValidation.ForeColor = Color.Green;
                issueValidated = true;
                btnCreate.Enabled = true;

                if (issueTitle != null)
                {
                    fetchedIssueTitle = issueTitle;
                    chkUseIssueTitle.Visible = true;
                    RelayoutControls();
                }

                issueGitHubUrl = ghUrl;
            }
            else
            {
                lblIssueValidation.Text = $"❌ Issue #{issueNum} not found";
                lblIssueValidation.ForeColor = Color.Red;
                issueValidated = false;
                btnCreate.Enabled = false;
            }

            btnCheckIssue.Enabled = true;
            isValidatingIssue = false;
            UpdatePreview();
        }

        // Wire up radio button changes
        void OnModeChanged(object? s, EventArgs e)
        {
            ResetPrValidation();
            ResetIssueValidation();
            RelayoutControls();
            UpdatePreview();
        }

        rdoExistingBranch.CheckedChanged += OnModeChanged;
        rdoNewBranch.CheckedChanged += OnModeChanged;
        rdoFromPr.CheckedChanged += OnModeChanged;
        rdoFromIssue.CheckedChanged += OnModeChanged;

        txtName.TextChanged += (s, e) => UpdatePreview();
        cmbBranch.SelectedIndexChanged += (s, e) => UpdatePreview();
        txtPrNumber.TextChanged += (s, e) => { ResetPrValidation(); UpdatePreview(); };
        cmbRemote.SelectedIndexChanged += (s, e) => { ResetPrValidation(); UpdatePreview(); };

        btnCheck.Click += async (s, e) => await ValidatePrAsync().ConfigureAwait(true);
        txtPrNumber.Leave += async (s, e) =>
        {
            if (rdoFromPr.Checked && !string.IsNullOrWhiteSpace(txtPrNumber.Text) && !prValidated)
            {
                await ValidatePrAsync().ConfigureAwait(true);
            }
        };

        chkUsePrTitle.CheckedChanged += (s, e) =>
        {
            if (chkUsePrTitle.Checked && fetchedPrTitle != null)
            {
                txtSessionName.Text = fetchedPrTitle;
                txtSessionName.ReadOnly = true;
            }
            else
            {
                txtSessionName.ReadOnly = false;
            }
        };

        txtIssueNumber.TextChanged += (s, e) => { ResetIssueValidation(); UpdatePreview(); };
        cmbIssueRemote.SelectedIndexChanged += (s, e) => { ResetIssueValidation(); UpdatePreview(); };
        txtSessionName.TextChanged += (s, e) => UpdatePreview();

        btnCheckIssue.Click += async (s, e) => await ValidateIssueAsync().ConfigureAwait(true);
        txtIssueNumber.Leave += async (s, e) =>
        {
            if (rdoFromIssue.Checked && !string.IsNullOrWhiteSpace(txtIssueNumber.Text) && !issueValidated)
            {
                await ValidateIssueAsync().ConfigureAwait(true);
            }
        };

        chkUseIssueTitle.CheckedChanged += (s, e) =>
        {
            if (chkUseIssueTitle.Checked && fetchedIssueTitle != null)
            {
                txtSessionName.Text = fetchedIssueTitle;
                txtSessionName.ReadOnly = true;
            }
            else
            {
                txtSessionName.ReadOnly = false;
            }
        };

        // Initial layout
        RelayoutControls();
        UpdatePreview();

        btnCreate.Click += async (s, e) =>
        {
            if (rdoFromPr.Checked)
            {
                // PR mode
                var remoteName = cmbRemote.SelectedItem?.ToString();
                var prText = txtPrNumber.Text.Trim();
                if (string.IsNullOrEmpty(remoteName) || !int.TryParse(prText, out var prNum) || prNum <= 0)
                {
                    MessageBox.Show("Enter a valid PR number.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!prValidated)
                {
                    MessageBox.Show("Please validate the PR first.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var platform = remotePlatforms[remoteName];
                btnCreate.Enabled = false;
                btnCreate.Text = "Creating...";
                var (worktreePath, success, error) = await Task.Run(() =>
                    WorkspaceCreationService.CreateWorkspaceFromPr(
                        repoPath, repoFolderName!, remoteName, prNum, platform, fetchedPrHeadBranch)).ConfigureAwait(true);
                if (success)
                {
                    var sessionName = txtSessionName.Text.Trim();

                    // Build GitHub URL for Edge tab
                    string? prGhUrl = null;
                    var prRemoteUrl = GitService.GetRemoteUrl(repoPath, remoteName);
                    if (!string.IsNullOrEmpty(prRemoteUrl))
                    {
                        var parsed = GitService.ParseGitHubOwnerRepo(prRemoteUrl);
                        if (parsed.HasValue)
                        {
                            prGhUrl = $"https://github.com/{parsed.Value.owner}/{parsed.Value.repo}/pull/{prNum}";
                        }
                    }

                    result = (worktreePath, string.IsNullOrEmpty(sessionName) ? null : sessionName, prGhUrl);
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                }
                else
                {
                    btnCreate.Enabled = true;
                    btnCreate.Text = "Create";
                    MessageBox.Show($"Failed to create workspace:\n{error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else if (rdoFromIssue.Checked)
            {
                // Issue mode
                var remoteName = cmbIssueRemote.SelectedItem?.ToString();
                var issueText = txtIssueNumber.Text.Trim();
                if (string.IsNullOrEmpty(remoteName) || !int.TryParse(issueText, out var issueNum) || issueNum <= 0)
                {
                    MessageBox.Show("Enter a valid issue number.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!issueValidated)
                {
                    MessageBox.Show("Please validate the issue first.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var sessionName = txtSessionName.Text.Trim();
                var branchName = Models.LauncherSettings.FormatBranchName(
                    Program._settings.IssueBranchPattern, issueNum, sessionName);
                var baseBranch = cmbIssueBaseBranch.SelectedItem?.ToString() ?? "main";

                btnCreate.Enabled = false;
                btnCreate.Text = "Creating...";
                var (worktreePath, success, error) = WorkspaceCreationService.CreateWorkspace(
                    repoPath, repoFolderName!, branchName, baseBranch);
                if (success)
                {
                    result = (worktreePath, string.IsNullOrEmpty(sessionName) ? null : sessionName, issueGitHubUrl);
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                }
                else
                {
                    btnCreate.Enabled = true;
                    btnCreate.Text = "Create";
                    MessageBox.Show($"Failed to create workspace:\n{error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else if (rdoNewBranch.Checked)
            {
                // New branch mode
                var workspaceName = txtName.Text.Trim();
                if (string.IsNullOrEmpty(workspaceName))
                {
                    MessageBox.Show("Branch name is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var selectedBaseBranch = cmbBranch.SelectedItem?.ToString()?.TrimStart('*', ' ') ?? "main";
                var (worktreePath, success, error) = WorkspaceCreationService.CreateWorkspace(
                    repoPath, repoFolderName!, workspaceName, selectedBaseBranch);
                if (success)
                {
                    var sessionName = txtSessionName.Text.Trim();
                    result = (worktreePath, string.IsNullOrEmpty(sessionName) ? null : sessionName, null);
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                }
                else
                {
                    MessageBox.Show($"Failed to create workspace:\n{error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                // Existing branch mode
                var selectedBaseBranch = cmbBranch.SelectedItem?.ToString()?.TrimStart('*', ' ') ?? "main";
                var (worktreePath, success, error) = WorkspaceCreationService.CreateWorkspaceFromExistingBranch(
                    repoPath, repoFolderName!, selectedBaseBranch);
                if (success)
                {
                    var sessionName = txtSessionName.Text.Trim();
                    result = (worktreePath, string.IsNullOrEmpty(sessionName) ? null : sessionName, null);
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                }
                else
                {
                    MessageBox.Show($"Failed to create workspace:\n{error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        };

        form.AcceptButton = btnCreate;
        form.CancelButton = btnCancel;

        return form.ShowDialog() == DialogResult.OK ? result : null;
    }
}
