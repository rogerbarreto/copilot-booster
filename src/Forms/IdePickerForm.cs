using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using CopilotApp.Services;

namespace CopilotApp.Forms;

/// <summary>
/// Provides a picker dialog for opening a session's working directory or repository in a configured IDE.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class IdePickerForm
{
    /// <summary>
    /// Displays an IDE picker dialog and opens the selected IDE for the specified session.
    /// </summary>
    /// <param name="sessionId">The identifier of the session to open in an IDE.</param>
    /// <param name="onLaunched">Optional callback invoked with the IDE description, process ID, and folder path when an IDE is launched.</param>
    internal static void OpenIdeForSession(string sessionId, Action<string, int, string>? onLaunched = null)
    {
        if (Program._settings.Ides.Count == 0)
        {
            MessageBox.Show("No IDEs configured.\nGo to Settings → IDEs tab to add one.",
                "Open in IDE", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Get session CWD
        var workspaceFile = Path.Combine(Program.SessionStateDir, sessionId, "workspace.yaml");
        if (!File.Exists(workspaceFile))
        {
            MessageBox.Show("Session not found.", "Open in IDE", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string? cwd = null;
        foreach (var line in File.ReadAllLines(workspaceFile))
        {
            if (line.StartsWith("cwd:"))
            {
                cwd = line[4..].Trim();
                break;
            }
        }

        if (string.IsNullOrEmpty(cwd))
        {
            MessageBox.Show("Session has no working directory.", "Open in IDE", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var repoRoot = SessionService.FindGitRoot(cwd);
        bool hasRepo = repoRoot != null && !string.Equals(repoRoot, cwd, StringComparison.OrdinalIgnoreCase);

        // Build a compact picker: one row per IDE, each with CWD and Repo buttons
        var form = new Form
        {
            Text = "Open in IDE",
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12)
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

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = hasRepo ? 3 : 2,
            Padding = new Padding(0),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };

        // Header
        var lblIde = new Label { Text = "IDE", Font = new Font(SystemFonts.DefaultFont.FontFamily, 9, FontStyle.Bold), AutoSize = true, Padding = new Padding(0, 4, 8, 4) };
        layout.Controls.Add(lblIde, 0, 0);
        var lblCwd = new Label { Text = $"CWD: {cwd}", Font = new Font(SystemFonts.DefaultFont.FontFamily, 8), AutoSize = true, Padding = new Padding(0, 4, 4, 4), ForeColor = Color.Gray };
        layout.Controls.Add(lblCwd, 1, 0);
        if (hasRepo)
        {
            var lblRepo = new Label { Text = $"Repo: {repoRoot}", Font = new Font(SystemFonts.DefaultFont.FontFamily, 8), AutoSize = true, Padding = new Padding(0, 4, 0, 4), ForeColor = Color.Gray };
            layout.Controls.Add(lblRepo, 2, 0);
        }

        int row = 1;
        foreach (var ide in Program._settings.Ides)
        {
            var ideName = new Label
            {
                Text = ide.Description,
                AutoSize = true,
                Padding = new Padding(0, 6, 8, 2),
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 9.5f)
            };
            layout.Controls.Add(ideName, 0, row);

            var btnCwd = new Button { Text = "Open CWD", Width = 100, Height = 28 };
            var capturedIde = ide;
            btnCwd.Click += (s, e) =>
            {
                var proc = LaunchIde(capturedIde.Path, cwd);
                if (proc != null)
                {
                    onLaunched?.Invoke(capturedIde.Description, proc.Id, cwd);
                }
                form.Close();
            };
            layout.Controls.Add(btnCwd, 1, row);

            if (hasRepo)
            {
                var btnRepo = new Button { Text = "Open Repo", Width = 100, Height = 28 };
                btnRepo.Click += (s, e) =>
                {
                    var proc = LaunchIde(capturedIde.Path, repoRoot!);
                    if (proc != null)
                    {
                        onLaunched?.Invoke(capturedIde.Description, proc.Id, repoRoot!);
                    }
                    form.Close();
                };
                layout.Controls.Add(btnRepo, 2, row);
            }

            row++;
        }

        form.Controls.Add(layout);
        form.CancelButton = null;
        form.KeyPreview = true;
        form.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                form.Close();
            }
        };
        form.ShowDialog();
    }

    /// <summary>
    /// Launches the specified IDE executable with the given folder path as an argument.
    /// </summary>
    /// <param name="idePath">The file path to the IDE executable.</param>
    /// <param name="folderPath">The folder path to open in the IDE.</param>
    /// <returns>The launched process, or null if the launch failed.</returns>
    internal static Process? LaunchIde(string idePath, string folderPath)
    {
        try
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = idePath,
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to launch IDE: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }
}
