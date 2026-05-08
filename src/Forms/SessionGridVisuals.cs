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

    private const int GitHubStatusIconSize = 16;
    private const int GitHubSpinnerFrameCount = 8;
    private const string GitHubRunningTooltip = "Detecting GitHub link... click to cancel.";

    private readonly DataGridView _grid;
    private readonly ActiveStatusTracker _activeTracker;
    private readonly LauncherSettings _settings;
    private readonly Image[] _spinnerFrames;
    private readonly Timer _spinnerTimer;
    private readonly Image _bellImage;
    private readonly Image _gitIcon;
    private readonly Image? _cwdWarningIcon;
    private readonly Image? _filesIcon;
    private readonly Image? _edgeIcon;
    private int _spinnerFrameIndex;
    private int _githubSpinnerFrameIndex;
    private bool _disposed;

    /// <summary>
    /// Callback to get the number of context files for a session.
    /// </summary>
    internal Func<string, int>? GetSessionFileCount;

    /// <summary>
    /// Callback to get cached file and tab counts for a session.
    /// </summary>
    internal Func<string, (int Files, int Tabs)>? GetContextCounts;

    /// <summary>
    /// Callback to get the GitHub tracking display value for a session (serialized item summary).
    /// </summary>
    internal Func<string, string>? GetGitHubValue;

    /// <summary>
    /// Fired when user clicks the GitHub column. Args: sessionId, click position, cell bounds.
    /// </summary>
    internal event Action<string, Point, Rectangle>? OnGitHubColumnClick;

    internal AiDetectionService? AiDetectionService
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
            {
                return;
            }

            field?.DetectionStateChanged -= this.OnDetectionStateChanged;

            field = value;
            field?.DetectionStateChanged += this.OnDetectionStateChanged;

            this.UpdateSpinnerTimerState();
        }
    }

    internal IConfirmDialog ConfirmDialog { get; set; } = new MessageBoxConfirmDialog();

    /// <summary>
    /// When true, the grid cursor is locked to <see cref="Cursors.Cross"/> (pin mode).
    /// </summary>
    internal bool PinMode { get; set; }

    /// <summary>
    /// Set to true when the user manually resizes the CWD column, preventing auto-fit from overriding it.
    /// </summary>
    internal bool CwdManuallyResized;

    /// <summary>
    /// Callback to get session files for the context menu popup.
    /// </summary>
    internal Func<string, List<(string Name, string FullPath)>>? GetSessionFiles;

    /// <summary>Fired when the user clicks the Edge icon in the Context column.</summary>
    internal event Action<string>? OnContextEdgeClicked;

    /// <summary>Fired when the user clicks a file in the context files popup.</summary>
    internal event Action<string>? OnOpenFile;

    internal SessionGridVisuals(DataGridView grid, ActiveStatusTracker activeTracker, LauncherSettings? settings = null)
    {
        this._grid = grid;
        this._activeTracker = activeTracker;
        this._settings = settings ?? Program._settings;
        this._spinnerTimer = new Timer { Interval = 100 };
        this._spinnerTimer.Tick += this.OnSpinnerTimerTick;

        var asm = typeof(SessionGridVisuals).Assembly;
        this._spinnerFrames = new Image[8];
        for (int i = 0; i < 8; i++)
        {
            using var stream = asm.GetManifestResourceStream($"CopilotBooster.Resources.spinner_{i}.png")!;
            this._spinnerFrames[i] = Image.FromStream(stream);
        }
        using var bellStream = asm.GetManifestResourceStream("CopilotBooster.Resources.bell.png")!;
        this._bellImage = Image.FromStream(bellStream);
        using var gitStream = asm.GetManifestResourceStream("CopilotBooster.Resources.git.png")!;
        this._gitIcon = new Bitmap(Image.FromStream(gitStream), 14, 14);

        var shell32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shell32.dll");
        this._filesIcon = ExistingSessionsVisuals.TryExtractIcon(shell32, 250);
        var warningIcon = ExistingSessionsVisuals.TryExtractIcon(shell32, 233);
        this._cwdWarningIcon = warningIcon != null ? new Bitmap(warningIcon, 14, 14) : null;
        this._edgeIcon = ExistingSessionsVisuals.TryGetExeIcon(
            EdgeWorkspaceService.FindEdgePath() ?? "");

        this.WireEvents();
    }

    private void WireEvents()
    {
        // Select the row under the cursor on right-click so context menu targets it
        this._grid.CellMouseDown += (s, e) =>
        {
            if (this.PinMode)
            {
                return;
            }

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

            // In pin mode, only the MainForm pin click handler should process clicks
            if (this.PinMode)
            {
                return;
            }

            var row = this._grid.Rows[e.RowIndex];

            // Dismiss bell on any left-click
            if (e.Button == MouseButtons.Left)
            {
                this.DismissBell(row);
            }

            // Context column — handle file/edge icon clicks
            if (e.ColumnIndex == 4 && row.Tag is string ctxSessionId)
            {
                this.HandleContextClick(row, ctxSessionId, e);
                return;
            }

            // GitHub column — handle PR/Issue icon clicks
            if (this._grid.Columns[e.ColumnIndex].Name == "GitHub" && row.Tag is string ghSessionId)
            {
                var cellBounds = this._grid.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
                this.HandleGitHubCellClick(e.RowIndex, e.Location, cellBounds);
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
            }
        };

        // Dismiss bell on Enter keypress
        this._grid.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter && this._grid.CurrentRow is { } row)
            {
                this.DismissBell(row);
            }
        };

        this._grid.CellMouseMove += (s, e) =>
        {
            if (this.PinMode)
            {
                this._grid.Cursor = Cursors.Cross;
                return;
            }

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
            if (e.RowIndex >= 0 && this._grid.Columns[e.ColumnIndex].Name == "GitHub"
                && this._grid.Rows[e.RowIndex].Tag is string ghSid)
            {
                this._grid.Cursor = this.ShouldUseHandCursorForGitHubCell(e.Location, ghSid)
                    ? Cursors.Hand
                    : Cursors.Default;
                this.UpdateGitHubTooltip(e.RowIndex, e.ColumnIndex, e.Location, ghSid);
                return;
            }
            this._grid.Cursor = this.PinMode ? Cursors.Cross : Cursors.Default;
        };

        this._grid.CellMouseLeave += (s, e) =>
        {
            this._grid.Cursor = this.PinMode ? Cursors.Cross : Cursors.Default;
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
                    int x = e.CellBounds.X + ((e.CellBounds.Width - img.Width) / 2);
                    int y = e.CellBounds.Y + ((e.CellBounds.Height - img.Height) / 2);
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

            // GitHub column — draw PR/Issue icons with state colors and overlays
            if (this._grid.Columns[e.ColumnIndex].Name == "GitHub")
            {
                e.PaintBackground(e.ClipBounds, true);
                var githubValue = e.Value as string;
                if (e.RowIndex >= 0 && this._grid.Rows[e.RowIndex].Tag is string sessionId)
                {
                    if (!string.IsNullOrEmpty(githubValue))
                    {
                        this.PaintGitHubIcons(e, sessionId);
                    }

                    this.PaintGitHubStatusIcon(e, sessionId);
                }
                e.Handled = true;
                return;
            }

            // CWD column — truncate text and draw git/warning icon
            if (this._grid.Columns[e.ColumnIndex].Name == "CWD")
            {
                e.PaintBackground(e.ClipBounds, true);
                var cwdValue = e.Value as string;
                if (!string.IsNullOrEmpty(cwdValue))
                {
                    var font = e.CellStyle!.Font ?? this._grid.Font;
                    var foreColor = (e.State & DataGridViewElementStates.Selected) != 0
                        ? e.CellStyle.SelectionForeColor
                        : e.CellStyle.ForeColor;
                    var cwdPadding = e.CellStyle.Padding;
                    var cellTag = this._grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag as string;
                    var icon = cellTag == "git" ? this._gitIcon
                        : cellTag == "missing" ? this._cwdWarningIcon
                        : null;
                    var iconWidth = icon != null ? icon.Width + 4 : 0;
                    var availableWidth = e.CellBounds.Width - cwdPadding.Left - cwdPadding.Right - 4;
                    var textWidth = availableWidth - iconWidth;
                    var ellipsisWidth = TextRenderer.MeasureText(e.Graphics!, "...", font).Width - 4;

                    var fullTextWidth = TextRenderer.MeasureText(e.Graphics!, cwdValue, font).Width;
                    string displayText;
                    if (fullTextWidth <= textWidth)
                    {
                        displayText = cwdValue;
                    }
                    else if (textWidth > ellipsisWidth)
                    {
                        var truncated = cwdValue;
                        while (truncated.Length > 1 && TextRenderer.MeasureText(e.Graphics!, truncated, font).Width > textWidth - ellipsisWidth)
                        {
                            truncated = truncated[..^1];
                        }

                        displayText = truncated + "...";
                    }
                    else
                    {
                        displayText = "";
                    }

                    var textBounds = new Rectangle(
                        e.CellBounds.X + cwdPadding.Left + 2,
                        e.CellBounds.Y,
                        textWidth,
                        e.CellBounds.Height);
                    TextRenderer.DrawText(e.Graphics!, displayText, font, textBounds, foreColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                    if (icon != null)
                    {
                        var actualTextWidth = string.IsNullOrEmpty(displayText) ? 0 : TextRenderer.MeasureText(e.Graphics!, displayText, font).Width;
                        var iconX = e.CellBounds.X + cwdPadding.Left + 2 + actualTextWidth;
                        var iconY = e.CellBounds.Y + ((e.CellBounds.Height - icon.Height) / 2);
                        e.Graphics!.DrawImage(icon, iconX, iconY);
                    }
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
                var dateText = session.LastModified.ToString(this._settings.DateFormat);
                var cwdText = session.Folder;

                var activeText = snapshot.ActiveTextBySessionId.GetValueOrDefault(session.Id, "");
                var statusIcon = snapshot.StatusIconBySessionId.GetValueOrDefault(session.Id, "");
                var displayName = !string.IsNullOrEmpty(session.Alias) ? session.Alias : session.Summary;
                if (session.IsPinned)
                {
                    displayName = "\U0001F4CC " + displayName;
                }

                // Build context indicator data
                var (fileCount, tabCount) = this.GetContextCounts?.Invoke(session.Id) ?? (0, 0);
                var contextValue = BuildContextValue(fileCount, tabCount);

                // Build GitHub tracking indicator
                var githubValue = this.GetGitHubValue?.Invoke(session.Id) ?? "";

                var rowIndex = this._grid.Rows.Add(statusIcon, displayName, cwdText, dateText, contextValue, activeText, githubValue);
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

                var cwdExists = !string.IsNullOrEmpty(session.Cwd) && Directory.Exists(session.Cwd);

                if (!cwdExists && !string.IsNullOrEmpty(session.Cwd))
                {
                    row.Cells["CWD"].ToolTipText = $"{session.Cwd}\n⚠ Working directory not found";
                    row.Cells["CWD"].Tag = "missing";
                }
                else if (session.IsGitRepo)
                {
                    row.Cells["CWD"].ToolTipText = $"{session.Cwd} (Git)";
                    row.Cells["CWD"].Tag = "git";
                }
                else
                {
                    row.Cells["CWD"].ToolTipText = session.Cwd;
                }

                ApplyRowStyling(row, statusIcon, activeText);
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
            this.UpdateSpinnerTimerState();
        }
    }

    internal void AutoFitCwdColumn()
    {
        if (this.CwdManuallyResized)
        {
            return;
        }

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

    /// <summary>
    /// Updates only the cells that changed between the previous and current snapshot.
    /// Avoids full grid rebuild for better performance and no visual flicker.
    /// </summary>
    internal void UpdateGridIncremental(ActiveStatusSnapshot snapshot)
    {
        foreach (DataGridViewRow row in this._grid.Rows)
        {
            if (row.Tag is not string sessionId)
            {
                continue;
            }

            var newActiveText = snapshot.ActiveTextBySessionId.GetValueOrDefault(sessionId, "");
            var newStatusIcon = snapshot.StatusIconBySessionId.GetValueOrDefault(sessionId, "");

            // Update status icon if changed
            var currentStatus = row.Cells[0].Value?.ToString() ?? "";
            if (currentStatus != newStatusIcon)
            {
                row.Cells[0].Value = newStatusIcon;
                ApplyRowStyling(row, newStatusIcon, newActiveText);
            }

            // Update active text if changed
            var currentActive = row.Cells[5].Value?.ToString() ?? "";
            if (currentActive != newActiveText)
            {
                row.Cells[5].Value = newActiveText;
                ApplyRowStyling(row, newStatusIcon, newActiveText);
            }

            // Update context column (tab/file counts) if changed
            var (fileCount, tabCount) = this.GetContextCounts?.Invoke(sessionId) ?? (0, 0);
            var newContextValue = BuildContextValue(fileCount, tabCount);
            var currentContext = row.Cells[4].Value?.ToString() ?? "";
            if (currentContext != newContextValue)
            {
                row.Cells[4].Value = newContextValue;
                row.Cells[4].ToolTipText = string.IsNullOrEmpty(newContextValue)
                    ? ""
                    : BuildContextTooltip(fileCount, tabCount);
            }

            // Update GitHub column if changed
            var newGitHub = this.GetGitHubValue?.Invoke(sessionId) ?? "";
            var currentGitHub = row.Cells[6].Value?.ToString() ?? "";
            if (currentGitHub != newGitHub)
            {
                row.Cells[6].Value = newGitHub;
            }
        }

        this.UpdateSpinnerTimerState();
    }

    private static void ApplyRowStyling(DataGridViewRow row, string statusIcon, string activeText)
    {
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
    }

    internal void AdvanceSpinnerFrame()
    {
        this._spinnerFrameIndex = (this._spinnerFrameIndex + 1) % 8;
        this._grid.InvalidateColumn(0);
    }

    internal static Rectangle GetStatusIconRegion(Rectangle cellBounds)
    {
        var size = Math.Min(GitHubStatusIconSize, Math.Min(cellBounds.Width, cellBounds.Height));
        return new Rectangle(Math.Max(0, cellBounds.Width - size), 0, size, size);
    }

    internal void HandleGitHubCellClick(int rowIndex, Point clickPos, Rectangle cellBounds)
    {
        if (rowIndex < 0 || rowIndex >= this._grid.Rows.Count
            || this._grid.Rows[rowIndex].Tag is not string sessionId)
        {
            return;
        }

        if (GetStatusIconRegion(cellBounds).Contains(clickPos))
        {
            var status = this.GetDetectionStatus(sessionId);
            switch (status)
            {
                case DetectionStatus.Running:
                    if (this.ConfirmDialog.Confirm(
                        "Cancel detection?",
                        "Stop detecting the GitHub link for this session?",
                        "Stop",
                        "Keep running"))
                    {
                        this.AiDetectionService?.CancelDetection(sessionId);
                    }
                    return;
                case DetectionStatus.Undecided:
                case DetectionStatus.Error:
                    // TODO: slice #22
                    return;
                default:
                    return;
            }
        }

        this.OnGitHubColumnClick?.Invoke(sessionId, clickPos, cellBounds);
    }

    internal bool IsSpinnerVisibleForSession(string sessionId)
    {
        return this.GetDetectionStatus(sessionId) == DetectionStatus.Running;
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
                ApplyRowStyling(row, statusIcon, activeText);
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

    private void PaintGitHubIcons(DataGridViewCellPaintingEventArgs e, string sessionId)
    {
        var data = GitHubTrackingService.Load(sessionId);
        if (data == null || data.Items.Count == 0)
        {
            return;
        }

        const int IconSize = 16;
        const int Spacing = 4;
        const int OverlaySize = 14;

        var statusVisible = this.GetDetectionStatus(sessionId) != DetectionStatus.Idle;
        var availableWidth = statusVisible
            ? Math.Max(0, e.CellBounds.Width - GitHubStatusIconSize)
            : e.CellBounds.Width;
        int totalWidth = (data.Items.Count * IconSize) + ((data.Items.Count - 1) * Spacing);
        int ix = e.CellBounds.X + ((availableWidth - totalWidth) / 2);
        int iy = e.CellBounds.Y + ((e.CellBounds.Height - IconSize) / 2);

        foreach (var item in data.Items)
        {
            Bitmap icon;
            if (item.IsPr)
            {
                icon = GitHubIconRenderer.GetPrIcon(item.State, item.Draft, IconSize);
            }
            else
            {
                icon = GitHubIconRenderer.GetIssueIcon(item.State, item.StateReason, IconSize);
            }

            e.Graphics!.DrawImage(icon, ix, iy, IconSize, IconSize);

            // PR overlays — only for open/draft PRs (not merged/closed)
            if (item.IsPr && !item.IsFinal)
            {
                // Pipeline CI overlay — top-left of the icon
                if (item.Checks == "failure")
                {
                    var xIcon = GitHubIconRenderer.GetXIcon(OverlaySize);
                    e.Graphics.DrawImage(xIcon, ix - 4, iy - 4, OverlaySize, OverlaySize);
                }
                else if (item.Checks == "success")
                {
                    var pipelineIcon = GitHubIconRenderer.GetPipelineCheckIcon(OverlaySize);
                    e.Graphics.DrawImage(pipelineIcon, ix - 4, iy - 4, OverlaySize, OverlaySize);
                }

                // Approval overlay — green ✓ above the count number, bottom-right
                if (item.Approvals > 0)
                {
                    var approvalIcon = GitHubIconRenderer.GetCheckIcon(OverlaySize - 4);
                    int ax = ix + IconSize - 2;
                    int ay = iy + 1;
                    e.Graphics.DrawImage(approvalIcon, ax, ay, OverlaySize - 4, OverlaySize - 4);

                    using var approvalFont = new Font(this._grid.Font.FontFamily, 6.5f, FontStyle.Bold);
                    TextRenderer.DrawText(e.Graphics, item.Approvals.ToString(), approvalFont,
                        new Point(ax, ay + OverlaySize - 5),
                        GitHubIconRenderer.CheckGreen);
                }
            }

            // Red notification dot for new activity
            if (item.HasNewActivity)
            {
                GitHubIconRenderer.DrawNotificationDot(e.Graphics!, ix, iy, IconSize);
            }

            ix += IconSize + Spacing;
        }
    }

    private void PaintGitHubStatusIcon(DataGridViewCellPaintingEventArgs e, string sessionId)
    {
        var status = this.GetDetectionStatus(sessionId);
        if (status == DetectionStatus.Idle)
        {
            return;
        }

        Bitmap? icon = status switch
        {
            DetectionStatus.Running => GitHubIconRenderer.GetSpinnerIcon(this._githubSpinnerFrameIndex, GitHubStatusIconSize),
            DetectionStatus.Undecided => null, // TODO: slice #22
            DetectionStatus.Error => null, // TODO: slice #22
            _ => null
        };

        if (icon == null)
        {
            return;
        }

        var relativeRegion = GetStatusIconRegion(e.CellBounds);
        var region = new Rectangle(
            e.CellBounds.X + relativeRegion.X,
            e.CellBounds.Y + relativeRegion.Y,
            relativeRegion.Width,
            relativeRegion.Height);
        e.Graphics!.DrawImage(icon, region.X, region.Y, region.Width, region.Height);
    }

    private void UpdateGitHubTooltip(int rowIndex, int colIndex, Point mousePos, string sessionId)
    {
        var cell = this._grid.Rows[rowIndex].Cells[colIndex];
        var cellBounds = this._grid.GetCellDisplayRectangle(colIndex, rowIndex, false);
        var status = this.GetDetectionStatus(sessionId);
        if (status != DetectionStatus.Idle && GetStatusIconRegion(cellBounds).Contains(mousePos))
        {
            cell.ToolTipText = status switch
            {
                DetectionStatus.Running => GitHubRunningTooltip,
                DetectionStatus.Undecided => string.Empty, // TODO: slice #22
                DetectionStatus.Error => string.Empty, // TODO: slice #22
                _ => string.Empty
            };
            return;
        }

        var data = GitHubTrackingService.Load(sessionId);
        if (data == null || data.Items.Count == 0)
        {
            cell.ToolTipText = "";
            return;
        }

        const int IconSize = 16;
        const int Spacing = 4;
        var availableWidth = status != DetectionStatus.Idle
            ? Math.Max(0, cellBounds.Width - GitHubStatusIconSize)
            : cellBounds.Width;
        int totalWidth = (data.Items.Count * IconSize) + ((data.Items.Count - 1) * Spacing);
        int startX = (availableWidth - totalWidth) / 2;
        int relativeX = mousePos.X - startX;

        int index = relativeX / (IconSize + Spacing);
        if (index < 0 || index >= data.Items.Count)
        {
            cell.ToolTipText = "";
            return;
        }

        var item = data.Items[index];
        var prefix = item.IsPr ? "PR" : "Issue";
        var tooltip = $"{prefix} #{item.Number}: {item.Title}\nState: {item.State}";
        if (item.IsPr && item.Approvals > 0)
        {
            tooltip += $"\nApprovals: {item.Approvals} ({string.Join(", ", item.Approvers)})";
        }

        if (item.IsPr && !string.IsNullOrEmpty(item.Checks))
        {
            tooltip += $"\nCI: {item.Checks}";
        }

        if (!string.IsNullOrEmpty(item.Author))
        {
            tooltip += $"\nAuthor: {item.Author}";
        }

        cell.ToolTipText = tooltip;
    }

    /// <summary>
    /// Clears the bell notification icon and row highlight for a session row, if active.
    /// </summary>
    internal void DismissBell(DataGridViewRow row)
    {
        var statusValue = row.Cells[0].Value as string;
        if (statusValue != "bell" || row.Tag is not string sessionId)
        {
            return;
        }

        this._activeTracker.InitStartedSessions([sessionId]);
        row.Cells[0].Value = "";

        var activeText = row.Cells[5].Value as string ?? "";
        ApplyRowStyling(row, "", activeText);
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
        menu.ShowOnCurrentScreen(this._grid, new Point(cellRect.Left + (cellRect.Width / 2), cellRect.Bottom));
    }

    internal void Dispose()
    {
        this._disposed = true;
        this.AiDetectionService = null;
        this._spinnerTimer.Stop();
        this._spinnerTimer.Tick -= this.OnSpinnerTimerTick;
        this._spinnerTimer.Dispose();
    }

    private void OnDetectionStateChanged(string sessionId, DetectionStatus oldStatus, DetectionStatus newStatus)
    {
        if (this._grid.IsDisposed)
        {
            return;
        }

        void Apply()
        {
            this.InvalidateGitHubCell(sessionId);
            if (newStatus == DetectionStatus.Running && !this._spinnerTimer.Enabled)
            {
                this._spinnerTimer.Start();
            }

            if (oldStatus == DetectionStatus.Running && newStatus != DetectionStatus.Running)
            {
                this.UpdateSpinnerTimerState();
            }
        }

        if (this._grid.IsHandleCreated && this._grid.InvokeRequired)
        {
            this._grid.BeginInvoke(Apply);
            return;
        }

        Apply();
    }

    private void OnSpinnerTimerTick(object? sender, EventArgs e)
    {
        this._githubSpinnerFrameIndex = (this._githubSpinnerFrameIndex + 1) % GitHubSpinnerFrameCount;
        var invalidated = this.InvalidateRunningGitHubCells();
        if (!invalidated)
        {
            this._spinnerTimer.Stop();
        }
    }

    private bool InvalidateRunningGitHubCells()
    {
        var invalidated = false;
        var columnIndex = this.GetGitHubColumnIndex();
        if (columnIndex < 0)
        {
            return false;
        }

        foreach (DataGridViewRow row in this._grid.Rows)
        {
            if (row.Tag is string sessionId && this.IsSpinnerVisibleForSession(sessionId))
            {
                this._grid.InvalidateCell(columnIndex, row.Index);
                invalidated = true;
            }
        }

        return invalidated;
    }

    private void InvalidateGitHubCell(string sessionId)
    {
        var columnIndex = this.GetGitHubColumnIndex();
        if (columnIndex < 0)
        {
            return;
        }

        foreach (DataGridViewRow row in this._grid.Rows)
        {
            if (row.Tag is string id && string.Equals(id, sessionId, StringComparison.OrdinalIgnoreCase))
            {
                this._grid.InvalidateCell(columnIndex, row.Index);
                return;
            }
        }
    }

    private void UpdateSpinnerTimerState()
    {
        if (this._disposed)
        {
            return;
        }

        if (this.HasRunningVisibleSession())
        {
            if (!this._spinnerTimer.Enabled)
            {
                this._spinnerTimer.Start();
            }
        }
        else
        {
            this._spinnerTimer.Stop();
        }
    }

    private bool HasRunningVisibleSession()
    {
        foreach (DataGridViewRow row in this._grid.Rows)
        {
            if (row.Tag is string sessionId && this.IsSpinnerVisibleForSession(sessionId))
            {
                return true;
            }
        }

        return false;
    }

    private bool ShouldUseHandCursorForGitHubCell(Point mousePos, string sessionId)
    {
        var columnIndex = this.GetGitHubColumnIndex();
        if (columnIndex < 0)
        {
            return false;
        }

        var rowIndex = this.FindRowIndexBySessionId(sessionId);
        if (rowIndex < 0)
        {
            return false;
        }

        var cellBounds = this._grid.GetCellDisplayRectangle(columnIndex, rowIndex, false);
        var status = this.GetDetectionStatus(sessionId);
        if (GetStatusIconRegion(cellBounds).Contains(mousePos))
        {
            return status == DetectionStatus.Running;
        }

        return !string.IsNullOrEmpty(this._grid.Rows[rowIndex].Cells[columnIndex].Value as string);
    }

    private int FindRowIndexBySessionId(string sessionId)
    {
        foreach (DataGridViewRow row in this._grid.Rows)
        {
            if (row.Tag is string id && string.Equals(id, sessionId, StringComparison.OrdinalIgnoreCase))
            {
                return row.Index;
            }
        }

        return -1;
    }

    private int GetGitHubColumnIndex()
    {
        return this._grid.Columns.Contains("GitHub") ? this._grid.Columns["GitHub"]!.Index : -1;
    }

    private DetectionStatus GetDetectionStatus(string sessionId)
    {
        return this.AiDetectionService?.TryGetState(sessionId).Status ?? DetectionStatus.Idle;
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
