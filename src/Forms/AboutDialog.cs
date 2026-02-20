using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
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
    private const string ChangelogUrlTemplate = "https://github.com/rogerbarreto/copilot-booster/releases/tag/v{0}";

    internal static void Show(IWin32Window owner, UpdateInfo? cachedUpdate = null)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var linkColor = Application.IsDarkModeEnabled ? Color.FromArgb(100, 180, 255) : Color.FromArgb(0, 102, 204);

        var dialog = new Form
        {
            Text = "About Copilot Booster",
            Size = new Size(400, 390),
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

        // Changelog link
        var changelogLink = new LinkLabel
        {
            Text = $"What's New in v{version}",
            AutoSize = true,
            Location = new Point(125, 240),
            LinkColor = linkColor,
            ActiveLinkColor = linkColor,
            VisitedLinkColor = linkColor
        };
        changelogLink.LinkClicked += (s, e) => OpenUrl(string.Format(ChangelogUrlTemplate, version));

        // Check for Updates button
        var updateButton = new Button
        {
            Text = "Check for Updates",
            Width = 250,
            Height = 30,
            Location = new Point(65, 275)
        };
        string? pendingInstallerUrl = null;

        void ApplyUpdate(UpdateInfo update)
        {
            pendingInstallerUrl = update.InstallerUrl;
            updateButton.BackColor = Color.FromArgb(0, 100, 180);
            updateButton.ForeColor = Color.White;
            updateButton.FlatStyle = FlatStyle.Flat;
            updateButton.Text = $"⬆ Update to {update.TagName}";
            updateButton.Enabled = true;
        }

        if (cachedUpdate != null)
        {
            ApplyUpdate(cachedUpdate);
        }

        bool isShowingResult = false;
        updateButton.Click += async (s, e) =>
        {
            // Download and install
            if (pendingInstallerUrl != null)
            {
                updateButton.Enabled = false;
                updateButton.Text = "⬇ Downloading update...";
                try
                {
                    await UpdateService.DownloadAndLaunchInstallerAsync(pendingInstallerUrl).ConfigureAwait(true);
                    dialog.Close();
                    Application.Exit();
                    return;
                }
                catch (Exception dlEx)
                {
                    updateButton.Text = $"✘ Download failed: {dlEx.Message}";
                    updateButton.Enabled = true;
                    await Task.Delay(5000).ConfigureAwait(true);
                    updateButton.Text = $"⬆ Update to {pendingInstallerUrl}";
                }

                return;
            }

            if (isShowingResult)
            {
                return;
            }

            updateButton.Enabled = false;
            updateButton.Text = "Checking...";
            try
            {
                var updateTask = UpdateService.CheckForUpdateAsync();
                await Task.WhenAll(updateTask, Task.Delay(2000)).ConfigureAwait(true);
                var update = await updateTask.ConfigureAwait(true);
                if (update?.TagName != null && update.InstallerUrl != null)
                {
                    ApplyUpdate(update);
                }
                else
                {
                    updateButton.Enabled = true;
                    isShowingResult = true;
                    updateButton.BackColor = Color.FromArgb(30, 100, 30);
                    updateButton.ForeColor = Color.White;
                    updateButton.FlatStyle = FlatStyle.Flat;
                    updateButton.Text = "✔ You're up to date";
                    await Task.Delay(5000).ConfigureAwait(true);
                    isShowingResult = false;
                    updateButton.BackColor = SystemColors.Control;
                    updateButton.ForeColor = SystemColors.ControlText;
                    updateButton.FlatStyle = FlatStyle.Standard;
                    updateButton.Text = "Check for Updates";
                }
            }
            catch (Exception ex)
            {
                updateButton.Text = $"✘ {ex.Message}";
                await Task.Delay(5000).ConfigureAwait(true);
                updateButton.Enabled = true;
                updateButton.Text = "Check for Updates";
            }
        };

        mainPanel.Controls.AddRange([logoPicture, nameLabel, versionLabel, creatorLabel, repoLink, issuesLink, changelogLink, updateButton]);
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
