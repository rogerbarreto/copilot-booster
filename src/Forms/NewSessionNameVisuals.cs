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

internal enum BranchAction { None, ExistingBranch, NewBranch, FromPr }

internal record NewSessionResult(
    string? SessionName,
    BranchAction Action,
    string? BranchName,
    string? BaseBranch,
    string? Remote,
    int? PrNumber,
    GitService.HostingPlatform? Platform);

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
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Width = 500,
            Height = 220,
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
            Text = "A stable label for your session (optional)",
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
            Location = new Point(300, y)
        };

        var btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 80,
            Location = new Point(390, y)
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
        const int SwitchBranchHeight = 310;
        const int PrModeHeight = 420;

        var form = new Form
        {
            Text = "New Copilot Session",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Width = FormWidthValue,
            Height = SameBranchHeight,
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
            Text = "A stable label for your session (optional)",
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

        var rdoFromPr = new RadioButton
        {
            Text = "From PR #",
            AutoSize = true,
            Location = new Point(310, y),
            Visible = remotePlatforms.Count > 0
        };
        form.Controls.Add(rdoFromPr);
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

        // Buttons
        var btnOk = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.None,
            Width = 80
        };

        var btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 80
        };

        form.Controls.Add(btnOk);
        form.Controls.Add(btnCancel);

        // Layout helper — repositions controls based on selected mode
        void RelayoutControls()
        {
            int cy = modeStartY;
            bool isSwitchBranch = rdoSwitchBranch.Checked;
            bool isPrMode = rdoFromPr.Checked;
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

                btnOk.Enabled = prValidated;
                form.Height = cy + 70;
            }
            else if (isSwitchBranch)
            {
                lblBranch.Location = new Point(14, cy);
                cmbBranch.Location = new Point(14, cy + 20);
                lblBranchHelper.Location = new Point(14, cy + 46);

                int buttonY = cy + 74;
                btnOk.Location = new Point(300, buttonY);
                btnCancel.Location = new Point(390, buttonY);

                btnOk.Enabled = true;
                form.Height = buttonY + 70;
            }
            else
            {
                // Same branch — compact
                btnOk.Location = new Point(300, cy);
                btnCancel.Location = new Point(390, cy);

                btnOk.Enabled = true;
                form.Height = cy + 70;
            }
        }

        void ResetPrValidation()
        {
            prValidated = false;
            fetchedPrTitle = null;
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
            try
            {
                (found, prTitle) = await Task.Run(async () =>
                {
                    var valid = GitService.ValidatePrRef(gitRoot, remoteName, detectedPlatform, prNum);
                    string? title = null;

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
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // API failure — don't affect the flow
                        }
                    }

                    return (valid, title);
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

        // Wire up radio button changes
        void OnModeChanged(object? s, EventArgs e)
        {
            ResetPrValidation();
            RelayoutControls();
        }

        rdoSameBranch.CheckedChanged += OnModeChanged;
        rdoSwitchBranch.CheckedChanged += OnModeChanged;
        rdoFromPr.CheckedChanged += OnModeChanged;

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
                result = new NewSessionResult(name, BranchAction.FromPr, null, null, remoteName, prNum, platform);
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
