using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CopilotBooster.Models;

namespace CopilotBooster.Forms;

/// <summary>
/// Helper methods for building and managing settings tab UI controls.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class SettingsVisuals
{
    /// <summary>
    /// Prefix applied to directory entries in the settings list when the directory does not exist.
    /// </summary>
    internal const string NotFoundPrefix = "(not found) ";

    /// <summary>
    /// Strips the <see cref="NotFoundPrefix"/> from a directory path if present.
    /// </summary>
    internal static string StripNotFoundPrefix(string path) =>
        path.StartsWith(NotFoundPrefix, StringComparison.Ordinal) ? path[NotFoundPrefix.Length..] : path;
    /// <summary>
    /// Wraps a <see cref="TextBox"/> in a <see cref="Panel"/> that provides a themed border.
    /// The text box is set to <see cref="BorderStyle.None"/> and fills the panel interior.
    /// </summary>
    /// <param name="textBox">The text box to wrap.</param>
    /// <returns>The wrapper panel containing the text box.</returns>
    internal static Panel WrapWithBorder(TextBox textBox)
    {
        textBox.BorderStyle = BorderStyle.None;
        textBox.Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f);
        var wrapper = new Panel
        {
            Location = textBox.Location,
            Size = new Size(textBox.Width, textBox.Height + 6),
            Anchor = textBox.Anchor,
            Padding = new Padding(1),
            BackColor = Application.IsDarkModeEnabled ? Color.FromArgb(80, 80, 80) : SystemColors.ControlDark
        };
        textBox.Location = Point.Empty;
        textBox.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        textBox.Dock = DockStyle.Fill;
        wrapper.Controls.Add(textBox);
        return wrapper;
    }

    /// <summary>
    /// Applies an info label and tooltip to a settings tab page.
    /// </summary>
    internal static void ApplyTabInfo(TabPage tab, string infoText, string tooltip)
    {
        tab.ToolTipText = tooltip;
        var label = new Label
        {
            Text = $"ℹ️ {infoText}",
            Dock = DockStyle.Top,
            Height = 24,
            Padding = new Padding(4, 4, 0, 0),
            ForeColor = Application.IsDarkModeEnabled ? Color.FromArgb(100, 149, 237) : SystemColors.GrayText
        };
        tab.Controls.Add(label);
    }

    /// <summary>
    /// Creates a panel with Add, Edit, and Remove buttons for a <see cref="ListBox"/>.
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
                    if (!listBox.Items.Cast<string>().Any(x => string.Equals(x, fbd.SelectedPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        listBox.Items.Add(fbd.SelectedPath);
                        listBox.SelectedIndex = listBox.Items.Count - 1;
                    }
                }
            }
            else
            {
                var value = PromptInput(addTitle, promptText, "");
                if (!string.IsNullOrWhiteSpace(value))
                {
                    if (!listBox.Items.Cast<string>().Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)))
                    {
                        listBox.Items.Add(value);
                        listBox.SelectedIndex = listBox.Items.Count - 1;
                    }
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

        panel.Controls.AddRange([btnAdd, btnEdit, btnRemove]);
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
                item.SubItems.Add(result.Value.filePattern);
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
            var currentPattern = sel.SubItems.Count > 2 ? sel.SubItems[2].Text : "";
            var result = PromptIdeEntry("Edit IDE", sel.SubItems[1].Text, sel.Text, currentPattern);
            if (result != null)
            {
                sel.Text = result.Value.desc;
                sel.SubItems[1].Text = result.Value.path;
                if (sel.SubItems.Count > 2)
                {
                    sel.SubItems[2].Text = result.Value.filePattern;
                }
                else
                {
                    sel.SubItems.Add(result.Value.filePattern);
                }
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
    internal static (string path, string desc, string filePattern)? PromptIdeEntry(string title, string defaultPath, string defaultDesc, string defaultPattern = "")
    {
        var form = new Form
        {
            Text = title,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f),
            Size = new Size(500, 255),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = Program._settings.AlwaysOnTop
        };
        AlignWithParent(form);

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

        var lblPattern = new Label { Text = "File pattern (optional)", Location = new Point(12, 115), AutoSize = true };
        var lblPatternInfo = new Label
        {
            Text = "ℹ️",
            Location = new Point(lblPattern.PreferredWidth + 34, 113),
            AutoSize = true,
            Cursor = Cursors.Help,
            Font = new Font("Segoe UI Emoji", lblPattern.Font.Size + 2)
        };
        var patternTooltip = new ToolTip { AutoPopDelay = 10000 };
        patternTooltip.SetToolTip(lblPatternInfo, "Semicolon-separated file patterns (e.g., *.sln;*.slnx).\nWhen set, the IDE context menu will search for matching\nproject files in the session directory and let you open them directly.");
        var txtPattern = new TextBox { Text = defaultPattern, Location = new Point(12, 135), Width = 455 };

        var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(310, 168), Width = 75 };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(392, 168), Width = 75 };

        form.Controls.AddRange([lblDesc, WrapWithBorder(txtDesc), lblPath, WrapWithBorder(txtPath), btnBrowse, lblPattern, lblPatternInfo, WrapWithBorder(txtPattern), btnOk, btnCancel]);
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        if (form.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(txtPath.Text))
        {
            return (txtPath.Text, txtDesc.Text, txtPattern.Text.Trim());
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
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f),
            Size = new Size(450, 150),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = Program._settings.AlwaysOnTop
        };
        AlignWithParent(form);

        var lbl = new Label { Text = label, Location = new Point(12, 15), AutoSize = true };
        var txt = new TextBox { Text = defaultValue, Location = new Point(12, 38), Width = 405 };
        var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(260, 72), Width = 75 };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(342, 72), Width = 75 };

        form.Controls.AddRange([lbl, WrapWithBorder(txt), btnOk, btnCancel]);
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        return form.ShowDialog() == DialogResult.OK ? txt.Text : null;
    }

    /// <summary>
    /// Positions a dialog centered horizontally on the active parent form and
    /// aligned vertically with the parent's title bar.
    /// </summary>
    internal static void AlignWithParent(Form dialog)
    {
        var parent = Form.ActiveForm;
        if (parent != null && parent != dialog &&
            (dialog.Width > parent.Width || dialog.Height > parent.Height))
        {
            dialog.StartPosition = FormStartPosition.CenterParent;
            return;
        }

        dialog.StartPosition = FormStartPosition.Manual;
        if (parent != null && parent != dialog)
        {
            dialog.Location = new Point(
                parent.Left + (parent.Width - dialog.Width) / 2,
                parent.Top);
        }
    }

    /// <summary>
    /// Applies themed selection colors to a <see cref="ListBox"/> using owner-draw.
    /// </summary>
    internal static void ApplyThemedSelection(ListBox listBox)
    {
        listBox.DrawMode = DrawMode.OwnerDrawFixed;
        listBox.ItemHeight = listBox.Font.Height + 6;
        listBox.DrawItem += (s, e) =>
        {
            if (e.Index < 0)
            {
                return;
            }

            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color back, fore;
            if (selected)
            {
                back = Application.IsDarkModeEnabled ? Color.FromArgb(0x11, 0x11, 0x11) : Color.FromArgb(200, 220, 245);
                fore = Application.IsDarkModeEnabled ? Color.White : Color.Black;
            }
            else
            {
                back = listBox.BackColor;
                fore = listBox.ForeColor;
            }

            using var backBrush = new SolidBrush(back);
            e.Graphics!.FillRectangle(backBrush, e.Bounds);

            var text = listBox.Items[e.Index]?.ToString() ?? "";
            TextRenderer.DrawText(e.Graphics, text, e.Font, e.Bounds, fore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            e.DrawFocusRectangle();
        };
    }

    /// <summary>
    /// Applies themed selection colors to a <see cref="ListView"/> using owner-draw.
    /// </summary>
    internal static void ApplyThemedSelection(ListView listView)
    {
        listView.OwnerDraw = true;

        // Track hot (hovered) item index for hover highlight
        int hotIndex = -1;
        listView.MouseMove += (s, e) =>
        {
            var hit = listView.HitTest(e.Location);
            var newHot = hit.Item?.Index ?? -1;
            if (newHot != hotIndex)
            {
                var oldHot = hotIndex;
                hotIndex = newHot;
                if (oldHot >= 0 && oldHot < listView.Items.Count)
                {
                    listView.Invalidate(listView.Items[oldHot].Bounds);
                }

                if (hotIndex >= 0)
                {
                    listView.Invalidate(listView.Items[hotIndex].Bounds);
                }
            }
        };
        listView.MouseLeave += (s, e) =>
        {
            if (hotIndex >= 0 && hotIndex < listView.Items.Count)
            {
                var old = hotIndex;
                hotIndex = -1;
                listView.Invalidate(listView.Items[old].Bounds);
            }
        };

        listView.DrawColumnHeader += (s, e) =>
        {
            var headerBack = Application.IsDarkModeEnabled ? Color.FromArgb(0x22, 0x22, 0x22) : Color.FromArgb(210, 210, 210);
            using var backBrush = new SolidBrush(headerBack);
            e.Graphics!.FillRectangle(backBrush, e.Bounds);
            var fore = Application.IsDarkModeEnabled ? Color.White : SystemColors.ControlText;
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", e.Font, e.Bounds, fore, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // Draw bottom border
            var borderColor = Application.IsDarkModeEnabled ? Color.FromArgb(80, 80, 80) : SystemColors.ControlDark;
            using var pen = new Pen(borderColor);
            e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            // Draw right border for column separation
            e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
        };
        listView.DrawItem += (s, e) =>
        {
            bool isHot = e.ItemIndex == hotIndex && !e.Item!.Selected;
            Color back = e.Item!.Selected
                ? (Application.IsDarkModeEnabled ? Color.FromArgb(0x11, 0x11, 0x11) : Color.FromArgb(200, 220, 245))
                : isHot
                    ? (Application.IsDarkModeEnabled ? Color.FromArgb(0x1A, 0x1A, 0x1A) : Color.FromArgb(230, 240, 250))
                    : listView.BackColor;
            using var brush = new SolidBrush(back);
            e.Graphics!.FillRectangle(brush, e.Bounds);
        };
        listView.DrawSubItem += (s, e) =>
        {
            bool selected = e.Item!.Selected;
            bool isHot = e.Item.Index == hotIndex && !selected;
            Color back = selected
                ? (Application.IsDarkModeEnabled ? Color.FromArgb(0x11, 0x11, 0x11) : Color.FromArgb(200, 220, 245))
                : isHot
                    ? (Application.IsDarkModeEnabled ? Color.FromArgb(0x1A, 0x1A, 0x1A) : Color.FromArgb(230, 240, 250))
                    : listView.BackColor;
            Color fore = Application.IsDarkModeEnabled
                ? (selected ? Color.White : Color.LightGray)
                : (selected ? Color.Black : SystemColors.ControlText);

            using var backBrush = new SolidBrush(back);
            e.Graphics!.FillRectangle(backBrush, e.Bounds);

            var text = e.SubItem?.Text ?? "";
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter;

            if (e.Header?.TextAlign == HorizontalAlignment.Center)
            {
                flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            }
            else if (e.Header?.TextAlign == HorizontalAlignment.Right)
            {
                flags = TextFormatFlags.Right | TextFormatFlags.VerticalCenter;
            }

            TextRenderer.DrawText(e.Graphics, text, e.Item.Font ?? listView.Font, e.Bounds, fore, flags);

            // Draw right border for column separation
            using var pen = new Pen(Application.IsDarkModeEnabled ? Color.FromArgb(80, 80, 80) : SystemColors.ControlLight);
            e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
        };
    }

    /// <summary>
    /// Reloads settings controls from the persisted <see cref="LauncherSettings"/>.
    /// Overload kept for callers that do not yet have a theme combo box.
    /// </summary>
    /// <param name="toolsList">The allowed tools list box.</param>
    /// <param name="dirsList">The allowed directories list box.</param>
    /// <param name="idesList">The IDEs list view.</param>
    /// <param name="workDirBox">The default work directory text box.</param>
    internal static void ReloadSettingsUI(ListBox toolsList, ListBox dirsList, ListView idesList, TextBox workDirBox)
        => ReloadSettingsUI(toolsList, dirsList, idesList, workDirBox, themeCombo: null);

    /// <summary>
    /// Reloads settings controls from the persisted <see cref="LauncherSettings"/>.
    /// </summary>
    /// <param name="toolsList">The allowed tools list box.</param>
    /// <param name="dirsList">The allowed directories list box.</param>
    /// <param name="idesList">The IDEs list view.</param>
    /// <param name="workDirBox">The default work directory text box.</param>
    /// <param name="themeCombo">The theme selection combo box, or <c>null</c> to skip theme reload.</param>
    internal static void ReloadSettingsUI(ListBox toolsList, ListBox dirsList, ListView idesList, TextBox workDirBox, ComboBox? themeCombo)
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
            dirsList.Items.Add(Directory.Exists(dir) ? dir : NotFoundPrefix + dir);
        }

        idesList.Items.Clear();
        foreach (var ide in fresh.Ides)
        {
            var item = new ListViewItem(ide.Description);
            item.SubItems.Add(ide.Path);
            idesList.Items.Add(item);
        }

        workDirBox.Text = fresh.DefaultWorkDir;

        if (themeCombo != null)
        {
            themeCombo.SelectedIndex = fresh.Theme switch
            {
                "light" => 1,
                "dark" => 2,
                _ => 0
            };
        }
    }
}
