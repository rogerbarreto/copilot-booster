using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CopilotBooster.Models;
using CopilotBooster.Services;

namespace CopilotBooster.Forms;

/// <summary>
/// Provides a modal dialog for creating a new git worktree workspace.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class WorkspaceCreatorVisuals
{

    /// <summary>
    /// Displays a modal dialog for creating a git worktree workspace from the specified repository.
    /// </summary>
    /// <param name="repoPath">The git repository root path.</param>
    /// <returns>A tuple of worktree path and optional session name on success, or <c>null</c> if the user cancels.</returns>
    internal static WorkspaceCreatorResult? ShowWorkspaceCreator(string repoPath, GitHubApiService? api = null)
    {
        WorkspaceCreatorResult? result = null;
        var repoFolderName = Path.GetFileName(repoPath);

        const int FormWidthValue = 500;
        const int CollapsedHeight = 390;
        const int ExpandedHeight = 460;

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
            Text = "Create New Worktree",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Width = FormWidthValue,
            Height = CollapsedHeight,
            TopMost = Program._settings.AlwaysOnTop
        };
        SettingsVisuals.AlignWithParent(form);

        bool isCreating = false;

        form.FormClosing += (s, e) =>
        {
            if (isCreating && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
            }
        };

        if (Program.AppIcon != null)
        {
            form.Icon = Program.AppIcon;
        }

        int y = 12;

        // Subtitle
        var lblSubtitle = new Label
        {
            Text = "Set up a new isolated worktree for your coding session",
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
            Text = "A descriptive name for your worktree (becomes the branch name)",
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
            Text = "The branch to create the worktree from",
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f),
            AutoSize = true,
            Location = new Point(14, y + 46)
        };
        form.Controls.Add(lblBranchHelper);

        var chkUpdateSource = new CheckBox
        {
            Text = "Update from upstream first",
            AutoSize = true,
            Checked = Program._settings.UpdateSourceBranchBeforeCreate,
            Location = new Point(14, y + 68)
        };
        form.Controls.Add(chkUpdateSource);

        var lblUpdateSourceHelper = new Label
        {
            Text = "Runs git fetch and fast-forwards the source branch before creating the worktree.",
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f),
            AutoSize = true,
            Location = new Point(14, y + 90)
        };
        form.Controls.Add(lblUpdateSourceHelper);

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
        WorkspaceGitHubLink? fetchedPrGitHubLink = null;

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

        var chkIssueOverrideBranch = new CheckBox
        {
            Text = "Override branch name",
            AutoSize = true,
            Location = new Point(14, y),
            Visible = false
        };
        form.Controls.Add(chkIssueOverrideBranch);

        var txtIssueBranchName = new TextBox
        {
            Location = new Point(14, y),
            Width = 450,
            ReadOnly = true,
            Visible = false
        };
        var txtIssueBranchNameWrapper = SettingsVisuals.WrapWithBorder(txtIssueBranchName);
        txtIssueBranchNameWrapper.Visible = false;
        form.Controls.Add(txtIssueBranchNameWrapper);

        void UpdateCalculatedIssueBranchName()
        {
            if (!chkIssueOverrideBranch.Checked)
            {
                var (num, _) = ParseSmartInput(txtIssueNumber.Text);
                if (num > 0)
                {
                    var alias = !string.IsNullOrWhiteSpace(txtSessionName.Text) ? txtSessionName.Text.Trim() : null;
                    txtIssueBranchName.Text = LauncherSettings.FormatBranchName(
                        Program._settings.IssueBranchPattern, num, alias);
                }
                else
                {
                    txtIssueBranchName.Text = "";
                }
            }
        }

        chkIssueOverrideBranch.CheckedChanged += (s, e) =>
        {
            txtIssueBranchName.ReadOnly = !chkIssueOverrideBranch.Checked;
            if (!chkIssueOverrideBranch.Checked)
            {
                UpdateCalculatedIssueBranchName();
            }
        };
        bool issueValidated = false;
        string? fetchedIssueTitle = null;
        WorkspaceGitHubLink? fetchedIssueGitHubLink = null;

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
            bool isBranchMode = isExistingBranch || isNewBranch;

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
            lblBranch.Visible = isBranchMode;
            cmbBranch.Visible = isBranchMode;
            lblBranchHelper.Visible = isBranchMode;
            chkUpdateSource.Visible = isBranchMode;
            lblUpdateSourceHelper.Visible = isBranchMode;

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
            chkIssueOverrideBranch.Visible = isIssueMode;
            txtIssueBranchName.Visible = isIssueMode;
            txtIssueBranchNameWrapper.Visible = isIssueMode;

            if (!isIssueMode)
            {
                chkUseIssueTitle.Visible = false;
                chkIssueOverrideBranch.Checked = false;
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

                // Override branch name
                chkIssueOverrideBranch.Location = new Point(14, cy);
                cy += 24;

                txtIssueBranchName.Location = new Point(14, cy);
                txtIssueBranchNameWrapper.Location = new Point(14, cy);
                cy += 30;

                UpdateCalculatedIssueBranchName();

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
                chkUpdateSource.Location = new Point(14, cy + 68);
                lblUpdateSourceHelper.Location = new Point(14, cy + 90);
                lblPreview.Location = new Point(14, cy + 116);

                int buttonY = cy + 148;
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
                chkUpdateSource.Location = new Point(14, cy + 68);
                lblUpdateSourceHelper.Location = new Point(14, cy + 90);
                lblPreview.Location = new Point(14, cy + 116);

                int buttonY = cy + 148;
                btnCreate.Location = new Point(300, buttonY);
                btnCancel.Location = new Point(390, buttonY);

                btnCreate.Enabled = true;
                form.Height = CollapsedHeight;
            }
        }

        static (int number, GitHubRef? parsedUrl) ParseSmartInput(string raw)
        {
            var trimmed = raw.Trim();
            if (int.TryParse(trimmed, out var number) && number > 0)
            {
                return (number, null);
            }

            if (GitHubLinkService.TryParseIssueOrPrUrl(trimmed, out var parsedUrl))
            {
                return (parsedUrl.Number, parsedUrl);
            }

            return (0, null);
        }

        bool TryGetRemoteOwnerRepo(string remoteName, out string owner, out string repo)
        {
            owner = "";
            repo = "";

            var remoteUrl = GitService.GetRemoteUrl(repoPath, remoteName);
            if (string.IsNullOrEmpty(remoteUrl))
            {
                return false;
            }

            var parsed = GitService.ParseGitHubOwnerRepo(remoteUrl);
            if (!parsed.HasValue)
            {
                return false;
            }

            (owner, repo) = parsed.Value;
            return true;
        }

        int FindRemoteIndex(ComboBox combo, GitHubRef parsedUrl)
        {
            for (var i = 0; i < combo.Items.Count; i++)
            {
                var remoteName = combo.Items[i]?.ToString();
                if (remoteName != null
                    && TryGetRemoteOwnerRepo(remoteName, out var owner, out var repo)
                    && owner.Equals(parsedUrl.Owner, StringComparison.OrdinalIgnoreCase)
                    && repo.Equals(parsedUrl.Repo, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        void UpdatePreview()
        {
            if (rdoFromPr.Checked)
            {
                var (prNum, _) = ParseSmartInput(txtPrNumber.Text);
                if (prNum > 0)
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
                var branchName = txtIssueBranchName.Text.Trim();
                lblPreview.Text = string.IsNullOrEmpty(branchName)
                    ? ""
                    : WorkspaceCreationService.BuildWorkspacePath(repoFolderName!, branchName);
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
            fetchedPrGitHubLink = null;
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
            fetchedIssueGitHubLink = null;
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

            var remoteName = cmbRemote.SelectedItem?.ToString() ?? "";
            var (prNum, parsedUrl) = ParseSmartInput(txtPrNumber.Text);
            if (string.IsNullOrEmpty(remoteName) || prNum <= 0)
            {
                lblPrValidation.Text = "Enter a valid PR number.";
                lblPrValidation.ForeColor = Color.Red;
                prValidated = false;
                btnCreate.Enabled = false;
                return;
            }

            var statusLines = new List<string>();
            if (parsedUrl is { } urlRef)
            {
                var remoteIndex = FindRemoteIndex(cmbRemote, urlRef);
                if (remoteIndex < 0)
                {
                    lblPrValidation.Text = $"❌ This URL points to {urlRef.Owner}/{urlRef.Repo}, which is not a configured remote.";
                    lblPrValidation.ForeColor = Color.Red;
                    prValidated = false;
                    btnCreate.Enabled = false;
                    return;
                }

                if (cmbRemote.SelectedIndex != remoteIndex)
                {
                    cmbRemote.SelectedIndex = remoteIndex;
                    remoteName = cmbRemote.SelectedItem?.ToString() ?? "";
                    statusLines.Add($"🔀 Switched remote to {remoteName} ({urlRef.Owner}/{urlRef.Repo})");
                }

                // Keep this panel selected; only the validation/tracked-link type follows the URL.
                statusLines.Add(urlRef.Type == GitHubRefType.Pr
                    ? "ℹ Detected PR from URL — validating as PR"
                    : "ℹ Detected Issue from URL — validating as Issue");
            }

            var itemType = parsedUrl?.Type ?? GitHubRefType.Pr;
            var itemTypeLabel = itemType == GitHubRefType.Pr ? "PR" : "Issue";

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
            string? prEffectiveState = null;
            bool prDraft = false;
            string? prAuthor = null;
            string? prUpdatedAt = null;
            string? prOwner = null;
            string? prRepo = null;
            string? prStateReason = null;
            List<string> prLabels = [];
            try
            {
                (found, prTitle, prHeadBranch, prEffectiveState, prDraft, prAuthor, prUpdatedAt, prOwner, prRepo, prStateReason, prLabels) = await Task.Run(async () =>
                {
                    var valid = itemType == GitHubRefType.Pr
                        && GitService.ValidatePrRef(repoPath, remoteName, platform, prNum);
                    string? title = null;
                    string? headRef = null;
                    string? state = null;
                    bool draft = false;
                    string? author = null;
                    string? updatedAt = null;
                    string? extractedOwner = null;
                    string? extractedRepo = null;
                    string? stateReason = null;
                    var labels = new List<string>();

                    if (platform == GitService.HostingPlatform.GitHub && api != null)
                    {
                        try
                        {
                            if (TryGetRemoteOwnerRepo(remoteName, out var owner, out var repo))
                            {
                                if (itemType == GitHubRefType.Pr && valid)
                                {
                                    using var doc = await api.GetPullRequestAsync(owner, repo, prNum).ConfigureAwait(false);
                                    if (doc != null)
                                    {
                                        var root = doc.RootElement;
                                        title = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
                                        headRef = root.TryGetProperty("head", out var headProp) && headProp.TryGetProperty("ref", out var refProp) ? refProp.GetString() : null;
                                        var rawState = root.TryGetProperty("state", out var sp) ? sp.GetString() ?? "open" : "open";
                                        draft = root.TryGetProperty("draft", out var dp) && dp.GetBoolean();
                                        author = root.TryGetProperty("user", out var up) && up.TryGetProperty("login", out var lp) ? lp.GetString() ?? "" : "";
                                        var merged = root.TryGetProperty("merged", out var mp) && mp.GetBoolean();
                                        updatedAt = root.TryGetProperty("updated_at", out var uap) ? uap.GetString() ?? "" : "";
                                        state = merged ? "merged" : rawState;
                                        extractedOwner = owner;
                                        extractedRepo = repo;
                                    }
                                }
                                else if (itemType == GitHubRefType.Issue)
                                {
                                    using var doc = await api.GetIssueAsync(owner, repo, prNum).ConfigureAwait(false);
                                    if (doc != null)
                                    {
                                        valid = true;
                                        var root = doc.RootElement;
                                        title = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
                                        state = root.TryGetProperty("state", out var sp) ? sp.GetString() ?? "open" : "open";
                                        author = root.TryGetProperty("user", out var up) && up.TryGetProperty("login", out var lp) ? lp.GetString() ?? "" : "";
                                        updatedAt = root.TryGetProperty("updated_at", out var uap) ? uap.GetString() ?? "" : "";
                                        stateReason = root.TryGetProperty("state_reason", out var srp) && srp.ValueKind != System.Text.Json.JsonValueKind.Null ? srp.GetString() : null;
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

                                        extractedOwner = owner;
                                        extractedRepo = repo;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Service failure — don't affect the flow
                        }
                    }

                    return (valid, title, headRef, state, draft, author, updatedAt, extractedOwner, extractedRepo, stateReason, labels);
                }).ConfigureAwait(true);
            }
            catch
            {
                found = false;
            }

            if (found)
            {
                statusLines.Add(prTitle != null
                    ? $"✅ {itemTypeLabel} #{prNum}: {prTitle}"
                    : $"✅ {itemTypeLabel} #{prNum} found");
                lblPrValidation.Text = string.Join("\n", statusLines);
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

                if (prOwner != null && prRepo != null)
                {
                    var item = new GitHubTrackedItem
                    {
                        Type = itemType == GitHubRefType.Pr ? "pr" : "issue",
                        Number = prNum,
                        State = prEffectiveState ?? "open",
                        Title = prTitle ?? "",
                        Author = prAuthor ?? "",
                        LastModifiedAt = prUpdatedAt ?? "",
                        LastSeenAt = DateTime.UtcNow.ToString("o"),
                    };

                    if (itemType == GitHubRefType.Pr)
                    {
                        item.Draft = prDraft;
                        item.HeadBranch = prHeadBranch ?? "";
                    }
                    else
                    {
                        item.StateReason = prStateReason;
                        item.Labels = prLabels;
                    }

                    fetchedPrGitHubLink = new WorkspaceGitHubLink
                    {
                        Owner = prOwner,
                        Repo = prRepo,
                        Item = item,
                    };
                }
            }
            else
            {
                lblPrValidation.Text = $"❌ {itemTypeLabel} #{prNum} not found";
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

            var remoteName = cmbIssueRemote.SelectedItem?.ToString() ?? "";
            var (issueNum, parsedUrl) = ParseSmartInput(txtIssueNumber.Text);
            if (string.IsNullOrEmpty(remoteName) || issueNum <= 0)
            {
                lblIssueValidation.Text = "Enter a valid issue number.";
                lblIssueValidation.ForeColor = Color.Red;
                issueValidated = false;
                btnCreate.Enabled = false;
                return;
            }

            var statusLines = new List<string>();
            if (parsedUrl is { } urlRef)
            {
                var remoteIndex = FindRemoteIndex(cmbIssueRemote, urlRef);
                if (remoteIndex < 0)
                {
                    lblIssueValidation.Text = $"❌ This URL points to {urlRef.Owner}/{urlRef.Repo}, which is not a configured remote.";
                    lblIssueValidation.ForeColor = Color.Red;
                    issueValidated = false;
                    btnCreate.Enabled = false;
                    return;
                }

                if (cmbIssueRemote.SelectedIndex != remoteIndex)
                {
                    cmbIssueRemote.SelectedIndex = remoteIndex;
                    remoteName = cmbIssueRemote.SelectedItem?.ToString() ?? "";
                    statusLines.Add($"🔀 Switched remote to {remoteName} ({urlRef.Owner}/{urlRef.Repo})");
                }

                // Keep this panel selected; only the validation/tracked-link type follows the URL.
                statusLines.Add(urlRef.Type == GitHubRefType.Pr
                    ? "ℹ Detected PR from URL — validating as PR"
                    : "ℹ Detected Issue from URL — validating as Issue");
            }

            var itemType = parsedUrl?.Type ?? GitHubRefType.Issue;
            var itemTypeLabel = itemType == GitHubRefType.Pr ? "PR" : "Issue";

            isValidatingIssue = true;
            lblIssueValidation.Text = "Checking...";
            lblIssueValidation.ForeColor = Color.Gray;
            btnCheckIssue.Enabled = false;

            bool found = false;
            string? issueTitle = null;
            string? issueExtOwner = null;
            string? issueExtRepo = null;
            string? issueExtState = null;
            string? issueExtStateReason = null;
            string? issueExtAuthor = null;
            string? issueExtUpdatedAt = null;
            string? issueHeadBranch = null;
            bool issueDraft = false;
            List<string> issueExtLabels = [];
            try
            {
                (found, issueTitle, issueExtOwner, issueExtRepo, issueExtState, issueExtStateReason, issueExtAuthor, issueExtUpdatedAt, issueExtLabels, issueHeadBranch, issueDraft) = await Task.Run(async () =>
                {
                    string? title = null;
                    string? extractedOwner = null;
                    string? extractedRepo = null;
                    string? state = null;
                    string? stateReason = null;
                    string? author = null;
                    string? updatedAt = null;
                    string? headBranch = null;
                    bool draft = false;
                    var labels = new List<string>();
                    bool valid = false;

                    try
                    {
                        if (TryGetRemoteOwnerRepo(remoteName, out var owner, out var repo) && api != null)
                        {
                            if (itemType == GitHubRefType.Issue)
                            {
                                using var doc = await api.GetIssueAsync(owner, repo, issueNum).ConfigureAwait(false);
                                if (doc != null)
                                {
                                    valid = true;
                                    var root = doc.RootElement;
                                    title = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
                                    state = root.TryGetProperty("state", out var sp) ? sp.GetString() ?? "open" : "open";
                                    author = root.TryGetProperty("user", out var up) && up.TryGetProperty("login", out var lp) ? lp.GetString() ?? "" : "";
                                    updatedAt = root.TryGetProperty("updated_at", out var uap) ? uap.GetString() ?? "" : "";
                                    stateReason = root.TryGetProperty("state_reason", out var srp) && srp.ValueKind != System.Text.Json.JsonValueKind.Null ? srp.GetString() : null;
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

                                    extractedOwner = owner;
                                    extractedRepo = repo;
                                }
                            }
                            else
                            {
                                using var doc = await api.GetPullRequestAsync(owner, repo, issueNum).ConfigureAwait(false);
                                if (doc != null)
                                {
                                    valid = true;
                                    var root = doc.RootElement;
                                    title = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
                                    headBranch = root.TryGetProperty("head", out var headProp) && headProp.TryGetProperty("ref", out var refProp) ? refProp.GetString() : null;
                                    var rawState = root.TryGetProperty("state", out var sp) ? sp.GetString() ?? "open" : "open";
                                    draft = root.TryGetProperty("draft", out var dp) && dp.GetBoolean();
                                    author = root.TryGetProperty("user", out var up) && up.TryGetProperty("login", out var lp) ? lp.GetString() ?? "" : "";
                                    var merged = root.TryGetProperty("merged", out var mp) && mp.GetBoolean();
                                    updatedAt = root.TryGetProperty("updated_at", out var uap) ? uap.GetString() ?? "" : "";
                                    state = merged ? "merged" : rawState;
                                    extractedOwner = owner;
                                    extractedRepo = repo;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Service failure
                    }

                    return (valid, title, extractedOwner, extractedRepo, state, stateReason, author, updatedAt, labels, headBranch, draft);
                }).ConfigureAwait(true);
            }
            catch
            {
                found = false;
            }

            if (found)
            {
                statusLines.Add(issueTitle != null
                    ? $"✅ {itemTypeLabel} #{issueNum}: {issueTitle}"
                    : $"✅ {itemTypeLabel} #{issueNum} found");
                lblIssueValidation.Text = string.Join("\n", statusLines);
                lblIssueValidation.ForeColor = Color.Green;
                issueValidated = true;
                btnCreate.Enabled = true;

                if (issueTitle != null)
                {
                    fetchedIssueTitle = issueTitle;
                    chkUseIssueTitle.Visible = true;
                    RelayoutControls();
                }

                if (issueExtOwner != null && issueExtRepo != null)
                {
                    var item = new GitHubTrackedItem
                    {
                        Type = itemType == GitHubRefType.Pr ? "pr" : "issue",
                        Number = issueNum,
                        State = issueExtState ?? "open",
                        Title = issueTitle ?? "",
                        Author = issueExtAuthor ?? "",
                        LastModifiedAt = issueExtUpdatedAt ?? "",
                        LastSeenAt = DateTime.UtcNow.ToString("o"),
                    };

                    if (itemType == GitHubRefType.Pr)
                    {
                        item.Draft = issueDraft;
                        item.HeadBranch = issueHeadBranch ?? "";
                    }
                    else
                    {
                        item.StateReason = issueExtStateReason;
                        item.Labels = issueExtLabels;
                    }

                    fetchedIssueGitHubLink = new WorkspaceGitHubLink
                    {
                        Owner = issueExtOwner,
                        Repo = issueExtRepo,
                        Item = item,
                    };
                }
            }
            else
            {
                lblIssueValidation.Text = $"❌ {itemTypeLabel} #{issueNum} not found";
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
        txtSessionName.TextChanged += (s, e) =>
        {
            UpdateCalculatedIssueBranchName();
            UpdatePreview();
        };

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
                var (prNum, _) = ParseSmartInput(txtPrNumber.Text);
                if (string.IsNullOrEmpty(remoteName) || prNum <= 0)
                {
                    MessageBox.Show("Enter a valid PR number.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!prValidated)
                {
                    MessageBox.Show("Please validate the PR first.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var sessionName = txtSessionName.Text.Trim();
                var isIssueUrl = fetchedPrGitHubLink?.Item.IsPr == false;
                isCreating = true;
                btnCreate.Enabled = false;
                btnCreate.Text = "Creating...";
                var (worktreePath, success, error) = isIssueUrl
                    ? await WorkspaceCreationService.CreateWorkspaceAsync(
                        repoPath,
                        repoFolderName!,
                        LauncherSettings.FormatBranchName(Program._settings.IssueBranchPattern, prNum, sessionName),
                        cmbIssueBaseBranch.SelectedItem?.ToString() ?? "main").ConfigureAwait(true)
                    : await WorkspaceCreationService.CreateWorkspaceFromPrAsync(
                        repoPath,
                        repoFolderName!,
                        remoteName,
                        prNum,
                        remotePlatforms[remoteName],
                        fetchedPrHeadBranch).ConfigureAwait(true);
                isCreating = false;
                if (success)
                {
                    result = new WorkspaceCreatorResult
                    {
                        WorktreePath = worktreePath,
                        SessionName = string.IsNullOrEmpty(sessionName) ? null : sessionName,
                        GitHubLink = fetchedPrGitHubLink,
                    };
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                }
                else
                {
                    btnCreate.Enabled = true;
                    btnCreate.Text = "Create";
                    MessageBox.Show($"Failed to create worktree:\n{error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else if (rdoFromIssue.Checked)
            {
                // Issue mode
                var remoteName = cmbIssueRemote.SelectedItem?.ToString();
                var (issueNum, _) = ParseSmartInput(txtIssueNumber.Text);
                if (string.IsNullOrEmpty(remoteName) || issueNum <= 0)
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
                var isPrUrl = fetchedIssueGitHubLink?.Item.IsPr == true;

                isCreating = true;
                btnCreate.Enabled = false;
                btnCreate.Text = "Creating...";
                var (worktreePath, success, error) = isPrUrl
                    ? await WorkspaceCreationService.CreateWorkspaceFromPrAsync(
                        repoPath,
                        repoFolderName!,
                        remoteName,
                        issueNum,
                        remotePlatforms[remoteName],
                        headBranch: fetchedIssueGitHubLink?.Item.HeadBranch).ConfigureAwait(true)
                    : await WorkspaceCreationService.CreateWorkspaceAsync(
                        repoPath,
                        repoFolderName!,
                        string.IsNullOrEmpty(txtIssueBranchName.Text.Trim())
                            ? LauncherSettings.FormatBranchName(Program._settings.IssueBranchPattern, issueNum, sessionName)
                            : txtIssueBranchName.Text.Trim(),
                        cmbIssueBaseBranch.SelectedItem?.ToString() ?? "main").ConfigureAwait(true);
                isCreating = false;
                if (success)
                {
                    result = new WorkspaceCreatorResult
                    {
                        WorktreePath = worktreePath,
                        SessionName = string.IsNullOrEmpty(sessionName) ? null : sessionName,
                        GitHubLink = fetchedIssueGitHubLink,
                    };
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                }
                else
                {
                    btnCreate.Enabled = true;
                    btnCreate.Text = "Create";
                    MessageBox.Show($"Failed to create worktree:\n{error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                var sourceRef = selectedBaseBranch;
                isCreating = true;
                btnCreate.Enabled = false;
                if (chkUpdateSource.Checked)
                {
                    btnCreate.Text = "Updating...";
                    var (updateOk, updateErr, effectiveSourceRef) = await WorkspaceCreationService.UpdateSourceBranchAsync(
                        repoPath, sourceRef, CancellationToken.None).ConfigureAwait(true);
                    if (!updateOk)
                    {
                        var proceed = MessageBox.Show(form,
                            $"Couldn't update from upstream:\n{updateErr}\n\nCreate worktree anyway with current state?",
                            "Update Failed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (proceed == DialogResult.No)
                        {
                            isCreating = false;
                            btnCreate.Enabled = true;
                            btnCreate.Text = "Create";
                            return;
                        }
                    }
                    else
                    {
                        sourceRef = effectiveSourceRef;
                    }
                }

                btnCreate.Text = "Creating...";
                var (worktreePath, success, error) = await WorkspaceCreationService.CreateWorkspaceAsync(
                    repoPath, repoFolderName!, workspaceName, sourceRef).ConfigureAwait(true);
                isCreating = false;
                if (success)
                {
                    var sessionName = txtSessionName.Text.Trim();
                    result = new WorkspaceCreatorResult { WorktreePath = worktreePath, SessionName = string.IsNullOrEmpty(sessionName) ? null : sessionName, GitHubLink = null };
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                }
                else
                {
                    btnCreate.Enabled = true;
                    btnCreate.Text = "Create";
                    MessageBox.Show($"Failed to create worktree:\n{error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                // Existing branch mode
                var selectedBaseBranch = cmbBranch.SelectedItem?.ToString()?.TrimStart('*', ' ') ?? "main";
                var sourceRef = selectedBaseBranch;
                isCreating = true;
                btnCreate.Enabled = false;
                if (chkUpdateSource.Checked)
                {
                    btnCreate.Text = "Updating...";
                    var (updateOk, updateErr, effectiveSourceRef) = await WorkspaceCreationService.UpdateSourceBranchAsync(
                        repoPath, sourceRef, CancellationToken.None).ConfigureAwait(true);
                    if (!updateOk)
                    {
                        var proceed = MessageBox.Show(form,
                            $"Couldn't update from upstream:\n{updateErr}\n\nCreate worktree anyway with current state?",
                            "Update Failed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (proceed == DialogResult.No)
                        {
                            isCreating = false;
                            btnCreate.Enabled = true;
                            btnCreate.Text = "Create";
                            return;
                        }
                    }
                    else
                    {
                        sourceRef = effectiveSourceRef;
                    }
                }

                btnCreate.Text = "Creating...";
                var (worktreePath, success, error) = await WorkspaceCreationService.CreateWorkspaceFromExistingBranchAsync(
                    repoPath, repoFolderName!, sourceRef).ConfigureAwait(true);
                isCreating = false;
                if (success)
                {
                    var sessionName = txtSessionName.Text.Trim();
                    result = new WorkspaceCreatorResult { WorktreePath = worktreePath, SessionName = string.IsNullOrEmpty(sessionName) ? null : sessionName, GitHubLink = null };
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                }
                else
                {
                    btnCreate.Enabled = true;
                    btnCreate.Text = "Create";
                    MessageBox.Show($"Failed to create worktree:\n{error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        };

        form.AcceptButton = btnCreate;
        form.CancelButton = btnCancel;

        return form.ShowDialog() == DialogResult.OK ? result : null;
    }
}

internal struct WorkspaceCreatorResult
{
    public string WorktreePath;
    public string? SessionName;
    public WorkspaceGitHubLink? GitHubLink;
}

internal struct WorkspaceGitHubLink
{
    public string Owner;
    public string Repo;
    public GitHubTrackedItem Item;
}
