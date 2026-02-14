using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CopilotApp.Models;
using CopilotApp.Services;

namespace CopilotApp.Forms;

/// <summary>
/// Controls the session DataGridView: population, painting, cursor, and click handling.
/// </summary>
[ExcludeFromCodeCoverage]
internal class SessionGridController
{
    private static readonly Color s_activeRowColor = Color.FromArgb(232, 245, 255);
    private readonly DataGridView _grid;
    private readonly ActiveStatusTracker _activeTracker;

    internal SessionGridController(DataGridView grid, ActiveStatusTracker activeTracker)
    {
        this._grid = grid;
        this._activeTracker = activeTracker;
        this.WireEvents();
    }

    private void WireEvents()
    {
        this._grid.CellMouseClick += (s, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 3)
            {
                return;
            }

            var row = this._grid.Rows[e.RowIndex];
            var activeText = row.Cells[3].Value as string;
            if (!string.IsNullOrEmpty(activeText) && row.Tag is string sessionId)
            {
                var lines = activeText.Split('\n');
                var font = row.Cells[3].InheritedStyle.Font ?? this._grid.Font;
                var padding = row.Cells[3].InheritedStyle.Padding;
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
            }
        };

        this._grid.CellMouseMove += (s, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 3)
            {
                var row = this._grid.Rows[e.RowIndex];
                var activeText = row.Cells[3].Value as string;
                if (!string.IsNullOrEmpty(activeText))
                {
                    var lines = activeText.Split('\n');
                    var font = row.Cells[3].InheritedStyle.Font ?? this._grid.Font;
                    var padding = row.Cells[3].InheritedStyle.Padding;
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
            if (e.RowIndex < 0 || e.ColumnIndex != 3 || e.Value is not string text || string.IsNullOrEmpty(text))
            {
                return;
            }

            e.PaintBackground(e.ClipBounds, true);

            var lines = text.Split('\n');
            var isSelected = (e.State & DataGridViewElementStates.Selected) != 0;
            var linkColor = isSelected ? Color.White : Color.FromArgb(0, 102, 204);
            var linkFont = new Font(e.CellStyle!.Font ?? this._grid.Font, FontStyle.Underline);
            var padding = e.CellStyle.Padding;
            int y = e.CellBounds.Y + padding.Top;

            foreach (var line in lines)
            {
                var size = TextRenderer.MeasureText(e.Graphics!, line, linkFont);
                TextRenderer.DrawText(e.Graphics!, line, linkFont, new Point(e.CellBounds.X + padding.Left, y), linkColor);
                y += size.Height;
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
            var rowIndex = this._grid.Rows.Add(session.Summary, cwdText, dateText, activeText);
            var row = this._grid.Rows[rowIndex];
            row.Tag = session.Id;

            if (!string.IsNullOrEmpty(activeText))
            {
                row.DefaultCellStyle.BackColor = s_activeRowColor;
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
                var currentName = row.Cells[0].Value?.ToString();
                if (currentName != newName)
                {
                    row.Cells[0].Value = newName;
                }
            }

            var activeText = snapshot.ActiveTextBySessionId.GetValueOrDefault(sessionId, "");
            row.Cells[3].Value = activeText;
            row.DefaultCellStyle.BackColor = string.IsNullOrEmpty(activeText) ? SystemColors.Window : s_activeRowColor;
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
            var rowIndex = this._grid.Rows.Add(session.Summary, cwdText, dateText, newActiveText);
            var newRow = this._grid.Rows[rowIndex];
            newRow.Tag = session.Id;

            if (!string.IsNullOrEmpty(newActiveText))
            {
                newRow.DefaultCellStyle.BackColor = s_activeRowColor;
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
}
