using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;

namespace CopilotBooster.Forms;

/// <summary>
/// Provides a modal dialog for editing session name and working directory.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class SessionEditorVisuals
{
    /// <summary>
    /// Displays a modal dialog for viewing session details and editing the session alias.
    /// Session name and CWD are read-only (managed by workspace.yaml).
    /// </summary>
    /// <param name="sessionId">The session ID to display.</param>
    /// <param name="currentAlias">The current session alias.</param>
    /// <param name="currentSummary">The current session summary/name.</param>
    /// <param name="currentCwd">The current working directory.</param>
    /// <returns>The updated alias on save, or <c>null</c> if the user cancels.</returns>
    internal static string? ShowEditor(string sessionId, string currentAlias, string currentSummary, string currentCwd)
    {
        string? result = null;

        var form = new Form
        {
            Text = "Session Details",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Width = 500,
            Height = 290,
            TopMost = Program._settings.AlwaysOnTop
        };
        SettingsVisuals.AlignWithParent(form);

        if (Program.AppIcon != null)
        {
            form.Icon = Program.AppIcon;
        }

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

        // Session Name (read-only — managed by workspace.yaml)
        var lblNameHeader = new Label
        {
            Text = "Session Name (managed by workspace.yaml)",
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblNameHeader);
        y += 20;

        var nameText = $"{currentSummary}";
        var lblName = new Label
        {
            Text = nameText,
            AutoSize = true,
            ForeColor = Color.Gray,
            Location = new Point(14, y + 2)
        };
        form.Controls.Add(lblName);

        var nameTextWidth = TextRenderer.MeasureText(nameText, form.Font).Width;
        var btnCopyName = new Button
        {
            Text = "📋",
            Width = 30,
            Height = 22,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Location = new Point(14 + nameTextWidth + 2, y - 1)
        };
        btnCopyName.FlatAppearance.BorderSize = 0;
        btnCopyName.Click += (s, e) =>
        {
            Clipboard.SetText(currentSummary);
            btnCopyName.Text = "✓";
            var timer = new Timer { Interval = 1500 };
            timer.Tick += (_, _) => { btnCopyName.Text = "📋"; timer.Stop(); timer.Dispose(); };
            timer.Start();
        };
        form.Controls.Add(btnCopyName);
        y += 28;

        // CWD (read-only — managed by workspace.yaml)
        var lblCwdHeader = new Label
        {
            Text = "Working Directory (managed by workspace.yaml)",
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblCwdHeader);
        y += 20;

        var cwdText = $"{currentCwd}";
        var lblCwd = new Label
        {
            Text = cwdText,
            AutoSize = true,
            ForeColor = Color.Gray,
            Location = new Point(14, y + 2),
            MaximumSize = new Size(420, 0)
        };
        form.Controls.Add(lblCwd);

        var cwdTextWidth = TextRenderer.MeasureText(cwdText, form.Font, new Size(420, 0), TextFormatFlags.WordBreak).Width;
        var btnCopyCwd = new Button
        {
            Text = "📋",
            Width = 30,
            Height = 22,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Location = new Point(14 + cwdTextWidth + 2, y - 1)
        };
        btnCopyCwd.FlatAppearance.BorderSize = 0;
        btnCopyCwd.Click += (s, e) =>
        {
            Clipboard.SetText(currentCwd);
            btnCopyCwd.Text = "✓";
            var timer = new Timer { Interval = 1500 };
            timer.Tick += (_, _) => { btnCopyCwd.Text = "📋"; timer.Stop(); timer.Dispose(); };
            timer.Start();
        };
        form.Controls.Add(btnCopyCwd);
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
            result = txtAlias.Text.Trim();
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
