using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CopilotApp.Services;

namespace CopilotApp.Forms;

/// <summary>
/// Provides a dialog for selecting a working directory from previously used directories or browsing for a new one.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class CwdPickerForm
{
    /// <summary>
    /// Displays the working directory picker dialog, listing previously used directories sorted by usage frequency.
    /// </summary>
    /// <param name="defaultWorkDir">The default directory path used when browsing for a new folder.</param>
    /// <returns>The selected directory path, or <c>null</c> if the dialog was cancelled.</returns>
    internal static string? ShowCwdPicker(string defaultWorkDir)
    {
        // Scan all session workspace.yaml files to collect CWDs and their frequency
        var cwdCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(Program.SessionStateDir))
        {
            foreach (var dir in Directory.GetDirectories(Program.SessionStateDir))
            {
                var wsFile = Path.Combine(dir, "workspace.yaml");
                if (!File.Exists(wsFile))
                {
                    continue;
                }

                try
                {
                    foreach (var line in File.ReadAllLines(wsFile))
                    {
                        if (line.StartsWith("cwd:"))
                        {
                            var cwd = line[4..].Trim();
                            if (!string.IsNullOrEmpty(cwd) && Directory.Exists(cwd))
                            {
                                cwdCounts.TryGetValue(cwd, out int count);
                                cwdCounts[cwd] = count + 1;
                            }
                            break;
                        }
                    }
                }
                catch { }
            }
        }

        var sortedCwds = cwdCounts
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();

        var form = new Form
        {
            Text = "New Session — Select Working Directory",
            Size = new Size(600, 420),
            MinimumSize = new Size(450, 300),
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.Sizable
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

        var listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = true
        };
        listView.Columns.Add("Directory", 350);
        listView.Columns.Add("# Sessions created", 120, HorizontalAlignment.Center);
        listView.Columns.Add("Git", 50, HorizontalAlignment.Center);

        foreach (var cwd in sortedCwds)
        {
            var item = new ListViewItem(cwd) { Tag = cwd };
            item.SubItems.Add(cwdCounts[cwd].ToString());
            item.SubItems.Add(GitService.IsGitRepository(cwd) ? "Yes" : "");
            listView.Items.Add(item);
        }

        if (listView.Items.Count > 0)
        {
            listView.Items[0].Selected = true;
        }

        string? selectedPath = null;

        listView.DoubleClick += (s, e) =>
        {
            if (listView.SelectedItems.Count > 0)
            {
                selectedPath = listView.SelectedItems[0].Tag as string;
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(5)
        };

        var btnCancel = new Button { Text = "Cancel", Width = 80 };
        btnCancel.Click += (s, e) => { form.DialogResult = DialogResult.Cancel; form.Close(); };

        var btnOpen = new Button { Text = "Start", Width = 80 };
        btnOpen.Click += (s, e) =>
        {
            if (listView.SelectedItems.Count > 0)
            {
                selectedPath = listView.SelectedItems[0].Tag as string;
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
        };

        var btnBrowse = new Button { Text = "Browse...", Width = 90 };
        btnBrowse.Click += (s, e) =>
        {
            using var fbd = new FolderBrowserDialog { SelectedPath = defaultWorkDir };
            if (fbd.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(fbd.SelectedPath))
            {
                selectedPath = fbd.SelectedPath;
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
        };

        // RightToLeft flow: Cancel first (rightmost), then Start, then Browse
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOpen);
        buttonPanel.Controls.Add(btnBrowse);

        form.Controls.Add(listView);
        form.Controls.Add(buttonPanel);
        form.AcceptButton = btnOpen;
        form.CancelButton = btnCancel;

        return form.ShowDialog() == DialogResult.OK ? selectedPath : null;
    }
}
