using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;
using CopilotBooster.Models;
using CopilotBooster.Services;

namespace CopilotBooster.Forms;

/// <summary>
/// Builds and owns the "Existing Sessions" tab UI controls.
/// Pure visuals — no service calls, no file I/O.
/// </summary>
[ExcludeFromCodeCoverage]
internal class ExistingSessionsVisuals
{
    internal TextBox SearchBox = null!;
    internal DataGridView SessionGrid = null!;
    internal SessionGridVisuals GridVisuals = null!;
    internal Label LoadingOverlay = null!;

    /// <summary>Fired when the user clicks Refresh.</summary>
    internal event Action? OnRefreshRequested;

    /// <summary>Fired when the user double-clicks a session row. Arg = session id.</summary>
    internal event Action<string>? OnSessionDoubleClicked;

    /// <summary>Fired when the user filters sessions via the search box.</summary>
    internal event Action? OnSearchChanged;

    // Context menu events — arg is always the selected session id.
    internal event Action<string>? OnOpenSession;
    internal event Action<string>? OnEditSession;
    internal event Action<string>? OnOpenAsNewSession;
    internal event Action<string>? OnOpenAsNewSessionWorkspace;
    internal event Action<string>? OnOpenTerminal;
    internal event Action<string>? OnOpenEdge;
    internal event Action<string>? OnDeleteSession;

    /// <summary>
    /// Fired for IDE context-menu clicks.
    /// Args: sessionId, IDE entry, useRepoRoot.
    /// </summary>
    internal event Action<string, IdeEntry, bool>? OnOpenInIde;

    /// <summary>
    /// Callback to determine git-root visibility for context menu.
    /// Returns (hasGitRoot, isSubfolder).
    /// </summary>
    internal Func<string, (bool hasGitRoot, bool isSubfolder)>? GetGitRootInfo;

    internal ExistingSessionsVisuals(TabPage sessionsTab, ActiveStatusTracker activeTracker)
    {
        this.InitializeSessionGrid();
        var searchPanel = this.BuildSearchPanel();
        this.GridVisuals = new SessionGridVisuals(this.SessionGrid, activeTracker);
        this.BuildGridContextMenu();

        this.LoadingOverlay = new Label
        {
            Text = "Loading sessions...",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 14f, FontStyle.Regular)
        };

        sessionsTab.Controls.Add(this.LoadingOverlay);
        this.LoadingOverlay.BringToFront();
        sessionsTab.Controls.Add(this.SessionGrid);
        sessionsTab.Controls.Add(searchPanel);
    }

    private void InitializeSessionGrid()
    {
        this.SessionGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BorderStyle = BorderStyle.None,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            CellBorderStyle = DataGridViewCellBorderStyle.Single,
            GridColor = Application.IsDarkModeEnabled ? Color.FromArgb(80, 80, 80) : SystemColors.ControlLight,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                WrapMode = DataGridViewTriState.True,
                Padding = new Padding(4, 4, 4, 4),
                SelectionBackColor = Application.IsDarkModeEnabled ? Color.FromArgb(0x11, 0x11, 0x11) : Color.FromArgb(200, 220, 245),
                SelectionForeColor = Application.IsDarkModeEnabled ? Color.White : Color.Black
            },
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                BackColor = Application.IsDarkModeEnabled ? Color.FromArgb(0x22, 0x22, 0x22) : Color.FromArgb(210, 210, 210),
                ForeColor = Application.IsDarkModeEnabled ? Color.White : SystemColors.ControlText,
                SelectionBackColor = Application.IsDarkModeEnabled ? Color.FromArgb(0x22, 0x22, 0x22) : Color.FromArgb(210, 210, 210),
                SelectionForeColor = Application.IsDarkModeEnabled ? Color.White : SystemColors.ControlText
            },
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        };
        this.SessionGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Status",
            HeaderText = "",
            Width = 30,
            MinimumWidth = 30,
            Resizable = DataGridViewTriState.False,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        this.SessionGrid.Columns.Add("Session", "Session");
        this.SessionGrid.Columns.Add("CWD", "CWD");
        this.SessionGrid.Columns.Add("Date", "Date");
        this.SessionGrid.Columns.Add("Active", "Active");
        this.SessionGrid.Columns["Session"]!.Width = 300;
        this.SessionGrid.Columns["Session"]!.MinimumWidth = 100;
        this.SessionGrid.Columns["CWD"]!.Width = 110;
        this.SessionGrid.Columns["CWD"]!.MinimumWidth = 60;
        this.SessionGrid.Columns["Date"]!.Width = 130;
        this.SessionGrid.Columns["Date"]!.MinimumWidth = 80;
        this.SessionGrid.Columns["Active"]!.Width = 100;
        this.SessionGrid.Columns["Active"]!.MinimumWidth = 60;

        bool adjustingSessionWidth = false;
        void AdjustSessionColumnWidth()
        {
            if (adjustingSessionWidth)
            {
                return;
            }
            adjustingSessionWidth = true;
            try
            {
                var otherWidth = this.SessionGrid.Columns["Status"]!.Width
                    + this.SessionGrid.Columns["CWD"]!.Width
                    + this.SessionGrid.Columns["Date"]!.Width
                    + this.SessionGrid.Columns["Active"]!.Width
                    + (this.SessionGrid.RowHeadersVisible ? this.SessionGrid.RowHeadersWidth : 0)
                    + SystemInformation.VerticalScrollBarWidth + 2;
                var fill = this.SessionGrid.ClientSize.Width - otherWidth;
                if (fill >= this.SessionGrid.Columns["Session"]!.MinimumWidth)
                {
                    this.SessionGrid.Columns["Session"]!.Width = fill;
                }
            }
            finally { adjustingSessionWidth = false; }
        }
        this.SessionGrid.Resize += (s, e) => AdjustSessionColumnWidth();
        this.SessionGrid.ColumnWidthChanged += (s, e) =>
        {
            if (e.Column.Name != "Session")
            {
                AdjustSessionColumnWidth();
            }
        };

        this.SessionGrid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex >= 0)
            {
                var sid = this.SessionGrid.Rows[e.RowIndex].Tag as string;
                if (sid != null)
                {
                    this.OnSessionDoubleClicked?.Invoke(sid);
                }
            }
        };

        this.SessionGrid.CellPainting += (s, e) =>
        {
            if (e.RowIndex != -1)
            {
                return;
            }

            e.PaintBackground(e.ClipBounds, false);
            var borderColor = Application.IsDarkModeEnabled ? Color.FromArgb(80, 80, 80) : SystemColors.ControlDark;
            var textColor = Application.IsDarkModeEnabled ? Color.White : SystemColors.ControlText;
            using var borderPen = new Pen(borderColor);
            e.Graphics!.DrawLine(borderPen, e.CellBounds.Right - 1, e.CellBounds.Top, e.CellBounds.Right - 1, e.CellBounds.Bottom - 1);
            e.Graphics.DrawLine(borderPen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right - 1, e.CellBounds.Bottom - 1);
            var textBounds = new Rectangle(e.CellBounds.X + 4, e.CellBounds.Y + 2, e.CellBounds.Width - 20, e.CellBounds.Height - 4);
            TextRenderer.DrawText(e.Graphics, e.Value?.ToString() ?? "", e.CellStyle!.Font, textBounds, textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            var col = this.SessionGrid.Columns[e.ColumnIndex];
            if (col.HeaderCell.SortGlyphDirection != SortOrder.None)
            {
                var glyphX = e.CellBounds.Right - 16;
                var glyphY = e.CellBounds.Top + (e.CellBounds.Height - 8) / 2;
                using var brush = new SolidBrush(textColor);
                if (col.HeaderCell.SortGlyphDirection == SortOrder.Ascending)
                {
                    e.Graphics.FillPolygon(brush, [new Point(glyphX, glyphY + 8), new Point(glyphX + 4, glyphY), new Point(glyphX + 8, glyphY + 8)]);
                }
                else
                {
                    e.Graphics.FillPolygon(brush, [new Point(glyphX, glyphY), new Point(glyphX + 4, glyphY + 8), new Point(glyphX + 8, glyphY)]);
                }
            }
            e.Handled = true;
        };
    }

    private Panel BuildSearchPanel()
    {
        var searchPanel = new Panel { Dock = DockStyle.Top, Height = 34 };
        var searchLabel = new Label
        {
            Text = "Search:",
            AutoSize = true,
            Location = new Point(5, 9)
        };
        var btnRefreshTop = new Button
        {
            Text = "Refresh",
            Width = 65,
            Height = 27,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(searchPanel.Width - 70, 3)
        };
        searchPanel.Resize += (s, e) => btnRefreshTop.Left = searchPanel.ClientSize.Width - 70;
        btnRefreshTop.Click += (s, e) => this.OnRefreshRequested?.Invoke();
        this.SearchBox = new TextBox
        {
            Location = new Point(55, 4),
            Width = 100,
            Height = 20,
            Multiline = true,
            WordWrap = false,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10f),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
            PlaceholderText = "Filter sessions..."
        };
        this.SearchBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
            }
        };
        var searchBorder = SettingsVisuals.WrapWithBorder(this.SearchBox);
        searchPanel.Resize += (s, e) => searchBorder.Width = searchPanel.ClientSize.Width - 130;
        this.SearchBox.TextChanged += (s, e) => this.OnSearchChanged?.Invoke();
        searchPanel.Controls.Add(searchBorder);
        searchPanel.Controls.Add(btnRefreshTop);
        searchPanel.Controls.Add(searchLabel);
        return searchPanel;
    }

    private void BuildGridContextMenu()
    {
        var gridContextMenu = new ContextMenuStrip();

        var menuOpenSession = new ToolStripMenuItem("Open Session");
        menuOpenSession.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnOpenSession?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuOpenSession);

        var editMenuItem = new ToolStripMenuItem("Edit Session");
        editMenuItem.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnEditSession?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(editMenuItem);

        gridContextMenu.Items.Add(new ToolStripSeparator());

        var menuOpenNewSession = new ToolStripMenuItem("Open as New Copilot Session");
        menuOpenNewSession.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnOpenAsNewSession?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuOpenNewSession);

        var menuOpenNewSessionWorkspace = new ToolStripMenuItem("Open as New Copilot Session Workspace");
        menuOpenNewSessionWorkspace.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnOpenAsNewSessionWorkspace?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuOpenNewSessionWorkspace);

        gridContextMenu.Items.Add(new ToolStripSeparator());

        var menuOpenTerminal = new ToolStripMenuItem("Open Terminal");
        menuOpenTerminal.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnOpenTerminal?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuOpenTerminal);

        var ideRepoMenuItems = new List<ToolStripMenuItem>();
        if (Program._settings.Ides.Count > 0)
        {
            gridContextMenu.Items.Add(new ToolStripSeparator());

            foreach (var ide in Program._settings.Ides)
            {
                var capturedIde = ide;

                var menuIdeCwd = new ToolStripMenuItem($"Open in {ide.Description} (CWD)");
                menuIdeCwd.Click += (s, e) =>
                {
                    var sid = this.GridVisuals.GetSelectedSessionId();
                    if (sid != null)
                    {
                        this.OnOpenInIde?.Invoke(sid, capturedIde, false);
                    }
                };
                gridContextMenu.Items.Add(menuIdeCwd);

                var menuIdeRepo = new ToolStripMenuItem($"Open in {ide.Description} (Repo Root)");
                menuIdeRepo.Click += (s, e) =>
                {
                    var sid = this.GridVisuals.GetSelectedSessionId();
                    if (sid != null)
                    {
                        this.OnOpenInIde?.Invoke(sid, capturedIde, true);
                    }
                };
                gridContextMenu.Items.Add(menuIdeRepo);
                ideRepoMenuItems.Add(menuIdeRepo);
            }
        }

        gridContextMenu.Items.Add(new ToolStripSeparator());

        var menuOpenEdge = new ToolStripMenuItem("Open in Edge");
        menuOpenEdge.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnOpenEdge?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuOpenEdge);

        gridContextMenu.Items.Add(new ToolStripSeparator());

        var menuDeleteSession = new ToolStripMenuItem("Delete Session");
        menuDeleteSession.Click += (s, e) =>
        {
            var sid = this.GridVisuals.GetSelectedSessionId();
            if (sid != null)
            {
                this.OnDeleteSession?.Invoke(sid);
            }
        };
        gridContextMenu.Items.Add(menuDeleteSession);

        gridContextMenu.Opening += (s, e) =>
        {
            bool hasGitRoot = false;
            bool isSubfolder = false;
            var sessionId = this.GridVisuals.GetSelectedSessionId();
            if (sessionId != null && this.GetGitRootInfo != null)
            {
                (hasGitRoot, isSubfolder) = this.GetGitRootInfo(sessionId);
            }
            menuOpenNewSessionWorkspace.Visible = hasGitRoot;
            foreach (var item in ideRepoMenuItems)
            {
                item.Visible = isSubfolder;
            }
        };

        this.SessionGrid.ContextMenuStrip = gridContextMenu;

        this.SessionGrid.CellMouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                this.SessionGrid.ClearSelection();
                this.SessionGrid.Rows[e.RowIndex].Selected = true;
                this.SessionGrid.CurrentCell = this.SessionGrid.Rows[e.RowIndex].Cells[0];
            }
        };
    }
}
