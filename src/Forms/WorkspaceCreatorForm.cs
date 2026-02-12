using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using CopilotApp.Services;

namespace CopilotApp.Forms;

/// <summary>
/// Provides a modal dialog for creating a new git worktree workspace.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class WorkspaceCreatorForm
{
    /// <summary>
    /// Displays a modal dialog for creating a git worktree workspace from the specified repository.
    /// </summary>
    /// <param name="repoPath">The git repository root path.</param>
    /// <returns>The worktree path on success, or <c>null</c> if the user cancels.</returns>
    internal static string? ShowWorkspaceCreator(string repoPath)
    {
        string? resultPath = null;
        var repoFolderName = Path.GetFileName(repoPath);

        var form = new Form
        {
            Text = "Create New Workspace",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Width = 500,
            Height = 280
        };

        try
        {
            var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon != null)
            {
                form.Icon = icon;
            }
        }
        catch { }

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

        // Workspace Name
        var lblName = new Label
        {
            Text = "Workspace feature branch name *",
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblName);
        y += 20;

        var txtName = new TextBox
        {
            PlaceholderText = "i.e: issues/123-new-issue",
            Location = new Point(14, y),
            Width = 450
        };
        form.Controls.Add(txtName);
        y += 26;

        var lblNameHelper = new Label
        {
            Text = "A descriptive name for your workspace (becomes the branch name)",
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f),
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblNameHelper);
        y += 22;

        // Base Branch
        var lblBranch = new Label
        {
            Text = "Base Branch",
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblBranch);
        y += 20;

        var cmbBranch = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(14, y),
            Width = 450
        };

        var branches = GitService.GetBranches(repoPath);
        foreach (var b in branches)
        {
            cmbBranch.Items.Add(b);
        }

        var currentBranch = GitService.GetCurrentBranch(repoPath);
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
        y += 26;

        var lblBranchHelper = new Label
        {
            Text = "The branch to create the workspace from",
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f),
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblBranchHelper);
        y += 22;

        // Preview label
        var lblPreview = new Label
        {
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f, FontStyle.Italic),
            AutoSize = true,
            Location = new Point(14, y),
            MaximumSize = new Size(460, 0)
        };
        form.Controls.Add(lblPreview);

        void UpdatePreview()
        {
            var name = txtName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                lblPreview.Text = "";
            }
            else
            {
                var dirName = GitService.SanitizeWorkspaceDirName(repoFolderName!, name);
                lblPreview.Text = Path.Combine(GitService.GetWorkspacesDir(), dirName);
            }
        }

        txtName.TextChanged += (s, e) => UpdatePreview();

        // Buttons
        var btnCreate = new Button
        {
            Text = "Create",
            DialogResult = DialogResult.None,
            Width = 80,
            Location = new Point(300, 205)
        };

        var btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 80,
            Location = new Point(390, 205)
        };

        btnCreate.Click += (s, e) =>
        {
            var workspaceName = txtName.Text.Trim();
            if (string.IsNullOrEmpty(workspaceName))
            {
                MessageBox.Show("Workspace name is required.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var dirName = GitService.SanitizeWorkspaceDirName(repoFolderName!, workspaceName);
            var worktreePath = Path.Combine(GitService.GetWorkspacesDir(), dirName);
            var selectedBaseBranch = cmbBranch.SelectedItem?.ToString() ?? "main";

            Directory.CreateDirectory(GitService.GetWorkspacesDir());

            var (success, error) = GitService.CreateWorktree(repoPath, worktreePath, workspaceName, selectedBaseBranch);
            if (success)
            {
                resultPath = worktreePath;
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
            else
            {
                MessageBox.Show($"Failed to create workspace:\n{error}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        form.Controls.Add(btnCreate);
        form.Controls.Add(btnCancel);
        form.AcceptButton = btnCreate;
        form.CancelButton = btnCancel;

        return form.ShowDialog() == DialogResult.OK ? resultPath : null;
    }
}
