using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;
using CopilotApp.Models;

namespace CopilotApp.Forms;

/// <summary>
/// Helper methods for building and managing settings tab UI controls.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class SettingsTabBuilder
{
    /// <summary>
    /// Creates a panel with Add, Edit, Remove, Move Up, and Move Down buttons for a <see cref="ListBox"/>.
    /// </summary>
    /// <param name="listBox">The list box to manage.</param>
    /// <param name="promptText">The label shown in the input prompt dialog.</param>
    /// <param name="addTitle">The title for the add dialog.</param>
    /// <param name="addBrowse">When <c>true</c>, use a folder browser instead of a text prompt.</param>
    /// <returns>A <see cref="FlowLayoutPanel"/> containing the management buttons.</returns>
    internal static FlowLayoutPanel CreateListButtons(ListBox listBox, string promptText, string addTitle, bool addBrowse)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 100,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(4)
        };

        var btnAdd = new Button { Text = "Add", Width = 88 };
        btnAdd.Click += (s, e) =>
        {
            if (addBrowse)
            {
                using var fbd = new FolderBrowserDialog();
                if (fbd.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(fbd.SelectedPath))
                {
                    listBox.Items.Add(fbd.SelectedPath);
                    listBox.SelectedIndex = listBox.Items.Count - 1;
                }
            }
            else
            {
                var value = PromptInput(addTitle, promptText, "");
                if (!string.IsNullOrWhiteSpace(value))
                {
                    listBox.Items.Add(value);
                    listBox.SelectedIndex = listBox.Items.Count - 1;
                }
            }
            listBox.Focus();
        };

        var btnEdit = new Button { Text = "Edit", Width = 88 };
        btnEdit.Click += (s, e) =>
        {
            if (listBox.SelectedIndex < 0)
            {
                return;
            }

            var current = listBox.SelectedItem?.ToString() ?? "";

            if (addBrowse)
            {
                using var fbd = new FolderBrowserDialog { SelectedPath = current };
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    listBox.Items[listBox.SelectedIndex] = fbd.SelectedPath;
                }
            }
            else
            {
                var value = PromptInput("Edit", promptText, current);
                if (value != null)
                {
                    listBox.Items[listBox.SelectedIndex] = value;
                }
            }
            listBox.Focus();
        };

        var btnRemove = new Button { Text = "Remove", Width = 88 };
        btnRemove.Click += (s, e) =>
        {
            int idx = listBox.SelectedIndex;
            if (idx >= 0)
            {
                listBox.Items.RemoveAt(idx);
                if (listBox.Items.Count > 0)
                {
                    listBox.SelectedIndex = Math.Min(idx, listBox.Items.Count - 1);
                }

                listBox.Focus();
            }
        };

        var btnUp = new Button { Text = "Move Up", Width = 88 };
        btnUp.Click += (s, e) =>
        {
            int idx = listBox.SelectedIndex;
            if (idx > 0)
            {
                var item = listBox.Items[idx];
                listBox.Items.RemoveAt(idx);
                listBox.Items.Insert(idx - 1, item);
                listBox.SelectedIndex = idx - 1;
                listBox.Focus();
            }
        };

        var btnDown = new Button { Text = "Move Down", Width = 88 };
        btnDown.Click += (s, e) =>
        {
            int idx = listBox.SelectedIndex;
            if (idx >= 0 && idx < listBox.Items.Count - 1)
            {
                var item = listBox.Items[idx];
                listBox.Items.RemoveAt(idx);
                listBox.Items.Insert(idx + 1, item);
                listBox.SelectedIndex = idx + 1;
                listBox.Focus();
            }
        };

        panel.Controls.AddRange([btnAdd, btnEdit, btnRemove, btnUp, btnDown]);
        return panel;
    }

    /// <summary>
    /// Creates a panel with Add, Edit, and Remove buttons for an IDEs <see cref="ListView"/>.
    /// </summary>
    /// <param name="idesList">The list view to manage.</param>
    /// <returns>A <see cref="FlowLayoutPanel"/> containing the management buttons.</returns>
    internal static FlowLayoutPanel CreateIdeButtons(ListView idesList)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 100,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(4)
        };

        var btnAdd = new Button { Text = "Add", Width = 88 };
        btnAdd.Click += (s, e) =>
        {
            var result = PromptIdeEntry("Add IDE", "", "");
            if (result != null)
            {
                var item = new ListViewItem(result.Value.desc);
                item.SubItems.Add(result.Value.path);
                idesList.Items.Add(item);
                item.Selected = true;
                idesList.Focus();
            }
        };

        var btnEdit = new Button { Text = "Edit", Width = 88 };
        btnEdit.Click += (s, e) =>
        {
            if (idesList.SelectedItems.Count == 0)
            {
                return;
            }

            var sel = idesList.SelectedItems[0];
            var result = PromptIdeEntry("Edit IDE", sel.SubItems[1].Text, sel.Text);
            if (result != null)
            {
                sel.Text = result.Value.desc;
                sel.SubItems[1].Text = result.Value.path;
            }
            idesList.Focus();
        };

        var btnRemove = new Button { Text = "Remove", Width = 88 };
        btnRemove.Click += (s, e) =>
        {
            if (idesList.SelectedItems.Count > 0)
            {
                int idx = idesList.SelectedIndices[0];
                idesList.Items.RemoveAt(idx);
                if (idesList.Items.Count > 0)
                {
                    int newIdx = Math.Min(idx, idesList.Items.Count - 1);
                    idesList.Items[newIdx].Selected = true;
                }
                idesList.Focus();
            }
        };

        panel.Controls.AddRange([btnAdd, btnEdit, btnRemove]);
        return panel;
    }

    /// <summary>
    /// Shows a dialog prompting the user for an IDE executable path and description.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="defaultPath">The default executable path.</param>
    /// <param name="defaultDesc">The default description.</param>
    /// <returns>A tuple of (path, desc), or <c>null</c> if the user cancelled.</returns>
    internal static (string path, string desc)? PromptIdeEntry(string title, string defaultPath, string defaultDesc)
    {
        var form = new Form
        {
            Text = title,
            Size = new Size(500, 190),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var lblDesc = new Label { Text = "Description:", Location = new Point(12, 15), AutoSize = true };
        var txtDesc = new TextBox { Text = defaultDesc, Location = new Point(12, 35), Width = 455 };

        var lblPath = new Label { Text = "Executable path:", Location = new Point(12, 65), AutoSize = true };
        var txtPath = new TextBox { Text = defaultPath, Location = new Point(12, 85), Width = 410 };
        var btnBrowse = new Button { Text = "...", Location = new Point(428, 84), Width = 40 };
        btnBrowse.Click += (s, e) =>
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
                FileName = txtPath.Text
            };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                txtPath.Text = ofd.FileName;
            }
        };

        var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(310, 118), Width = 75 };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(392, 118), Width = 75 };

        form.Controls.AddRange([lblDesc, txtDesc, lblPath, txtPath, btnBrowse, btnOk, btnCancel]);
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        if (form.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(txtPath.Text))
        {
            return (txtPath.Text, txtDesc.Text);
        }

        return null;
    }

    /// <summary>
    /// Shows a simple text input dialog with a label and text box.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="label">The label text displayed above the input.</param>
    /// <param name="defaultValue">The default value pre-filled in the text box.</param>
    /// <returns>The entered text, or <c>null</c> if the user cancelled.</returns>
    internal static string? PromptInput(string title, string label, string defaultValue)
    {
        var form = new Form
        {
            Text = title,
            Size = new Size(450, 150),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var lbl = new Label { Text = label, Location = new Point(12, 15), AutoSize = true };
        var txt = new TextBox { Text = defaultValue, Location = new Point(12, 38), Width = 405 };
        var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(260, 72), Width = 75 };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(342, 72), Width = 75 };

        form.Controls.AddRange([lbl, txt, btnOk, btnCancel]);
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        return form.ShowDialog() == DialogResult.OK ? txt.Text : null;
    }

    /// <summary>
    /// Reloads settings controls from the persisted <see cref="LauncherSettings"/>.
    /// </summary>
    /// <param name="toolsList">The allowed tools list box.</param>
    /// <param name="dirsList">The allowed directories list box.</param>
    /// <param name="idesList">The IDEs list view.</param>
    /// <param name="workDirBox">The default work directory text box.</param>
    internal static void ReloadSettingsUI(ListBox toolsList, ListBox dirsList, ListView idesList, TextBox workDirBox)
    {
        var fresh = LauncherSettings.Load();

        toolsList.Items.Clear();
        foreach (var tool in fresh.AllowedTools)
        {
            toolsList.Items.Add(tool);
        }

        dirsList.Items.Clear();
        foreach (var dir in fresh.AllowedDirs)
        {
            dirsList.Items.Add(dir);
        }

        idesList.Items.Clear();
        foreach (var ide in fresh.Ides)
        {
            var item = new ListViewItem(ide.Description);
            item.SubItems.Add(ide.Path);
            idesList.Items.Add(item);
        }

        workDirBox.Text = fresh.DefaultWorkDir;
    }
}
