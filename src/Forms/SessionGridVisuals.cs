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
        : Color.FromArgb(255, 238, 238);
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
                var lines = activeText.Split('\n');
                var font = row.Cells[4].InheritedStyle.Font ?? this._grid.Font;
                var padding = row.Cells[4].InheritedStyle.Padding;
                int clickedLine = lines.Length - 1;
                int cumY = padding.Top;
                for (int i = 0; i < lines.Length; i++)
                {
                    cumY += TextRenderer.MeasureText(lines[i], font).Height;
                    if (e.Location.Y < cumY)
                    {
                        clickedLine = i;
                        break;
                    }
                }
                this._activeTracker.FocusActiveProcess(sessionId, clickedLine);

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
                    var lines = activeText.Split('\n');
                    var font = row.Cells[4].InheritedStyle.Font ?? this._grid.Font;
                    var padding = row.Cells[4].InheritedStyle.Padding;
                    int cumY = padding.Top;
                    bool overLink = false;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var sz = TextRenderer.MeasureText(lines[i], font);
                        if (e.Location.Y >= cumY && e.Location.Y < cumY + sz.Height
                            && e.Location.X >= padding.Left && e.Location.X < padding.Left + sz.Width)
                        {
                            overLink = true;
                            break;
                        }
                        cumY += sz.Height;
                    }
                    this._grid.Cursor = overLink ? Cursors.Hand : Cursors.Default;
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
            var linkColor = isSelected ? Color.White : Application.IsDarkModeEnabled ? Color.FromArgb(100, 180, 255) : Color.FromArgb(0, 102, 204);
            var linkFont = new Font(e.CellStyle!.Font ?? this._grid.Font, FontStyle.Underline);
            var padding = e.CellStyle.Padding;
            int ly = e.CellBounds.Y + padding.Top;

            foreach (var line in lines)
            {
                var size = TextRenderer.MeasureText(e.Graphics!, line, linkFont);
                TextRenderer.DrawText(e.Graphics!, line, linkFont, new Point(e.CellBounds.X + padding.Left, ly), linkColor);
                ly += size.Height;
            }

            linkFont.Dispose();
            e.Handled = true;
        };
    }

    internal void Populate(List<NamedSession> sessions, ActiveStatusSnapshot snapshot, string? searchQuery)
    {
        this._grid.Rows.Clear();
        var isSearching = !string.IsNullOrWhiteSpace(searchQuery);

        var displayed = isSearching
            ? SessionService.SearchSessions(sessions, searchQuery!)
            : sessions.Take(50).ToList();

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
            }
            else if (statusIcon == "working" || !string.IsNullOrEmpty(activeText))
            {
                row.DefaultCellStyle.BackColor = ActiveRowColor;
                row.DefaultCellStyle.ForeColor = ActiveRowForeColor;
            }
        }

        this.AutoFitCwdColumn();
    }

    internal void ApplySnapshot(List<NamedSession> sessions, ActiveStatusSnapshot snapshot, string? searchQuery)
    {
        foreach (DataGridViewRow row in this._grid.Rows)
        {
            if (row.Tag is not string sessionId)
            {
                continue;
            }

            if (snapshot.SessionNamesById.TryGetValue(sessionId, out var newName))
            {
                // Find the session to check for alias
                var session = sessions.Find(s => s.Id == sessionId);
                var displayName = session != null && !string.IsNullOrEmpty(session.Alias) ? session.Alias : newName;
                var currentDisplay = row.Cells[1].Value?.ToString();
                if (currentDisplay != displayName)
                {
                    row.Cells[1].Value = displayName;
                }

                // Update tooltip with current name when alias is shown
                if (session != null && !string.IsNullOrEmpty(session.Alias))
                {
                    row.Cells[1].ToolTipText = newName;
                }
                else
                {
                    row.Cells[1].ToolTipText = "";
                }
            }

            var activeText = snapshot.ActiveTextBySessionId.GetValueOrDefault(sessionId, "");
            row.Cells[4].Value = activeText;

            var statusIcon = snapshot.StatusIconBySessionId.GetValueOrDefault(sessionId, "");
            row.Cells[0].Value = statusIcon;

            if (statusIcon == "bell")
            {
                row.DefaultCellStyle.BackColor = BellRowColor;
                row.DefaultCellStyle.ForeColor = Color.Empty;
            }
            else if (statusIcon == "working" || !string.IsNullOrEmpty(activeText))
            {
                row.DefaultCellStyle.BackColor = ActiveRowColor;
                row.DefaultCellStyle.ForeColor = ActiveRowForeColor;
            }
            else
            {
                row.DefaultCellStyle.BackColor = Color.Empty;
                row.DefaultCellStyle.ForeColor = Color.Empty;
            }
        }

        // Detect new sessions not yet in the grid and add them
        var displayedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow row in this._grid.Rows)
        {
            if (row.Tag is string id)
            {
                displayedIds.Add(id);
            }
        }

        var isSearching = !string.IsNullOrWhiteSpace(searchQuery);
        var filtered = isSearching
            ? SessionService.SearchSessions(sessions, searchQuery!)
            : sessions.Take(50).ToList();

        foreach (var session in filtered)
        {
            if (displayedIds.Contains(session.Id))
            {
                continue;
            }

            var dateText = session.LastModified.ToString("yyyy-MM-dd HH:mm");
            var cwdText = session.Folder;
            if (session.IsGitRepo)
            {
                cwdText += " ⌗";
            }

            var newActiveText = snapshot.ActiveTextBySessionId.GetValueOrDefault(session.Id, "");
            var newStatusIcon = snapshot.StatusIconBySessionId.GetValueOrDefault(session.Id, "");
            var newDisplayName = !string.IsNullOrEmpty(session.Alias) ? session.Alias : session.Summary;
            var rowIndex = this._grid.Rows.Add(newStatusIcon, newDisplayName, cwdText, dateText, newActiveText);
            var newRow = this._grid.Rows[rowIndex];
            newRow.Tag = session.Id;

            if (!string.IsNullOrEmpty(session.Alias) && !string.IsNullOrEmpty(session.Summary))
            {
                newRow.Cells[1].ToolTipText = session.Summary;
            }

            if (newStatusIcon == "bell")
            {
                newRow.DefaultCellStyle.BackColor = BellRowColor;
            }
            else if (newStatusIcon == "working" || !string.IsNullOrEmpty(newActiveText))
            {
                newRow.DefaultCellStyle.BackColor = ActiveRowColor;
                newRow.DefaultCellStyle.ForeColor = ActiveRowForeColor;
            }
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

    internal void AdvanceSpinnerFrame()
    {
        this._spinnerFrameIndex = (this._spinnerFrameIndex + 1) % 8;
        this._grid.InvalidateColumn(0);
    }
}
