using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
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
    internal static (string WorktreePath, string? SessionName)? ShowWorkspaceCreator(string repoPath)
    {
        (string WorktreePath, string? SessionName)? result = null;
        var repoFolderName = Path.GetFileName(repoPath);

        const int formWidthValue = 500;
        const int collapsedHeight = 340;
        const int expandedHeight = 410;

        var form = new Form
        {
            Text = "Create New Workspace",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Width = formWidthValue,
            Height = collapsedHeight,
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

        // Create as new branch checkbox
        var chkNewBranch = new CheckBox
        {
            Text = "Create as new branch",
            AutoSize = true,
            Location = new Point(14, y),
            Checked = false
        };
        form.Controls.Add(chkNewBranch);
        y += 26;

        // Workspace Name (new branch only — hidden by default)
        var branchNameY = y;
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

        const int branchFieldHeight = 68;

        // Base Branch
        var lblBranch = new Label
        {
            Text = "Base Branch",
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

        var branches = WorkspaceCreationService.GetBranches(repoPath);
        var remotes = GitService.GetRemotes(repoPath);
        foreach (var b in branches)
        {
            cmbBranch.Items.Add(b);
        }

        var currentBranch = WorkspaceCreationService.GetCurrentBranch(repoPath);
        if (!string.IsNullOrEmpty(currentBranch) && cmbBranch.Items.Contains(currentBranch))
        {
            cmbBranch.SelectedItem = currentBranch;
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

        // Layout helper — repositions controls below the checkbox based on mode
        void RelayoutControls()
        {
            int cy = branchNameY;
            bool isNewBranch = chkNewBranch.Checked;

            lblName.Visible = isNewBranch;
            txtName.Visible = isNewBranch;
            txtNameWrapper.Visible = isNewBranch;
            lblNameHelper.Visible = isNewBranch;

            if (isNewBranch)
            {
                lblName.Location = new Point(14, cy);
                txtName.Location = new Point(14, cy + 20);
                txtNameWrapper.Location = new Point(14, cy + 20);
                lblNameHelper.Location = new Point(14, cy + 46);
                cy += branchFieldHeight;
            }

            lblBranch.Text = isNewBranch ? "Base Branch" : "Branch";
            lblBranchHelper.Text = isNewBranch
                ? "The branch to create the new branch from"
                : "The existing branch to check out";

            lblBranch.Location = new Point(14, cy);
            cmbBranch.Location = new Point(14, cy + 20);
            lblBranchHelper.Location = new Point(14, cy + 46);

            lblPreview.Location = new Point(14, cy + 68);

            int buttonY = cy + 100;
            btnCreate.Location = new Point(300, buttonY);
            btnCancel.Location = new Point(390, buttonY);

            form.Height = isNewBranch ? expandedHeight : collapsedHeight;
        }

        void UpdatePreview()
        {
            if (chkNewBranch.Checked)
            {
                var name = txtName.Text.Trim();
                lblPreview.Text = string.IsNullOrEmpty(name)
                    ? ""
                    : WorkspaceCreationService.BuildWorkspacePath(repoFolderName!, name);
            }
            else
            {
                var branch = cmbBranch.SelectedItem?.ToString();
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

        chkNewBranch.CheckedChanged += (s, e) => { RelayoutControls(); UpdatePreview(); };
        txtName.TextChanged += (s, e) => UpdatePreview();
        cmbBranch.SelectedIndexChanged += (s, e) => UpdatePreview();

        // Initial layout
        RelayoutControls();
        UpdatePreview();

        btnCreate.Click += (s, e) =>
        {
            var selectedBaseBranch = cmbBranch.SelectedItem?.ToString() ?? "main";

            if (chkNewBranch.Checked)
            {
                // New branch mode — current behavior
                var workspaceName = txtName.Text.Trim();
                if (string.IsNullOrEmpty(workspaceName))
                {
                    MessageBox.Show("Branch name is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var (worktreePath, success, error) = WorkspaceCreationService.CreateWorkspace(
                    repoPath, repoFolderName!, workspaceName, selectedBaseBranch);
                if (success)
                {
                    var sessionName = txtSessionName.Text.Trim();
                    result = (worktreePath, string.IsNullOrEmpty(sessionName) ? null : sessionName);
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
                // Existing branch mode — create a local branch tracking the selected ref
                var (worktreePath, success, error) = WorkspaceCreationService.CreateWorkspaceFromExistingBranch(
                    repoPath, repoFolderName!, selectedBaseBranch);
                if (success)
                {
                    var sessionName = txtSessionName.Text.Trim();
                    result = (worktreePath, string.IsNullOrEmpty(sessionName) ? null : sessionName);
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
