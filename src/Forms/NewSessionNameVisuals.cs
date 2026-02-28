using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using CopilotBooster.Services;

namespace CopilotBooster.Forms;

internal enum BranchAction { None, ExistingBranch, NewBranch, FromPr, FromIssue }

internal record NewSessionResult(
    string? SessionName,
    BranchAction Action,
    string? BranchName,
    string? BaseBranch,
    string? Remote,
    int? PrNumber,
    GitService.HostingPlatform? Platform,
    string? HeadBranch = null,
    int? IssueNumber = null,
    string? GitHubUrl = null);

/// <summary>
/// Provides a modal dialog for naming a new Copilot session.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class NewSessionNameVisuals
{
    private static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Displays a modal dialog prompting the user for a session name.
    /// </summary>
    /// <returns>The session name on OK, or <c>null</c> if the user cancels.</returns>
    internal static string? ShowNamePrompt()
    {
        string? result = null;

        var form = new Form
        {
            Text = "New Copilot Session",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Width = 500,
            Height = 220,
            TopMost = Program._settings.AlwaysOnTop
        };
        SettingsVisuals.AlignWithParent(form);

        if (Program.AppIcon != null)
        {
            form.Icon = Program.AppIcon;
        }

        int y = 12;

        // Subtitle
        var lblSubtitle = new Label
        {
            Text = "Give your session an alias",
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 8.5f),
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblSubtitle);
        y += 28;

        // Session Alias
        var lblName = new Label
        {
            Text = "Session Alias",
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblName);
        y += 20;

        var txtName = new TextBox
        {
            PlaceholderText = "e.g., Feature: User Authentication",
            Location = new Point(14, y),
            Width = 450
        };
        form.Controls.Add(SettingsVisuals.WrapWithBorder(txtName));
        y += 26;

        var lblHelper = new Label
        {
            Text = "A stable label for your session (required)",
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f),
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblHelper);
        y += 28;

        // Buttons
        var btnOk = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.None,
            Width = 80,
            Location = new Point(300, y),
            Enabled = false
        };

        var btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 80,
            Location = new Point(390, y)
        };

        txtName.TextChanged += (s, e) =>
        {
            btnOk.Enabled = !string.IsNullOrWhiteSpace(txtName.Text);
        };

        btnOk.Click += (s, e) =>
        {
            result = txtName.Text.Trim();
            form.DialogResult = DialogResult.OK;
            form.Close();
        };

        form.Controls.Add(btnOk);
        form.Controls.Add(btnCancel);
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        return form.ShowDialog() == DialogResult.OK ? result : null;
    }

    /// <summary>
    /// Displays an enhanced modal dialog with optional branch/PR selection for a git repository.
    /// Falls back to the simple name-only dialog when <paramref name="repoPath"/> is not inside a git repo.
    /// </summary>
    internal static NewSessionResult? ShowNamePrompt(string repoPath)
    {
        var gitRoot = SessionService.FindGitRoot(repoPath);
        if (gitRoot == null)
        {
            var simpleName = ShowNamePrompt();
            return simpleName == null
                ? null
                : new NewSessionResult(simpleName, BranchAction.None, null, null, null, null, null);
        }

        var branches = WorkspaceCreationService.GetBranches(gitRoot);
        var remotes = GitService.GetRemotes(gitRoot);
        var currentBranch = WorkspaceCreationService.GetCurrentBranch(gitRoot);

        var remotePlatforms = new Dictionary<string, GitService.HostingPlatform>();
        foreach (var remote in remotes)
        {
            var url = GitService.GetRemoteUrl(gitRoot, remote);
            if (!string.IsNullOrEmpty(url))
            {
                var platform = GitService.DetectHostingPlatform(url);
                if (platform != GitService.HostingPlatform.Unknown)
                {
                    remotePlatforms[remote] = platform;
                }
            }
        }

        NewSessionResult? result = null;

        const int FormWidthValue = 500;
        const int SameBranchHeight = 220;

        var form = new Form
        {
            Text = "New Copilot Session",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Width = FormWidthValue,
            Height = SameBranchHeight,
            TopMost = Program._settings.AlwaysOnTop
        };
        SettingsVisuals.AlignWithParent(form);

        if (Program.AppIcon != null)
        {
            form.Icon = Program.AppIcon;
        }

        int y = 12;

        // Subtitle
        var lblSubtitle = new Label
        {
            Text = "Give your session an alias",
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 8.5f),
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblSubtitle);
        y += 28;

        // Session Alias
        var lblSessionName = new Label
        {
            Text = "Session Alias",
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
            Text = "A stable label for your session (required)",
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f),
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblSessionNameHelper);
        y += 22;

        // --- Branch section ---
        var lblBranchSection = new Label
        {
            Text = "Branch",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 9f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblBranchSection);
        y += 22;

        // Radio buttons — horizontal layout
        var rdoSameBranch = new RadioButton
        {
            Text = "Same branch",
            AutoSize = true,
            Location = new Point(14, y),
            Checked = true
        };
        form.Controls.Add(rdoSameBranch);

        var rdoSwitchBranch = new RadioButton
        {
            Text = "Switch branch",
            AutoSize = true,
            Location = new Point(160, y)
        };
        form.Controls.Add(rdoSwitchBranch);

        var rdoNewBranch = new RadioButton
        {
            Text = "New branch",
            AutoSize = true,
            Location = new Point(310, y)
        };
        form.Controls.Add(rdoNewBranch);
        y += 26;

        // Second row of radio buttons
        var rdoFromPr = new RadioButton
        {
            Text = "From PR #",
            AutoSize = true,
            Location = new Point(14, y),
            Visible = remotePlatforms.Count > 0
        };
        form.Controls.Add(rdoFromPr);

        var rdoFromIssue = new RadioButton
        {
            Text = "From Issue #",
            AutoSize = true,
            Location = new Point(160, y),
            Visible = remotePlatforms.Count > 0
        };
        form.Controls.Add(rdoFromIssue);
        y += 26;

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

        var modeStartY = y;

        // --- Switch Branch controls (hidden by default) ---
        var lblBranch = new Label
        {
            Text = "Branch",
            AutoSize = true,
            Location = new Point(14, y),
            Visible = false
        };
        form.Controls.Add(lblBranch);

        var cmbBranch = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(14, y + 20),
            Width = 450,
            Visible = false
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
            Text = "The existing branch to switch to",
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f),
            AutoSize = true,
            Location = new Point(14, y + 46),
            Visible = false
        };
        form.Controls.Add(lblBranchHelper);

        // --- New Branch controls (hidden by default) ---
        var lblNewBranchName = new Label
        {
            Text = "Branch Name *",
            AutoSize = true,
            Location = new Point(14, y),
            Visible = false
        };
        form.Controls.Add(lblNewBranchName);

        var txtNewBranchName = new TextBox
        {
            PlaceholderText = "e.g., issues/123-new-issue",
            Location = new Point(14, y + 20),
            Width = 450,
            Visible = false
        };
        var txtNewBranchNameWrapper = SettingsVisuals.WrapWithBorder(txtNewBranchName);
        txtNewBranchNameWrapper.Visible = false;
        form.Controls.Add(txtNewBranchNameWrapper);

        var lblNewBranchHelper = new Label
        {
            Text = "Name for the new branch",
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f),
            AutoSize = true,
            Location = new Point(14, y + 46),
            Visible = false
        };
        form.Controls.Add(lblNewBranchHelper);

        var lblBaseBranch = new Label
        {
            Text = "Base Branch",
            AutoSize = true,
            Location = new Point(14, y),
            Visible = false
        };
        form.Controls.Add(lblBaseBranch);

        var cmbBaseBranch = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(14, y + 20),
            Width = 450,
            Visible = false
        };
        foreach (var b in branches)
        {
            cmbBaseBranch.Items.Add(b);
        }
        if (cmbBaseBranch.Items.Contains("main"))
        {
            cmbBaseBranch.SelectedItem = "main";
        }
        else if (cmbBaseBranch.Items.Contains("master"))
        {
            cmbBaseBranch.SelectedItem = "master";
        }
        else if (cmbBaseBranch.Items.Count > 0)
        {
            cmbBaseBranch.SelectedIndex = 0;
        }
        form.Controls.Add(cmbBaseBranch);

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

        var lblIssueBranch = new Label
        {
            Text = "Base Branch",
            AutoSize = true,
            Location = new Point(14, y),
            Visible = false
        };
        form.Controls.Add(lblIssueBranch);

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

        // Buttons
        var btnOk = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.None,
            Width = 80,
            Enabled = false
        };

        var btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 80
        };

        // Alias is required — enable OK only when non-empty
        txtSessionName.TextChanged += (s, e) =>
        {
            bool hasAlias = !string.IsNullOrWhiteSpace(txtSessionName.Text);
            if (rdoFromPr.Checked)
            {
                btnOk.Enabled = hasAlias && prValidated;
            }
            else if (rdoFromIssue.Checked)
            {
                btnOk.Enabled = hasAlias && issueValidated;
            }
            else if (rdoNewBranch.Checked)
            {
                btnOk.Enabled = hasAlias && !string.IsNullOrWhiteSpace(txtNewBranchName.Text);
            }
            else
            {
                btnOk.Enabled = hasAlias;
            }
        };

        form.Controls.Add(btnOk);
        form.Controls.Add(btnCancel);

        // Layout helper — repositions controls based on selected mode
        void RelayoutControls()
        {
            int cy = modeStartY;
            bool isSwitchBranch = rdoSwitchBranch.Checked;
            bool isNewBranch = rdoNewBranch.Checked;
            bool isPrMode = rdoFromPr.Checked;
            bool isIssueMode = rdoFromIssue.Checked;
            bool isSameBranch = rdoSameBranch.Checked;

            // Current branch label — only visible in Same Branch mode
            lblCurrentBranch.Visible = isSameBranch;
            if (isSameBranch)
            {
                lblCurrentBranch.Location = new Point(14, cy);
                cy += 18;
            }

            // Branch dropdown (visible in Switch Branch mode only)
            lblBranch.Visible = isSwitchBranch;
            cmbBranch.Visible = isSwitchBranch;
            lblBranchHelper.Visible = isSwitchBranch;

            // New Branch controls
            lblNewBranchName.Visible = isNewBranch;
            txtNewBranchName.Visible = isNewBranch;
            txtNewBranchNameWrapper.Visible = isNewBranch;
            lblNewBranchHelper.Visible = isNewBranch;
            lblBaseBranch.Visible = isNewBranch;
            cmbBaseBranch.Visible = isNewBranch;

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
            lblIssueBranch.Visible = isIssueMode;
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

                cy += 8;

                // Buttons
                btnOk.Location = new Point(300, cy);
                btnCancel.Location = new Point(390, cy);

                btnOk.Enabled = prValidated && !string.IsNullOrWhiteSpace(txtSessionName.Text);
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
                lblIssueBranch.Location = new Point(14, cy);
                cmbIssueBaseBranch.Location = new Point(14, cy + 20);
                cy += 50;

                cy += 8;

                // Buttons
                btnOk.Location = new Point(300, cy);
                btnCancel.Location = new Point(390, cy);

                btnOk.Enabled = issueValidated && !string.IsNullOrWhiteSpace(txtSessionName.Text);
                form.Height = cy + 70;
            }
            else if (isNewBranch)
            {
                lblNewBranchName.Location = new Point(14, cy);
                txtNewBranchName.Location = new Point(14, cy + 20);
                txtNewBranchNameWrapper.Location = new Point(14, cy + 20);
                lblNewBranchHelper.Location = new Point(14, cy + 46);
                cy += 68;

                lblBaseBranch.Location = new Point(14, cy);
                cmbBaseBranch.Location = new Point(14, cy + 20);
                cy += 50;

                int buttonY = cy + 8;
                btnOk.Location = new Point(300, buttonY);
                btnCancel.Location = new Point(390, buttonY);

                btnOk.Enabled = !string.IsNullOrWhiteSpace(txtSessionName.Text)
                    && !string.IsNullOrWhiteSpace(txtNewBranchName.Text);
                form.Height = buttonY + 70;
            }
            else if (isSwitchBranch)
            {
                lblBranch.Location = new Point(14, cy);
                cmbBranch.Location = new Point(14, cy + 20);
                lblBranchHelper.Location = new Point(14, cy + 46);

                int buttonY = cy + 74;
                btnOk.Location = new Point(300, buttonY);
                btnCancel.Location = new Point(390, buttonY);

                btnOk.Enabled = !string.IsNullOrWhiteSpace(txtSessionName.Text);
                form.Height = buttonY + 70;
            }
            else
            {
                // Same branch — compact
                btnOk.Location = new Point(300, cy);
                btnCancel.Location = new Point(390, cy);

                btnOk.Enabled = !string.IsNullOrWhiteSpace(txtSessionName.Text);
                form.Height = cy + 70;
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
                btnOk.Enabled = false;
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
                btnOk.Enabled = false;
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
                btnOk.Enabled = false;
                return;
            }

            if (!remotePlatforms.TryGetValue(remoteName, out var detectedPlatform))
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
                (found, prTitle, prHeadBranch) = await Task.Run(async () =>
                {
                    var valid = GitService.ValidatePrRef(gitRoot, remoteName, detectedPlatform, prNum);
                    string? title = null;
                    string? headRef = null;

                    if (valid && detectedPlatform == GitService.HostingPlatform.GitHub)
                    {
                        try
                        {
                            var remoteUrl = GitService.GetRemoteUrl(gitRoot, remoteName);
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
                btnOk.Enabled = true;

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
                btnOk.Enabled = false;
            }

            btnCheck.Enabled = true;
            isValidating = false;
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
                btnOk.Enabled = false;
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
                        var remoteUrl = GitService.GetRemoteUrl(gitRoot, remoteName);
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
                btnOk.Enabled = !string.IsNullOrWhiteSpace(txtSessionName.Text);

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
                btnOk.Enabled = false;
            }

            btnCheckIssue.Enabled = true;
            isValidatingIssue = false;
        }

        // Wire up radio button changes
        void OnModeChanged(object? s, EventArgs e)
        {
            ResetPrValidation();
            ResetIssueValidation();
            RelayoutControls();
        }

        rdoSameBranch.CheckedChanged += OnModeChanged;
        rdoSwitchBranch.CheckedChanged += OnModeChanged;
        rdoNewBranch.CheckedChanged += OnModeChanged;
        rdoFromPr.CheckedChanged += OnModeChanged;
        rdoFromIssue.CheckedChanged += OnModeChanged;

        txtNewBranchName.TextChanged += (s, e) =>
        {
            if (rdoNewBranch.Checked)
            {
                btnOk.Enabled = !string.IsNullOrWhiteSpace(txtSessionName.Text)
                    && !string.IsNullOrWhiteSpace(txtNewBranchName.Text);
            }
        };

        txtPrNumber.TextChanged += (s, e) => ResetPrValidation();
        cmbRemote.SelectedIndexChanged += (s, e) => ResetPrValidation();

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

        txtIssueNumber.TextChanged += (s, e) => ResetIssueValidation();
        cmbIssueRemote.SelectedIndexChanged += (s, e) => ResetIssueValidation();

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

        btnOk.Click += (s, e) =>
        {
            var sessionName = txtSessionName.Text.Trim();
            var name = string.IsNullOrEmpty(sessionName) ? null : sessionName;

            if (rdoFromPr.Checked)
            {
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

                // Build GitHub URL for Edge tab
                string? prGhUrl = null;
                var prRemoteUrl = GitService.GetRemoteUrl(gitRoot, remoteName);
                if (!string.IsNullOrEmpty(prRemoteUrl))
                {
                    var parsed = GitService.ParseGitHubOwnerRepo(prRemoteUrl);
                    if (parsed.HasValue)
                    {
                        prGhUrl = $"https://github.com/{parsed.Value.owner}/{parsed.Value.repo}/pull/{prNum}";
                    }
                }

                result = new NewSessionResult(name, BranchAction.FromPr, null, null, remoteName, prNum, platform, fetchedPrHeadBranch, GitHubUrl: prGhUrl);
            }
            else if (rdoFromIssue.Checked)
            {
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

                var branchName = Models.LauncherSettings.FormatBranchName(
                    Program._settings.IssueBranchPattern, issueNum, name);
                var baseBranch = cmbIssueBaseBranch.SelectedItem?.ToString() ?? "main";

                result = new NewSessionResult(name, BranchAction.FromIssue, branchName, baseBranch, remoteName, null, null, IssueNumber: issueNum, GitHubUrl: issueGitHubUrl);
            }
            else if (rdoNewBranch.Checked)
            {
                var branchName = txtNewBranchName.Text.Trim();
                if (string.IsNullOrEmpty(branchName))
                {
                    MessageBox.Show("Enter a branch name.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var baseBranch = cmbBaseBranch.SelectedItem?.ToString() ?? "main";
                result = new NewSessionResult(name, BranchAction.NewBranch, branchName, baseBranch, null, null, null);
            }
            else if (rdoSwitchBranch.Checked)
            {
                var selectedBranch = cmbBranch.SelectedItem?.ToString()?.TrimStart('*', ' ');
                if (string.IsNullOrEmpty(selectedBranch))
                {
                    MessageBox.Show("Select a branch.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                result = new NewSessionResult(name, BranchAction.ExistingBranch, selectedBranch, null, null, null, null);
            }
            else
            {
                result = new NewSessionResult(name, BranchAction.None, null, null, null, null, null);
            }

            form.DialogResult = DialogResult.OK;
            form.Close();
        };

        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        return form.ShowDialog() == DialogResult.OK ? result : null;
    }
}
