using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;
using CopilotBooster.Services;

namespace CopilotBooster.Forms;

/// <summary>
/// Displays a welcome popup thanking the user and requesting a GitHub star.
/// Two modes: authenticated (star via API) and unauthenticated (open browser).
/// </summary>
[ExcludeFromCodeCoverage]
internal static class WelcomePopupVisuals
{
    private const string RepoUrl = "https://github.com/rogerbarreto/copilot-booster";

    /// <summary>
    /// Shows the welcome popup. Returns <c>true</c> if the user checked "Don't show again".
    /// </summary>
    internal static bool Show(GitHubApiService api)
    {
        bool dismissed = false;
        bool isAuthenticated = api.IsAuthenticated;

        using var form = new Form
        {
            Text = "Welcome to Copilot Booster",
            Size = new Size(460, 280),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White
        };

        var lblTitle = new Label
        {
            Text = "⭐ Thank you for using Copilot Booster!",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 13f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(30, 25),
            ForeColor = Color.White
        };

        var lblMessage = new Label
        {
            Text = "If you find this tool helpful, please consider giving it a star\non GitHub. It helps others discover it and motivates development!",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 9.5f),
            AutoSize = true,
            Location = new Point(30, 65),
            ForeColor = Color.FromArgb(200, 200, 200)
        };

        var btnStar = new Button
        {
            Text = "⭐ Star on GitHub",
            Size = new Size(160, 36),
            Location = new Point(30, 130),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(45, 120, 45),
            ForeColor = Color.White,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnStar.FlatAppearance.BorderColor = Color.FromArgb(60, 150, 60);

        var chkDismiss = new CheckBox
        {
            Text = "Don't show this again",
            AutoSize = true,
            Location = new Point(30, 185),
            ForeColor = Color.FromArgb(170, 170, 170),
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 8.5f)
        };

        var btnClose = new Button
        {
            Text = "Close",
            Size = new Size(80, 32),
            Location = new Point(350, 185),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(55, 55, 55),
            ForeColor = Color.White,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 9f)
        };
        btnClose.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);

        btnStar.Click += async (s, e) =>
        {
            if (isAuthenticated)
            {
                btnStar.Enabled = false;
                btnStar.Text = "Starring...";
                var success = await api.StarRepoAsync("rogerbarreto", "copilot-booster").ConfigureAwait(true);
                if (success)
                {
                    btnStar.Text = "✅ Thank you!";
                    btnStar.BackColor = Color.FromArgb(30, 80, 30);
                }
                else
                {
                    // Fall back to browser if API fails
                    OpenInBrowser();
                    btnStar.Text = "Opened in browser";
                }
            }
            else
            {
                OpenInBrowser();
                btnStar.Text = "Opened in browser";
            }
        };

        btnClose.Click += (s, e) =>
        {
            dismissed = chkDismiss.Checked;
            form.Close();
        };

        form.FormClosing += (s, e) =>
        {
            dismissed = chkDismiss.Checked;
        };

        form.Controls.AddRange([lblTitle, lblMessage, btnStar, chkDismiss, btnClose]);
        form.AcceptButton = btnStar;
        form.CancelButton = btnClose;
        form.ShowDialog();

        return dismissed;
    }

    private static void OpenInBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true });
        }
        catch { }
    }
}
