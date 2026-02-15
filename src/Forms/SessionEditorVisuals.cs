using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CopilotBooster.Forms;

/// <summary>
/// Provides a modal dialog for editing session name and working directory.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class SessionEditorVisuals
{
    /// <summary>
    /// Displays a modal dialog for editing a session's summary and CWD.
    /// </summary>
    /// <param name="currentSummary">The current session summary/name.</param>
    /// <param name="currentCwd">The current working directory.</param>
    /// <returns>A tuple of (newSummary, newCwd) on save, or <c>null</c> if the user cancels.</returns>
    internal static (string Summary, string Cwd)? ShowEditor(string currentSummary, string currentCwd)
    {
        (string Summary, string Cwd)? result = null;

        var form = new Form
        {
            Text = "Edit Session",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Width = 500,
            Height = 220
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

        int y = 14;

        // Session Name
        var lblName = new Label
        {
            Text = "Session Name",
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblName);
        y += 20;

        var txtName = new TextBox
        {
            Text = currentSummary,
            Location = new Point(14, y),
            Width = 450
        };
        form.Controls.Add(SettingsVisuals.WrapWithBorder(txtName));
        y += 34;

        // CWD
        var lblCwd = new Label
        {
            Text = "Working Directory",
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblCwd);
        y += 20;

        var txtCwd = new TextBox
        {
            Text = currentCwd,
            Location = new Point(14, y),
            Width = 370
        };
        form.Controls.Add(SettingsVisuals.WrapWithBorder(txtCwd));

        var btnBrowse = new Button
        {
            Text = "Browse...",
            Width = 75,
            Location = new Point(389, y - 1)
        };
        btnBrowse.Click += (s, e) =>
        {
            using var dlg = new FolderBrowserDialog();
            var initial = txtCwd.Text.Trim();
            if (Directory.Exists(initial))
            {
                dlg.SelectedPath = initial;
            }

            if (dlg.ShowDialog(form) == DialogResult.OK)
            {
                txtCwd.Text = dlg.SelectedPath;
            }
        };
        form.Controls.Add(btnBrowse);
        y += 40;

        // Buttons
        var btnSave = new Button
        {
            Text = "Save",
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

        btnSave.Click += (s, e) =>
        {
            result = (txtName.Text.Trim(), txtCwd.Text.Trim());
            form.DialogResult = DialogResult.OK;
            form.Close();
        };

        form.Controls.Add(btnSave);
        form.Controls.Add(btnCancel);
        form.AcceptButton = btnSave;
        form.CancelButton = btnCancel;

        return form.ShowDialog() == DialogResult.OK ? result : null;
    }
}
