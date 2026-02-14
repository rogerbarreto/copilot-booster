using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;

namespace CopilotApp.Forms;

/// <summary>
/// Provides a modal dialog for naming a new Copilot session.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class NewSessionNameForm
{
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
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Width = 500,
            Height = 200
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
            Text = "Name your new coding session",
            ForeColor = Color.Gray,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 8.5f),
            AutoSize = true,
            Location = new Point(14, y)
        };
        form.Controls.Add(lblSubtitle);
        y += 28;

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
            PlaceholderText = "e.g., Feature: User Authentication",
            Location = new Point(14, y),
            Width = 450
        };
        form.Controls.Add(txtName);
        y += 26;

        var lblHelper = new Label
        {
            Text = "A descriptive name for your session (optional)",
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
}
