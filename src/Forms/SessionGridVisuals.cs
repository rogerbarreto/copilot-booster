using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
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
    private int _spinnerFrameIndex;

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

        this.WireEvents();
    }

    private void WireEvents()
    {
        this._grid.CellMouseClick += (s, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 4)
            {
                return;
            }

            var row = this._grid.Rows[e.RowIndex];
            var activeText = row.Cells[4].Value as string;
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
                var row = this._grid.Rows[e.RowIndex];
                var activeText = row.Cells[4].Value as string;
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

            // Active column (4) — draw underlined links
            if (e.ColumnIndex != 4 || e.Value is not string text || string.IsNullOrEmpty(text))
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
        var activeText = row.Cells[4].Value as string;
        if (string.IsNullOrEmpty(activeText))
        {
            return -1;
        }

        var lines = activeText.Split('\n');
        var font = row.Cells[4].InheritedStyle.Font ?? this._grid.Font;
        var linkFont = new Font(font, FontStyle.Underline);
        var cellBounds = this._grid.GetCellDisplayRectangle(4, row.Index, false);
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

        this._grid.Rows.Clear();

        foreach (var session in displayed)
        {
            var dateText = session.LastModified.ToString("yyyy-MM-dd HH:mm");
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
            var rowIndex = this._grid.Rows.Add(statusIcon, displayName, cwdText, dateText, activeText);
            var row = this._grid.Rows[rowIndex];
            row.Tag = session.Id;

            // Tooltip shows the current session name when alias is displayed
            if (!string.IsNullOrEmpty(session.Alias) && !string.IsNullOrEmpty(session.Summary))
            {
                row.Cells[1].ToolTipText = session.Summary;
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

        // Restore selection
        if (selectedIds.Count > 0)
        {
            this._grid.ClearSelection();
            foreach (DataGridViewRow row in this._grid.Rows)
            {
                if (row.Tag is string id && selectedIds.Contains(id))
                {
                    row.Selected = true;
                }
            }
        }

        this.AutoFitCwdColumn();
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
                var activeText = row.Cells[4].Value?.ToString() ?? "";

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
}
