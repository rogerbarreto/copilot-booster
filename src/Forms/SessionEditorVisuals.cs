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
    /// Displays a modal dialog for editing a session's alias and CWD.
    /// The session name (summary) is displayed read-only since it's managed by Copilot CLI.
    /// </summary>
    /// <param name="sessionId">The session ID to display.</param>
    /// <param name="currentAlias">The current session alias.</param>
    /// <param name="currentSummary">The current session summary/name.</param>
    /// <param name="currentCwd">The current working directory.</param>
    /// <returns>A tuple of (Alias, Cwd) on save, or <c>null</c> if the user cancels.</returns>
    internal static (string Alias, string Cwd)? ShowEditor(string sessionId, string currentAlias, string currentSummary, string currentCwd)
    {
        (string Alias, string Cwd)? result = null;

        var form = new Form
        {
            Text = "Edit Session",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Width = 500,
            Height = 330,
            TopMost = Program._settings.AlwaysOnTop
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

        // Session ID (read-only label + copy button)
        var idText = $"Session ID: {sessionId}";
        var lblSessionId = new Label
        {
            Text = idText,
            AutoSize = true,
            ForeColor = Color.Gray,
            Location = new Point(14, y + 2)
        };
        form.Controls.Add(lblSessionId);

        var idTextWidth = TextRenderer.MeasureText(idText, form.Font).Width;
        var btnCopy = new Button
        {
            Text = "📋",
            Width = 30,
            Height = 22,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Location = new Point(14 + idTextWidth + 2, y - 1)
        };
        btnCopy.FlatAppearance.BorderSize = 0;
        btnCopy.Click += (s, e) =>
        {
            Clipboard.SetText(sessionId);
            btnCopy.Text = "✓";
            var timer = new Timer { Interval = 1500 };
            timer.Tick += (_, _) => { btnCopy.Text = "📋"; timer.Stop(); timer.Dispose(); };
            timer.Start();
        };
        form.Controls.Add(btnCopy);
        y += 28;

        // Session Alias
        var lblAlias = new Label
        {
            Text = "Session Alias (your label — won't change)",
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblAlias);
        y += 20;

        var txtAlias = new TextBox
        {
            Text = currentAlias,
            Location = new Point(14, y),
            Width = 450
        };
        form.Controls.Add(SettingsVisuals.WrapWithBorder(txtAlias));
        y += 34;

        // Session Name (read-only — managed by Copilot CLI)
        var lblName = new Label
        {
            Text = "Session Name (managed by Copilot CLI)",
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblName);
        y += 20;

        var txtName = new TextBox
        {
            Text = currentSummary,
            Location = new Point(14, y),
            Width = 450,
            ReadOnly = true,
            ForeColor = Color.Gray
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
            result = (txtAlias.Text.Trim(), txtCwd.Text.Trim());
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
