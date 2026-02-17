using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using CopilotBooster.Services;

namespace CopilotBooster.Forms;

/// <summary>
/// Displays the About dialog with app info, links, and update check.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class AboutDialog
{
    private const string RepoUrl = "https://github.com/rogerbarreto/copilot-booster";
    private const string IssuesUrl = "https://github.com/rogerbarreto/copilot-booster/issues";

    internal static void Show(IWin32Window owner)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var linkColor = Application.IsDarkModeEnabled ? Color.FromArgb(100, 180, 255) : Color.FromArgb(0, 102, 204);

        var dialog = new Form
        {
            Text = "About Copilot Booster",
            Size = new Size(400, 360),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f),
            TopMost = (owner as Form)?.TopMost ?? false
        };

        if (Program.AppIcon != null)
        {
            dialog.Icon = Program.AppIcon;
        }

        var mainPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

        // App logo — use embedded high-res PNG
        var logoPicture = new PictureBox
        {
            Size = new Size(80, 80),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(150, 10)
        };
        var assembly = Assembly.GetExecutingAssembly();
        var logoStream = assembly.GetManifestResourceStream("CopilotBooster.Resources.logo.png");
        if (logoStream != null)
        {
            logoPicture.Image = Image.FromStream(logoStream);
        }
        else if (Program.AppIcon != null)
        {
            using var largeIcon = new Icon(Program.AppIcon, 256, 256);
            logoPicture.Image = largeIcon.ToBitmap();
        }

        // App name
        var nameLabel = new Label
        {
            Text = "Copilot Booster",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 16f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(110, 100)
        };

        // Version
        var versionLabel = new Label
        {
            Text = $"Version {version}",
            AutoSize = true,
            Location = new Point(150, 135)
        };

        // Creator
        var creatorLabel = new Label
        {
            Text = "Created by Roger Barreto",
            AutoSize = true,
            Location = new Point(115, 160)
        };

        // Repo link
        var repoLink = new LinkLabel
        {
            Text = "GitHub Repository",
            AutoSize = true,
            Location = new Point(135, 190),
            LinkColor = linkColor,
            ActiveLinkColor = linkColor,
            VisitedLinkColor = linkColor
        };
        repoLink.LinkClicked += (s, e) => OpenUrl(RepoUrl);

        // Issues link
        var issuesLink = new LinkLabel
        {
            Text = "Report Issues / Feedback",
            AutoSize = true,
            Location = new Point(115, 215),
            LinkColor = linkColor,
            ActiveLinkColor = linkColor,
            VisitedLinkColor = linkColor
        };
        issuesLink.LinkClicked += (s, e) => OpenUrl(IssuesUrl);

        // Check for Updates button
        var updateButton = new Button
        {
            Text = "Check for Updates",
            Width = 160,
            Height = 30,
            Location = new Point(110, 250)
        };
        updateButton.Click += async (s, e) =>
        {
            updateButton.Enabled = false;
            updateButton.Text = "Checking...";
            try
            {
                var update = await UpdateService.CheckForUpdateAsync().ConfigureAwait(true);
                if (update?.TagName != null)
                {
                    var result = MessageBox.Show(
                        $"New version available: {update.TagName}\n\nWould you like to download it?",
                        "Update Available",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);
                    if (result == DialogResult.Yes && update.InstallerUrl != null)
                    {
                        OpenUrl(update.InstallerUrl);
                    }
                }
                else
                {
                    MessageBox.Show("You're running the latest version.", "Up to Date", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to check for updates: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                updateButton.Enabled = true;
                updateButton.Text = "Check for Updates";
            }
        };

        mainPanel.Controls.AddRange([logoPicture, nameLabel, versionLabel, creatorLabel, repoLink, issuesLink, updateButton]);
        dialog.Controls.Add(mainPanel);
        dialog.ShowDialog(owner);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Ignore errors opening URL
        }
    }
}
