using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CopilotBooster.Models;
using CopilotBooster.Services;

namespace CopilotBooster.Forms;

/// <summary>
/// Controls the session DataGridView: population, painting, cursor, and click handling.
/// </summary>
[ExcludeFromCodeCoverage]
internal class SessionGridVisuals
{
    private static Color ActiveRowColor => Application.IsDarkModeEnabled
        ? Color.FromArgb(0x22, 0x22, 0x22)
        : Color.FromArgb(220, 235, 250);

    private static Color ActiveRowForeColor => Application.IsDarkModeEnabled
        ? Color.White
        : Color.Black;

    private static Color BellRowColor => Application.IsDarkModeEnabled
        ? Color.FromArgb(90, 30, 30)
        : Color.FromArgb(255, 200, 200);

    private static Color BellRowSelectedColor => Application.IsDarkModeEnabled
        ? Color.FromArgb(120, 40, 40)
        : Color.FromArgb(240, 160, 160);
    private readonly DataGridView _grid;
    private readonly ActiveStatusTracker _activeTracker;
    private readonly Image[] _spinnerFrames;
    private readonly Image _bellImage;
    private readonly Image? _filesIcon;
    private readonly Image? _edgeIcon;
    private int _spinnerFrameIndex;

    /// <summary>
    /// Callback to get the number of context files for a session.
    /// </summary>
    internal Func<string, int>? GetSessionFileCount;

    /// <summary>
    /// Callback to get session files for the context menu popup.
    /// </summary>
    internal Func<string, List<(string Name, string FullPath)>>? GetSessionFiles;

    /// <summary>Fired when the user clicks the Edge icon in the Context column.</summary>
    internal event Action<string>? OnContextEdgeClicked;

    /// <summary>Fired when the user clicks a file in the context files popup.</summary>
    internal event Action<string>? OnOpenFile;

    internal SessionGridVisuals(DataGridView grid, ActiveStatusTracker activeTracker)
    {
        this._grid = grid;
        this._activeTracker = activeTracker;

        var asm = typeof(SessionGridVisuals).Assembly;
        this._spinnerFrames = new Image[8];
        for (int i = 0; i < 8; i++)
        {
            using var stream = asm.GetManifestResourceStream($"CopilotBooster.Resources.spinner_{i}.png")!;
            this._spinnerFrames[i] = Image.FromStream(stream);
        }
        using var bellStream = asm.GetManifestResourceStream("CopilotBooster.Resources.bell.png")!;
        this._bellImage = Image.FromStream(bellStream);

        var shell32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
        this._filesIcon = ExistingSessionsVisuals.TryExtractIcon(shell32, 250);
        this._edgeIcon = ExistingSessionsVisuals.TryGetExeIcon(
            EdgeWorkspaceService.FindEdgePath() ?? "");

        this.WireEvents();
    }

    private void WireEvents()
    {
        // Select the row under the cursor on right-click so context menu targets it
        this._grid.CellMouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                var row = this._grid.Rows[e.RowIndex];
                if (!row.Selected)
                {
                    this._grid.ClearSelection();
                    row.Selected = true;
                    this._grid.CurrentCell = row.Cells[0];
                }
            }
        };

        this._grid.CellMouseClick += (s, e) =>
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            var row = this._grid.Rows[e.RowIndex];

            // Context column — handle file/edge icon clicks
            if (e.ColumnIndex == 4 && row.Tag is string ctxSessionId)
            {
                this.HandleContextClick(row, ctxSessionId, e);
                return;
            }

            // RunningApps column (5)
            if (e.ColumnIndex != 5)
            {
                return;
            }

            var activeText = row.Cells[5].Value as string;
            if (!string.IsNullOrEmpty(activeText) && row.Tag is string sessionId)
            {
                var clickedLine = this.HitTestLinkLine(row, e.Location);
                if (clickedLine >= 0)
                {
                    this._activeTracker.FocusActiveProcess(sessionId, clickedLine);
                }

                // Suppress bell until session transitions to working again
                this._activeTracker.InitStartedSessions([sessionId]);

                // Clear bell status when user focuses a session
                row.Cells[0].Value = "";
                if (!string.IsNullOrEmpty(activeText))
                {
                    row.DefaultCellStyle.BackColor = ActiveRowColor;
                    row.DefaultCellStyle.ForeColor = ActiveRowForeColor;
                }
            }
        };

        this._grid.CellMouseMove += (s, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 4)
            {
                var contextValue = this._grid.Rows[e.RowIndex].Cells[4].Value as string;
                if (!string.IsNullOrEmpty(contextValue))
                {
                    this._grid.Cursor = Cursors.Hand;
                    return;
                }
            }
            if (e.RowIndex >= 0 && e.ColumnIndex == 5)
            {
                var row = this._grid.Rows[e.RowIndex];
                var activeText = row.Cells[5].Value as string;
                if (!string.IsNullOrEmpty(activeText))
                {
                    var hit = this.HitTestLinkLine(row, e.Location);
                    this._grid.Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
                    return;
                }
            }
            this._grid.Cursor = Cursors.Default;
        };

        this._grid.CellMouseLeave += (s, e) =>
        {
            this._grid.Cursor = Cursors.Default;
        };

        this._grid.CellPainting += (s, e) =>
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            // Status column (0) — draw spinner/bell image
            if (e.ColumnIndex == 0)
            {
                var statusValue = e.Value as string;
                Image? img = null;
                if (statusValue == "working")
                {
                    img = this._spinnerFrames[this._spinnerFrameIndex % 8];
                }
                else if (statusValue == "bell")
                {
                    img = this._bellImage;
                }

                e.PaintBackground(e.ClipBounds, true);
                if (img != null)
                {
                    int x = e.CellBounds.X + (e.CellBounds.Width - img.Width) / 2;
                    int y = e.CellBounds.Y + (e.CellBounds.Height - img.Height) / 2;
                    e.Graphics!.DrawImage(img, x, y, img.Width, img.Height);
                }
                e.Handled = true;
                return;
            }

            // Context column (4) — draw file/edge icons
            if (e.ColumnIndex == 4)
            {
                e.PaintBackground(e.ClipBounds, true);
                var contextValue = e.Value as string;
                if (!string.IsNullOrEmpty(contextValue))
                {
                    this.PaintContextIcons(e, contextValue);
                }
                e.Handled = true;
                return;
            }

            // Active column (5) — draw underlined links
            if (e.ColumnIndex != 5 || e.Value is not string text || string.IsNullOrEmpty(text))
            {
                return;
            }

            e.PaintBackground(e.ClipBounds, true);

            var lines = text.Split('\n');
            var isSelected = (e.State & DataGridViewElementStates.Selected) != 0;
            var linkColor = isSelected
                ? (Application.IsDarkModeEnabled ? Color.FromArgb(140, 220, 255) : Color.FromArgb(0, 60, 160))
                : (Application.IsDarkModeEnabled ? Color.FromArgb(100, 180, 255) : Color.FromArgb(0, 102, 204));
            var linkFont = new Font(e.CellStyle!.Font ?? this._grid.Font, FontStyle.Underline);
            var padding = e.CellStyle.Padding;
            const int LineSpacing = 2;

            // Calculate total content height for vertical centering
            var lineHeight = TextRenderer.MeasureText(e.Graphics!, "X", linkFont).Height;
            int totalHeight = (lines.Length * lineHeight) + ((lines.Length - 1) * LineSpacing);
            int ly = e.CellBounds.Y + ((e.CellBounds.Height - totalHeight) / 2);

            foreach (var line in lines)
            {
                var size = TextRenderer.MeasureText(e.Graphics!, line, linkFont);
                int lx = e.CellBounds.X + ((e.CellBounds.Width - size.Width) / 2);
                TextRenderer.DrawText(e.Graphics!, line, linkFont, new Point(lx, ly), linkColor);
                ly += size.Height + LineSpacing;
            }

            linkFont.Dispose();
            e.Handled = true;
        };
    }

    /// <summary>
    /// Hit-tests a mouse location against the centered link lines in the Running column.
    /// Returns the 0-based line index if over a link, or -1 if not.
    /// </summary>
    private int HitTestLinkLine(DataGridViewRow row, Point location)
    {
        var activeText = row.Cells[5].Value as string;
        if (string.IsNullOrEmpty(activeText))
        {
            return -1;
        }

        var lines = activeText.Split('\n');
        var font = row.Cells[5].InheritedStyle.Font ?? this._grid.Font;
        var linkFont = new Font(font, FontStyle.Underline);
        var cellBounds = this._grid.GetCellDisplayRectangle(5, row.Index, false);
        const int LineSpacing = 2;

        var lineHeight = TextRenderer.MeasureText("X", linkFont).Height;
        int totalHeight = (lines.Length * lineHeight) + ((lines.Length - 1) * LineSpacing);
        int ly = (cellBounds.Height - totalHeight) / 2;

        for (int i = 0; i < lines.Length; i++)
        {
            var sz = TextRenderer.MeasureText(lines[i], linkFont);
            int lx = (cellBounds.Width - sz.Width) / 2;
            if (location.Y >= ly && location.Y < ly + sz.Height
                && location.X >= lx && location.X < lx + sz.Width)
            {
                linkFont.Dispose();
                return i;
            }

            ly += sz.Height + LineSpacing;
        }

        linkFont.Dispose();
        return -1;
    }

    internal void Populate(List<NamedSession> sessions, ActiveStatusSnapshot snapshot, string? searchQuery)
    {
        var isSearching = !string.IsNullOrWhiteSpace(searchQuery);

        var displayed = isSearching
            ? SessionService.SearchSessions(sessions, searchQuery!)
            : sessions.Take(50).ToList();

        // Preserve selection across repopulate
        var selectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow row in this._grid.SelectedRows)
        {
            if (row.Tag is string id)
            {
                selectedIds.Add(id);
            }
        }

        var currentId = this._grid.CurrentRow?.Tag as string;
        var scrollIndex = this._grid.FirstDisplayedScrollingRowIndex;

        using (new SuspendDrawingScope(this._grid))
        {
            this._grid.Rows.Clear();

            foreach (var session in displayed)
            {
                var dateText = session.LastModified.ToString(Program._settings.DateFormat);
                var cwdText = session.Folder;
                if (session.IsGitRepo)
                {
                    cwdText += " \u2387";
                }

                var activeText = snapshot.ActiveTextBySessionId.GetValueOrDefault(session.Id, "");
                var statusIcon = snapshot.StatusIconBySessionId.GetValueOrDefault(session.Id, "");
                var displayName = !string.IsNullOrEmpty(session.Alias) ? session.Alias : session.Summary;
                if (session.IsPinned)
                {
                    displayName = "\U0001F4CC " + displayName;
                }

                // Build context indicator data
                var fileCount = this.GetSessionFileCount?.Invoke(session.Id) ?? 0;
                var hasTabs = EdgeTabPersistenceService.HasSavedTabs(session.Id);
                var tabCount = hasTabs ? EdgeTabPersistenceService.LoadTabs(session.Id).Count : 0;
                var contextValue = BuildContextValue(fileCount, tabCount);

                var rowIndex = this._grid.Rows.Add(statusIcon, displayName, cwdText, dateText, contextValue, activeText);
                var row = this._grid.Rows[rowIndex];
                row.Tag = session.Id;

                // Context column tooltip
                if (!string.IsNullOrEmpty(contextValue))
                {
                    row.Cells[4].ToolTipText = BuildContextTooltip(fileCount, tabCount);
                }

                // Tooltip shows the current session name when alias is displayed
                if (!string.IsNullOrEmpty(session.Alias) && !string.IsNullOrEmpty(session.Summary))
                {
                    row.Cells[1].ToolTipText = session.Summary;
                }

                if (session.IsGitRepo)
                {
                    row.Cells["CWD"].ToolTipText = "Git-enabled repository";
                }

                if (statusIcon == "bell")
                {
                    row.DefaultCellStyle.BackColor = BellRowColor;
                    row.DefaultCellStyle.SelectionBackColor = BellRowSelectedColor;
                }
                else if (statusIcon == "working" || !string.IsNullOrEmpty(activeText))
                {
                    row.DefaultCellStyle.BackColor = ActiveRowColor;
                    row.DefaultCellStyle.ForeColor = ActiveRowForeColor;
                }
            }

            // Restore selection and CurrentCell
            if (selectedIds.Count > 0)
            {
                this._grid.ClearSelection();

                // Find the rows to restore
                DataGridViewRow? currentRow = null;
                var rowsToSelect = new List<DataGridViewRow>();
                foreach (DataGridViewRow row in this._grid.Rows)
                {
                    if (row.Tag is string id && selectedIds.Contains(id))
                    {
                        rowsToSelect.Add(row);
                        if (string.Equals(id, currentId, StringComparison.OrdinalIgnoreCase))
                        {
                            currentRow = row;
                        }
                    }
                }

                // Set CurrentCell first (this clears selection in FullRowSelect mode),
                // then restore all selected rows so multi-selection is preserved.
                if (currentRow != null)
                {
                    this._grid.CurrentCell = currentRow.Cells[0];
                }
                else if (rowsToSelect.Count > 0)
                {
                    this._grid.CurrentCell = rowsToSelect[0].Cells[0];
                }

                foreach (var row in rowsToSelect)
                {
                    row.Selected = true;
                }

                // No matching rows found on this tab — select the first row
                if (rowsToSelect.Count == 0 && this._grid.RowCount > 0)
                {
                    this._grid.CurrentCell = this._grid.Rows[0].Cells[0];
                    this._grid.Rows[0].Selected = true;
                }
            }
            else if (this._grid.RowCount > 0)
            {
                this._grid.CurrentCell = this._grid.Rows[0].Cells[0];
                this._grid.Rows[0].Selected = true;
            }

            // Restore scroll position
            if (scrollIndex >= 0 && scrollIndex < this._grid.RowCount)
            {
                this._grid.FirstDisplayedScrollingRowIndex = scrollIndex;
            }

            this.AutoFitCwdColumn();
        }
    }

    internal void AutoFitCwdColumn()
    {
        var cwdCol = this._grid.Columns["CWD"]!;
        var font = this._grid.Font;
        int maxWidth = cwdCol.MinimumWidth;
        foreach (DataGridViewRow row in this._grid.Rows)
        {
            var text = row.Cells["CWD"].Value?.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                var w = TextRenderer.MeasureText(text, font).Width + 20;
                if (w > maxWidth)
                {
                    maxWidth = w;
                }
            }
        }
        cwdCol.Width = Math.Min(maxWidth, 300);
    }

    internal string? GetSelectedSessionId()
    {
        return this._grid.CurrentRow?.Tag as string;
    }

    internal List<string> GetSelectedSessionIds()
    {
        var ids = new List<string>();
        foreach (DataGridViewRow row in this._grid.SelectedRows)
        {
            if (row.Tag is string id)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    internal int GetSelectedRowIndex()
    {
        return this._grid.CurrentRow?.Index ?? -1;
    }

    internal void SelectRowByIndex(int index)
    {
        if (this._grid.Rows.Count == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, this._grid.Rows.Count - 1);
        this._grid.ClearSelection();
        this._grid.Rows[index].Selected = true;
        this._grid.CurrentCell = this._grid.Rows[index].Cells[0];
    }

    /// <summary>
    /// Removes a single row by session ID and selects the adjacent row.
    /// </summary>
    internal void RemoveRowBySessionId(string sessionId)
    {
        for (int i = 0; i < this._grid.Rows.Count; i++)
        {
            if (this._grid.Rows[i].Tag is string id
                && string.Equals(id, sessionId, StringComparison.OrdinalIgnoreCase))
            {
                this._grid.Rows.RemoveAt(i);
                this.SelectRowByIndex(i);
                return;
            }
        }
    }

    internal void AdvanceSpinnerFrame()
    {
        this._spinnerFrameIndex = (this._spinnerFrameIndex + 1) % 8;
        this._grid.InvalidateColumn(0);
    }

    /// <summary>
    /// Updates the status icon for a single session row by session ID.
    /// Called from the FileSystemWatcher event handler (must be on UI thread).
    /// </summary>
    internal void UpdateSessionStatus(string sessionId, string statusIcon)
    {
        foreach (DataGridViewRow row in this._grid.Rows)
        {
            if (row.Tag is string id && string.Equals(id, sessionId, StringComparison.OrdinalIgnoreCase))
            {
                row.Cells[0].Value = statusIcon;
                var activeText = row.Cells[5].Value?.ToString() ?? "";

                if (statusIcon == "bell")
                {
                    row.DefaultCellStyle.BackColor = BellRowColor;
                    row.DefaultCellStyle.SelectionBackColor = BellRowSelectedColor;
                    row.DefaultCellStyle.ForeColor = Color.Empty;
                }
                else if (statusIcon == "working" || !string.IsNullOrEmpty(activeText))
                {
                    row.DefaultCellStyle.BackColor = ActiveRowColor;
                    row.DefaultCellStyle.SelectionBackColor = Color.Empty;
                    row.DefaultCellStyle.ForeColor = ActiveRowForeColor;
                }
                else
                {
                    row.DefaultCellStyle.BackColor = Color.Empty;
                    row.DefaultCellStyle.SelectionBackColor = Color.Empty;
                    row.DefaultCellStyle.ForeColor = Color.Empty;
                }

                break;
            }
        }
    }

    private static string BuildContextValue(int fileCount, int tabCount)
    {
        if (fileCount <= 0 && tabCount <= 0)
        {
            return "";
        }

        var parts = new List<string>(2);
        if (fileCount > 0)
        {
            parts.Add($"files:{fileCount}");
        }
        if (tabCount > 0)
        {
            parts.Add($"tabs:{tabCount}");
        }
        return string.Join("|", parts);
    }

    private static string BuildContextTooltip(int fileCount, int tabCount)
    {
        var parts = new List<string>(2);
        if (fileCount > 0)
        {
            parts.Add(fileCount == 1 ? "1 file" : $"{fileCount} files");
        }
        if (tabCount > 0)
        {
            parts.Add(tabCount == 1 ? "1 Edge tab" : $"{tabCount} Edge tabs");
        }
        return string.Join(", ", parts);
    }

    private static (int fileCount, int tabCount) ParseContextValue(string contextValue)
    {
        int fileCount = 0, tabCount = 0;
        foreach (var part in contextValue.Split('|'))
        {
            if (part.StartsWith("files:") && int.TryParse(part.AsSpan(6), out var f))
            {
                fileCount = f;
            }
            else if (part.StartsWith("tabs:") && int.TryParse(part.AsSpan(5), out var t))
            {
                tabCount = t;
            }
        }
        return (fileCount, tabCount);
    }

    private void PaintContextIcons(DataGridViewCellPaintingEventArgs e, string contextValue)
    {
        var (fileCount, tabCount) = ParseContextValue(contextValue);
        var icons = new List<Image>(2);
        if (fileCount > 0 && this._filesIcon != null)
        {
            icons.Add(this._filesIcon);
        }
        if (tabCount > 0 && this._edgeIcon != null)
        {
            icons.Add(this._edgeIcon);
        }
        if (icons.Count == 0)
        {
            return;
        }

        const int Spacing = 9;
        const int LeftPad = 6;
        int totalWidth = icons.Sum(i => i.Width) + ((icons.Count - 1) * Spacing);
        int ix = e.CellBounds.X + ((e.CellBounds.Width - totalWidth) / 2) + LeftPad;
        int iy = e.CellBounds.Y + ((e.CellBounds.Height - icons[0].Height) / 2);

        using var countFont = new Font(this._grid.Font.FontFamily, 7f, FontStyle.Bold);
        var countColor = Application.IsDarkModeEnabled ? Color.White : Color.Black;

        foreach (var icon in icons)
        {
            // Determine count for this icon
            int count = (icon == this._filesIcon) ? fileCount : (icon == this._edgeIcon) ? tabCount : 0;

            // Draw count to the left of the icon
            if (count > 0)
            {
                var countText = count.ToString();
                var countSize = TextRenderer.MeasureText(countText, countFont);
                int cx = ix - countSize.Width + 2;
                int cy = iy + ((icon.Height - countSize.Height) / 2);
                TextRenderer.DrawText(e.Graphics!, countText, countFont, new Point(cx, cy), countColor);
            }

            e.Graphics!.DrawImage(icon, ix, iy, icon.Width, icon.Height);
            ix += icon.Width + Spacing;
        }
    }

    private void HandleContextClick(DataGridViewRow row, string sessionId, DataGridViewCellMouseEventArgs e)
    {
        var contextValue = row.Cells[4].Value as string;
        if (string.IsNullOrEmpty(contextValue))
        {
            return;
        }

        var (fileCount, tabCount) = ParseContextValue(contextValue);
        if (fileCount <= 0 && tabCount <= 0)
        {
            return;
        }

        // Determine which icon was clicked based on x-position
        var cellBounds = this._grid.GetCellDisplayRectangle(4, row.Index, false);
        var icons = new List<(Image icon, string type)>();
        if (fileCount > 0 && this._filesIcon != null)
        {
            icons.Add((this._filesIcon, "files"));
        }
        if (tabCount > 0 && this._edgeIcon != null)
        {
            icons.Add((this._edgeIcon, "tabs"));
        }

        const int Spacing = 4;
        int totalWidth = icons.Sum(i => i.icon.Width) + ((icons.Count - 1) * Spacing);
        int ix = (cellBounds.Width - totalWidth) / 2;

        string? clickedType = null;
        foreach (var (icon, type) in icons)
        {
            if (e.X >= ix && e.X < ix + icon.Width)
            {
                clickedType = type;
                break;
            }
            ix += icon.Width + Spacing;
        }

        if (clickedType == "files")
        {
            this.ShowFilesContextMenu(sessionId, row.Index);
        }
        else if (clickedType == "tabs")
        {
            this.OnContextEdgeClicked?.Invoke(sessionId);
        }
    }

    private void ShowFilesContextMenu(string sessionId, int rowIndex)
    {
        var files = this.GetSessionFiles?.Invoke(sessionId);
        if (files is not { Count: > 0 })
        {
            return;
        }

        var menu = new ContextMenuStrip();

        // "Open folder" item at the top
        var folderPath = files[0].FullPath;
        var folderDir = Path.GetDirectoryName(folderPath);
        if (folderDir != null && Directory.Exists(folderDir))
        {
            Image? folderIcon = null;
            try
            {
                folderIcon = ExistingSessionsVisuals.TryExtractIcon("shell32.dll", 3);
            }
            catch { /* ignore */ }

            var openFolderItem = new ToolStripMenuItem("Open Session Folder") { Image = folderIcon };
            var capturedDir = folderDir;
            openFolderItem.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", capturedDir) { UseShellExecute = true });
            menu.Items.Add(openFolderItem);
            menu.Items.Add(new ToolStripSeparator());
        }

        foreach (var (name, fullPath) in files)
        {
            var capturedPath = fullPath;
            Image? fileIcon = null;
            try
            {
                var ico = Icon.ExtractAssociatedIcon(fullPath);
                if (ico != null)
                {
                    fileIcon = new Bitmap(ico.ToBitmap(), 16, 16);
                }
            }
            catch { /* ignore icon extraction failures */ }

            var item = new ToolStripMenuItem(name) { Image = fileIcon };
            item.Click += (_, _) => this.OnOpenFile?.Invoke(capturedPath);
            menu.Items.Add(item);
        }

        var cellRect = this._grid.GetCellDisplayRectangle(4, rowIndex, false);
        menu.Show(this._grid, new Point(cellRect.Left + cellRect.Width / 2, cellRect.Bottom));
    }

    [ExcludeFromCodeCoverage]
    private readonly ref struct SuspendDrawingScope
    {
        private const int WM_SETREDRAW = 0x000B;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

        private readonly Control _control;

        public SuspendDrawingScope(Control control)
        {
            this._control = control;
            SendMessage(control.Handle, WM_SETREDRAW, 0, 0);
        }

        public void Dispose()
        {
            SendMessage(this._control.Handle, WM_SETREDRAW, 1, 0);
            this._control.Invalidate(true);
            this._control.Update();
        }
    }
}
